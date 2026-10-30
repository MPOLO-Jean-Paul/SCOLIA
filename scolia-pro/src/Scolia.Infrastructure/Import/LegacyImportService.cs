using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Application.Services;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Import;

/// <summary>Migration des classeurs SCOLIA V79 (.xlsm/.xlsx) : promotions, apprenants (matricules conservés)
/// et paiements (un reçu par ancien numéro d'enregistrement, réparti par mois). Simulation possible avant écriture.
/// Un même paiement n'est jamais importé deux fois (clé d'idempotence par ancien numéro).</summary>
public sealed class LegacyImportService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock)
{
    private sealed record LegacyPromotion(string Code, string Name, string? Section, int Level, decimal Monthly, bool Active);
    private sealed record LegacyStudent(string Matricule, string Last, string Middle, string First, string Gender, DateOnly? Birth, string? Place, string Promotion,
        string? Guardian, string? Phone1, string? Phone2, DateOnly? Enrolled, string Status, string? Address, string Source);
    private sealed record LegacyPayment(long Id, DateOnly? Date, string Number, string Matricule, string Promotion, string Month, decimal Amount, string Source, int Row);

    public Task<ImportReport> ImportAsync(IReadOnlyList<(string Name, Stream Content)> files, bool apply, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var promotions = new Dictionary<string, LegacyPromotion>(StringComparer.Ordinal);
        var students = new Dictionary<string, LegacyStudent>(StringComparer.Ordinal);
        var payments = new List<LegacyPayment>();

        foreach (var (name, content) in files)
        {
            XLWorkbook workbook;
            try { workbook = new XLWorkbook(content); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                errors.Add($"{name} : fichier illisible ({ex.Message}).");
                continue;
            }
            using (workbook)
            {
                if (workbook.TryGetWorksheet("PROMOTIONS", out var p)) ReadPromotions(p, promotions, warnings);
                if (workbook.TryGetWorksheet("APPRENANTS", out var a)) ReadStudents(a, name, students, warnings);
                if (workbook.TryGetWorksheet("PAIEMENTS", out var pay)) ReadPayments(pay, name, payments, warnings, errors);
                if (!workbook.Worksheets.Any(w => w.Name is "PROMOTIONS" or "APPRENANTS" or "PAIEMENTS"))
                    warnings.Add($"{name} : aucune feuille SCOLIA (PROMOTIONS, APPRENANTS, PAIEMENTS) trouvée.");
            }
        }

        var year = await YearContext.CurrentAsync(db, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var periodsByName = year.Periods.GroupBy(p => TextKey.Normalize(p.Name)).ToDictionary(g => g.Key, g => g.First());

        // Promotions : celles des apprenants doivent exister ; la mensualité vient du classeur.
        foreach (var code in students.Values.Select(s => s.Promotion).Where(c => c.Length > 0).Distinct())
            if (!promotions.ContainsKey(code))
            {
                var fee = payments.Where(x => x.Promotion == code).Select(x => x.Amount).DefaultIfEmpty(0).Max();
                promotions[code] = new LegacyPromotion(code, code, null, 0, fee, true);
                // Une liste nominative n'est pas un journal comptable : la mensualité peut être renseignée ensuite.
            }

        var existingPromotions = await db.Promotions.Include(p => p.Fees).Where(p => p.AcademicYearId == year.Id).ToDictionaryAsync(p => p.Code, ct);
        var existingSections = await db.Sections.ToDictionaryAsync(s => TextKey.Normalize(s.Name), ct);
        var existingMatricules = (await db.Students.Select(s => s.Matricule).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var existingKeys = (await db.Receipts.Where(r => r.IdempotencyKey != null && r.IdempotencyKey.StartsWith("import:")).Select(r => r.IdempotencyKey!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var promotionEntities = new Dictionary<string, Promotion>(existingPromotions, StringComparer.Ordinal);
        var newPromotions = 0;
        foreach (var lp in promotions.Values)
        {
            if (lp.Code.Length != 4) { warnings.Add($"Promotion « {lp.Code} » ignorée : le code doit compter 4 caractères."); continue; }
            if (promotionEntities.ContainsKey(lp.Code)) continue;
            Section? section = null;
            if (!string.IsNullOrWhiteSpace(lp.Section))
            {
                var key = TextKey.Normalize(lp.Section);
                if (!existingSections.TryGetValue(key, out section))
                {
                    var code = new string(key.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
                    if (code.Length == 0 || existingSections.Values.Any(s => s.Code == code)) code = $"S{existingSections.Count + 1}";
                    section = new Section { Code = code, Name = lp.Section.Trim() };
                    existingSections[key] = section;
                    if (apply) db.Sections.Add(section);
                }
            }
            var promotion = new Promotion { AcademicYearId = year.Id, Code = lp.Code, Name = lp.Name, Level = lp.Level, SectionId = section?.Id, IsActive = lp.Active };
            promotion.Fees.AddRange(SchoolService.MonthlyFees(promotion.Id, year, lp.Monthly));
            promotionEntities[lp.Code] = promotion;
            newPromotions++;
            if (apply) db.Promotions.Add(promotion);
        }

        var enrollments = new Dictionary<string, (Student Student, Enrollment Enrollment, Promotion Promotion)>(StringComparer.Ordinal);
        var newStudents = 0;
        foreach (var ls in students.Values)
        {
            if (existingMatricules.Contains(ls.Matricule)) { warnings.Add($"{ls.Matricule} existe déjà dans SCOLIA : dossier conservé tel quel."); continue; }
            if (!promotionEntities.TryGetValue(ls.Promotion, out var promotion)) { errors.Add($"{ls.Matricule} : promotion « {ls.Promotion} » inconnue, dossier non importé."); continue; }
            var student = new Student
            {
                Matricule = ls.Matricule, LastName = ls.Last.ToUpperInvariant(), MiddleName = ls.Middle.ToUpperInvariant(), FirstName = TextKey.ProperName(ls.First),
                Gender = ls.Gender.StartsWith('F') ? Gender.F : ls.Gender.StartsWith('M') ? Gender.M : Gender.Inconnu, BirthDate = ls.Birth, BirthPlace = ls.Place, GuardianName = ls.Guardian,
                GuardianPhone = Phone(ls.Phone1, ls.Matricule, warnings), GuardianPhone2 = Phone(ls.Phone2, ls.Matricule, warnings), Address = ls.Address,
                Notes = $"Importé de {ls.Source}",
            };
            student.RefreshKeys();
            // Les coordonnées absentes sont normales pour un registre nominatif ; on peut les compléter plus tard.
            var status = TextKey.Normalize(ls.Status) switch { "SUSPENDU" => EnrollmentStatus.Suspendu, "SORTI" => EnrollmentStatus.Sorti, _ => EnrollmentStatus.Actif };
            var enrollment = new Enrollment { StudentId = student.Id, AcademicYearId = year.Id, PromotionId = promotion.Id, EnrolledOn = ls.Enrolled ?? year.StartDate, Status = status };
            enrollments[ls.Matricule] = (student, enrollment, promotion);
            newStudents++;
            if (apply) { db.Students.Add(student); db.Enrollments.Add(enrollment); }
        }

        // Paiements : un reçu par ancien numéro, réparti sur les mois correspondants de la promotion actuelle.
        var counterName = $"RC-{year.FirstCalendarYear}";
        var counter = await db.Counters.FirstOrDefaultAsync(c => c.Name == counterName, ct);
        long sequence = counter?.Value ?? 0;
        var newReceipts = 0;
        var newAllocations = 0;
        var groups = payments.GroupBy(p => string.IsNullOrWhiteSpace(p.Number) ? $"{p.Matricule}|ID{p.Id}" : $"{p.Matricule}|{p.Number}");
        foreach (var group in groups.OrderBy(g => g.Min(x => x.Date ?? DateOnly.MaxValue)).ThenBy(g => g.Min(x => x.Id)))
        {
            var first = group.First();
            var key = $"import:{group.Key}";
            if (existingKeys.Contains(key)) { warnings.Add($"Reçu {first.Number} de {first.Matricule} déjà importé : ignoré."); continue; }
            if (!enrollments.TryGetValue(first.Matricule, out var target))
            {
                errors.Add($"Paiement {first.Number} ({first.Source}, ligne {first.Row}) : matricule {first.Matricule} absent des apprenants importés.");
                continue;
            }
            var allocations = new List<ReceiptAllocation>();
            foreach (var p in group)
            {
                if (!periodsByName.TryGetValue(TextKey.Normalize(p.Month), out var period)
                    && !periodsByName.TryGetValue(TextKey.Normalize(MonthOnly(p.Month)), out period))
                {
                    errors.Add($"Paiement {p.Number} ({p.Source}, ligne {p.Row}) : mois « {p.Month} » absent de l'année {year.Label}.");
                    continue;
                }
                var fee = target.Promotion.Fees.FirstOrDefault(f => f.Kind == FeeKind.Mensualite && f.PeriodId == period.Id);
                if (fee is null) { errors.Add($"Paiement {p.Number} : aucune mensualité {period.Name} pour {target.Promotion.Code}."); continue; }
                if (p.Promotion.Length > 0 && p.Promotion != target.Promotion.Code)
                    warnings.Add($"Paiement {p.Number} de {p.Matricule} : promotion historique {p.Promotion}, affecté à {target.Promotion.Code}.");
                allocations.Add(new ReceiptAllocation { FeeItemId = fee.Id, Amount = p.Amount });
            }
            if (allocations.Count == 0) continue;
            var receipt = new Receipt
            {
                Number = DocumentNumbers.Format("RC", year.FirstCalendarYear, ++sequence), VerificationCode = DocumentNumbers.NewVerificationCode(),
                Date = group.Select(x => x.Date).Where(d => d is not null).DefaultIfEmpty(clock.Today).Min() ?? clock.Today,
                StudentId = target.Student.Id, EnrollmentId = target.Enrollment.Id, AcademicYearId = year.Id, Amount = allocations.Sum(a => a.Amount),
                Method = PaymentMethod.Especes, Reference = string.IsNullOrWhiteSpace(first.Number) ? null : $"Ancien reçu {first.Number}",
                CashierName = "Import SCOLIA V79", Note = $"Importé de {first.Source}", IdempotencyKey = key,
            };
            foreach (var a in allocations) { a.ReceiptId = receipt.Id; receipt.Allocations.Add(a); }
            newReceipts++;
            newAllocations += allocations.Count;
            if (apply) db.Receipts.Add(receipt);
        }

        if (apply)
        {
            if (counter is null) db.Counters.Add(new Counter { Name = counterName, Value = sequence }); else counter.Value = sequence;
            audit.Record("import.scolia", "Import", null, $"Import SCOLIA : {newPromotions} promotion(s), {newStudents} apprenant(s), {newReceipts} reçu(s).",
                new { fichiers = files.Select(f => f.Name), avertissements = warnings.Count, erreurs = errors.Count });
            await db.SaveChangesAsync(ct);
        }
        return new ImportReport(apply, newPromotions, newStudents, newReceipts, newAllocations, warnings.Take(500).ToList(), errors.Take(500).ToList());
    }, ct);

    /// <summary>Colonnes lues par l'import, dans l'ordre du classeur V79 : titre et exemple affiché au-dessus.</summary>
    private static readonly (string Sheet, (string Title, string Hint)[] Columns)[] TemplateSheets =
    [
        ("PROMOTIONS", [("Code", "INF1 · 4 caractères"), ("Désignation", "Première année"), ("Section", "Infirmière"), ("Niveau", "1"), ("Année", "Non lue"),
            ("Mensualité", "55000"), ("Total annuel", "Non lue"), ("Activation", "Active ou Inactive")]),
        ("APPRENANTS", [("Matricule", "INF101 · obligatoire"), ("Nom", "KALONJI"), ("Post-nom", "MBOMBO"), ("Prénom", "Ruth"), ("Sexe", "F ou M"),
            ("Naissance", "12/06/2008 · obligatoire"), ("Lieu", "Lubumbashi · obligatoire"), ("Promotion", "INF1 · code de PROMOTIONS"), ("Parent / tuteur", "KALONJI Pierre"),
            ("Contact 1", "0812345678 · obligatoire"), ("Contact 2", "Facultatif, autre numéro"), ("Inscription", "01/09/2026"), ("Année", "Non lue"),
            ("Statut", "Actif, Suspendu ou Sorti"), ("Adresse", "Av. Kasaï 12")]),
        ("PAIEMENTS", [("N°", "1"), ("Date", "05/09/2026"), ("N° reçu", "ancien numéro"), ("Matricule", "INF101 · de APPRENANTS"), ("Apprenant", "Non lue"),
            ("Promotion", "INF1"), ("Mois", "Septembre"), ("Montant dû", "Non lue"), ("Montant payé", "55000 · obligatoire")]),
    ];

    /// <summary>
    /// Classeur vide au format attendu par l'import : une feuille par type de données, les titres en ligne 4 et un exemple
    /// en ligne 3, pour reprendre des données tenues dans un autre fichier Excel. Les lignes de saisie commencent en ligne 5.
    /// </summary>
    public static byte[] Template()
    {
        using var book = new XLWorkbook();
        var notice = book.AddWorksheet("NOTICE");
        string[] lines =
        [
            "Modèle d'import SCOLIA",
            "Recopiez vos données à partir de la ligne 5 de chaque feuille. La ligne 3 montre un exemple : elle n'est pas importée.",
            "1. PROMOTIONS : une ligne par promotion. Le code compte 4 caractères. La mensualité est en francs congolais.",
            "2. APPRENANTS : une ligne par apprenant. Le matricule est conservé. La promotion reprend un code de la feuille PROMOTIONS.",
            "   Les dates, lieux et contacts sont facultatifs à l’importation. Complétez-les après l’enregistrement, avant les relances.",
            "3. PAIEMENTS : une ligne par mois payé. Plusieurs lignes avec le même N° reçu forment un seul reçu.",
            "Le mois s'écrit en toutes lettres, comme dans l'année scolaire : Septembre, Octobre...",
            "Les colonnes marquées « Non lue » peuvent rester vides.",
            "Dans SCOLIA : Sauvegardes, Importation du classeur. Un aperçu montre ce qui sera créé avant toute écriture.",
            "Relancer l'import ne crée aucun doublon : les matricules et les reçus déjà importés sont ignorés.",
        ];
        for (var i = 0; i < lines.Length; i++) notice.Cell(i + 1, 1).Value = lines[i];
        notice.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml("#13294B"));
        notice.Column(1).Width = 120;

        foreach (var (name, columns) in TemplateSheets)
        {
            var sheet = book.AddWorksheet(name);
            sheet.Cell(1, 1).Value = name;
            sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(XLColor.FromHtml("#13294B"));
            sheet.Cell(2, 1).Value = "Saisie à partir de la ligne 5. La ligne 3 est un exemple.";
            sheet.Cell(2, 1).Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
            for (var c = 0; c < columns.Length; c++)
            {
                var hint = sheet.Cell(3, c + 1);
                hint.Value = columns[c].Hint;
                hint.Style.Font.SetItalic().Font.SetFontColor(XLColor.FromHtml("#64748B"));
                var title = sheet.Cell(4, c + 1);
                title.Value = columns[c].Title;
                title.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#13294B"));
                sheet.Column(c + 1).Width = Math.Max(14, columns[c].Hint.Length + 2);
            }
            // Matricules, codes et téléphones restent du texte : Excel garderait sinon le zéro initial perdu.
            foreach (var title in new[] { "Code", "Matricule", "Promotion", "Contact 1", "Contact 2", "N° reçu" })
            {
                var index = Array.FindIndex(columns, c => c.Title == title);
                if (index >= 0) sheet.Range(5, index + 1, 2000, index + 1).Style.NumberFormat.Format = "@";
            }
            sheet.SheetView.FreezeRows(4);
        }
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static string? Phone(string? raw, string matricule, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return IdentityRules.NormalizePhone(raw); }
        catch (DomainException)
        {
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 9) return "0" + digits; // Zéro initial perdu par Excel.
            warnings.Add($"{matricule} : numéro « {raw} » conservé tel quel (format inhabituel).");
            return raw.Trim();
        }
    }

    private static string Text(IXLCell cell) => cell.IsEmpty() ? string.Empty : cell.GetFormattedString().Trim();

    private static DateOnly? Date(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        if (cell.DataType == XLDataType.Number) { var n = cell.GetDouble(); if (n is > 20000 and < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n)); }
        var text = Text(cell);
        return DateOnly.TryParseExact(text, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static decimal? Number(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
        var text = Text(cell).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("FC", string.Empty, StringComparison.OrdinalIgnoreCase);
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet sheet)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 5; r <= last; r++)
        {
            var row = sheet.Row(r);
            if (!row.IsEmpty()) yield return row;
        }
    }

    private static void ReadPromotions(IXLWorksheet sheet, Dictionary<string, LegacyPromotion> target, List<string> warnings)
    {
        foreach (var row in DataRows(sheet))
        {
            var code = Promotion.NormalizeCode(Text(row.Cell(1)));
            if (code.Length == 0) continue;
            var monthly = Number(row.Cell(6)) ?? 0;
            // La mensualité manquante est volontairement autorisée pour une importation nominative.
            var level = int.TryParse(new string(Text(row.Cell(4)).Where(char.IsDigit).Take(2).ToArray()), out var parsed) ? parsed : 0;
            target.TryAdd(code, new LegacyPromotion(code, string.IsNullOrWhiteSpace(Text(row.Cell(2))) ? code : Text(row.Cell(2)), NullIfEmpty(Text(row.Cell(3))), level,
                monthly, TextKey.Normalize(Text(row.Cell(8))) != "INACTIVE"));
        }
    }

    private static void ReadStudents(IXLWorksheet sheet, string source, Dictionary<string, LegacyStudent> target, List<string> warnings)
    {
        foreach (var row in DataRows(sheet))
        {
            var matricule = TextKey.Normalize(Text(row.Cell(1))).Replace(" ", string.Empty, StringComparison.Ordinal);
            if (matricule.Length == 0) { warnings.Add($"{source} APPRENANTS ligne {row.RowNumber()} : matricule absent, ligne ignorée."); continue; }
            if (target.ContainsKey(matricule)) { warnings.Add($"{source} : matricule {matricule} en double (ligne {row.RowNumber()}), seule la première fiche est importée."); continue; }
            target[matricule] = new LegacyStudent(matricule, Text(row.Cell(2)), Text(row.Cell(3)), Text(row.Cell(4)), TextKey.Normalize(Text(row.Cell(5))), Date(row.Cell(6)),
                NullIfEmpty(Text(row.Cell(7))), Promotion.NormalizeCode(Text(row.Cell(8))), NullIfEmpty(Text(row.Cell(9))), NullIfEmpty(Text(row.Cell(10))),
                NullIfEmpty(Text(row.Cell(11))), Date(row.Cell(12)), Text(row.Cell(14)), NullIfEmpty(Text(row.Cell(15))), source);
        }
    }

    private static void ReadPayments(IXLWorksheet sheet, string source, List<LegacyPayment> target, List<string> warnings, List<string> errors)
    {
        foreach (var row in DataRows(sheet))
        {
            var matricule = TextKey.Normalize(Text(row.Cell(4))).Replace(" ", string.Empty, StringComparison.Ordinal);
            var amount = Number(row.Cell(9));
            if (matricule.Length == 0) { errors.Add($"{source} PAIEMENTS ligne {row.RowNumber()} : montant sans matricule, à rapprocher manuellement."); continue; }
            if (amount is null) { errors.Add($"{source} PAIEMENTS ligne {row.RowNumber()} : montant payé absent ou non numérique."); continue; }
            if (amount <= 0) { warnings.Add($"{source} PAIEMENTS ligne {row.RowNumber()} : montant nul ou négatif ignoré."); continue; }
            var id = (long)(Number(row.Cell(1)) ?? row.RowNumber());
            target.Add(new LegacyPayment(id, Date(row.Cell(2)), TextKey.Normalize(Text(row.Cell(3))), matricule, Promotion.NormalizeCode(Text(row.Cell(6))),
                Text(row.Cell(7)), decimal.Round(amount.Value, 2), source, row.RowNumber()));
        }
    }

    /// <summary>« Septembre 2026 » : seul le nom du mois compte, l'année est celle de l'année scolaire.</summary>
    private static string MonthOnly(string month) => month.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? month;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
