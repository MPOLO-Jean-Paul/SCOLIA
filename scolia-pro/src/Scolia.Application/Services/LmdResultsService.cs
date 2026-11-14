using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Notes des EC, résultats et délibération annuelle d'une promotion LMD :
/// publication des résultats partiels du premier semestre, résultats de la première
/// session sur tous les cours de la promotion, deuxième session, décisions et dettes.
/// </summary>
public sealed class LmdResultsService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user)
{
    private string Actor => string.IsNullOrWhiteSpace(user.Name) ? "Utilisateur" : user.Name.Trim();

    private sealed record Context(
        SchoolProfile School, AcademicYear Year, Promotion Promotion, string? Filiere,
        AcademicRegulation Regulation, List<CurriculumItem> Units, List<CurriculumItem> Elements,
        List<Enrollment> Enrollments, List<LmdMark> Marks, LmdDeliberation? Deliberation, List<LmdDecision> Decisions);

    private async Task<Context> LoadAsync(Guid promotionId, bool tracking, CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (InstitutionProfiles.Canonicalize(school.InstitutionType) != InstitutionProfiles.SuperieurLmd)
            throw new DomainException("lmd", "Ce module est réservé aux universités et instituts supérieurs LMD.");
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId, ct)
            ?? throw new NotFoundException("Promotion introuvable.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == promotion.AcademicYearId, ct);
        var regulation = await AcademicRegulationService.RequireValidatedAsync(db, year.Id, RegulationScope.SuperieurLmd, ct);
        var filiere = promotion.AcademicUnitId is { } unitId
            ? await db.AcademicStructureUnits.AsNoTracking().Where(u => u.Id == unitId).Select(u => u.Name).FirstOrDefaultAsync(ct)
            : null;
        var items = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.PromotionId == promotion.Id && i.AcademicYearId == year.Id && i.IsActive).ToListAsync(ct);
        var enrollments = await db.Enrollments.AsNoTracking().Include(e => e.Student)
            .Where(e => e.PromotionId == promotion.Id && e.AcademicYearId == year.Id &&
                        e.Status != EnrollmentStatus.Sorti && e.Status != EnrollmentStatus.Transfere)
            .ToListAsync(ct);
        var marks = tracking
            ? await db.LmdMarks.Where(m => m.PromotionId == promotion.Id && m.AcademicYearId == year.Id).ToListAsync(ct)
            : await db.LmdMarks.AsNoTracking().Where(m => m.PromotionId == promotion.Id && m.AcademicYearId == year.Id).ToListAsync(ct);
        var deliberation = tracking
            ? await db.LmdDeliberations.FirstOrDefaultAsync(d => d.PromotionId == promotion.Id && d.AcademicYearId == year.Id, ct)
            : await db.LmdDeliberations.AsNoTracking().FirstOrDefaultAsync(d => d.PromotionId == promotion.Id && d.AcademicYearId == year.Id, ct);
        var decisions = deliberation is null ? []
            : tracking ? await db.LmdDecisions.Where(d => d.DeliberationId == deliberation.Id).ToListAsync(ct)
            : await db.LmdDecisions.AsNoTracking().Where(d => d.DeliberationId == deliberation.Id).ToListAsync(ct);
        return new Context(school, year, promotion, filiere, regulation,
            items.Where(i => i.Kind == CurriculumItemKind.TeachingUnit).ToList(),
            items.Where(i => i.Kind == CurriculumItemKind.TeachingElement).ToList(),
            enrollments, marks, deliberation, decisions);
    }

    private static IReadOnlyList<LmdStudentResult> Compute(Context c, CourseSemester? semester, bool second)
    {
        if (c.Units.Count == 0)
            throw new DomainException("resultatsLmd", "La maquette de la promotion ne comporte aucune UE.");
        return LmdResultsCalculator.Compute(c.Regulation,
            c.Units.Select(u => new LmdUeSpec(u.Id, u.Code, u.Name, u.Semester, u.Credits)).ToList(),
            c.Elements.Select(e => new LmdEcSpec(e.Id, e.ParentItemId!.Value, e.Code, e.Name, e.Credits, e.Hours)).ToList(),
            c.Enrollments.Select(e => e.Id).ToList(),
            c.Marks.Select(m => new LmdMarkEntry(m.EnrollmentId, m.ElementId, m.Session, m.Score)).ToList(),
            semester, second);
    }

    private static LmdSessionStatus StatusOf(Context c) => c.Deliberation?.Status ?? LmdSessionStatus.Ouverte;

    // ----------------------------------------------------------------- résultats

    /// <summary>
    /// Étape affichée : « S1 » (résultats partiels, avant la clôture de la première session),
    /// « PremiereSession » ou « Definitif ». Sans étape, celle qui correspond au statut.
    /// </summary>
    public async Task<LmdPromotionResultsDto> ResultsAsync(Guid promotionId, string? stage, CancellationToken ct = default)
    {
        var c = await LoadAsync(promotionId, false, ct);
        return Results(c, stage);
    }

    private LmdPromotionResultsDto Results(Context c, string? stage)
    {
        var status = StatusOf(c);
        stage ??= status switch
        {
            LmdSessionStatus.Ouverte or LmdSessionStatus.S1Publie => "S1",
            LmdSessionStatus.PremiereSessionCloturee => "PremiereSession",
            _ => "Definitif",
        };
        var (semester, second) = stage switch
        {
            "S1" => ((CourseSemester?)CourseSemester.S1, false),
            "PremiereSession" => (null, false),
            "Definitif" => (null, true),
            _ => throw new DomainException("resultatsLmd", "Étape inconnue : S1, PremiereSession ou Definitif."),
        };
        var results = Compute(c, semester, second);
        var decisions = c.Decisions.ToDictionary(d => d.EnrollmentId);
        var byId = c.Enrollments.ToDictionary(e => e.Id);
        var students = results.Select(r =>
        {
            var e = byId[r.EnrollmentId];
            decisions.TryGetValue(r.EnrollmentId, out var d);
            var computed = stage switch
            {
                "S1" => r.Complete ? (r.CreditsAcquired >= r.CreditsPlanned ? "S1 validé" : "S1 non validé") : "Incomplet",
                "PremiereSession" => LmdResultsCalculator.FirstSession(r).ToString(),
                _ => d?.FirstOutcome == LmdOutcome.DeuxiemeSession
                    ? LmdResultsCalculator.Final(c.Regulation, r).ToString()
                    : (d?.FirstOutcome ?? LmdResultsCalculator.FirstSession(r)).ToString(),
            };
            var (jury, reason) = stage == "Definitif" ? (d?.FinalJury, d?.FinalJuryReason) : (d?.Jury, d?.JuryReason);
            string? decision = stage switch
            {
                "PremiereSession" when status >= LmdSessionStatus.PremiereSessionCloturee && d?.FirstOutcome is { } f =>
                    LmdResultsCalculator.Label(f),
                "Definitif" when status == LmdSessionStatus.Cloturee && d?.Final is { } f => LmdResultsCalculator.Label(f),
                _ => null,
            };
            return new LmdStudentLineDto(e.Id, e.StudentId, e.Student!.Matricule, e.Student.FullName,
                Semester(r.S1), Semester(r.S2), r.Average, r.CreditsAcquired, r.CreditsPlanned, r.Complete,
                stage == "S1" ? RankBy(results, r, x => x.S1.Average) : r.Rank,
                computed, jury?.ToString(), reason, decision,
                r.Units.Select(u => new LmdUeLineDto(u.Id, u.Code, u.Name, u.Semester.ToString(), u.Credits,
                    u.Average, u.Acquired, u.ByCompensation,
                    u.Elements.Select(x => new LmdEcLineDto(x.Id, x.Code, x.Name, x.Weight, x.Mark, x.Session,
                        x.Mark is { } m ? Appreciation(m, c.Regulation.MarkScale) : null)).ToList())).ToList());
        })
        .OrderBy(s => s.Rank ?? int.MaxValue).ThenBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var dl = c.Deliberation;
        return new LmdPromotionResultsDto(c.Promotion.Id, c.Promotion.Name,
            LmdDeliberationRules.LevelLabel(c.Promotion.Level), c.Filiere, c.Year.Label, stage, status.ToString(),
            c.Regulation.MarkScale, c.Regulation.UeValidationMark, c.Regulation.MaxDebtCredits,
            dl?.S1PublishedBy, dl?.S1PublishedAtUtc, dl?.FirstSessionClosedBy, dl?.FirstSessionClosedAtUtc,
            dl?.ClosedBy, dl?.ClosedAtUtc, students);
    }

    private static int? RankBy(IReadOnlyList<LmdStudentResult> all, LmdStudentResult me, Func<LmdStudentResult, decimal?> key)
    {
        if (key(me) is not { } mine) return null;
        return all.Count(o => key(o) is { } other && other > mine) + 1;
    }

    private static string? Appreciation(decimal mark, decimal scale) =>
        scale == 20 ? LmdMarkAppreciation2025.FromNote(mark).Code : null;

    private static LmdSemesterLineDto Semester(LmdSemesterResult s) =>
        new(s.Semester.ToString(), s.Average, s.CreditsAcquired, s.CreditsPlanned, s.Complete);

    // ----------------------------------------------------------------- notes

    public async Task<LmdMarkSheetDto> SheetAsync(Guid promotionId, Guid elementId, int session, CancellationToken ct = default)
    {
        var c = await LoadAsync(promotionId, false, ct);
        var (element, ue) = Element(c, elementId);
        string? lockReason = null;
        try { LmdDeliberationRules.EnsureMarkEditable(StatusOf(c), element.Semester, session); }
        catch (DomainException e) { lockReason = e.Message; }
        var eligible = Eligible(c, session, ue.Id);
        var lines = c.Enrollments.OrderBy(e => e.Student!.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new LmdMarkLineDto(e.Id, e.Student!.Matricule, e.Student.FullName,
                c.Marks.FirstOrDefault(m => m.EnrollmentId == e.Id && m.ElementId == element.Id && m.Session == session)?.Score,
                lockReason is null && eligible.Contains(e.Id)))
            .ToList();
        return new LmdMarkSheetDto(element.Id, element.Code, element.Name, ue.Code, element.Semester.ToString(),
            session, c.Regulation.MarkScale, lockReason is null, lockReason, lines);
    }

    private static (CurriculumItem Element, CurriculumItem Ue) Element(Context c, Guid elementId)
    {
        var element = c.Elements.FirstOrDefault(e => e.Id == elementId)
            ?? throw new NotFoundException("Élément constitutif introuvable dans la maquette de la promotion.");
        var ue = c.Units.First(u => u.Id == element.ParentItemId);
        return (element, ue);
    }

    /// <summary>En deuxième session, seuls les étudiants ajournés dont l'UE n'est pas acquise composent.</summary>
    private static HashSet<Guid> Eligible(Context c, int session, Guid ueId)
    {
        if (session == 1) return c.Enrollments.Select(e => e.Id).ToHashSet();
        var first = c.Deliberation is { Status: >= LmdSessionStatus.PremiereSessionCloturee }
            ? Compute(c, null, false).ToDictionary(r => r.EnrollmentId) : [];
        return c.Decisions.Where(d => d.FirstOutcome == LmdOutcome.DeuxiemeSession &&
                                      first.TryGetValue(d.EnrollmentId, out var r) &&
                                      r.Units.Any(u => u.Id == ueId && !u.Acquired))
            .Select(d => d.EnrollmentId).ToHashSet();
    }

    public Task<LmdMarkSheetDto> SaveMarksAsync(Guid promotionId, Guid elementId, LmdMarkSheetInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var c = await LoadAsync(promotionId, true, ct);
        if (c.Year.Status == AcademicYearStatus.Closed)
            throw new DomainException("deliberationLmd", "L'année est clôturée.");
        var (element, ue) = Element(c, elementId);
        LmdDeliberationRules.EnsureMarkEditable(StatusOf(c), element.Semester, input.Session);
        var eligible = Eligible(c, input.Session, ue.Id);
        foreach (var line in input.Lines ?? [])
        {
            if (!eligible.Contains(line.EnrollmentId))
                throw new DomainException("deliberationLmd", input.Session == 2
                    ? "Un étudiant de la saisie ne compose pas cette UE en deuxième session."
                    : "Un étudiant de la saisie n'est pas inscrit dans cette promotion.");
            if (line.Score is { } s && (s < 0 || s > c.Regulation.MarkScale || s != decimal.Round(s, 2)))
                throw new DomainException("deliberationLmd",
                    $"Une note est comprise entre 0 et {c.Regulation.MarkScale}, avec deux décimales au plus.");
            var mark = c.Marks.FirstOrDefault(m => m.EnrollmentId == line.EnrollmentId && m.ElementId == element.Id &&
                                                   m.Session == input.Session);
            if (mark is null)
            {
                mark = new LmdMark
                {
                    AcademicYearId = c.Year.Id, PromotionId = c.Promotion.Id, EnrollmentId = line.EnrollmentId,
                    ElementId = element.Id, Session = input.Session,
                };
                db.LmdMarks.Add(mark);
                c.Marks.Add(mark);
            }
            mark.Score = line.Score;
        }
        audit.Record("lmd.notes.saisies", "ElementConstitutif", element.Id.ToString(),
            $"{c.Promotion.Name} : notes de l'EC {element.Code} ({ue.Code}), session {input.Session}, " +
            $"{(input.Lines ?? []).Count} étudiant(s).");
        await db.SaveChangesAsync(ct);
        return await SheetAsync(promotionId, elementId, input.Session, ct);
    }, ct);

    // ----------------------------------------------------------------- délibération

    private LmdDeliberation Ensure(Context c)
    {
        if (c.Deliberation is not null) return c.Deliberation;
        var d = new LmdDeliberation { AcademicYearId = c.Year.Id, PromotionId = c.Promotion.Id };
        db.LmdDeliberations.Add(d);
        return d;
    }

    private LmdDecision DecisionFor(Context c, LmdDeliberation d, Guid enrollmentId)
    {
        var decision = c.Decisions.FirstOrDefault(x => x.EnrollmentId == enrollmentId);
        if (decision is not null) return decision;
        decision = new LmdDecision { DeliberationId = d.Id, EnrollmentId = enrollmentId };
        db.LmdDecisions.Add(decision);
        c.Decisions.Add(decision);
        return decision;
    }

    private static void RequireOpenYear(Context c)
    {
        if (c.Year.Status == AcademicYearStatus.Closed)
            throw new DomainException("deliberationLmd", "L'année est clôturée.");
    }

    private Context WithDeliberation(Context c, LmdDeliberation d) => c with { Deliberation = d };

    /// <summary>Publie les résultats partiels du premier semestre : notes du S1 figées.</summary>
    public Task<LmdPromotionResultsDto> PublishS1Async(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var c = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (StatusOf(c) != LmdSessionStatus.Ouverte)
            throw new DomainException("deliberationLmd", "Les résultats du premier semestre sont déjà publiés.");
        var results = Compute(c, CourseSemester.S1, false);
        var missing = results.Where(r => !r.Complete).Select(r => c.Enrollments.First(e => e.Id == r.EnrollmentId).Student!.FullName).ToList();
        if (missing.Count > 0)
            throw new DomainException("deliberationLmd",
                "Notes du premier semestre manquantes pour " + string.Join(", ", missing.Order()) + ".");
        var d = Ensure(c);
        foreach (var r in results)
        {
            var decision = DecisionFor(c, d, r.EnrollmentId);
            decision.S1Average = r.S1.Average;
            decision.S1Credits = r.S1.CreditsAcquired;
        }
        d.Status = LmdSessionStatus.S1Publie;
        d.S1PublishedBy = Actor;
        d.S1PublishedAtUtc = DateTime.UtcNow;
        audit.Record("lmd.s1.publie", "Promotion", c.Promotion.Id.ToString(),
            $"{c.Promotion.Name} {c.Year.Label} : résultats partiels du premier semestre publiés par {Actor}.");
        await db.SaveChangesAsync(ct);
        return Results(WithDeliberation(c, d), "S1");
    }, ct);

    /// <summary>Décision motivée du jury : avant la première session, puis après la deuxième.</summary>
    public Task<LmdPromotionResultsDto> SetJuryAsync(Guid promotionId, Guid enrollmentId, LmdJuryInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var c = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (c.Enrollments.All(e => e.Id != enrollmentId))
            throw new NotFoundException("Étudiant introuvable dans cette promotion.");
        LmdOutcome? jury = string.IsNullOrWhiteSpace(input.Outcome) ? null
            : Enum.TryParse<LmdOutcome>(input.Outcome, true, out var parsed) && Enum.IsDefined(parsed) ? parsed
            : throw new DomainException("deliberationLmd", "Décision du jury inconnue.");
        LmdDeliberationRules.ValidateJury(jury, input.Reason);
        var status = StatusOf(c);
        if (status == LmdSessionStatus.Cloturee)
            throw new DomainException("deliberationLmd", "La délibération de l'année est clôturée.");
        var d = Ensure(c);
        var decision = DecisionFor(c, d, enrollmentId);
        var final = status == LmdSessionStatus.PremiereSessionCloturee;
        if (final)
        {
            if (decision.FirstOutcome != LmdOutcome.DeuxiemeSession)
                throw new DomainException("deliberationLmd", "La décision de première session de cet étudiant est publiée.");
            if (jury == LmdOutcome.DeuxiemeSession)
                throw new DomainException("deliberationLmd", "Après la deuxième session, le jury décide : admis, admis avec dettes ou ajourné.");
            decision.FinalJury = jury;
            decision.FinalJuryReason = jury is null ? null : input.Reason!.Trim();
        }
        else
        {
            if (jury == LmdOutcome.AdmisAvecDettes)
                throw new DomainException("deliberationLmd", "Le passage avec dettes se décide après la deuxième session.");
            decision.Jury = jury;
            decision.JuryReason = jury is null ? null : input.Reason!.Trim();
        }
        audit.Record("lmd.jury", "DecisionLmd", enrollmentId.ToString(),
            $"{c.Promotion.Name} ({(final ? "deuxième session" : "première session")}) : " +
            (jury is null ? "décision du jury retirée." : $"{LmdResultsCalculator.Label(jury.Value)} — {input.Reason!.Trim()}"));
        await db.SaveChangesAsync(ct);
        return Results(WithDeliberation(c, d), final ? "Definitif" : "PremiereSession");
    }, ct);

    /// <summary>
    /// Résultats de la première session : tous les cours programmés pour la promotion (S1 et S2).
    /// Sans étudiant en deuxième session, l'année est directement clôturée.
    /// </summary>
    public Task<LmdPromotionResultsDto> CloseFirstSessionAsync(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var c = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (StatusOf(c) != LmdSessionStatus.S1Publie)
            throw new DomainException("deliberationLmd", StatusOf(c) == LmdSessionStatus.Ouverte
                ? "Publiez d'abord les résultats du premier semestre."
                : "La première session est déjà clôturée.");
        var d = c.Deliberation!;
        var results = Compute(c, null, false);
        var undecided = new List<string>();
        foreach (var r in results)
        {
            var decision = DecisionFor(c, d, r.EnrollmentId);
            var outcome = decision.Jury ?? LmdResultsCalculator.FirstSession(r);
            if (outcome == LmdOutcome.Incomplet)
                undecided.Add(c.Enrollments.First(e => e.Id == r.EnrollmentId).Student!.FullName);
            decision.FirstOutcome = outcome;
            decision.FirstAverage = r.Average;
            decision.FirstCredits = r.CreditsAcquired;
            decision.Rank = r.Rank;
        }
        if (undecided.Count > 0)
            throw new DomainException("deliberationLmd",
                "Notes manquantes : complétez-les ou fixez la décision du jury pour " + string.Join(", ", undecided.Order()) + ".");
        d.Status = LmdSessionStatus.PremiereSessionCloturee;
        d.FirstSessionClosedBy = Actor;
        d.FirstSessionClosedAtUtc = DateTime.UtcNow;
        if (!c.Decisions.Any(x => x.FirstOutcome == LmdOutcome.DeuxiemeSession))
            await FinalizeAsync(c, d, results, ct);
        audit.Record("lmd.premiereSession", "Promotion", c.Promotion.Id.ToString(),
            $"{c.Promotion.Name} {c.Year.Label} : première session clôturée par {Actor}. " + Counts(c.Decisions, x => x.FirstOutcome));
        await db.SaveChangesAsync(ct);
        return Results(WithDeliberation(c, d), d.Status == LmdSessionStatus.Cloturee ? "Definitif" : "PremiereSession");
    }, ct);

    /// <summary>Clôture la deuxième session : décisions définitives, dettes créées pour les admis avec dettes.</summary>
    public Task<LmdPromotionResultsDto> CloseAsync(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var c = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (StatusOf(c) != LmdSessionStatus.PremiereSessionCloturee)
            throw new DomainException("deliberationLmd", "Clôturez d'abord la première session.");
        var d = c.Deliberation!;
        var results = Compute(c, null, true);
        var undecided = results.Where(r =>
                c.Decisions.First(x => x.EnrollmentId == r.EnrollmentId) is { FirstOutcome: LmdOutcome.DeuxiemeSession } x &&
                (x.FinalJury ?? LmdResultsCalculator.Final(c.Regulation, r)) == LmdOutcome.Incomplet)
            .Select(r => c.Enrollments.First(e => e.Id == r.EnrollmentId).Student!.FullName).ToList();
        if (undecided.Count > 0)
            throw new DomainException("deliberationLmd",
                "Résultats incomplets en deuxième session pour " + string.Join(", ", undecided.Order()) + ".");
        await FinalizeAsync(c, d, results, ct);
        audit.Record("lmd.cloture", "Promotion", c.Promotion.Id.ToString(),
            $"{c.Promotion.Name} {c.Year.Label} : deuxième session clôturée par {Actor}. " + Counts(c.Decisions, x => x.Final));
        await db.SaveChangesAsync(ct);
        return Results(WithDeliberation(c, d), "Definitif");
    }, ct);

    private async Task FinalizeAsync(Context c, LmdDeliberation d, IReadOnlyList<LmdStudentResult> results, CancellationToken ct)
    {
        var existingDebts = await db.LmdDebts.Where(x => c.Enrollments.Select(e => e.Id).Contains(x.OriginEnrollmentId))
            .Select(x => new { x.OriginEnrollmentId, x.UeId }).ToListAsync(ct);
        foreach (var r in results)
        {
            var decision = c.Decisions.First(x => x.EnrollmentId == r.EnrollmentId);
            decision.Final = decision.FirstOutcome == LmdOutcome.DeuxiemeSession
                ? decision.FinalJury ?? LmdResultsCalculator.Final(c.Regulation, r)
                : decision.FirstOutcome;
            decision.FinalAverage = r.Average;
            decision.FinalCredits = r.CreditsAcquired;
            if (decision.Final != LmdOutcome.AdmisAvecDettes) continue;
            var enrollment = c.Enrollments.First(e => e.Id == r.EnrollmentId);
            foreach (var ue in r.MissingUnits.Where(u => !existingDebts.Any(x => x.OriginEnrollmentId == r.EnrollmentId && x.UeId == u.Id)))
                db.LmdDebts.Add(new LmdDebt
                {
                    StudentId = enrollment.StudentId, OriginEnrollmentId = enrollment.Id, OriginYearId = c.Year.Id,
                    UeId = ue.Id, UeCode = ue.Code, UeName = ue.Name, Semester = ue.Semester.ToString(), Credits = ue.Credits,
                });
        }
        d.Status = LmdSessionStatus.Cloturee;
        d.ClosedBy = Actor;
        d.ClosedAtUtc = DateTime.UtcNow;
    }

    private static string Counts(IEnumerable<LmdDecision> decisions, Func<LmdDecision, LmdOutcome?> pick) =>
        string.Join(", ", decisions.Where(x => pick(x) is not null).GroupBy(x => pick(x)!.Value).OrderBy(g => g.Key)
            .Select(g => $"{LmdResultsCalculator.Label(g.Key)} : {g.Count()}"));

    // ----------------------------------------------------------------- dettes

    public async Task<IReadOnlyList<LmdDebtDto>> DebtsAsync(Guid? studentId, CancellationToken ct = default)
    {
        var query = from x in db.LmdDebts.AsNoTracking()
                    join s in db.Students.AsNoTracking() on x.StudentId equals s.Id
                    join y in db.AcademicYears.AsNoTracking() on x.OriginYearId equals y.Id
                    where studentId == null || x.StudentId == studentId
                    select new { x, s, y.Label };
        var rows = await query.ToListAsync(ct);
        return rows.OrderBy(r => r.s.LastName).ThenBy(r => r.x.UeCode)
            .Select(r => new LmdDebtDto(r.x.Id, r.s.Id, r.s.Matricule, r.s.FullName, r.Label, r.x.UeCode, r.x.UeName,
                r.x.Semester, r.x.Credits, r.x.ClearedMark, r.x.ClearedBy, r.x.ClearedAtUtc)).ToList();
    }

    /// <summary>Apurement d'une dette : la note de l'UE, à l'échelle du règlement de l'année courante.</summary>
    public Task<LmdDebtDto> ClearDebtAsync(Guid debtId, LmdDebtClearInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var debt = await db.LmdDebts.FirstOrDefaultAsync(x => x.Id == debtId, ct)
            ?? throw new NotFoundException("Dette introuvable.");
        if (debt.IsCleared) throw new DomainException("detteLmd", "Cette dette est déjà apurée.");
        var year = await YearContext.CurrentAsync(db, ct);
        var regulation = await AcademicRegulationService.RequireValidatedAsync(db, year.Id, RegulationScope.SuperieurLmd, ct);
        if (input.Mark < 0 || input.Mark > regulation.MarkScale)
            throw new DomainException("detteLmd", $"La note est comprise entre 0 et {regulation.MarkScale}.");
        if (input.Mark < regulation.UeValidationMark)
            throw new DomainException("detteLmd",
                $"Une dette s'apure avec au moins {regulation.UeValidationMark}/{regulation.MarkScale}.");
        debt.ClearedMark = input.Mark;
        debt.ClearedBy = Actor;
        debt.ClearedAtUtc = DateTime.UtcNow;
        audit.Record("lmd.dette.apuree", "DetteLmd", debt.Id.ToString(),
            $"UE {debt.UeCode} apurée avec {input.Mark}/{regulation.MarkScale} par {Actor}.");
        await db.SaveChangesAsync(ct);
        return (await DebtsAsync(debt.StudentId, ct)).First(x => x.Id == debt.Id);
    }, ct);

    // ----------------------------------------------------------------- impression

    public async Task<LmdTranscriptPrintDto> TranscriptAsync(Guid promotionId, string? stage, Guid? enrollmentId,
        CancellationToken ct = default)
    {
        var c = await LoadAsync(promotionId, false, ct);
        var status = StatusOf(c);
        var results = Results(c, stage);
        var published = results.Stage switch
        {
            "S1" => status >= LmdSessionStatus.S1Publie,
            "PremiereSession" => status >= LmdSessionStatus.PremiereSessionCloturee,
            _ => status == LmdSessionStatus.Cloturee,
        };
        if (!published)
            throw new DomainException("deliberationLmd", "Un relevé officiel s'imprime après la publication de l'étape choisie.");
        var students = enrollmentId is { } one ? results.Students.Where(s => s.EnrollmentId == one).ToList() : results.Students.ToList();
        if (students.Count == 0) throw new DomainException("deliberationLmd", "Aucun étudiant à imprimer.");
        return new LmdTranscriptPrintDto(c.School.Name, c.School.Address, c.School.City, c.School.Logo,
            c.School.CurrencySymbol, results, students);
    }
}
