using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Délibération, fusion d'ITMCD dans SCOLIA : codes anonymes officiels attribués aux apprenants déjà inscrits,
/// cotes par semestre et moyennes d'appui, tableau du jury, décisions motivées, clôture avec résultats figés,
/// réouverture tracée et documents officiels (procès-verbal, grille, palmarès, bulletin).
/// Tout se fait sur l'année scolaire courante : les années ne se mélangent jamais.
/// </summary>
public sealed class DeliberationService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user, IPasswordService passwords, IDocumentService documents)
{
    private const int MinReasonLength = 5;
    private const int MaxReasonLength = 500;

    // ---------- Réglages ----------

    public async Task<DeliberationSettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        return ToDto(await SettingsOfAsync(year.Id, ct));
    }

    public Task<DeliberationSettingsDto> UpdateSettingsAsync(DeliberationSettingsDto input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        if (input.PassPercent is < 1 or > 100 || decimal.Round(input.PassPercent, 2) != input.PassPercent)
            throw new DomainException("passPercent", "Le seuil de réussite est un pourcentage de 1 à 100.");
        var settings = await db.DeliberationSettings.FirstOrDefaultAsync(s => s.AcademicYearId == year.Id, ct);
        if (settings is null)
        {
            settings = new DeliberationSettings { AcademicYearId = year.Id };
            db.DeliberationSettings.Add(settings);
        }
        // Une décision officielle clôturée doit rester conforme au règlement appliqué lors du jury.
        // Les autorisations d'encodage et de téléchargement restent ajustables sans altérer le calcul.
        if ((settings.PassPercent != input.PassPercent || settings.SupportEnabled != input.SupportEnabled)
            && await db.DeliberationSessions.AsNoTracking().AnyAsync(
                x => x.AcademicYearId == year.Id && x.Status == SessionStatus.Cloturee, ct))
            throw new ConflictException("Une délibération est déjà clôturée pour cette année : le seuil de réussite et les règles d'appui ne peuvent plus être modifiés. Réouvrez d'abord les sessions concernées, avec un motif.");
        settings.EncodingS1Open = input.EncodingS1Open;
        settings.EncodingS2Open = input.EncodingS2Open;
        settings.SupportEnabled = input.SupportEnabled;
        settings.PassPercent = input.PassPercent;
        settings.TeacherUpdates = input.TeacherUpdates;
        settings.TeacherDownloads = input.TeacherDownloads;
        audit.Record("deliberation.reglages", "ReglagesDeliberation", settings.Id.ToString(),
            $"Délibération {year.Label} : encodage S1 {(input.EncodingS1Open ? "ouvert" : "fermé")}, S2 {(input.EncodingS2Open ? "ouvert" : "fermé")}, "
            + $"moyennes d'appui {(input.SupportEnabled ? "actives" : "désactivées")}, mise à jour des cotes par les enseignants "
            + $"{(input.TeacherUpdates ? "autorisée" : "fermée")}, téléchargements {(input.TeacherDownloads ? "autorisés" : "fermés")}, seuil {input.PassPercent:0.##} %.", input);
        await db.SaveChangesAsync(ct);
        return ToDto(settings);
    }, ct);

    // ---------- Autorités (projet Délibération) ----------

    public const int MaxJuryMembers = 4;

    public async Task<AuthoritiesDto> AuthoritiesAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        return Authorities(await SettingsOfAsync(year.Id, ct));
    }

    /// <summary>Préfet, proviseur, président, secrétaire et membres du jury (quatre au plus) : ils signent les documents officiels.</summary>
    public Task<AuthoritiesDto> UpdateAuthoritiesAsync(AuthoritiesDto input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        static string? Name(string? value, string field, string label)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Length > 120) throw new DomainException(field, $"{label} : 120 caractères au plus.");
            return text;
        }
        var members = (input.JuryMembers ?? []).Select(m => m?.Trim() ?? string.Empty).Where(m => m.Length > 0).ToList();
        if (members.Count > MaxJuryMembers) throw new DomainException("juryMembers", $"Le jury compte au plus {MaxJuryMembers} membres en plus du président et du secrétaire.");
        if (members.Any(m => m.Length > 120)) throw new DomainException("juryMembers", "Nom d'un membre du jury : 120 caractères au plus.");
        var settings = await db.DeliberationSettings.FirstOrDefaultAsync(x => x.AcademicYearId == year.Id, ct);
        if (settings is null)
        {
            settings = new DeliberationSettings { AcademicYearId = year.Id };
            db.DeliberationSettings.Add(settings);
        }
        settings.Prefect = Name(input.Prefect, "prefect", "Préfet");
        settings.Principal = Name(input.Principal, "principal", "Proviseur");
        settings.JuryPresident = Name(input.JuryPresident, "juryPresident", "Président du jury");
        settings.JurySecretary = Name(input.JurySecretary, "jurySecretary", "Secrétaire du jury");
        settings.JuryMembers = members.Count == 0 ? null : string.Join('\n', members);
        var result = Authorities(settings);
        audit.Record("deliberation.autorites", "ReglagesDeliberation", settings.Id.ToString(),
            $"Autorités {year.Label} : préfet {result.Prefect ?? "—"}, proviseur {result.Principal ?? "—"}, président du jury {result.JuryPresident ?? "—"}, "
            + $"secrétaire {result.JurySecretary ?? "—"}, {members.Count} membre(s).", result);
        await db.SaveChangesAsync(ct);
        return result;
    }, ct);

    private static AuthoritiesDto Authorities(DeliberationSettings s) => new(s.Prefect, s.Principal, s.JuryPresident, s.JurySecretary,
        (s.JuryMembers ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Signatures(AuthoritiesDto a) =>
        $"Le secrétaire du jury : {a.JurySecretary ?? "……………………"} · Le président du jury : {a.JuryPresident ?? "……………………"}"
        + (a.JuryMembers is { Count: > 0 } m ? $" · Membres du jury : {string.Join(", ", m)}" : " · Les membres du jury");

    // ---------- Épreuves du jury (projet Délibération) ----------

    /// <summary>
    /// Crée dans la promotion les épreuves du jury standard du projet Délibération (stage, situation d'intégration, dossier,
    /// épreuve d'intégration ; blocs prévention, diagnostic et gestion), avec leur maxima. Une épreuve déjà présente
    /// (même nom) n'est pas recréée ; toutes restent modifiables dans « Cours et notes ».
    /// </summary>
    public Task<JuryEvaluationsResult> CreateJuryEvaluationsAsync(Guid promotionId, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId && p.AcademicYearId == year.Id, ct)
            ?? throw new NotFoundException("Promotion introuvable dans l'année courante.");
        var existing = await db.Courses.Where(c => c.PromotionId == promotionId && c.AcademicYearId == year.Id).ToListAsync(ct);
        var names = existing.Select(c => TextKey.Normalize(c.Name)).ToHashSet(StringComparer.Ordinal);
        var numbers = existing.Select(c => c.Number).ToHashSet();
        var next = 100;
        var created = new List<string>();
        foreach (var (name, max, coefficient, semester) in DeliberationEngine.StandardJuryEvaluations)
        {
            if (names.Contains(TextKey.Normalize(name))) continue;
            while (numbers.Contains(next)) next++;
            numbers.Add(next);
            db.Courses.Add(new Course
            {
                AcademicYearId = year.Id, PromotionId = promotionId, Number = next, Name = name, Coefficient = coefficient, Hours = 1, MaxScore = max, Semester = semester,
            });
            created.Add($"{next}. {name}");
        }
        if (created.Count > 0)
        {
            audit.Record("deliberation.epreuves.creees", "Promotion", promotionId.ToString(),
                $"Épreuves du jury créées pour {promotion.Code} : {string.Join(", ", created)}.", created);
            await db.SaveChangesAsync(ct);
        }
        return new JuryEvaluationsResult(created.Count, DeliberationEngine.StandardJuryEvaluations.Count - created.Count);
    }, ct);

    private static DeliberationSettingsDto ToDto(DeliberationSettings s) =>
        new(s.EncodingS1Open, s.EncodingS2Open, s.SupportEnabled, s.PassPercent, s.TeacherUpdates, s.TeacherDownloads);

    /// <summary>« Mise à jour des cotes » fermée : un enseignant n'ajoute que des cotes nouvelles ; le secrétariat et le jury corrigent.</summary>
    private async Task EnsureTeacherMayReplaceAsync(Guid yearId, CancellationToken ct)
    {
        if (!TeacherOnly) return;
        if (!(await SettingsOfAsync(yearId, ct)).TeacherUpdates)
            throw new ConflictException("La mise à jour des cotes déjà enregistrées est fermée par le secrétariat : demandez-lui la correction.");
    }

    private async Task<DeliberationSettings> SettingsOfAsync(Guid yearId, CancellationToken ct) =>
        await db.DeliberationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.AcademicYearId == yearId, ct) ?? new DeliberationSettings { AcademicYearId = yearId };

    // ---------- Codes anonymes ----------

    /// <summary>Apprenants inscrits de l'année (ou d'une promotion) avec leur code anonyme actif.</summary>
    public async Task<IReadOnlyList<AnonymousCodeRowDto>> CodesAsync(Guid? promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var students = await EnrolledAsync(year.Id, promotionId, ct);
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var codes = await db.AnonymousCodes.AsNoTracking().Where(c => c.AcademicYearId == year.Id && c.IsActive && ids.Contains(c.EnrollmentId))
            .ToDictionaryAsync(c => c.EnrollmentId, ct);
        return students.Select(s =>
        {
            var code = codes.GetValueOrDefault(s.EnrollmentId);
            return new AnonymousCodeRowDto(s.EnrollmentId, s.Matricule, s.FullName, s.PromotionCode, code?.Code, code?.IsManual ?? false, code?.UpdatedAtUtc ?? code?.CreatedAtUtc);
        }).OrderBy(r => r.PromotionCode).ThenBy(r => r.FullName).ToList();
    }

    /// <summary>
    /// Importe les apprenants déjà inscrits dans SCOLIA et attribue un code officiel à chacun de ceux qui n'en ont pas.
    /// Les codes existants ne changent pas : l'opération peut être relancée sans risque après de nouvelles inscriptions.
    /// </summary>
    public Task<CodeAssignmentResult> AssignCodesAsync(CodeAssignmentRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var students = await EnrolledAsync(year.Id, input.PromotionId, ct);
        var taken = (await db.AnonymousCodes.Where(c => c.AcademicYearId == year.Id).Select(c => c.Code).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var coded = (await db.AnonymousCodes.Where(c => c.AcademicYearId == year.Id && c.IsActive).Select(c => c.EnrollmentId).ToListAsync(ct)).ToHashSet();
        var assigned = 0;
        foreach (var s in students.Where(s => !coded.Contains(s.EnrollmentId)))
        {
            var code = AnonymousCodeRules.Generate(taken);
            taken.Add(code);
            db.AnonymousCodes.Add(new AnonymousCode { AcademicYearId = year.Id, EnrollmentId = s.EnrollmentId, Code = code });
            assigned++;
        }
        var scope = input.PromotionId is null ? $"toutes les promotions de {year.Label}" : $"la promotion {students.FirstOrDefault()?.PromotionCode ?? "choisie"}";
        audit.Record("deliberation.codes.attribues", "CodeAnonyme", input.PromotionId?.ToString(),
            $"{assigned} code(s) anonyme(s) attribué(s) pour {scope} ; {students.Count - assigned} apprenant(s) avaient déjà un code.", new { assigned, total = students.Count });
        await db.SaveChangesAsync(ct);
        return new CodeAssignmentResult(assigned, students.Count - assigned, students.Count);
    }, ct);

    /// <summary>
    /// Saisit le code officiel d'un apprenant (par exemple son code ITMCD) ou en tire un nouveau. Remplacer un code actif
    /// exige le droit du jury et un motif ; l'ancien code reste réservé dans l'année et dans l'historique.
    /// </summary>
    public Task<AnonymousCodeRowDto> SetCodeAsync(Guid enrollmentId, CodeChangeRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var student = await EnrolledOneAsync(year.Id, enrollmentId, ct)
            ?? throw new NotFoundException("Cet apprenant n'est pas inscrit (actif) dans l'année courante.");
        var all = await db.AnonymousCodes.Where(c => c.AcademicYearId == year.Id).ToListAsync(ct);
        var current = all.FirstOrDefault(c => c.EnrollmentId == enrollmentId && c.IsActive);
        var manual = !string.IsNullOrWhiteSpace(input.Code);
        var code = manual ? AnonymousCodeRules.Official(input.Code) : AnonymousCodeRules.Generate(all.Select(c => c.Code).ToHashSet(StringComparer.Ordinal));
        if (current?.Code == code) return Row(student, current);
        if (all.Any(c => c.Code == code))
            throw new ConflictException($"Le code {code} est déjà utilisé cette année (ou l'a été) : un code n'est jamais réattribué.");
        string? reason = null;
        if (current is not null)
        {
            if (!user.HasPermission(Permissions.DeliberationManage))
                throw new DomainException("droits", "Seul le jury peut remplacer un code anonyme déjà attribué.");
            reason = Reason(input.Reason, "Indiquez le motif du remplacement du code (5 caractères au moins).");
            current.IsActive = false;
            current.ReplacedReason = reason;
            current.ReplacedAtUtc = DateTime.UtcNow;
            // Le code actif doit disparaître de l'index avant l'ajout du nouveau.
            audit.Record("deliberation.code.retire", "CodeAnonyme", enrollmentId.ToString(), $"Code anonyme {current.Code} de {student.Matricule} retiré. Motif : {reason}.");
            await db.SaveChangesAsync(ct);
        }
        var created = new AnonymousCode { AcademicYearId = year.Id, EnrollmentId = enrollmentId, Code = code, IsManual = manual };
        db.AnonymousCodes.Add(created);
        audit.Record(current is null ? "deliberation.code.attribue" : "deliberation.code.remplace", "CodeAnonyme", enrollmentId.ToString(),
            current is null
                ? $"Code anonyme {code} {(manual ? "officiel saisi" : "tiré")} pour {student.Matricule}."
                : $"Code anonyme de {student.Matricule} remplacé : {current.Code} → {code}. Motif : {reason}.",
            new { previous = current?.Code, code, manual, reason });
        await db.SaveChangesAsync(ct);
        return Row(student, created);
    }, ct);

    /// <summary>
    /// Reprend les codes officiels déjà portés par les apprenants (export d'ITMCD, liste Excel) : chaque ligne est rattachée
    /// à une inscription de l'année par le matricule SCOLIA, ou par le nom complet (avec la promotion en cas d'homonymes).
    /// L'aperçu ne change rien ; l'application n'attribue que les lignes « À attribuer » : un code déjà différent se remplace
    /// à la main, avec un motif, et un code déjà utilisé dans l'année n'est jamais réattribué.
    /// </summary>
    public Task<CodeImportResult> ImportCodesAsync(CodeImportRequest input, CancellationToken ct = default) =>
        // L'aperçu ne retient pas la file d'écriture des autres postes : seule l'application y passe.
        input.Apply ? gate.RunAsync(() => ImportCodesCoreAsync(input, ct), ct) : ImportCodesCoreAsync(input, ct);

    private async Task<CodeImportResult> ImportCodesCoreAsync(CodeImportRequest input, CancellationToken ct)
    {
        var year = input.Apply ? await OpenYearAsync(ct) : await YearContext.CurrentAsync(db, ct);
        var parsed = CodeImportParser.Parse(input.Text);
        if (parsed.Count == 0) throw new DomainException("import", "La liste est vide : une ligne par apprenant, « matricule ou nom ; code ».");
        var students = await EnrolledAsync(year.Id, input.PromotionId, ct);
        var byMatricule = students.GroupBy(s => TextKey.Normalize(s.Matricule)).ToDictionary(g => g.Key, g => g.ToList());
        var byName = students.GroupBy(s => CodeImportParser.NameKey(s.FullName)).ToDictionary(g => g.Key, g => g.ToList());
        var all = await db.AnonymousCodes.AsNoTracking().Where(c => c.AcademicYearId == year.Id).ToListAsync(ct);
        var active = all.Where(c => c.IsActive).ToDictionary(c => c.EnrollmentId, c => c.Code);
        var owners = all.GroupBy(c => c.Code, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().EnrollmentId, StringComparer.Ordinal);
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var seenStudents = new HashSet<Guid>();
        var lines = new List<CodeImportLineDto>();
        var toAssign = new List<(Guid EnrollmentId, string Code)>();
        foreach (var line in parsed)
        {
            var code = AnonymousCodeRules.Normalize(line.Code);
            CodeImportLineDto Result(string status, EnrolledStudent? s = null) =>
                new(line.Number, line.Identifier, code, status, s?.EnrollmentId, s?.Matricule, s?.FullName, s?.PromotionCode);
            if (!AnonymousCodeRules.IsValid(code)) { lines.Add(Result("Code invalide")); continue; }
            var candidates = byMatricule.GetValueOrDefault(TextKey.Normalize(line.Identifier)) ?? byName.GetValueOrDefault(CodeImportParser.NameKey(line.Identifier)) ?? [];
            if (candidates.Count > 1 && line.Promotion is { } promo)
                candidates = candidates.Where(c => TextKey.Normalize(c.PromotionCode) == TextKey.Normalize(promo)).ToList();
            if (candidates.Count == 0) { lines.Add(Result("Introuvable")); continue; }
            if (candidates.Count > 1) { lines.Add(Result("Ambigu")); continue; }
            var student = candidates[0];
            if (!seenCodes.Add(code) || !seenStudents.Add(student.EnrollmentId)) { lines.Add(Result("Doublon dans la liste", student)); continue; }
            var current = active.GetValueOrDefault(student.EnrollmentId);
            if (current == code) { lines.Add(Result("Déjà attribué", student)); continue; }
            // Les anciens codes restent définitivement réservés, même s'ils appartenaient à cet apprenant.
            if (owners.ContainsKey(code)) { lines.Add(Result("Code pris", student)); continue; }
            if (current is not null) { lines.Add(Result("Code différent", student)); continue; }
            lines.Add(Result("À attribuer", student));
            toAssign.Add((student.EnrollmentId, code));
        }
        if (input.Apply && toAssign.Count > 0)
        {
            foreach (var (enrollmentId, code) in toAssign)
                db.AnonymousCodes.Add(new AnonymousCode { AcademicYearId = year.Id, EnrollmentId = enrollmentId, Code = code, IsManual = true });
            audit.Record("deliberation.codes.importes", "CodeAnonyme", input.PromotionId?.ToString(),
                $"{toAssign.Count} code(s) officiel(s) importé(s) pour {year.Label} ; {lines.Count - toAssign.Count} ligne(s) laissée(s) de côté.",
                lines.Where(l => l.Status != "À attribuer").Select(l => new { l.Line, l.Identifier, l.Code, l.Status }));
            await db.SaveChangesAsync(ct);
        }
        var unchanged = lines.Count(l => l.Status == "Déjà attribué");
        return new CodeImportResult(input.Apply, input.Apply ? toAssign.Count : 0, toAssign.Count, unchanged, lines.Count - toAssign.Count - unchanged, lines);
    }

    /// <summary>Résout un code anonyme de l'année : la promotion seulement, jamais l'identité.</summary>
    public async Task<CodeLookupDto> LookupAsync(string code, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var (_, student) = await ResolveCodeAsync(year.Id, code, ct);
        return new CodeLookupDto(AnonymousCodeRules.Normalize(code), student.EnrollmentId, student.PromotionId, student.PromotionCode);
    }

    public async Task<TableDocument> CodesTableAsync(Guid? promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var rows = await CodesAsync(promotionId, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        return new TableDocument(PaymentService.Header(school), "Codes anonymes — document confidentiel",
            $"Année {year.Label} · {rows.Count(r => r.Code is not null)} code(s) attribué(s) sur {rows.Count} · à conserver par le jury",
            ["Promotion", "Matricule", "Nom complet", "Code anonyme"],
            rows.Select(r => (IReadOnlyList<string>)[r.PromotionCode, r.Matricule, r.FullName, r.Code ?? "—"]).ToList());
    }

    private static AnonymousCodeRowDto Row(EnrolledStudent s, AnonymousCode c) =>
        new(s.EnrollmentId, s.Matricule, s.FullName, s.PromotionCode, c.Code, c.IsManual, c.UpdatedAtUtc ?? c.CreatedAtUtc);

    private async Task<(AnonymousCode Code, EnrolledStudent Student)> ResolveCodeAsync(Guid yearId, string? value, CancellationToken ct)
    {
        var code = AnonymousCodeRules.Normalize(value);
        var found = await db.AnonymousCodes.AsNoTracking().FirstOrDefaultAsync(c => c.AcademicYearId == yearId && c.Code == code, ct)
            ?? throw new NotFoundException($"Code anonyme inconnu : {code}.");
        if (!found.IsActive) throw new DomainException("code", $"Le code {code} a été remplacé : utilisez le code actuel de l'apprenant.");
        var student = await EnrolledOneAsync(yearId, found.EnrollmentId, ct)
            ?? throw new DomainException("code", $"Le code {code} appartient à un apprenant qui n'est plus inscrit.");
        return (found, student);
    }

    // ---------- Cours et encodage ----------

    public async Task<IReadOnlyList<DeliberationCourseDto>> CoursesAsync(Guid promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        return await CoursesCoreAsync(year.Id, promotionId, ct);
    }

    private async Task<IReadOnlyList<DeliberationCourseDto>> CoursesCoreAsync(Guid yearId, Guid promotionId, CancellationToken ct)
    {
        var courses = await CoursesOfAsync(yearId, promotionId, ct);
        var students = await EnrolledAsync(yearId, promotionId, ct);
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var courseIds = courses.Select(c => c.Id).ToList();
        var counts = await db.CourseMarks.AsNoTracking().Where(m => courseIds.Contains(m.CourseId) && ids.Contains(m.EnrollmentId))
            .GroupBy(m => new { m.CourseId, m.Kind }).Select(g => new { g.Key.CourseId, g.Key.Kind, Count = g.Count() }).ToListAsync(ct);
        var closed = await ClosedPeriodsAsync(promotionId, ct);
        var teachers = await TeacherNamesAsync(courseIds, ct);
        var submitted = (await db.MarkSubmissions.AsNoTracking().Where(m => courseIds.Contains(m.CourseId)).Select(m => new { m.CourseId, m.Kind }).ToListAsync(ct))
            .Select(m => (m.CourseId, m.Kind)).ToHashSet();
        int Count(Guid id, MarkKind kind) => counts.FirstOrDefault(c => c.CourseId == id && c.Kind == kind)?.Count ?? 0;
        return courses.Select(c => new DeliberationCourseDto(c.Id, c.Number, c.Name, DeliberationText.Semester(c.Semester), c.MaxScore, teachers.GetValueOrDefault(c.Id),
            students.Count, Count(c.Id, MarkKind.CoteS1), Count(c.Id, MarkKind.CoteS2), Count(c.Id, MarkKind.Appui),
            IsLocked(closed, DeliberationEngine.IsJury(c.Semester) ? MarkKind.Jury : MarkKind.CoteS1, c.Semester),
            IsLocked(closed, DeliberationEngine.IsJury(c.Semester) ? MarkKind.Jury : MarkKind.CoteS2, c.Semester),
            submitted.Contains((c.Id, MarkKind.CoteS1)) || submitted.Contains((c.Id, MarkKind.Jury)), submitted.Contains((c.Id, MarkKind.CoteS2)),
            Count(c.Id, MarkKind.Jury))).ToList();
    }

    /// <summary>Feuille de cotes d'un cours. En mode anonyme, la feuille est triée par code et ne montre ni nom ni matricule.</summary>
    public async Task<MarkSheetDto> SheetAsync(Guid courseId, string kindText, bool anonymous, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var kind = DeliberationText.Kind(kindText);
        var course = await CourseInYearAsync(courseId, year.Id, ct);
        await EnsureCourseAccessAsync(course, write: false, ct);
        // L'enseignant corrige des copies anonymes : sa feuille ne montre jamais les noms (portail enseignant d'ITMCD).
        anonymous |= TeacherOnly;
        if (!DeliberationEngine.Accepts(course.Semester, kind)) DeliberationEngine.ValidateMark(kind, course.Semester, 0, course.MaxScore);
        var settings = await SettingsOfAsync(year.Id, ct);
        var students = await EnrolledAsync(year.Id, course.PromotionId, ct);
        var codes = await ActiveCodesAsync(year.Id, ct);
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var marks = (await db.CourseMarks.AsNoTracking().Where(m => m.CourseId == courseId && ids.Contains(m.EnrollmentId)).ToListAsync(ct))
            .ToDictionary(m => (m.EnrollmentId, m.Kind), m => m.Value);
        var closed = await ClosedPeriodsAsync(course.PromotionId, ct);
        var period = kind == MarkKind.CoteS2 ? DeliberationPeriod.S2 : kind == MarkKind.CoteS1 ? DeliberationPeriod.S1
            : course.Semester == CourseSemester.S2 ? DeliberationPeriod.S2 : course.Semester == CourseSemester.S1 ? DeliberationPeriod.S1 : DeliberationPeriod.Annuel;
        if (kind == MarkKind.Jury) period = DeliberationPeriod.Annuel;
        var rows = students.Select(s =>
        {
            decimal? Mark(MarkKind k) => marks.TryGetValue((s.EnrollmentId, k), out var v) ? v : null;
            var result = DeliberationEngine.Course(new DeliberationEngine.CourseInput(course.Id, course.Semester, course.MaxScore,
                Mark(MarkKind.CoteS1), Mark(MarkKind.CoteS2), Mark(MarkKind.Appui), Mark(MarkKind.Jury)), period, settings.SupportEnabled);
            // La correspondance code ↔ nom est confidentielle : elle n'est montrée qu'au jury.
            var code = anonymous || user.HasPermission(Permissions.DeliberationManage) ? codes.GetValueOrDefault(s.EnrollmentId) : null;
            return new MarkRowDto(s.EnrollmentId, code, anonymous ? null : s.Matricule, anonymous ? null : s.FullName, Mark(kind),
                result.Base, result.Graded ? result.Points : null, result.Failed);
        });
        var list = (anonymous ? rows.OrderBy(r => r.Code ?? "~", StringComparer.Ordinal) : rows).ToList();
        var submission = await db.MarkSubmissions.AsNoTracking().FirstOrDefaultAsync(m => m.CourseId == course.Id && m.Kind == kind, ct);
        var retained = list.Where(r => r.Retained is not null).Select(r => r.Retained!.Value).ToList();
        return new MarkSheetDto(course.Id, $"{course.Number}. {course.Name}", DeliberationText.Semester(course.Semester), course.MaxScore, DeliberationText.Kind(kind),
            anonymous, IsLocked(closed, kind, course.Semester), EncodingOpen(settings, kind), list, submission?.SubmittedAtUtc, submission?.SubmittedByName,
            list.Count(r => r.Failed), retained.Count == 0 ? null : Math.Round(retained.Average(), 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Enregistre la feuille d'un cours ; une valeur vide efface la cote. Chaque changement garde l'ancienne valeur au journal.</summary>
    public Task<int> SaveSheetAsync(Guid courseId, string kindText, IReadOnlyList<MarkInput> input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var kind = DeliberationText.Kind(kindText);
        var course = await CourseInYearAsync(courseId, year.Id, ct);
        await EnsureCourseAccessAsync(course, write: true, ct);
        var submitted = await EnsureWritableAsync(year.Id, course, kind, ct);
        var allowed = (await EnrolledAsync(year.Id, course.PromotionId, ct)).Select(s => s.EnrollmentId).ToHashSet();
        if (input.Select(i => i.EnrollmentId).Distinct().Count() != input.Count) throw new DomainException("cotes", "Un apprenant apparaît deux fois dans la feuille.");
        var v = new ValidationBuilder();
        foreach (var i in input)
        {
            v.Require(allowed.Contains(i.EnrollmentId), "cotes", "Un apprenant n'est pas inscrit (actif) dans la promotion de ce cours.");
            if (i.Value is { } value)
                try { DeliberationEngine.ValidateMark(kind, course.Semester, value, course.MaxScore); }
                catch (DomainException ex) { v.Add("cotes", ex.Message); }
        }
        v.ThrowIfInvalid("Certaines cotes sont incorrectes.");
        var ids = input.Select(i => i.EnrollmentId).ToList();
        var existing = await db.CourseMarks.Where(m => m.CourseId == courseId && m.Kind == kind && ids.Contains(m.EnrollmentId)).ToDictionaryAsync(m => m.EnrollmentId, ct);
        if (input.Any(i => existing.TryGetValue(i.EnrollmentId, out var m) && m.Value != i.Value)) await EnsureTeacherMayReplaceAsync(year.Id, ct);
        var changes = new List<object>();
        foreach (var i in input)
        {
            var mark = existing.GetValueOrDefault(i.EnrollmentId);
            if (mark?.Value == i.Value || (mark is null && i.Value is null)) continue;
            changes.Add(new { inscription = i.EnrollmentId, avant = mark?.Value, apres = i.Value });
            if (i.Value is null) db.CourseMarks.Remove(mark!);
            else if (mark is null) db.CourseMarks.Add(new CourseMark { EnrollmentId = i.EnrollmentId, CourseId = courseId, Kind = kind, Value = i.Value.Value });
            else { mark.Value = i.Value.Value; mark.EnteredAnonymously = false; }
        }
        if (changes.Count == 0) return 0;
        audit.Record("deliberation.cotes.saisies", "Cours", courseId.ToString(),
            $"{changes.Count} cote(s) {DeliberationText.Kind(kind)} enregistrée(s) pour le cours {course.Number} « {course.Name} »"
            + (submitted is null ? "." : " — correction du jury après la remise."), changes);
        await db.SaveChangesAsync(ct);
        return changes.Count;
    }, ct);

    /// <summary>
    /// Reprise d'une liste de cotes pour un cours (export d'ITMCD, feuille Excel) : chaque ligne est rattachée par le code
    /// anonyme officiel (ou, pour le secrétariat, par le matricule). Aperçu d'abord ; l'application suit exactement les
    /// règles de la feuille (droits, encodage ouvert, période non clôturée, remise, 0 à maxima) et garde chaque ancienne valeur au journal.
    /// </summary>
    public Task<MarkImportResult> ImportMarksAsync(MarkImportRequest input, CancellationToken ct = default) =>
        // L'aperçu ne retient pas la file d'écriture des autres postes : seule l'application y passe.
        input.Apply ? gate.RunAsync(() => ImportMarksCoreAsync(input, ct), ct) : ImportMarksCoreAsync(input, ct);

    private async Task<MarkImportResult> ImportMarksCoreAsync(MarkImportRequest input, CancellationToken ct)
    {
        var year = input.Apply ? await OpenYearAsync(ct) : await YearContext.CurrentAsync(db, ct);
        var kind = DeliberationText.Kind(input.Kind);
        var course = await CourseInYearAsync(input.CourseId, year.Id, ct);
        await EnsureCourseAccessAsync(course, write: true, ct);
        var submitted = await EnsureWritableAsync(year.Id, course, kind, ct);
        var parsed = MarkImportParser.Parse(input.Text);
        if (parsed.Count == 0) throw new DomainException("import", "La liste est vide : une ligne par copie, « code anonyme ; cote ».");
        var students = (await EnrolledAsync(year.Id, course.PromotionId, ct)).ToDictionary(x => x.EnrollmentId);
        var codes = await db.AnonymousCodes.AsNoTracking().Where(c => c.AcademicYearId == year.Id && c.IsActive)
            .ToDictionaryAsync(c => c.Code, c => c.EnrollmentId, StringComparer.Ordinal, ct);
        // Un enseignant corrige des copies anonymes : il identifie par code seulement, jamais par matricule.
        var byMatricule = TeacherOnly ? new Dictionary<string, Guid>(StringComparer.Ordinal)
            : students.Values.ToDictionary(x => TextKey.Normalize(x.Matricule), x => x.EnrollmentId, StringComparer.Ordinal);
        var existing = await db.CourseMarks.Where(m => m.CourseId == course.Id && m.Kind == kind).ToDictionaryAsync(m => m.EnrollmentId, ct);
        var mayReplace = !TeacherOnly || (await SettingsOfAsync(year.Id, ct)).TeacherUpdates;
        var seen = new HashSet<Guid>();
        var lines = new List<MarkImportLineDto>();
        var ready = new List<(Guid EnrollmentId, decimal Value, bool ByCode)>();
        foreach (var line in parsed)
        {
            var code = AnonymousCodeRules.Normalize(line.Identifier);
            Guid? enrollment = codes.TryGetValue(code, out var byCode) ? byCode
                : byMatricule.TryGetValue(TextKey.Normalize(line.Identifier), out var byMat) ? byMat : null;
            var previous = enrollment is { } e && existing.TryGetValue(e, out var mark) ? mark.Value : (decimal?)null;
            MarkImportLineDto Result(string status) => new(line.Number, line.Identifier, line.RawValue, status, previous);
            if (enrollment is not { } id) { lines.Add(Result(TeacherOnly ? "Code inconnu" : "Code ou matricule inconnu")); continue; }
            if (!students.ContainsKey(id)) { lines.Add(Result("Hors promotion")); continue; }
            if (line.Value is not { } value) { lines.Add(Result("Cote invalide")); continue; }
            try { DeliberationEngine.ValidateMark(kind, course.Semester, value, course.MaxScore); }
            catch (DomainException) { lines.Add(Result("Cote invalide")); continue; }
            if (!seen.Add(id)) { lines.Add(Result("Doublon dans la liste")); continue; }
            if (previous == value) { lines.Add(Result("Inchangée")); continue; }
            if (previous is not null && !mayReplace) { lines.Add(Result("Déjà encodée (mise à jour fermée)")); continue; }
            lines.Add(Result(previous is null ? "À enregistrer" : "Remplace"));
            ready.Add((id, value, codes.ContainsKey(code)));
        }
        if (input.Apply && ready.Count > 0)
        {
            var changes = new List<object>();
            foreach (var (id, value, byCodeEntry) in ready)
            {
                var mark = existing.GetValueOrDefault(id);
                changes.Add(new { inscription = id, avant = mark?.Value, apres = value });
                if (mark is null) db.CourseMarks.Add(new CourseMark { EnrollmentId = id, CourseId = course.Id, Kind = kind, Value = value, EnteredAnonymously = byCodeEntry });
                else { mark.Value = value; mark.EnteredAnonymously = byCodeEntry; }
            }
            audit.Record("deliberation.cotes.importees", "Cours", course.Id.ToString(),
                $"{ready.Count} cote(s) {DeliberationText.Kind(kind)} reprise(s) d'une liste pour le cours {course.Number} « {course.Name} »"
                + (submitted is null ? "." : " — correction du jury après la remise."), changes);
            await db.SaveChangesAsync(ct);
        }
        var unchanged = lines.Count(l => l.Status == "Inchangée");
        return new MarkImportResult(input.Apply, input.Apply ? ready.Count : 0, ready.Count, unchanged, lines.Count - ready.Count - unchanged, lines);
    }

    /// <summary>Encodage anonyme : la copie porte le code, l'encodeur tape le code et la cote sans connaître le nom.</summary>
    public Task<MarkByCodeResult> SaveByCodeAsync(MarkByCodeRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var kind = DeliberationText.Kind(input.Kind);
        var course = await CourseInYearAsync(input.CourseId, year.Id, ct);
        await EnsureCourseAccessAsync(course, write: true, ct);
        var submitted = await EnsureWritableAsync(year.Id, course, kind, ct);
        var (code, student) = await ResolveCodeAsync(year.Id, input.Code, ct);
        if (student.PromotionId != course.PromotionId)
            throw new DomainException("code", $"Le code {code.Code} n'appartient pas à la promotion de ce cours : vérifiez la copie.");
        var value = DeliberationEngine.ValidateMark(kind, course.Semester, input.Value, course.MaxScore);
        var mark = await db.CourseMarks.FirstOrDefaultAsync(m => m.EnrollmentId == student.EnrollmentId && m.CourseId == course.Id && m.Kind == kind, ct);
        var previous = mark?.Value;
        if (previous is { } old && old != value) await EnsureTeacherMayReplaceAsync(year.Id, ct);
        if (mark is null) db.CourseMarks.Add(new CourseMark { EnrollmentId = student.EnrollmentId, CourseId = course.Id, Kind = kind, Value = value, EnteredAnonymously = true });
        else { mark.Value = value; mark.EnteredAnonymously = true; }
        audit.Record("deliberation.cote.anonyme", "Cours", course.Id.ToString(),
            $"Cote {DeliberationText.Kind(kind)} du code {code.Code} pour le cours {course.Number} « {course.Name} » : {value:0.##}"
            + (previous is { } p ? $" (avant {p:0.##})" : string.Empty) + (submitted is null ? "." : " — correction du jury après la remise."),
            new { code = code.Code, avant = previous, apres = value });
        await db.SaveChangesAsync(ct);
        // Copies restantes : deux comptages en base, sans recharger les dossiers de la promotion à chaque copie.
        var enrolled = db.Enrollments.AsNoTracking().Where(e => e.AcademicYearId == year.Id && e.PromotionId == course.PromotionId
            && (e.Status == EnrollmentStatus.Actif || e.Status == EnrollmentStatus.Suspendu)).Select(e => e.Id);
        var total = await enrolled.CountAsync(ct);
        var graded = await db.CourseMarks.CountAsync(m => m.CourseId == course.Id && m.Kind == kind && enrolled.Contains(m.EnrollmentId), ct);
        return new MarkByCodeResult(code.Code, $"{course.Number}. {course.Name}", DeliberationText.Kind(kind), value, previous, total - graded);
    }, ct);

    /// <summary>
    /// Contrôles avant d'écrire une cote : cours actif, nature acceptée, encodage ouvert, période non clôturée, feuille non remise
    /// (seul le jury corrige une feuille remise). Rend la remise éventuelle, pour la mentionner au journal.
    /// </summary>
    private async Task<MarkSubmission?> EnsureWritableAsync(Guid yearId, Course course, MarkKind kind, CancellationToken ct)
    {
        if (!course.IsActive) throw new DomainException("cours", "Ce cours est archivé : réactivez-le pour encoder ses cotes.");
        if (!DeliberationEngine.Accepts(course.Semester, kind)) DeliberationEngine.ValidateMark(kind, course.Semester, 0, course.MaxScore);
        var settings = await SettingsOfAsync(yearId, ct);
        if (!EncodingOpen(settings, kind))
            throw new DomainException("encodage", kind == MarkKind.Appui
                ? "Les moyennes d'appui sont désactivées dans les réglages de la délibération."
                : $"L'encodage du {(kind == MarkKind.CoteS1 ? "premier" : "second")} semestre est fermé dans les réglages de la délibération.");
        var closed = await ClosedPeriodsAsync(course.PromotionId, ct);
        if (IsLocked(closed, kind, course.Semester))
            throw new ConflictException("La délibération concernée est clôturée : ces cotes sont figées. Le jury doit rouvrir la session, avec un motif, pour les corriger.");
        var submission = await db.MarkSubmissions.AsNoTracking().FirstOrDefaultAsync(m => m.CourseId == course.Id && m.Kind == kind, ct);
        if (submission is not null && !user.HasPermission(Permissions.DeliberationManage))
            throw new ConflictException($"Les cotes {DeliberationText.Kind(kind)} de ce cours ont été remises le {When(submission.SubmittedAtUtc)} par {submission.SubmittedByName} : "
                + "seul le jury peut les corriger ou renvoyer la feuille.");
        return submission;
    }

    private static string When(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy 'à' HH:mm", Fr);

    private static bool EncodingOpen(DeliberationSettings s, MarkKind kind) => kind switch
    {
        MarkKind.CoteS1 => s.EncodingS1Open,
        MarkKind.CoteS2 => s.EncodingS2Open,
        MarkKind.Jury => true,
        _ => s.SupportEnabled,
    };

    private static bool IsLocked(IReadOnlyCollection<DeliberationPeriod> closed, MarkKind kind, CourseSemester semester) =>
        closed.Any(p => DeliberationEngine.Locks(p, kind, semester));

    private async Task<List<DeliberationPeriod>> ClosedPeriodsAsync(Guid promotionId, CancellationToken ct) =>
        await db.DeliberationSessions.AsNoTracking().Where(s => s.PromotionId == promotionId && s.Status == SessionStatus.Cloturee).Select(s => s.Period).ToListAsync(ct);

    // ---------- Remise des cotes et suivi de l'encodage (repris d'ITMCD) ----------

    /// <summary>
    /// Remise officielle d'une feuille de cotes (le « dépôt » d'ITMCD) : la feuille doit être complète (sauf l'appui, facultatif).
    /// Après la remise, l'enseignant ne modifie plus la feuille ; le jury peut la corriger ou la renvoyer avec un motif.
    /// </summary>
    public Task<MarkSubmissionDto> SubmitAsync(Guid courseId, string kindText, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var kind = DeliberationText.Kind(kindText);
        var course = await CourseInYearAsync(courseId, year.Id, ct);
        await EnsureCourseAccessAsync(course, write: true, ct);
        var existing = await EnsureWritableAsync(year.Id, course, kind, ct);
        if (existing is not null)
            throw new ConflictException($"Ces cotes ont déjà été remises le {When(existing.SubmittedAtUtc)} par {existing.SubmittedByName}.");
        var ids = (await EnrolledAsync(year.Id, course.PromotionId, ct)).Select(s => s.EnrollmentId).ToList();
        if (ids.Count == 0) throw new DomainException("remise", "Aucun apprenant inscrit dans la promotion de ce cours : il n'y a rien à remettre.");
        var graded = await db.CourseMarks.CountAsync(m => m.CourseId == courseId && m.Kind == kind && ids.Contains(m.EnrollmentId), ct);
        if (kind != MarkKind.Appui && graded < ids.Count)
            throw new DomainException("remise", $"Feuille incomplète : {ids.Count - graded} cote(s) manquante(s) sur {ids.Count}. Encodez toutes les cotes avant de les remettre.");
        var name = user.Name.Length > 120 ? user.Name[..120] : user.Name;
        var submission = new MarkSubmission { AcademicYearId = year.Id, CourseId = courseId, Kind = kind, SubmittedAtUtc = DateTime.UtcNow, SubmittedByName = name };
        db.MarkSubmissions.Add(submission);
        audit.Record("deliberation.cotes.remises", "RemiseCotes", courseId.ToString(),
            $"Cotes {DeliberationText.Kind(kind)} du cours {course.Number} « {course.Name} » remises : {graded} cote(s) sur {ids.Count} apprenant(s).",
            new { nature = DeliberationText.Kind(kind), graded, total = ids.Count });
        await db.SaveChangesAsync(ct);
        return new MarkSubmissionDto(courseId, $"{course.Number}. {course.Name}", DeliberationText.Kind(kind), submission.SubmittedAtUtc, name, graded, ids.Count);
    }, ct);

    /// <summary>Renvoi d'une feuille remise à l'enseignant, par le jury, avec un motif : la feuille redevient modifiable.</summary>
    public Task ReturnAsync(Guid courseId, string kindText, MarkReturnRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var kind = DeliberationText.Kind(kindText);
        var course = await CourseInYearAsync(courseId, year.Id, ct);
        var submission = await db.MarkSubmissions.FirstOrDefaultAsync(m => m.CourseId == courseId && m.Kind == kind, ct)
            ?? throw new NotFoundException("Ces cotes n'ont pas été remises : il n'y a rien à renvoyer.");
        if (IsLocked(await ClosedPeriodsAsync(course.PromotionId, ct), kind, course.Semester))
            throw new ConflictException("La délibération concernée est clôturée : rouvrez-la d'abord, avec un motif.");
        var reason = Reason(input.Reason, "Indiquez le motif du renvoi de la feuille (5 caractères au moins).");
        db.MarkSubmissions.Remove(submission);
        audit.Record("deliberation.cotes.renvoyees", "RemiseCotes", courseId.ToString(),
            $"Feuille {DeliberationText.Kind(kind)} du cours {course.Number} « {course.Name} » renvoyée à l'enseignant (remise du {When(submission.SubmittedAtUtc)} par {submission.SubmittedByName}). Motif : {reason}.",
            new { nature = DeliberationText.Kind(kind), reason });
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    /// <summary>
    /// Suivi de l'encodage (moniteur d'ITMCD), sur les inscriptions de l'année seulement : une ligne par cours et par cote
    /// attendue, avec l'enseignant, l'avancement, les échecs, la moyenne, la remise et l'état de la délibération.
    /// Période vide ou jury annuel : S1 et S2 ; « S1 » ou « S2 » : ce semestre seulement.
    /// </summary>
    public async Task<EncodingProgressDto> ProgressAsync(Guid? promotionId, string? periodText, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        DeliberationPeriod? period = string.IsNullOrWhiteSpace(periodText) ? null : DeliberationText.Period(periodText);
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.AcademicYearId == year.Id && (promotionId == null || p.Id == promotionId))
            .Select(p => new { p.Id, p.Code, p.Name }).ToListAsync(ct);
        var promotionIds = promotions.Select(p => p.Id).ToList();
        var courses = await db.Courses.AsNoTracking().Where(c => c.AcademicYearId == year.Id && c.IsActive && promotionIds.Contains(c.PromotionId)).ToListAsync(ct);
        var courseIds = courses.Select(c => c.Id).ToList();
        var enrolled = (await EnrolledAsync(year.Id, promotionId, ct)).GroupBy(s => s.PromotionId).ToDictionary(g => g.Key, g => g.Select(s => s.EnrollmentId).ToHashSet());
        var marks = (await db.CourseMarks.AsNoTracking().Where(m => courseIds.Contains(m.CourseId))
                .Select(m => new { m.CourseId, m.EnrollmentId, m.Kind, m.Value }).ToListAsync(ct))
            .ToDictionary(m => (m.CourseId, m.EnrollmentId, m.Kind), m => m.Value);
        var submissions = await db.MarkSubmissions.AsNoTracking().Where(m => courseIds.Contains(m.CourseId)).ToDictionaryAsync(m => (m.CourseId, m.Kind), ct);
        var closed = await db.DeliberationSessions.AsNoTracking().Where(x => promotionIds.Contains(x.PromotionId) && x.Status == SessionStatus.Cloturee)
            .Select(x => new { x.PromotionId, x.Period }).ToListAsync(ct);
        var settings = await SettingsOfAsync(year.Id, ct);
        var teachers = await TeacherNamesAsync(courseIds, ct);

        var kinds = period switch
        {
            DeliberationPeriod.S1 => new[] { MarkKind.CoteS1 },
            DeliberationPeriod.S2 => new[] { MarkKind.CoteS2 },
            _ => new[] { MarkKind.CoteS1, MarkKind.CoteS2, MarkKind.Jury },
        };
        var rows = new List<EncodingProgressRowDto>();
        foreach (var promotion in promotions.OrderBy(p => p.Code, StringComparer.CurrentCulture))
        {
            var students = enrolled.GetValueOrDefault(promotion.Id) ?? [];
            foreach (var course in courses.Where(c => c.PromotionId == promotion.Id).OrderBy(c => c.Number))
            foreach (var kind in kinds.Where(k => DeliberationEngine.Accepts(course.Semester, k)))
            {
                var semester = kind switch { MarkKind.CoteS1 => DeliberationPeriod.S1, MarkKind.CoteS2 => DeliberationPeriod.S2, _ => DeliberationPeriod.Annuel };
                decimal? Mark(Guid enrollment, MarkKind k) => marks.TryGetValue((course.Id, enrollment, k), out var v) ? v : null;
                var results = students.Select(e => DeliberationEngine.Course(new DeliberationEngine.CourseInput(course.Id, course.Semester, course.MaxScore,
                    Mark(e, MarkKind.CoteS1), Mark(e, MarkKind.CoteS2), Mark(e, MarkKind.Appui), Mark(e, MarkKind.Jury)), semester, settings.SupportEnabled)).Where(r => r.Graded).ToList();
                var submission = submissions.GetValueOrDefault((course.Id, kind));
                var locked = closed.Any(x => x.PromotionId == promotion.Id && DeliberationEngine.Locks(x.Period, kind, course.Semester));
                var graded = results.Count;
                var status = locked ? "Figé" : submission is not null ? "Remis" : students.Count > 0 && graded == students.Count ? "Complet" : graded == 0 ? "À encoder" : "En cours";
                rows.Add(new EncodingProgressRowDto(course.Id, promotion.Id, $"{promotion.Code} — {promotion.Name}", course.Number, course.Name,
                    DeliberationText.Semester(course.Semester), DeliberationText.Kind(kind), teachers.GetValueOrDefault(course.Id), students.Count, graded,
                    students.Count == 0 ? 0 : Math.Round(graded * 100m / students.Count, 1, MidpointRounding.AwayFromZero), results.Count(r => r.Failed),
                    graded == 0 ? null : Math.Round(results.Average(r => r.Points), 2, MidpointRounding.AwayFromZero), submission?.SubmittedAtUtc, submission?.SubmittedByName, locked, status));
            }
        }
        var expected = rows.Sum(r => r.Students);
        var done = rows.Sum(r => r.Graded);
        return new EncodingProgressDto(year.Label, period is { } chosen ? DeliberationEngine.PeriodLabel(chosen) : "Toute l'année", rows.Count,
            rows.Count(r => r.Students > 0 && r.Graded == r.Students), rows.Count(r => r.SubmittedAtUtc is not null), rows.Count(r => r.Locked), expected, done,
            expected == 0 ? 0 : Math.Round(done * 100m / expected, 1, MidpointRounding.AwayFromZero), rows);
    }

    public async Task<TableDocument> ProgressTableAsync(Guid? promotionId, string? periodText, CancellationToken ct = default)
    {
        var progress = await ProgressAsync(promotionId, periodText, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        return new TableDocument(PaymentService.Header(school), "Suivi de l'encodage des cotes",
            $"Année {progress.AcademicYear} · {progress.PeriodLabel} · {progress.GradedMarks} cote(s) sur {progress.ExpectedMarks} ({progress.Percent:0.#} %) · "
            + $"{progress.Submitted} feuille(s) remise(s) sur {progress.Sheets}",
            ["Promotion", "N°", "Cours", "Cote", "Enseignant", "Encodées", "%", "Échecs", "Moyenne", "État"],
            progress.Rows.Select(r => (IReadOnlyList<string>)[r.Promotion, r.Number.ToString(Fr), r.Course, r.Kind, r.TeacherName ?? "Non attribué",
                $"{r.Graded} / {r.Students}", $"{r.Percent:0.#}", r.Failed.ToString(Fr), r.Average is { } a ? $"{a:0.##}" : "—",
                r.SubmittedAtUtc is { } at ? $"{r.Status} le {When(at)}" : r.Status]).ToList(), Landscape: true);
    }

    /// <summary>
    /// Relevé des cotes d'un cours (l'export d'ITMCD), ou seulement les échecs : cote saisie, cote retenue avec l'appui et observation.
    /// Les codes anonymes n'y figurent que pour le jury.
    /// </summary>
    public async Task<TableDocument> SheetTableAsync(Guid courseId, string kindText, bool failedOnly, CancellationToken ct = default)
    {
        if (TeacherOnly && !(await SettingsOfAsync((await YearContext.CurrentAsync(db, ct)).Id, ct)).TeacherDownloads)
            throw new ForbiddenException("Les téléchargements des relevés sont fermés par le secrétariat.");
        var sheet = await SheetAsync(courseId, kindText, anonymous: false, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var teacher = (await TeacherNamesAsync([courseId], ct)).GetValueOrDefault(courseId);
        var withCodes = user.HasPermission(Permissions.DeliberationManage) || sheet.Anonymous;
        var withNames = !sheet.Anonymous;
        var rows = sheet.Rows.Where(r => !failedOnly || r.Failed).ToList();
        var columns = new List<string>();
        if (withCodes) columns.Add("Code");
        if (withNames) columns.AddRange(["Matricule", "Nom complet"]);
        columns.AddRange([sheet.Kind == "Appui" ? "Appui /10" : $"Cote /{sheet.MaxScore:0.##}", "Retenue", "Observation"]);
        return new TableDocument(PaymentService.Header(school), failedOnly ? "Liste des échecs" : "Relevé des cotes",
            $"{sheet.Course} · {(sheet.Kind == "Appui" ? "moyenne d'appui" : $"cote du {sheet.Kind}")} · enseignant : {teacher ?? "non attribué"} · "
            + $"{sheet.Failed} échec(s) sur {sheet.Rows.Count}" + (sheet.SubmittedAtUtc is { } at ? $" · remis le {When(at)}" : " · non remis"),
            columns,
            rows.Select(r =>
            {
                var cells = new List<string>();
                if (withCodes) cells.Add(r.Code ?? "—");
                if (withNames) cells.AddRange([r.Matricule ?? string.Empty, r.FullName ?? string.Empty]);
                cells.AddRange([r.Value is { } v ? $"{v:0.##}" : "—",
                    r.Retained is { } t ? $"{t:0.##}" : "INC", r.Retained is null ? "Cote manquante" : r.Failed ? "Échec" : "Réussi"]);
                return (IReadOnlyList<string>)cells;
            }).ToList());
    }

    // ---------- Portail enseignant (repris d'ITMCD) ----------

    /// <summary>Compte enseignant : il encode ses cours sans accès au reste de la scolarité.</summary>
    private bool TeacherOnly => !user.HasPermission(Permissions.AcademicsRead)
        && (user.HasPermission(Permissions.MarksEncode) || user.HasPermission(Permissions.JuryEncode));

    /// <summary>Fiche du personnel reliée au compte connecté, pour un compte enseignant.</summary>
    private async Task<Guid> MyStaffIdAsync(CancellationToken ct)
    {
        var id = user.Id ?? throw new DomainException("session", "Session expirée : reconnectez-vous.");
        var account = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.StaffMemberId, u.MustChangePassword }).FirstOrDefaultAsync(ct);
        // Le code provisoire est connu du secrétariat : tant qu'il n'est pas remplacé, personne ne peut encoder au nom de l'enseignant.
        if (account is { MustChangePassword: true })
            throw new ForbiddenException("Votre code d'accès est provisoire : choisissez votre propre code PIN ou mot de passe (Compte › Sécurité du compte) avant d'ouvrir vos cours.");
        return account?.StaffMemberId
            ?? throw new ForbiddenException("Ce compte enseignant n'est relié à aucune fiche du personnel : demandez au secrétariat de le compléter.");
    }

    /// <summary>
    /// Le secrétariat lit (scolarite.lire) et encode (scolarite.ecrire) tous les cours ; un enseignant (cotes.encoder)
    /// seulement les cours qui lui sont attribués aujourd'hui, comme dans le portail enseignant d'ITMCD.
    /// </summary>
    private async Task EnsureCourseAccessAsync(Course course, bool write, CancellationToken ct)
    {
        // Compte enseignant (y compris membre du jury) : jamais avec le code provisoire remis par le secrétariat.
        if (TeacherOnly) await MyStaffIdAsync(ct);
        // Les épreuves du jury (projet Délibération) sont cotées par le jury : le secrétariat (deliberation.gerer) ou un
        // enseignant qui a reçu le privilège jury (jury.encoder). Le privilège ne rend pas titulaire de l'épreuve.
        if (DeliberationEngine.IsJury(course.Semester))
        {
            if (user.HasPermission(Permissions.DeliberationManage) || user.HasPermission(Permissions.JuryEncode)) return;
            if (write) throw new ForbiddenException($"« {course.Name} » est une épreuve du jury : seuls les membres du jury en encodent les cotes.");
        }
        if (user.HasPermission(write ? Permissions.AcademicsWrite : Permissions.AcademicsRead)) return;
        if (!user.HasPermission(Permissions.MarksEncode)) throw new ForbiddenException("Ce compte n'a pas accès à l'encodage des cotes.");
        var staffId = await MyStaffIdAsync(ct);
        if (!await db.CourseAssignments.AsNoTracking().AnyAsync(a => a.CourseId == course.Id && a.TeacherId == staffId && a.EndedOn == null, ct))
            throw new ForbiddenException($"Le cours {course.Number} « {course.Name} » ne vous est pas attribué : vous ne pouvez pas en encoder les cotes.");
    }

    /// <summary>
    /// Annonces de la délibération (notifications d'ITMCD), tirées du journal d'audit : ouverture ou fermeture de l'encodage,
    /// remises et renvois. Un enseignant ne voit que les remises et renvois de ses cours ; le secrétariat voit tout.
    /// </summary>
    public async Task<IReadOnlyList<AnnouncementDto>> AnnouncementsAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-45);
        string[] actions = ["deliberation.reglages", "deliberation.cotes.remises", "deliberation.cotes.renvoyees"];
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AtUtc >= since && actions.Contains(a.Action))
            .OrderByDescending(a => a.Id).Take(200)
            .Select(a => new { a.AtUtc, a.Action, a.EntityId, a.Summary, a.UserName }).ToListAsync(ct);
        HashSet<string>? mine = null;
        if (!user.HasPermission(Permissions.AcademicsRead))
        {
            if (!user.HasPermission(Permissions.MarksEncode) && !user.HasPermission(Permissions.JuryEncode))
                throw new ForbiddenException("Ce compte n'a pas accès aux annonces de la délibération.");
            mine = (await MyCourseIdsAsync(ct)).Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        }
        return entries
            .Where(e => e.Action == "deliberation.reglages" || mine is null || (e.EntityId is not null && mine.Contains(e.EntityId)))
            .Take(20)
            .Select(e => new AnnouncementDto(e.AtUtc, e.Action switch
            {
                "deliberation.reglages" => "Encodage des cotes",
                "deliberation.cotes.remises" => "Cotes remises",
                _ => "Feuille renvoyée",
            }, e.Summary, e.UserName)).ToList();
    }

    /// <summary>
    /// Cours attribués à l'enseignant connecté pour l'année courante, avec l'avancement et la remise de chaque feuille ;
    /// pour un membre du jury, en plus, les épreuves du jury de toutes les promotions.
    /// </summary>
    /// <summary>Identifiants des cours de l'enseignant connecté (attribués, et épreuves du jury pour un membre du jury), sans calcul d'avancement.</summary>
    private async Task<List<Guid>> MyCourseIdsAsync(CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var staffId = await MyStaffIdAsync(ct);
        var jury = user.HasPermission(Permissions.JuryEncode);
        var assigned = db.CourseAssignments.AsNoTracking().Where(a => a.TeacherId == staffId && a.EndedOn == null).Select(a => a.CourseId);
        return await db.Courses.AsNoTracking()
            .Where(c => c.AcademicYearId == year.Id && c.IsActive
                && (assigned.Contains(c.Id) || (jury && (c.Semester == CourseSemester.JuryEpreuve || c.Semester == CourseSemester.JuryBloc))))
            .Select(c => c.Id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TeacherCourseDto>> MyCoursesAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var staffId = await MyStaffIdAsync(ct);
        var mine = await (from a in db.CourseAssignments.AsNoTracking()
                          join c in db.Courses.AsNoTracking() on a.CourseId equals c.Id
                          join p in db.Promotions.AsNoTracking() on c.PromotionId equals p.Id
                          where a.TeacherId == staffId && a.EndedOn == null && c.AcademicYearId == year.Id && c.IsActive
                          select new { c.Id, c.PromotionId, p.Code, p.Name }).ToListAsync(ct);
        if (user.HasPermission(Permissions.JuryEncode))
        {
            var jury = await (from c in db.Courses.AsNoTracking()
                              join p in db.Promotions.AsNoTracking() on c.PromotionId equals p.Id
                              where c.AcademicYearId == year.Id && c.IsActive
                                  && (c.Semester == CourseSemester.JuryEpreuve || c.Semester == CourseSemester.JuryBloc)
                              select new { c.Id, c.PromotionId, p.Code, p.Name }).ToListAsync(ct);
            mine = mine.Concat(jury).DistinctBy(m => m.Id).ToList();
        }
        var result = new List<TeacherCourseDto>();
        foreach (var promotion in mine.GroupBy(m => new { m.PromotionId, m.Code, m.Name }).OrderBy(g => g.Key.Code, StringComparer.CurrentCulture))
        {
            var ids = promotion.Select(m => m.Id).ToHashSet();
            foreach (var course in (await CoursesCoreAsync(year.Id, promotion.Key.PromotionId, ct)).Where(c => ids.Contains(c.Id)))
                result.Add(new TeacherCourseDto(promotion.Key.PromotionId, $"{promotion.Key.Code} — {promotion.Key.Name}", course));
        }
        return result;
    }

    // ---------- Délibération ----------

    public async Task<DeliberationBoardDto> BoardAsync(Guid promotionId, string periodText, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var period = DeliberationText.Period(periodText);
        var computation = await ComputeAsync(year, promotionId, period, closing: false, ct);
        return computation.Board;
    }

    public async Task<StudentDeliberationDto> StudentAsync(Guid promotionId, string periodText, Guid enrollmentId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var period = DeliberationText.Period(periodText);
        var computation = await ComputeAsync(year, promotionId, period, closing: false, ct);
        var row = computation.Board.Rows.FirstOrDefault(r => r.EnrollmentId == enrollmentId) ?? throw new NotFoundException("Apprenant absent de cette délibération.");
        var details = computation.Details.GetValueOrDefault(enrollmentId) ?? [];
        return new StudentDeliberationDto(row, computation.Board.Promotion, computation.Board.PeriodLabel, details);
    }

    /// <summary>Décision motivée du jury, pendant la session ouverte. La cote de l'enseignant n'est jamais modifiée.</summary>
    public Task<DeliberationRowDto> DecideAsync(JuryDecisionRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var period = DeliberationText.Period(input.Period);
        var session = await SessionAsync(year.Id, input.PromotionId, period, create: true, ct);
        if (session.IsClosed) throw new ConflictException("Cette délibération est clôturée : rouvrez-la, avec un motif, pour changer une décision.");
        if (!(await EnrolledAsync(year.Id, input.PromotionId, ct)).Any(s => s.EnrollmentId == input.EnrollmentId))
            throw new NotFoundException("Cet apprenant n'est pas inscrit (actif) dans cette promotion.");
        var existing = await db.JuryDecisions.FirstOrDefaultAsync(d => d.SessionId == session.Id && d.EnrollmentId == input.EnrollmentId, ct);
        if (string.IsNullOrWhiteSpace(input.Outcome))
        {
            if (existing is not null)
            {
                db.JuryDecisions.Remove(existing);
                audit.Record("deliberation.decision.retiree", "SessionDeliberation", session.Id.ToString(),
                    $"Décision du jury retirée ({DeliberationEngine.PeriodLabel(period)}) : le résultat calculé s'applique.", new { input.EnrollmentId, avant = existing.Outcome.ToString() });
            }
        }
        else
        {
            var outcome = DeliberationText.Outcome(input.Outcome);
            if (outcome == Outcome.Incomplet) throw new DomainException("decision", "Le jury décide « Admis » ou « Ajourné ».");
            var reason = Reason(input.Reason, "Une décision du jury doit être motivée (5 caractères au moins).");
            if (existing is null)
            {
                existing = new JuryDecision { SessionId = session.Id, EnrollmentId = input.EnrollmentId };
                db.JuryDecisions.Add(existing);
            }
            var previous = existing.Outcome;
            existing.Outcome = outcome;
            existing.Reason = reason;
            audit.Record("deliberation.decision", "SessionDeliberation", session.Id.ToString(),
                $"Décision du jury ({DeliberationEngine.PeriodLabel(period)}) : {DeliberationEngine.Label(outcome)}. Motif : {reason}.",
                new { input.EnrollmentId, avant = previous.ToString(), apres = outcome.ToString(), reason });
        }
        await db.SaveChangesAsync(ct);
        var board = (await ComputeAsync(year, input.PromotionId, period, closing: false, ct)).Board;
        return board.Rows.First(r => r.EnrollmentId == input.EnrollmentId);
    }, ct);

    /// <summary>Clôture : mot de passe du compte, président du jury ; les résultats sont figés et les cotes de la période verrouillées.</summary>
    public Task<DeliberationBoardDto> CloseAsync(SessionCloseRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var period = DeliberationText.Period(input.Period);
        await VerifyPasswordAsync(input.Password, ct);
        // Par défaut, le jury enregistré dans les autorités de l'année (projet Délibération).
        var authorities = Authorities(await SettingsOfAsync(year.Id, ct));
        var president = (string.IsNullOrWhiteSpace(input.President) ? authorities.JuryPresident : input.President)?.Trim() ?? string.Empty;
        var members = string.IsNullOrWhiteSpace(input.Members) && authorities.JuryMembers is { Count: > 0 } known
            ? string.Join(", ", new[] { authorities.JurySecretary is { } sec ? $"{sec} (secrétaire)" : null }.Concat(known).OfType<string>())
            : input.Members;
        if (president.Length is < 3 or > 120) throw new DomainException("president", "Indiquez le président du jury (3 à 120 caractères).");
        if (members?.Length > 1000 || input.Notes?.Length > 2000) throw new DomainException("membres", "Liste des membres ou observations trop longue.");
        var session = await SessionAsync(year.Id, input.PromotionId, period, create: true, ct);
        if (session.IsClosed) throw new ConflictException("Cette délibération est déjà clôturée.");
        await db.SaveChangesAsync(ct);

        var computation = await ComputeAsync(year, input.PromotionId, period, closing: true, ct);
        if (computation.Board.Rows.Count == 0) throw new DomainException("deliberation", "Aucun apprenant inscrit dans cette promotion : rien à délibérer.");
        if (computation.Board.Courses.Count == 0) throw new DomainException("deliberation", "Aucun cours actif pour cette période : ajoutez les cours avant de clôturer la délibération.");
        foreach (var r in computation.Board.Rows)
            db.DeliberationResults.Add(new DeliberationResult
            {
                SessionId = session.Id, EnrollmentId = r.EnrollmentId, Code = r.Code ?? string.Empty, Matricule = r.Matricule, StudentName = r.FullName,
                Points = r.Points, MaxPoints = r.Max, Percent = r.Percent, Missing = r.Missing, FailedCourses = r.Failed,
                Computed = computation.Computed[r.EnrollmentId], Final = computation.Finals[r.EnrollmentId], Mention = r.Mention, Rank = r.Rank, Reason = r.JuryReason,
                VerificationCode = Domain.Finance.DocumentNumbers.NewVerificationCode(12),
            });
        session.Status = SessionStatus.Cloturee;
        session.President = president;
        session.Members = string.IsNullOrWhiteSpace(members) ? null : members.Trim();
        session.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        session.ClosedAtUtc = DateTime.UtcNow;
        session.ClosedByName = user.Name;
        var admitted = computation.Finals.Values.Count(o => o == Outcome.Admis);
        audit.Record("deliberation.cloturee", "SessionDeliberation", session.Id.ToString(),
            $"Délibération clôturée : {computation.Board.Promotion}, {DeliberationEngine.PeriodLabel(period)}, {admitted} admis sur {computation.Board.Rows.Count}. Président : {president}.",
            computation.Board.Rows.Select(r => new { r.EnrollmentId, r.Code, r.Percent, decision = computation.Finals[r.EnrollmentId].ToString() }));
        await db.SaveChangesAsync(ct);
        return (await ComputeAsync(year, input.PromotionId, period, closing: false, ct)).Board;
    }, ct);

    /// <summary>Réouverture par le jury : mot de passe et motif obligatoires ; les résultats figés sont retirés, l'historique reste au journal.</summary>
    public Task<DeliberationBoardDto> ReopenAsync(SessionReopenRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var period = DeliberationText.Period(input.Period);
        await VerifyPasswordAsync(input.Password, ct);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 10 or > MaxReasonLength) throw new DomainException("motif", "La réouverture exige un motif de 10 à 500 caractères.");
        var session = await SessionAsync(year.Id, input.PromotionId, period, create: false, ct);
        if (!session.IsClosed) throw new ConflictException("Cette délibération n'est pas clôturée.");
        await db.DeliberationResults.Where(r => r.SessionId == session.Id).ExecuteDeleteAsync(ct);
        session.Status = SessionStatus.Ouverte;
        session.ReopenCount++;
        session.LastReopenReason = reason;
        audit.Record("deliberation.rouverte", "SessionDeliberation", session.Id.ToString(),
            $"Délibération rouverte ({DeliberationEngine.PeriodLabel(period)}), réouverture n° {session.ReopenCount}. Motif : {reason}.", new { reason });
        await db.SaveChangesAsync(ct);
        return (await ComputeAsync(year, input.PromotionId, period, closing: false, ct)).Board;
    }, ct);

    // ---------- Documents ----------

    /// <summary>Procès-verbal : jury, statistiques, décisions motivées et statistiques par cours. « Provisoire » tant que la session est ouverte.</summary>
    public async Task<byte[]> MinutesPdfAsync(Guid promotionId, string periodText, CancellationToken ct = default)
    {
        var board = await BoardAsync(promotionId, periodText, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var closed = board.Status == "Clôturée";
        var figures = new List<(string, string)>
        {
            ("Effectif", board.Students.ToString(Fr)), ("Admis", board.Admitted.ToString(Fr)), ("Ajournés", board.Failed.ToString(Fr)),
            ("Incomplets", board.Incomplete.ToString(Fr)), ("Taux de réussite", $"{board.SuccessRate:0.##} %"),
        };
        var jury = new ReportSection("Jury",
            ["Rubrique", "Information"],
            [
                ["État", closed ? $"Clôturée le {board.ClosedAtUtc!.Value.ToLocalTime():dd/MM/yyyy à HH:mm} par {board.ClosedBy}" : "Session ouverte — document provisoire"],
                ["Président du jury", board.President ?? "—"],
                ["Membres", board.Members ?? "—"],
                ["Seuil de réussite", $"{board.PassPercent:0.##} % et aucune cote manquante"],
                ["Réouvertures", board.ReopenCount == 0 ? "Aucune" : $"{board.ReopenCount} (dernier motif : {board.LastReopenReason})"],
            ]);
        var decisions = new ReportSection("Décisions",
            // Le procès-verbal circule : il ne porte jamais les codes anonymes, qui restent au jury.
            ["Rang", "Matricule", "Nom complet", "Points", "%", "Mention", "Décision", "Motif du jury"],
            board.Rows.Select(r => (IReadOnlyList<string>)[r.Rank?.ToString(Fr) ?? "—", r.Matricule, r.FullName,
                $"{r.Points:0.##} / {r.Max:0.##}", $"{r.Percent:0.##}", r.Mention, r.Grade is { } g ? $"{r.Decision} ({g})" : r.Decision, r.JuryReason ?? string.Empty]).ToList(),
            Empty: "Aucun apprenant.");
        var courses = new ReportSection("Statistiques par cours",
            ["N°", "Cours", "Semestre", "Maxima", "Réussites", "Échecs", "Cotes manquantes", "Moyenne"],
            board.Courses.Select(c => (IReadOnlyList<string>)[c.Number.ToString(Fr), c.Course, c.Semester, $"{c.Max:0.##}", c.Passed.ToString(Fr),
                c.Failed.ToString(Fr), c.Missing.ToString(Fr), c.Average is { } a ? $"{a:0.##}" : "—"]).ToList(),
            Empty: "Aucun cours pour cette période.");
        return documents.Report(new ReportDocument(PaymentService.Header(school), "Procès-verbal de délibération",
            $"{board.Promotion} · {board.PeriodLabel} · année {board.AcademicYear}{(closed ? string.Empty : " · PROVISOIRE")}",
            figures, [jury, decisions, courses],
            Signatures(await AuthoritiesAsync(ct))));
    }

    /// <summary>
    /// Un classeur pour toutes les promotions réellement configurées dans l'année courante :
    /// les codes et cotes viennent du même calcul que la grille officielle, jamais d'un modèle ancien.
    /// Aucun code n'est attribué silencieusement pendant un GET : le jury doit utiliser
    /// « Attribuer les codes » avant d'exporter si un apprenant n'a pas encore de code.
    /// </summary>
    public async Task<IReadOnlyList<(string SheetName, TableDocument Document)>> GridsAsync(
        string periodText, CancellationToken ct = default)
    {
        _ = DeliberationText.Period(periodText);
        var year = await YearContext.CurrentAsync(db, ct);
        var promotions = await db.Promotions.AsNoTracking()
            .Where(p => p.AcademicYearId == year.Id)
            .OrderBy(p => p.Code)
            .Select(p => new { p.Id, p.Code })
            .ToListAsync(ct);
        if (promotions.Count == 0)
            throw new DomainException("promotions", "Aucune promotion configurée dans l'année scolaire courante.");

        var grids = new List<(string SheetName, TableDocument Document)>(promotions.Count);
        foreach (var promotion in promotions)
        {
            var grid = await GridAsync(promotion.Id, periodText, detailedHeaders: true, ct: ct);
            if (grid.Rows.Any(row => row.Count < 2 || string.IsNullOrWhiteSpace(row[1]) || row[1] == "—"))
                throw new ConflictException($"Codes anonymes incomplets pour {promotion.Code} : attribuez les codes avant l'export de toutes les promotions.");
            grids.Add((promotion.Code, grid));
        }
        return grids;
    }

    /// <summary>Grille des cotes : une colonne par cours (points retenus), total, pourcentage et décision.</summary>
    public Task<TableDocument> GridAsync(Guid promotionId, string periodText, CancellationToken ct = default) =>
        GridAsync(promotionId, periodText, detailedHeaders: false, ct: ct);

    private async Task<TableDocument> GridAsync(Guid promotionId, string periodText, bool detailedHeaders, CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var period = DeliberationText.Period(periodText);
        var computation = await ComputeAsync(year, promotionId, period, closing: false, ct);
        var board = computation.Board;
        var school = await YearContext.SchoolAsync(db, ct);
        var courses = board.Courses;
        var official = board.Rows.Any(r => r.Totals is not null);
        // Grille officielle du projet Délibération : épreuves → TOTAL 1, cours → SOUS-TOTAL 1, blocs → SOUS-TOTAL 2, TOTAL 2, TOTAL GÉNÉRAL.
        var columns = new List<string> { "Rang", "Code", "Nom complet" };
        var blocks = new List<(string Kind, CourseStatDto? Course)>();
        if (official)
        {
            foreach (var c in courses.Where(c => c.Semester == nameof(CourseSemester.JuryEpreuve))) blocks.Add(("cours", c));
            blocks.Add(("TOTAL 1", null));
            foreach (var c in courses.Where(c => !c.Semester.StartsWith("Jury", StringComparison.Ordinal))) blocks.Add(("cours", c));
            blocks.Add(("SOUS-TOTAL 1", null));
            foreach (var c in courses.Where(c => c.Semester == nameof(CourseSemester.JuryBloc))) blocks.Add(("cours", c));
            blocks.Add(("SOUS-TOTAL 2", null));
            blocks.Add(("TOTAL 2", null));
            blocks.Add(("TOTAL GÉNÉRAL", null));
        }
        else blocks.AddRange(courses.Select(c => ("cours", (CourseStatDto?)c)));
        // La grille officielle compte beaucoup de colonnes : en-têtes courts (numéro du cours, sigles des totaux) et légende en sous-titre.
        string Header((string Kind, CourseStatDto? Course) b, JuryTotalsDto? max) => b.Course is { } c
            ? official && !detailedHeaders ? $"{c.Number} /{c.Max:0.##}" : $"{c.Number}. {c.Course} (/{c.Max:0.##})"
            : b.Kind switch
            {
                "TOTAL 1" => $"T1 /{max?.Total1Max:0.##}",
                "SOUS-TOTAL 1" => $"ST1 /{max?.Subtotal1Max:0.##}",
                "SOUS-TOTAL 2" => $"ST2 /{max?.Subtotal2Max:0.##}",
                "TOTAL 2" => $"T2 /{max?.Total2Max:0.##}",
                _ => $"TG /{max?.GeneralMax:0.##}",
            };
        var reference = board.Rows.Select(r => r.Totals).FirstOrDefault(t => t is not null);
        columns.AddRange(blocks.Select(b => Header(b, reference)));
        columns.AddRange(official ? new[] { "%", "Décision", "Code jury" } : new[] { "Total", "%", "Décision" });
        var rows = board.Rows.Select(r =>
        {
            var details = computation.Details.GetValueOrDefault(r.EnrollmentId) ?? [];
            var cells = new List<string> { r.Rank?.ToString(Fr) ?? "—", r.Code ?? "—", r.FullName };
            cells.AddRange(blocks.Select(b => b.Course is { } c
                ? details.FirstOrDefault(d => d.CourseId == c.CourseId) is { Graded: true } d ? $"{d.Points:0.##}" : "INC"
                : b.Kind switch
                {
                    "TOTAL 1" => $"{r.Totals?.Total1:0.##}",
                    "SOUS-TOTAL 1" => $"{r.Totals?.Subtotal1:0.##}",
                    "SOUS-TOTAL 2" => $"{r.Totals?.Subtotal2:0.##}",
                    "TOTAL 2" => $"{r.Totals?.Total2:0.##}",
                    _ => $"{r.Totals?.General:0.##}",
                }));
            cells.AddRange(official ? new[] { $"{r.Percent:0.##}", r.Decision, r.Grade ?? "—" } : new[] { $"{r.Points:0.##} / {r.Max:0.##}", $"{r.Percent:0.##}", r.Decision });
            return (IReadOnlyList<string>)cells;
        }).ToList();
        var authorities = await AuthoritiesAsync(ct);
        return new TableDocument(PaymentService.Header(school), official ? "Grille de délibération" : "Grille des cotes",
            $"{board.Promotion} · {board.PeriodLabel} · {board.AcademicYear}{(board.Status == "Clôturée" ? string.Empty : " · provisoire")}"
            + (official ? " · " + string.Join(" · ", courses.Select(c => $"{c.Number} : {c.Course}"))
                + " · T1 : épreuves intégratives · ST1 : cours · ST2 : blocs · T2 = ST1 + ST2 · TG = T1 + T2 · P : réussite totale, A : un cours en échec, E : sous le seuil" : string.Empty)
            + $" · {Signatures(authorities)}",
            columns, rows, Landscape: true);
    }

    /// <summary>Palmarès : apprenants dans l'ordre du rang, admis d'abord.</summary>
    public async Task<TableDocument> RankingAsync(Guid promotionId, string periodText, CancellationToken ct = default)
    {
        var board = await BoardAsync(promotionId, periodText, ct);
        var school = await YearContext.SchoolAsync(db, ct);
        var rows = board.Rows.OrderBy(r => r.Decision.StartsWith("Admis", StringComparison.Ordinal) ? 0 : 1).ThenBy(r => r.Rank ?? int.MaxValue).ThenBy(r => r.FullName)
            .Select(r => (IReadOnlyList<string>)[r.Rank?.ToString(Fr) ?? "—", r.Matricule, r.FullName, $"{r.Percent:0.##} %", r.Mention,
                r.Grade is { } g ? $"{r.Decision} ({g})" : r.Decision]).ToList();
        return new TableDocument(PaymentService.Header(school), "Palmarès",
            $"{board.Promotion} · {board.PeriodLabel} · {board.AcademicYear} · {board.Admitted} admis sur {board.Students}",
            ["Rang", "Matricule", "Nom complet", "Pourcentage", "Mention", "Décision"], rows);
    }

    /// <summary>Bulletin de délibération d'un apprenant : cours, cote de base, appui, points retenus, total, mention et décision.</summary>
    public async Task<byte[]> BulletinPdfAsync(Guid promotionId, string periodText, Guid enrollmentId, string? verificationBaseUrl = null, CancellationToken ct = default)
    {
        // Un seul calcul de la délibération pour la fiche et le rang (auparavant deux).
        var (computation, school, authorities) = await BulletinContextAsync(promotionId, periodText, ct);
        var row = computation.Board.Rows.FirstOrDefault(r => r.EnrollmentId == enrollmentId) ?? throw new NotFoundException("Apprenant absent de cette délibération.");
        return documents.Report(Bulletin(computation, row, school, authorities, verificationBaseUrl));
    }

    /// <summary>Bulletins de toute la promotion dans un seul PDF, un apprenant par page, dans l'ordre du rang (bulletins d'ITMCD).</summary>
    public async Task<byte[]> BulletinsPdfAsync(Guid promotionId, string periodText, string? verificationBaseUrl = null, CancellationToken ct = default)
    {
        var (computation, school, authorities) = await BulletinContextAsync(promotionId, periodText, ct);
        if (computation.Board.Rows.Count == 0) throw new DomainException("bulletins", "Aucun apprenant dans cette délibération : aucun bulletin à produire.");
        return documents.Reports(computation.Board.Rows.Select(r => Bulletin(computation, r, school, authorities, verificationBaseUrl)).ToList());
    }

    /// <summary>
    /// Vérification publique d'un bulletin officiel (QR code), comme pour les reçus : le résultat figé à la clôture,
    /// avec un nom masqué. Un bulletin provisoire ou d'une session rouverte n'est pas vérifiable.
    /// </summary>
    public async Task<BulletinVerification> VerifyBulletinAsync(string code, CancellationToken ct = default)
    {
        var normalized = TextKey.Normalize(code);
        var none = new BulletinVerification(false, null, null, null, null, null, null, null, null, null);
        if (normalized.Length is < 8 or > 16) return none;
        var found = await (from r in db.DeliberationResults.AsNoTracking()
                           join x in db.DeliberationSessions.AsNoTracking() on r.SessionId equals x.Id
                           join p in db.Promotions.AsNoTracking() on x.PromotionId equals p.Id
                           join y in db.AcademicYears.AsNoTracking() on x.AcademicYearId equals y.Id
                           where r.VerificationCode == normalized && x.Status == SessionStatus.Cloturee
                           select new { r.StudentName, r.Percent, r.Final, r.FailedCourses, r.Mention, x.Period, x.ClosedAtUtc, x.AcademicYearId, p.Code, Year = y.Label }).FirstOrDefaultAsync(ct);
        if (found is null) return none;
        var school = await YearContext.SchoolAsync(db, ct);
        var words = found.StudentName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var masked = words.Length == 0 ? "—" : words.Length == 1 ? words[0] : $"{words[0]} {string.Join(' ', words.Skip(1).Select(w => w[0] + "."))}";
        var pass = (await SettingsOfAsync(found.AcademicYearId, ct)).PassPercent;
        var grade = found.Period == DeliberationPeriod.Annuel ? DeliberationEngine.Grade(found.Final, found.Percent, found.FailedCourses, pass) : string.Empty;
        return new BulletinVerification(true, school.Name, found.Year, found.Code, DeliberationEngine.PeriodLabel(found.Period), masked, found.Percent,
            DeliberationEngine.Label(found.Final), grade.Length == 0 ? null : grade, found.ClosedAtUtc);
    }

    private async Task<(Computation Computation, Domain.School.SchoolProfile School, AuthoritiesDto Authorities)> BulletinContextAsync(Guid promotionId, string periodText, CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var computation = await ComputeAsync(year, promotionId, DeliberationText.Period(periodText), closing: false, ct);
        return (computation, await YearContext.SchoolAsync(db, ct), Authorities(await SettingsOfAsync(year.Id, ct)));
    }

    /// <summary>Bulletin de délibération d'un apprenant : tient toujours sur une seule page.</summary>
    private static ReportDocument Bulletin(Computation computation, DeliberationRowDto r, Domain.School.SchoolProfile school, AuthoritiesDto authorities,
        string? verificationBaseUrl = null)
    {
        // Bulletin officiel (session clôturée) : code de vérification et QR, comme les reçus.
        var code = computation.Verification.GetValueOrDefault(r.EnrollmentId);
        var url = code is null || string.IsNullOrWhiteSpace(verificationBaseUrl) ? null : $"{verificationBaseUrl.TrimEnd('/')}/api/verifier-bulletin/{code}";
        var board = computation.Board;
        var courses = computation.Details.GetValueOrDefault(r.EnrollmentId) ?? [];
        var section = new ReportSection("Cours",
            ["N°", "Cours", "Maxima", "Cote", "Appui", "Points retenus", "Observation"],
            courses.Select(c => (IReadOnlyList<string>)[c.Number.ToString(Fr), c.Course, $"{c.Max:0.##}", c.Base is { } b ? $"{b:0.##}" : "INC",
                c.Bonus > 0 ? $"+{c.Bonus:0.##}" : string.Empty, c.Graded ? $"{c.Points:0.##}" : "INC", !c.Graded ? "Cote manquante" : c.Failed ? "Échec" : "Réussi"]).ToList(),
            [string.Empty, "Total", $"{r.Max:0.##}", string.Empty, string.Empty, $"{r.Points:0.##}", string.Empty], "Aucun cours pour cette période.");
        return new ReportDocument(PaymentService.Header(school), "Bulletin de délibération",
            $"{r.FullName} · matricule {r.Matricule} · {board.Promotion} · {board.PeriodLabel} · {board.AcademicYear}{(board.Status == "Clôturée" ? string.Empty : " · PROVISOIRE")}",
            [("Pourcentage", $"{r.Percent:0.##} %"), ("Mention", r.Mention), ("Décision", r.Grade is { } g ? $"{r.Decision} ({g})" : r.Decision),
                ("Rang", r.Rank is { } rank ? $"{rank} / {board.Students}" : "—")],
            [section], BulletinFooter(r, authorities) + (code is null ? string.Empty : $" — Code de vérification : {code}"), OnePage: true, VerificationUrl: url);
    }

    private static string BulletinFooter(DeliberationRowDto r, AuthoritiesDto a)
    {
        var lines = new List<string>();
        if (r.Totals is { } t)
            lines.Add($"TOTAL 1 (épreuves) : {t.Total1:0.##} / {t.Total1Max:0.##} · SOUS-TOTAL 1 (cours) : {t.Subtotal1:0.##} / {t.Subtotal1Max:0.##} · "
                + $"SOUS-TOTAL 2 (blocs) : {t.Subtotal2:0.##} / {t.Subtotal2Max:0.##} · TOTAL 2 : {t.Total2:0.##} / {t.Total2Max:0.##} · TOTAL GÉNÉRAL : {t.General:0.##} / {t.GeneralMax:0.##}");
        if (r.Grade is { } g) lines.Add($"Code du jury : {DeliberationEngine.GradeLabel(g)}");
        if (r.JuryReason is not null) lines.Add($"Décision du jury : {r.JuryReason}");
        lines.Add($"Le Préfet : {a.Prefect ?? "……………………"} · Le Proviseur : {a.Principal ?? "……………………"}");
        return string.Join(" — ", lines);
    }

    // ---------- Calcul ----------

    /// <param name="Verification">Codes de vérification des bulletins officiels (session clôturée seulement).</param>
    private sealed record Computation(DeliberationBoardDto Board, IReadOnlyDictionary<Guid, List<CourseOutcomeDto>> Details,
        IReadOnlyDictionary<Guid, Outcome> Computed, IReadOnlyDictionary<Guid, Outcome> Finals, IReadOnlyDictionary<Guid, string> Verification);

    private async Task<Computation> ComputeAsync(AcademicYear year, Guid promotionId, DeliberationPeriod period, bool closing, CancellationToken ct)
    {
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId && p.AcademicYearId == year.Id, ct)
            ?? throw new NotFoundException("Promotion introuvable dans l'année courante.");
        var settings = await SettingsOfAsync(year.Id, ct);
        var courses = (await CoursesOfAsync(year.Id, promotionId, ct)).Where(c => DeliberationEngine.Includes(c.Semester, period)).ToList();
        var students = await EnrolledAsync(year.Id, promotionId, ct);
        Dictionary<Guid, string> codes = user.HasPermission(Permissions.DeliberationManage) ? await ActiveCodesAsync(year.Id, ct) : [];
        var session = await db.DeliberationSessions.AsNoTracking().FirstOrDefaultAsync(s => s.PromotionId == promotionId && s.Period == period, ct);
        Dictionary<Guid, JuryDecision> decisions = session is null ? [] : await db.JuryDecisions.AsNoTracking().Where(d => d.SessionId == session.Id).ToDictionaryAsync(d => d.EnrollmentId, ct);
        Dictionary<Guid, DeliberationResult> frozen = session is { IsClosed: true } && !closing
            ? await db.DeliberationResults.AsNoTracking().Where(r => r.SessionId == session.Id).ToDictionaryAsync(r => r.EnrollmentId, ct)
            : [];
        // Une session clôturée utilise exclusivement la liste figée du jury : un diplômé,
        // un transféré ou un élève sorti après la clôture reste au procès-verbal officiel.
        // Les inscrits ajoutés après la clôture n'y figurent pas.
        if (session is { IsClosed: true } && !closing)
        {
            if (frozen.Count == 0)
                throw new ConflictException("Cette délibération est clôturée, mais aucun résultat officiel figé n'a été retrouvé. Vérifiez la base avant de générer les documents.");
            students = frozen.Values.Select(f => new EnrolledStudent(
                f.EnrollmentId, promotionId, promotion.Code, f.Matricule, f.StudentName)).ToList();
        }
        var courseIds = courses.Select(c => c.Id).ToList();
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var marks = (await db.CourseMarks.AsNoTracking().Where(m => courseIds.Contains(m.CourseId) && ids.Contains(m.EnrollmentId)).ToListAsync(ct))
            .ToDictionary(m => (m.EnrollmentId, m.CourseId, m.Kind), m => m.Value);

        var results = students.ToDictionary(s => s.EnrollmentId, s => DeliberationEngine.Student(courses.Select(c => new DeliberationEngine.CourseInput(c.Id, c.Semester, c.MaxScore,
            marks.TryGetValue((s.EnrollmentId, c.Id, MarkKind.CoteS1), out var s1) ? s1 : null,
            marks.TryGetValue((s.EnrollmentId, c.Id, MarkKind.CoteS2), out var s2) ? s2 : null,
            marks.TryGetValue((s.EnrollmentId, c.Id, MarkKind.Appui), out var ap) ? ap : null,
            marks.TryGetValue((s.EnrollmentId, c.Id, MarkKind.Jury), out var jy) ? jy : null)), period, settings.SupportEnabled, settings.PassPercent));
        var semesters = courses.ToDictionary(c => c.Id, c => c.Semester);
        var hasJury = courses.Any(c => DeliberationEngine.IsJury(c.Semester));
        JuryTotalsDto? TotalsOf(Guid enrollmentId)
        {
            if (!hasJury) return null;
            var t = DeliberationEngine.Totals(results[enrollmentId].Courses.Select(r => (semesters[r.CourseId], r)));
            return new JuryTotalsDto(t.Total1, t.Total1Max, t.Subtotal1, t.Subtotal1Max, t.Subtotal2, t.Subtotal2Max, t.Total2, t.Total2Max, t.General, t.GeneralMax);
        }
        string? GradeOf(Outcome final, decimal percent, int failed) =>
            period == DeliberationPeriod.Annuel ? DeliberationEngine.Grade(final, percent, failed, settings.PassPercent) is { Length: > 0 } g ? g : null : null;
        var ranks = DeliberationEngine.Ranks(results.Select(r => (r.Key, r.Value.Percent, r.Value.Missing == 0 && r.Value.Max > 0)));

        var computed = new Dictionary<Guid, Outcome>();
        var finals = new Dictionary<Guid, Outcome>();
        var rows = new List<DeliberationRowDto>();
        foreach (var s in students)
        {
            var result = results[s.EnrollmentId];
            var jury = decisions.GetValueOrDefault(s.EnrollmentId);
            DeliberationRowDto row;
            if (frozen.TryGetValue(s.EnrollmentId, out var f))
            {
                row = new DeliberationRowDto(s.EnrollmentId, f.Code.Length == 0 || !user.HasPermission(Permissions.DeliberationManage) ? null : f.Code, f.Matricule, f.StudentName, f.Points, f.MaxPoints, f.Percent, f.Missing, f.FailedCourses,
                    DeliberationEngine.Label(f.Computed), DeliberationEngine.DecisionLabel(f.Computed, f.Final, f.Reason is not null), jury is null ? null : DeliberationEngine.Label(jury.Outcome),
                    f.Reason, f.Mention, f.Rank, false, GradeOf(f.Final, f.Percent, f.FailedCourses), TotalsOf(s.EnrollmentId));
                computed[s.EnrollmentId] = f.Computed;
                finals[s.EnrollmentId] = f.Final;
            }
            else
            {
                var final = DeliberationEngine.Final(result.Outcome, jury?.Outcome, closing);
                var near = result.Outcome != Outcome.Admis && jury is null
                    && (result.Missing == 1 || (result.Missing == 0 && result.Percent >= settings.PassPercent - 5));
                row = new DeliberationRowDto(s.EnrollmentId, codes.GetValueOrDefault(s.EnrollmentId), s.Matricule, s.FullName, result.Points, result.Max, result.Percent,
                    result.Missing, result.Failed, DeliberationEngine.Label(result.Outcome), DeliberationEngine.DecisionLabel(result.Outcome, final, jury is not null),
                    jury is null ? null : DeliberationEngine.Label(jury.Outcome), jury?.Reason, result.Mention, ranks.TryGetValue(s.EnrollmentId, out var rank) ? rank : null, near,
                    GradeOf(final, result.Percent, result.Failed), TotalsOf(s.EnrollmentId));
                computed[s.EnrollmentId] = result.Outcome;
                finals[s.EnrollmentId] = final;
            }
            rows.Add(row);
        }
        // Après la clôture, un apprenant inscrit plus tard n'apparaît pas au procès-verbal figé.
        if (frozen.Count > 0) rows = rows.Where(r => frozen.ContainsKey(r.EnrollmentId)).ToList();
        rows = rows.OrderBy(r => r.Rank ?? int.MaxValue).ThenBy(r => r.FullName, StringComparer.CurrentCulture).ToList();

        // Ordre officiel de la grille (projet Délibération) : épreuves intégratives, cours, puis blocs.
        static int Order(CourseSemester semester) => semester switch { CourseSemester.JuryEpreuve => 0, CourseSemester.JuryBloc => 2, _ => 1 };
        courses = courses.OrderBy(c => Order(c.Semester)).ThenBy(c => c.Number).ToList();
        // Accès direct par cours (au lieu de recherches linéaires) : le calcul reste rapide pour une grande promotion.
        var byCourse = results.ToDictionary(r => r.Key, r => r.Value.Courses.ToDictionary(c => c.CourseId));
        var details = students.ToDictionary(s => s.EnrollmentId, s => courses.Select(course =>
        {
            var c = byCourse[s.EnrollmentId][course.Id];
            return new CourseOutcomeDto(c.CourseId, course.Number, course.Name, DeliberationText.Semester(course.Semester), c.Max, c.Base, c.Bonus, c.Points, c.Graded, c.Failed);
        }).ToList());
        var courseStats = courses.Select(c =>
        {
            var lines = byCourse.Values.Select(r => r[c.Id]).ToList();
            var graded = lines.Where(l => l.Graded).ToList();
            return new CourseStatDto(c.Id, c.Number, c.Name, DeliberationText.Semester(c.Semester), c.MaxScore, graded.Count(l => !l.Failed), graded.Count(l => l.Failed),
                lines.Count - graded.Count, graded.Count == 0 ? null : Math.Round(graded.Average(l => l.Points), 2, MidpointRounding.AwayFromZero));
        }).ToList();

        var finalOutcomes = rows.Select(r => finals[r.EnrollmentId]).ToList();
        var admitted = finalOutcomes.Count(o => o == Outcome.Admis);
        var board = new DeliberationBoardDto(promotion.Id, $"{promotion.Code} — {promotion.Name}", DeliberationText.Period(period), DeliberationEngine.PeriodLabel(period), year.Label,
            session is { IsClosed: true } ? "Clôturée" : "Ouverte", session?.ClosedAtUtc, session?.ClosedByName, session?.President, session?.Members,
            session?.ReopenCount ?? 0, session?.LastReopenReason, rows.Count, admitted, finalOutcomes.Count(o => o == Outcome.Ajourne), finalOutcomes.Count(o => o == Outcome.Incomplet),
            rows.Count == 0 ? 0 : Math.Round(admitted * 100m / rows.Count, 2, MidpointRounding.AwayFromZero), settings.PassPercent, courseStats, rows);
        return new Computation(board, details, computed, finals,
            frozen.Where(f => f.Value.VerificationCode.Length > 0).ToDictionary(f => f.Key, f => f.Value.VerificationCode));
    }

    // ---------- Accès aux données ----------

    private sealed record EnrolledStudent(Guid EnrollmentId, Guid PromotionId, string PromotionCode, string Matricule, string FullName);

    /// <summary>Inscriptions actives ou suspendues de l'année : les apprenants existants de SCOLIA, sans nouvelle saisie.</summary>
    private async Task<List<EnrolledStudent>> EnrolledAsync(Guid yearId, Guid? promotionId, CancellationToken ct) =>
        (await (from e in db.Enrollments.AsNoTracking()
                join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                where e.AcademicYearId == yearId && (promotionId == null || e.PromotionId == promotionId)
                    && (e.Status == EnrollmentStatus.Actif || e.Status == EnrollmentStatus.Suspendu)
                orderby s.LastName, s.MiddleName, s.FirstName
                select new { e.Id, e.PromotionId, p.Code, s.Matricule, s.LastName, s.MiddleName, s.FirstName }).ToListAsync(ct))
        .Select(x => new EnrolledStudent(x.Id, x.PromotionId, x.Code, x.Matricule,
            string.Join(' ', new[] { x.LastName, x.MiddleName, x.FirstName }.Where(n => !string.IsNullOrWhiteSpace(n))))).ToList();

    /// <summary>Une seule inscription (saisie par code, changement de code) : sans charger toute l'année.</summary>
    private async Task<EnrolledStudent?> EnrolledOneAsync(Guid yearId, Guid enrollmentId, CancellationToken ct) =>
        (await (from e in db.Enrollments.AsNoTracking()
                join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                where e.Id == enrollmentId && e.AcademicYearId == yearId
                    && (e.Status == EnrollmentStatus.Actif || e.Status == EnrollmentStatus.Suspendu)
                select new { e.Id, e.PromotionId, p.Code, s.Matricule, s.LastName, s.MiddleName, s.FirstName }).ToListAsync(ct))
        .Select(x => new EnrolledStudent(x.Id, x.PromotionId, x.Code, x.Matricule,
            string.Join(' ', new[] { x.LastName, x.MiddleName, x.FirstName }.Where(n => !string.IsNullOrWhiteSpace(n))))).FirstOrDefault();

    private async Task<Dictionary<Guid, string>> ActiveCodesAsync(Guid yearId, CancellationToken ct) =>
        await db.AnonymousCodes.AsNoTracking().Where(c => c.AcademicYearId == yearId && c.IsActive).ToDictionaryAsync(c => c.EnrollmentId, c => c.Code, ct);

    private async Task<List<Course>> CoursesOfAsync(Guid yearId, Guid promotionId, CancellationToken ct) =>
        await db.Courses.AsNoTracking().Where(c => c.AcademicYearId == yearId && c.PromotionId == promotionId && c.IsActive).OrderBy(c => c.Number).ToListAsync(ct);

    private async Task<Course> CourseInYearAsync(Guid courseId, Guid yearId, CancellationToken ct) =>
        await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courseId && c.AcademicYearId == yearId, ct)
        ?? throw new NotFoundException("Cours introuvable dans l'année courante.");

    private async Task<Dictionary<Guid, string>> TeacherNamesAsync(List<Guid> courseIds, CancellationToken ct) =>
        (await (from a in db.CourseAssignments.AsNoTracking()
                join t in db.Staff.AsNoTracking() on a.TeacherId equals t.Id
                where courseIds.Contains(a.CourseId) && a.EndedOn == null
                select new { a.CourseId, t.LastName, t.MiddleName, t.FirstName }).ToListAsync(ct))
        .GroupBy(x => x.CourseId).ToDictionary(g => g.Key, g => string.Join(' ', new[] { g.First().LastName, g.First().MiddleName, g.First().FirstName }.Where(n => !string.IsNullOrWhiteSpace(n))));

    private async Task<DeliberationSession> SessionAsync(Guid yearId, Guid promotionId, DeliberationPeriod period, bool create, CancellationToken ct)
    {
        var session = await db.DeliberationSessions.FirstOrDefaultAsync(s => s.PromotionId == promotionId && s.Period == period, ct);
        if (session is not null) return session;
        if (!create) throw new NotFoundException("Aucune délibération n'a encore été tenue pour cette période.");
        if (!await db.Promotions.AnyAsync(p => p.Id == promotionId && p.AcademicYearId == yearId, ct)) throw new NotFoundException("Promotion introuvable dans l'année courante.");
        session = new DeliberationSession { AcademicYearId = yearId, PromotionId = promotionId, Period = period };
        db.DeliberationSessions.Add(session);
        audit.Record("deliberation.ouverte", "SessionDeliberation", session.Id.ToString(), $"Session de délibération ouverte : {DeliberationEngine.PeriodLabel(period)}.");
        return session;
    }

    private async Task<AcademicYear> OpenYearAsync(CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        if (year.Status == AcademicYearStatus.Closed) throw new DomainException("annee", $"L'année {year.Label} est clôturée : la délibération est consultable mais ne change plus.");
        return year;
    }

    private async Task VerifyPasswordAsync(string? password, CancellationToken ct)
    {
        var id = user.Id ?? throw new DomainException("session", "Session expirée : reconnectez-vous.");
        var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Utilisateur introuvable.");
        if (!passwords.Verify(account.PasswordHash, password ?? string.Empty, out _))
            throw new DomainException("motDePasse", "Mot de passe incorrect : la délibération n'a pas changé.");
    }

    private static string Reason(string? value, string message)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length is < MinReasonLength or > MaxReasonLength) throw new DomainException("motif", message);
        return text;
    }

    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
}
