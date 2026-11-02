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
        string? Guardian, string? Phone1, string? Phone2, DateOnly? Enrolled, string Status, string? Address, string Source, int Row = 0);
    private sealed record LegacyPayment(long Id, DateOnly? Date, string Number, string Matricule, string Promotion, string Month, decimal Amount, string Source, int Row);

    public Task<ImportReport> ImportAsync(IReadOnlyList<(string Name, Stream Content)> files, bool apply, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var promotions = new Dictionary<string, LegacyPromotion>(StringComparer.Ordinal);
        var students = new List<LegacyStudent>();
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
                else if (StudentSheet(workbook) is { } list) ReadStudents(list, name, students, warnings);
                if (workbook.TryGetWorksheet("PAIEMENTS", out var pay)) ReadPayments(pay, name, payments, warnings, errors);
                if (!workbook.Worksheets.Any(w => w.Name is "PROMOTIONS" or "APPRENANTS" or "PAIEMENTS") && StudentSheet(workbook) is null)
                    warnings.Add($"{name} : aucune feuille SCOLIA (PROMOTIONS, APPRENANTS, PAIEMENTS) trouvée.");
            }
        }

        var year = await YearContext.CurrentAsync(db, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var periodsByName = year.Periods.GroupBy(p => TextKey.Normalize(p.Name)).ToDictionary(g => g.Key, g => g.First());

        // Aucun apprenant n'est écarté pour un code de promotion : un code mal formé reçoit un code de 4 caractères,
        // une promotion absente range l'apprenant dans « À classer », à réaffecter ensuite.
        var codes = new PromotionCodes(promotions.Keys.Concat(students.Select(x => x.Promotion)), warnings);
        foreach (var raw in promotions.Keys.ToList())
        {
            var fixedCode = codes.For(raw);
            if (fixedCode == raw) continue;
            var lp = promotions[raw];
            promotions.Remove(raw);
            promotions.TryAdd(fixedCode, lp with { Code = fixedCode, Name = lp.Name == raw ? $"{lp.Name} (code d'origine {raw})" : lp.Name });
        }
        students = students.Select(x => x with { Promotion = x.Promotion.Length == 0 ? Unclassified : codes.For(x.Promotion) }).ToList();
        if (students.Any(x => x.Promotion == Unclassified) && !promotions.ContainsKey(Unclassified))
            promotions[Unclassified] = new LegacyPromotion(Unclassified, "À classer", null, 0, 0, true);

        // Promotions : celles des apprenants doivent exister ; la mensualité vient du classeur.
        foreach (var code in students.Select(s => s.Promotion).Where(c => c.Length > 0).Distinct())
            if (!promotions.ContainsKey(code))
            {
                var fee = payments.Where(x => codes.For(x.Promotion) == code).Select(x => x.Amount).DefaultIfEmpty(0).Max();
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

        // Permet d'ajouter les paiements plus tard sans réimporter les apprenants déjà enregistrés.
        // Ne jamais recréer un dossier permanent lorsque son matricule est connu.
        List<Enrollment> storedEnrollments = payments.Count == 0 ? [] : await db.Enrollments
            .Include(e => e.Student)
            .Where(e => e.AcademicYearId == year.Id)
            .ToListAsync(ct);
        // Enrollment contient PromotionId, et pas de navigation Promotion.
        // Réutiliser les promotions réelles de l'année avec leurs rubriques de frais.
        var existingEnrollmentByMatricule = storedEnrollments
            .Where(e => e.Student is not null && existingPromotions.Values.Any(p => p.Id == e.PromotionId))
            .GroupBy(e => e.Student!.Matricule, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                var enrollment = g.First();
                var promotion = existingPromotions.Values.First(p => p.Id == enrollment.PromotionId);
                return (Student: enrollment.Student!, Enrollment: enrollment, Promotion: promotion);
            }, StringComparer.Ordinal);
        var enrollments = new Dictionary<string, (Student Student, Enrollment Enrollment, Promotion Promotion)>(StringComparer.Ordinal);
        var newStudents = 0;
        // Chaque ligne devient un apprenant : un matricule absent est attribué, un matricule repris par une autre
        // personne est renuméroté. Seul un doublon exact (même matricule et même identité) est fusionné.
        var taken = new HashSet<string>(existingMatricules, StringComparer.Ordinal);
        foreach (var ls in students) if (ls.Matricule.Length > 0) taken.Add(ls.Matricule);
        var seen = new Dictionary<string, LegacyStudent>(StringComparer.Ordinal);
        // Tous ceux qui ont porté un même matricule dans le classeur : une ligne recopiée est reconnue même après renumérotation.
        var sameMatricule = new Dictionary<string, List<LegacyStudent>>(StringComparer.Ordinal);
        var stats = new ImportStats(students.Count);
        foreach (var raw in students)
        {
            var ls = raw;
            var who = Who(ls);
            if (ls.Matricule.Length == 0)
            {
                var given = NextMatricule(ls.Promotion, taken);
                warnings.Add($"{ls.Source} APPRENANTS ligne {ls.Row} : {who} sans matricule, reçoit le matricule {given}.");
                ls = ls with { Matricule = given };
                stats.Given++;
            }
            else if (seen.TryGetValue(ls.Matricule, out var earlier))
            {
                var holders = sameMatricule.GetValueOrDefault(ls.Matricule) ?? [earlier];
                if (holders.Any(h => SameIdentity(h, ls)))
                {
                    warnings.Add($"{ls.Source} APPRENANTS ligne {ls.Row} : {who} ({ls.Matricule}) figure deux fois, une seule fiche est créée.");
                    stats.Merged++;
                    continue;
                }
                var given = NextMatricule(ls.Promotion, taken);
                warnings.Add($"{ls.Source} APPRENANTS ligne {ls.Row} : matricule {ls.Matricule} déjà pris par {Who(earlier)} ; {who} reçoit le matricule {given}.");
                holders.Add(ls);
                sameMatricule[ls.Matricule] = holders;
                ls = ls with { Matricule = given };
                stats.Renumbered++;
            }
            seen[ls.Matricule] = ls;
            if (existingMatricules.Contains(ls.Matricule)) { warnings.Add($"{ls.Matricule} existe déjà dans SCOLIA : dossier conservé tel quel."); stats.Present++; continue; }
            if (!promotionEntities.TryGetValue(ls.Promotion, out var promotion)) { errors.Add($"{ls.Matricule} : promotion « {ls.Promotion} » inconnue, dossier non importé."); continue; }
            if (ls.Promotion == Unclassified) stats.Unclassified++;
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
        if (stats.Read > 0) warnings.Insert(0, stats.Summary(newStudents));

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
                if (!existingEnrollmentByMatricule.TryGetValue(first.Matricule, out var stored))
                {
                    errors.Add($"Paiement {first.Number} ({first.Source}, ligne {first.Row}) : aucun dossier actif pour {first.Matricule} dans l'année {year.Label}.");
                    continue;
                }
                target = stored;
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

        // Aucune écriture partielle si des matricules, mois ou frais sont introuvables :
        // une erreur exige d'abord la correction du classeur ou du paramétrage des frais.
        if (apply && errors.Count > 0)
            return new ImportReport(false, newPromotions, newStudents, newReceipts, newAllocations,
                warnings.Take(500).ToList(), errors.Take(500).ToList());

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
        ("APPRENANTS", [("Matricule", "INF101 · attribué s'il manque"), ("Nom", "KALONJI"), ("Post-nom", "MBOMBO"), ("Prénom", "Ruth"), ("Sexe", "F ou M"),
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
            "2. APPRENANTS : une ligne par apprenant. Le matricule est conservé ; s'il manque, ou s'il est déjà pris par une autre personne, SCOLIA en attribue un.",
            "   La promotion reprend un code de la feuille PROMOTIONS. Sans promotion, l'apprenant va dans « À classer ». Aucune ligne n'est écartée.",
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

    private static IEnumerable<IXLRow> DataRows(IXLWorksheet sheet, int first = 5)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = first; r <= last; r++)
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

    /// <summary>Colonnes de la feuille APPRENANTS : trouvées par leur titre, sinon l'ordre du classeur V79.</summary>
    private sealed class StudentColumns
    {
        public int FirstRow = 5;
        public int Matricule = 1, Last = 2, Middle = 3, First = 4, Gender = 5, Birth = 6, Place = 7, Promotion = 8, Guardian = 9,
            Phone1 = 10, Phone2 = 11, Enrolled = 12, Status = 14, Address = 15, FullName;

        /// <summary>Repère la ligne des titres (Matricule, Nom…) dans les 15 premières lignes, où qu'elle soit.</summary>
        public static StudentColumns Find(IXLWorksheet sheet)
        {
            var columns = new StudentColumns();
            var lastColumn = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 15, 40);
            for (var r = 1; r <= 15; r++)
            {
                var titles = Enumerable.Range(1, lastColumn).Select(c => (Column: c, Key: Key(Text(sheet.Cell(r, c))))).Where(t => t.Key.Length > 0).ToList();
                if (!titles.Any(t => t.Key.StartsWith("MATRICULE", StringComparison.Ordinal))) continue;
                var defaults = new StudentColumns();
                var titled = titles.Select(t => t.Column).ToHashSet();
                int Pick(Func<string, bool> match) => titles.FirstOrDefault(t => match(t.Key)).Column;
                // Titre absent pour une colonne : on garde la place habituelle du classeur V79 si cette colonne n'a pas
                // d'autre titre. Le modèle d'import, qui n'a parfois que « Matricule », reste lu comme avant.
                int Or(int found, int usual) => found > 0 ? found : titled.Contains(usual) ? 0 : usual;
                columns = new StudentColumns
                {
                    FirstRow = r + 1,
                    Matricule = Pick(k => k.StartsWith("MATRICULE", StringComparison.Ordinal)),
                    Last = Or(Pick(k => k is "NOM" or "NOMDEFAMILLE"), defaults.Last),
                    Middle = Or(Pick(k => k is "POSTNOM" or "POSTNOMS"), defaults.Middle),
                    First = Or(Pick(k => k is "PRENOM" or "PRENOMS"), defaults.First),
                    FullName = Pick(k => k is "NOMS" or "NOMCOMPLET" or "APPRENANT" or "APPRENANTS" or "NOMSETPRENOMS" or "NOMETPRENOM" or "NOMPOSTNOMPRENOM" or "IDENTITE"),
                    Gender = Or(Pick(k => k is "SEXE" or "GENRE" or "S"), defaults.Gender),
                    Birth = Or(Pick(k => k.Contains("NAISSANCE", StringComparison.Ordinal) && !k.Contains("LIEU", StringComparison.Ordinal)), defaults.Birth),
                    Place = Or(Pick(k => k.StartsWith("LIEU", StringComparison.Ordinal)), defaults.Place),
                    Promotion = Or(Pick(k => k is "PROMOTION" or "PROMO" or "CLASSE" or "CODEPROMOTION"), defaults.Promotion),
                    Guardian = Or(Pick(k => k.Contains("PARENT", StringComparison.Ordinal) || k.Contains("TUTEUR", StringComparison.Ordinal)), defaults.Guardian),
                    Phone1 = Or(Pick(k => k is "CONTACT1" or "CONTACT" or "TELEPHONE" or "TEL" or "TELEPHONE1" or "CONTACTPARENT"), defaults.Phone1),
                    Phone2 = Or(Pick(k => k is "CONTACT2" or "TELEPHONE2" or "TEL2"), defaults.Phone2),
                    Enrolled = Or(Pick(k => k.Contains("INSCRIPTION", StringComparison.Ordinal)), defaults.Enrolled),
                    Status = Or(Pick(k => k is "STATUT" or "ETAT"), defaults.Status),
                    Address = Or(Pick(k => k.StartsWith("ADRESSE", StringComparison.Ordinal)), defaults.Address),
                };
                // Titres V79 dans l'ordre habituel : la ligne d'exemple n'existe que dans le modèle, au-dessus des titres.
                break;
            }
            return columns;
        }

        private static string Key(string title) => new(TextKey.Normalize(title).Where(char.IsAsciiLetterOrDigit).ToArray());
    }

    /// <summary>Liste nominative sans feuille APPRENANTS : la première feuille dont une ligne de titres contient « Matricule ».</summary>
    private static IXLWorksheet? StudentSheet(XLWorkbook book) =>
        book.Worksheets.FirstOrDefault(w => w.Name is not ("PROMOTIONS" or "PAIEMENTS" or "NOTICE")
            && Enumerable.Range(1, 15).Any(r => Enumerable.Range(1, 20).Any(c => TextKey.Normalize(Text(w.Cell(r, c))).StartsWith("MATRICULE", StringComparison.Ordinal))));

    private static string Cell(IXLRow row, int column) => column > 0 ? Text(row.Cell(column)) : string.Empty;
    private static DateOnly? DateCell(IXLRow row, int column) => column > 0 ? Date(row.Cell(column)) : null;

    private static void ReadStudents(IXLWorksheet sheet, string source, List<LegacyStudent> target, List<string> warnings)
    {
        var map = StudentColumns.Find(sheet);
        foreach (var row in DataRows(sheet, map.FirstRow))
        {
            var matricule = TextKey.Normalize(Cell(row, map.Matricule)).Replace(" ", string.Empty, StringComparison.Ordinal);
            var (last, middle, first) = (Cell(row, map.Last), Cell(row, map.Middle), Cell(row, map.First));
            if (map.FullName > 0 && last.Length == 0 && first.Length == 0) (last, middle, first) = SplitName(Cell(row, map.FullName));
            // Ligne sans matricule ni nom : ligne de total, de titre répété ou de séparation, sans apprenant.
            if (matricule.Length == 0 && last.Length == 0 && middle.Length == 0 && first.Length == 0) continue;
            if (TextKey.Normalize(last).StartsWith("TOTAL", StringComparison.Ordinal) || matricule.StartsWith("TOTAL", StringComparison.Ordinal)) continue;
            if (matricule.StartsWith("MATRICULE", StringComparison.Ordinal)) continue;
            target.Add(new LegacyStudent(matricule, last, middle, first, TextKey.Normalize(Cell(row, map.Gender)), DateCell(row, map.Birth),
                NullIfEmpty(Cell(row, map.Place)), Promotion.NormalizeCode(Cell(row, map.Promotion)), NullIfEmpty(Cell(row, map.Guardian)),
                NullIfEmpty(Cell(row, map.Phone1)), NullIfEmpty(Cell(row, map.Phone2)), DateCell(row, map.Enrolled), Cell(row, map.Status),
                NullIfEmpty(Cell(row, map.Address)), source, row.RowNumber()));
        }
    }

    /// <summary>« KALONJI MBOMBO Ruth Grâce » : nom, post-nom, puis prénoms.</summary>
    private static (string Last, string Middle, string First) SplitName(string full)
    {
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (string.Empty, string.Empty, string.Empty),
            1 => (parts[0], string.Empty, string.Empty),
            2 => (parts[0], string.Empty, parts[1]),
            _ => (parts[0], parts[1], string.Join(' ', parts.Skip(2))),
        };
    }

    // ---------- Aucun apprenant écarté : codes, matricules et compte rendu ----------
    private const string Unclassified = "ACLS";

    private static string Who(LegacyStudent s)
    {
        var name = string.Join(' ', new[] { s.Last, s.Middle, s.First }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return name.Length == 0 ? "apprenant sans nom" : name;
    }

    private static bool SameIdentity(LegacyStudent a, LegacyStudent b) =>
        TextKey.Normalize($"{a.Last} {a.Middle} {a.First}") == TextKey.Normalize($"{b.Last} {b.Middle} {b.First}");

    private static string NextMatricule(string promotion, HashSet<string> taken)
    {
        var given = MatriculeGenerator.Next(promotion, taken);
        taken.Add(given);
        return given;
    }

    /// <summary>Codes de promotion de 4 caractères : un code mal formé du classeur reçoit un code proche et unique.</summary>
    private sealed class PromotionCodes
    {
        private readonly Dictionary<string, string> map = new(StringComparer.Ordinal);

        public PromotionCodes(IEnumerable<string> raws, List<string> warnings)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var all = raws.Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            foreach (var raw in all.Where(Valid)) { map[raw] = raw; used.Add(raw); }
            foreach (var raw in all.Where(r => !Valid(r)))
            {
                var clean = new string(raw.Where(char.IsAsciiLetterOrDigit).ToArray());
                var letters = new string(clean.Where(char.IsAsciiLetter).Take(3).ToArray());
                var digit = clean.FirstOrDefault(char.IsAsciiDigit);
                var candidate = clean.Length >= 4
                    ? (letters.Length > 0 && digit != default ? (letters.PadRight(3, 'X') + digit) : clean[..4])
                    : clean.PadRight(4, '0');
                const string Tail = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                for (var i = 0; used.Contains(candidate) && i < Tail.Length; i++) candidate = candidate[..3] + Tail[i];
                map[raw] = candidate;
                used.Add(candidate);
                warnings.Add($"Promotion « {raw} » : le code doit compter 4 caractères, elle prend le code {candidate}.");
            }
        }

        private static bool Valid(string code) => code.Length == 4 && code.All(char.IsAsciiLetterOrDigit);
        public string For(string raw) => raw.Length == 0 ? raw : map.GetValueOrDefault(raw, raw);
    }

    private sealed class ImportStats(int read)
    {
        public int Read { get; } = read;
        public int Given, Renumbered, Merged, Present, Unclassified;

        public string Summary(int created)
        {
            var parts = new List<string> { $"{Read} apprenant(s) lu(s) dans le classeur", $"{created} à créer" };
            if (Present > 0) parts.Add($"{Present} déjà présent(s) dans SCOLIA");
            if (Merged > 0) parts.Add($"{Merged} ligne(s) en double fusionnée(s)");
            if (Given > 0) parts.Add($"{Given} matricule(s) attribué(s)");
            if (Renumbered > 0) parts.Add($"{Renumbered} matricule(s) renuméroté(s)");
            if (Unclassified > 0) parts.Add($"{Unclassified} dans la promotion « À classer »");
            return "Bilan : " + string.Join(" · ", parts) + ".";
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
