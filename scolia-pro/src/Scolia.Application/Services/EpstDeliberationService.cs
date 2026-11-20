using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>
/// Délibération de fin d'année d'une classe du primaire ou du secondaire :
/// décisions calculées selon le règlement validé, décisions motivées du jury,
/// clôture de la première session, repêchage puis clôture définitive.
/// Une fois la première session clôturée, les cotes de la classe sont figées.
/// </summary>
public sealed class EpstDeliberationService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user,
    EpstResultsService results)
{
    private string Actor => string.IsNullOrWhiteSpace(user.Name) ? "Utilisateur" : user.Name.Trim();

    private async Task<(EpstResultsService.Computation C, EpstDeliberation? D, List<EpstDecision> Decisions,
        List<EpstRepechageMark> Marks)> LoadAsync(Guid promotionId, bool tracking, CancellationToken ct)
    {
        var c = await results.ComputeAsync(promotionId, ct);
        var query = tracking ? db.EpstDeliberations : db.EpstDeliberations.AsNoTracking();
        var d = await query.FirstOrDefaultAsync(x => x.PromotionId == c.Classroom.Id && x.AcademicYearId == c.Year.Id, ct);
        if (d is null) return (c, null, [], []);
        var decisions = tracking
            ? await db.EpstDecisions.Where(x => x.DeliberationId == d.Id).ToListAsync(ct)
            : await db.EpstDecisions.AsNoTracking().Where(x => x.DeliberationId == d.Id).ToListAsync(ct);
        var marks = tracking
            ? await db.EpstRepechageMarks.Where(x => x.DeliberationId == d.Id).ToListAsync(ct)
            : await db.EpstRepechageMarks.AsNoTracking().Where(x => x.DeliberationId == d.Id).ToListAsync(ct);
        return (c, d, decisions, marks);
    }

    public async Task<EpstDeliberationDto> GetAsync(Guid promotionId, CancellationToken ct = default)
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, false, ct);
        return ToDto(c, d, decisions, marks);
    }

    /// <summary>Palmarès et procès-verbal : disponibles après la clôture de la première session.</summary>
    public async Task<EpstPalmaresPrintDto> PalmaresAsync(Guid promotionId, CancellationToken ct = default)
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, false, ct);
        if (d is null || d.Status == EpstSessionStatus.Ouverte)
            throw new DomainException("deliberationEpst", "Le palmarès est publié après la clôture de la première session.");
        return new EpstPalmaresPrintDto(c.School.Name, c.School.Address, c.School.City, c.School.CardMinistry,
            c.School.CardDivision, c.School.Logo, c.Cycle.Name, ToDto(c, d, decisions, marks));
    }

    private static EpstDeliberationDto ToDto(EpstResultsService.Computation c, EpstDeliberation? d,
        IReadOnlyList<EpstDecision> decisions, IReadOnlyList<EpstRepechageMark> marks)
    {
        var byEnrollment = c.Enrollments.ToDictionary(e => e.Id);
        var open = d is null || d.Status == EpstSessionStatus.Ouverte;
        var pupils = c.Result.Pupils.Select(p =>
        {
            var e = byEnrollment[p.EnrollmentId];
            var stored = decisions.FirstOrDefault(x => x.EnrollmentId == p.EnrollmentId);
            var (computed, subjects) = open || stored is null
                ? EpstDeliberationRules.FirstSession(c.Regulation, p)
                : (stored.Computed, stored.RepechageSubjectIds);
            var effective = stored?.Final ?? stored?.Jury ?? computed;
            if (!open && stored is not null) subjects = stored.RepechageSubjectIds;
            var lines = subjects.Select(id =>
            {
                var subject = p.Subjects.First(s => s.SubjectId == id);
                var mark = marks.FirstOrDefault(m => m.EnrollmentId == p.EnrollmentId && m.SubjectId == id);
                return new EpstRepechageLineDto(id, subject.Name, subject.Percent, mark?.Score, mark?.MaxScore);
            }).ToList();
            return new EpstDecisionDto(e.Id, e.Student!.Matricule, e.Student.FullName,
                open ? p.Percent : stored?.Percent ?? p.Percent,
                c.Regulation.ShowRank ? (open ? p.Rank : stored?.Rank ?? p.Rank) : null,
                p.FailedSubjects, p.MissingCells,
                computed.ToString(), stored?.Jury?.ToString(), stored?.JuryReason, stored?.Final?.ToString(),
                effective.ToString(), EpstDeliberationRules.Label(effective), lines);
        })
        .OrderBy(x => x.Rank ?? int.MaxValue).ThenBy(x => x.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
        return new EpstDeliberationDto(c.Classroom.Id, c.Classroom.Name, c.Year.Label,
            (d?.Status ?? EpstSessionStatus.Ouverte).ToString(),
            d?.FirstSessionClosedBy, d?.FirstSessionClosedAtUtc, d?.ClosedBy, d?.ClosedAtUtc,
            c.Regulation.PassPercent, c.Regulation.SubjectPassPercent, c.Regulation.MaxRepechageSubjects, pupils);
    }

    private EpstDeliberation Ensure(EpstResultsService.Computation c, ref EpstDeliberation? d)
    {
        if (d is not null) return d;
        d = new EpstDeliberation { AcademicYearId = c.Year.Id, PromotionId = c.Classroom.Id };
        db.EpstDeliberations.Add(d);
        return d;
    }

    private static void RequireOpenYear(EpstResultsService.Computation c)
    {
        if (c.Year.Status == AcademicYearStatus.Closed)
            throw new DomainException("deliberationEpst", "L'année scolaire est clôturée.");
    }

    /// <summary>Décision motivée du jury avant la clôture de la première session.</summary>
    public Task<EpstDeliberationDto> SetJuryAsync(Guid promotionId, Guid enrollmentId, EpstJuryInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (d is { Status: not EpstSessionStatus.Ouverte })
            throw new DomainException("deliberationEpst", "La première session est clôturée : la décision du jury est publiée.");
        var pupil = c.Result.Pupils.FirstOrDefault(p => p.EnrollmentId == enrollmentId)
            ?? throw new NotFoundException("Élève introuvable dans cette classe.");
        EpstOutcome? jury = string.IsNullOrWhiteSpace(input.Outcome) ? null
            : Enum.TryParse<EpstOutcome>(input.Outcome, true, out var parsed) && Enum.IsDefined(parsed) ? parsed
            : throw new DomainException("deliberationEpst", "Décision du jury inconnue.");
        EpstDeliberationRules.ValidateJury(jury, input.Reason);
        if (jury == EpstOutcome.Repechage && !pupil.Subjects.Any(s => s.Failed))
            throw new DomainException("deliberationEpst", "Le repêchage suppose au moins une branche en échec.");
        var deliberation = Ensure(c, ref d);
        var decision = decisions.FirstOrDefault(x => x.EnrollmentId == enrollmentId);
        if (decision is null)
        {
            decision = new EpstDecision { DeliberationId = deliberation.Id, EnrollmentId = enrollmentId };
            db.EpstDecisions.Add(decision);
            decisions.Add(decision);
        }
        decision.Computed = EpstDeliberationRules.FirstSession(c.Regulation, pupil).Outcome;
        decision.Jury = jury;
        decision.JuryReason = jury is null ? null : input.Reason!.Trim();
        audit.Record("deliberationEpst.jury", "DecisionCycle", enrollmentId.ToString(),
            jury is null
                ? $"{c.Classroom.Name} : décision du jury retirée, retour au calcul."
                : $"{c.Classroom.Name} : jury « {EpstDeliberationRules.Label(jury.Value)} » — {decision.JuryReason}");
        await db.SaveChangesAsync(ct);
        return ToDto(c, d, decisions, marks);
    }, ct);

    /// <summary>
    /// Clôture la première session : décisions publiées, cotes figées. S'il n'y a aucun
    /// repêchage, l'année de la classe est directement clôturée.
    /// </summary>
    public Task<EpstDeliberationDto> CloseFirstSessionAsync(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (d is { Status: not EpstSessionStatus.Ouverte })
            throw new DomainException("deliberationEpst", "La première session est déjà clôturée.");
        var names = c.Enrollments.ToDictionary(e => e.Id, e => e.Student!.FullName);
        var undecided = new List<string>();
        var deliberation = Ensure(c, ref d);
        foreach (var pupil in c.Result.Pupils)
        {
            var (computed, failed) = EpstDeliberationRules.FirstSession(c.Regulation, pupil);
            var decision = decisions.FirstOrDefault(x => x.EnrollmentId == pupil.EnrollmentId);
            if (decision is null)
            {
                decision = new EpstDecision { DeliberationId = deliberation.Id, EnrollmentId = pupil.EnrollmentId };
                db.EpstDecisions.Add(decision);
                decisions.Add(decision);
            }
            decision.Computed = computed;
            decision.Percent = pupil.Percent;
            decision.Rank = pupil.Rank;
            var effective = decision.Jury ?? computed;
            if (effective == EpstOutcome.Incomplet) undecided.Add(names[pupil.EnrollmentId]);
            decision.RepechageSubjects = effective == EpstOutcome.Repechage
                ? string.Join(',', (failed.Count > 0 ? failed : pupil.Subjects.Where(s => s.Failed).Select(s => s.SubjectId)))
                : string.Empty;
            if (effective == EpstOutcome.Repechage && decision.RepechageSubjects.Length == 0)
                undecided.Add(names[pupil.EnrollmentId]);
        }
        if (undecided.Count > 0)
            throw new DomainException("deliberationEpst",
                "Résultats incomplets : saisissez les points manquants ou fixez la décision du jury pour " +
                string.Join(", ", undecided.Order()) + ".");
        var actor = Actor;
        deliberation.Status = EpstSessionStatus.PremiereSessionCloturee;
        deliberation.FirstSessionClosedBy = actor;
        deliberation.FirstSessionClosedAtUtc = DateTime.UtcNow;
        if (!decisions.Any(x => x.Effective == EpstOutcome.Repechage))
            Finalize(deliberation, decisions, actor);
        audit.Record("deliberationEpst.premiereSession", "Promotion", c.Classroom.Id.ToString(),
            $"{c.Classroom.Name} {c.Year.Label} : première session clôturée par {actor}. " + Counts(decisions));
        await db.SaveChangesAsync(ct);
        return ToDto(c, d, decisions, marks);
    }, ct);

    public Task<EpstDeliberationDto> SaveRepechageAsync(Guid promotionId, Guid enrollmentId, EpstRepechageInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (d is not { Status: EpstSessionStatus.PremiereSessionCloturee })
            throw new DomainException("deliberationEpst", "Le repêchage s'ouvre après la clôture de la première session.");
        var decision = decisions.FirstOrDefault(x => x.EnrollmentId == enrollmentId);
        if (decision is not { Effective: EpstOutcome.Repechage })
            throw new DomainException("deliberationEpst", "Cet élève n'est pas admis au repêchage.");
        var allowed = decision.RepechageSubjectIds;
        foreach (var line in input.Marks ?? [])
        {
            if (!allowed.Contains(line.SubjectId))
                throw new DomainException("deliberationEpst", "Cette branche n'est pas présentée au repêchage par l'élève.");
            var mark = marks.FirstOrDefault(m => m.EnrollmentId == enrollmentId && m.SubjectId == line.SubjectId);
            if (mark is null)
            {
                mark = new EpstRepechageMark { DeliberationId = d.Id, EnrollmentId = enrollmentId, SubjectId = line.SubjectId };
                db.EpstRepechageMarks.Add(mark);
                marks.Add(mark);
            }
            mark.Score = line.Score;
            mark.MaxScore = line.MaxScore;
            EpstDeliberationRules.ValidateMark(mark);
        }
        audit.Record("deliberationEpst.repechage", "DecisionCycle", enrollmentId.ToString(),
            $"{c.Classroom.Name} : points de repêchage saisis ({(input.Marks ?? []).Count} branche(s)).");
        await db.SaveChangesAsync(ct);
        return ToDto(c, d, decisions, marks);
    }, ct);

    /// <summary>Clôture le repêchage : issue définitive de chaque élève de la classe.</summary>
    public Task<EpstDeliberationDto> CloseAsync(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (c, d, decisions, marks) = await LoadAsync(promotionId, true, ct);
        RequireOpenYear(c);
        if (d is not { Status: EpstSessionStatus.PremiereSessionCloturee })
            throw new DomainException("deliberationEpst", "Clôturez d'abord la première session.");
        var names = c.Enrollments.ToDictionary(e => e.Id, e => e.Student!.FullName);
        var missing = new List<string>();
        foreach (var decision in decisions.Where(x => x.Effective == EpstOutcome.Repechage))
        {
            var own = marks.Where(m => m.EnrollmentId == decision.EnrollmentId).ToList();
            if (decision.RepechageSubjectIds.Any(id => own.FirstOrDefault(m => m.SubjectId == id) is not { Score: not null }))
                missing.Add(names[decision.EnrollmentId]);
        }
        if (missing.Count > 0)
            throw new DomainException("deliberationEpst",
                "Points de repêchage manquants pour " + string.Join(", ", missing.Order()) + ".");
        Finalize(d, decisions, Actor, c.Regulation, marks);
        audit.Record("deliberationEpst.cloture", "Promotion", c.Classroom.Id.ToString(),
            $"{c.Classroom.Name} {c.Year.Label} : repêchage clôturé par {d.ClosedBy}. " + Counts(decisions));
        await db.SaveChangesAsync(ct);
        return ToDto(c, d, decisions, marks);
    }, ct);

    private static void Finalize(EpstDeliberation d, IEnumerable<EpstDecision> decisions, string actor,
        AcademicRegulation? regulation = null, IReadOnlyCollection<EpstRepechageMark>? marks = null)
    {
        foreach (var decision in decisions)
        {
            var effective = decision.Jury ?? decision.Computed;
            decision.Final = effective == EpstOutcome.Repechage
                ? EpstDeliberationRules.AfterRepechage(regulation!, decision.RepechageSubjectIds,
                    (marks ?? []).Where(m => m.EnrollmentId == decision.EnrollmentId).ToList())
                : effective;
        }
        d.Status = EpstSessionStatus.Cloturee;
        d.ClosedBy = actor;
        d.ClosedAtUtc = DateTime.UtcNow;
    }

    private static string Counts(IEnumerable<EpstDecision> decisions) =>
        string.Join(", ", decisions.GroupBy(x => x.Effective).OrderBy(g => g.Key)
            .Select(g => $"{EpstDeliberationRules.Label(g.Key)} : {g.Count()}"));

    /// <summary>Refuse toute modification de cotes ou de conduite après la première session.</summary>
    public static async Task EnsureMarksEditableAsync(IAppDbContext db, Guid promotionId, Guid yearId, CancellationToken ct)
    {
        if (await db.EpstDeliberations.AsNoTracking().AnyAsync(d =>
                d.PromotionId == promotionId && d.AcademicYearId == yearId && d.Status != EpstSessionStatus.Ouverte, ct))
            throw new DomainException("deliberationEpst",
                "La délibération de cette classe est clôturée : les cotes et la conduite sont figées.");
    }
}
