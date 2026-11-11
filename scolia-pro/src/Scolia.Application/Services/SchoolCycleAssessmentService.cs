using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Prépare les évaluations maternelles / primaires / secondaires sans toucher
/// aux cotes ITM/IM, aux délibérations du jury ni au moteur LMD.
/// Les écritures restent verrouillées tant que le profil n'est pas prêt.
/// </summary>
public sealed class SchoolCycleAssessmentService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    private sealed record Context(
        SchoolProfile School, AcademicYear Year, SchoolCycle Cycle,
        Promotion Promotion, Enrollment Enrollment);

    private async Task<Context> ResolveAsync(Guid enrollmentId, CancellationToken ct)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstAsync(ct);
        var profile = InstitutionProfiles.Canonicalize(school.InstitutionType);
        if (profile is InstitutionProfiles.ItmIm or InstitutionProfiles.SuperieurLmd)
            throw new DomainException("evaluationCycle", "Les évaluations maternelles, primaires et secondaires n'appartiennent pas au moteur ITM/IM ou LMD.");
        if (!InstitutionProfiles.IsKnown(profile))
            throw new DomainException("evaluationCycle", "Type d'établissement inconnu.");

        var enrollment = await db.Enrollments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enrollmentId, ct)
            ?? throw new NotFoundException("Inscription introuvable.");
        var promotion = await db.Promotions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enrollment.PromotionId, ct)
            ?? throw new NotFoundException("Classe introuvable.");
        var year = await db.AcademicYears.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enrollment.AcademicYearId, ct)
            ?? throw new NotFoundException("Année scolaire introuvable.");
        if (year.Id != promotion.AcademicYearId)
            throw new DomainException("evaluationCycle", "La classe et l'inscription ne correspondent pas à la même année.");
        var cycle = promotion.SchoolCycleId is { } id
            ? await db.SchoolCycles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.SchoolId == school.Id, ct)
            : null;
        if (cycle is null)
            throw new DomainException("evaluationCycle", "Cette classe n'est rattachée à aucun cycle scolaire.");
        SchoolCycleRules.ValidatePromotionCycle(school, cycle, promotion);
        return new Context(school, year, cycle, promotion, enrollment);
    }

    public async Task<SchoolCycleBulletinPreviewDto> PreviewAsync(
        Guid enrollmentId, CancellationToken ct = default)
    {
        var context = await ResolveAsync(enrollmentId, ct);
        var periods = await db.SchoolCyclePeriods.AsNoTracking()
            .Where(p => p.AcademicYearId == context.Year.Id &&
                        p.SchoolCycleId == context.Cycle.Id)
            .OrderBy(p => p.Order).ToListAsync(ct);
        var rows = await db.SchoolCycleAssessments.AsNoTracking()
            .Where(a => a.EnrollmentId == enrollmentId &&
                        a.AcademicYearId == context.Year.Id &&
                        a.SchoolCycleId == context.Cycle.Id).ToListAsync(ct);

        // L'aperçu refuse toute collision ou donnée venant d'un autre cycle.
        _ = SchoolCycleAssessmentRules.Preview(context.Cycle, enrollmentId, periods, rows,
            context.Promotion.Id, context.Year.Id);
        var periodById = periods.ToDictionary(p => p.Id);
        var list = rows.OrderBy(x => periodById[x.SchoolCyclePeriodId].Order)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => ToDto(x, periodById[x.SchoolCyclePeriodId].Code)).ToArray();
        // Les regroupements sont ceux du calendrier de CE cycle et de CETTE
        // année : jamais les mensualités de la caisse ni les sessions ITM.
        // Conserver aussi les périodes sans notes et les cotes non encodées.
        var byPeriodEntries = list.GroupBy(x => x.SchoolCyclePeriodId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SchoolCycleAssessmentItemDto>)g.ToArray());
        var termPrefix = context.Cycle.Kind switch
        {
            SchoolCycleKind.Primaire => "Trimestre",
            SchoolCycleKind.Secondaire => "Semestre",
            _ => "Période d'observation",
        };
        var terms = periods.OrderBy(p => p.Order)
            .GroupBy(p => p.TermCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SchoolCycleBulletinTermDto(
                group.Key, $"{termPrefix} {group.Key}",
                group.Select(period => new SchoolCycleBulletinPeriodDto(
                    period.Id, period.Code, period.Name, period.TermCode,
                    period.StartDate, period.EndDate, period.Mode.ToString(),
                    byPeriodEntries.GetValueOrDefault(period.Id)
                        ?? Array.Empty<SchoolCycleAssessmentItemDto>())).ToArray()))
            .ToArray();

        return new SchoolCycleBulletinPreviewDto(enrollmentId,
            context.Cycle.Id, context.Cycle.Name, context.Promotion.Name,
            context.Year.Label, list, Terms: terms);
    }

    public Task<SchoolCycleAssessmentItemDto> SaveAsync(
        Guid enrollmentId, SchoolCycleAssessmentInput input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var context = await ResolveAsync(enrollmentId, ct);
        if (!InstitutionProfiles.IsEnabled(context.School.InstitutionType))
            throw new DomainException("evaluationCycle",
                "Les évaluations des nouveaux cycles restent verrouillées tant que leurs programmes et bulletins officiels ne sont pas validés.");
        if (!context.Year.IsCurrent || context.Year.Status != AcademicYearStatus.Open)
            throw new DomainException("evaluationCycle",
                "Les évaluations ne peuvent être enregistrées que pendant une année scolaire courante ouverte.");
        if (context.Enrollment.Status != EnrollmentStatus.Actif)
            throw new DomainException("evaluationCycle", "Cette inscription n'est pas active.");
        var period = await db.SchoolCyclePeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == input.SchoolCyclePeriodId &&
                p.AcademicYearId == context.Year.Id && p.SchoolCycleId == context.Cycle.Id, ct)
            ?? throw new DomainException("evaluationCycle", "Période du cycle introuvable.");

        var subject = input.CurriculumItemId is { } subjectId
            ? await db.CurriculumItems.AsNoTracking().FirstOrDefaultAsync(c => c.Id == subjectId, ct)
            : null;
        var code = (input.EvaluationCode ?? string.Empty).Trim().ToUpperInvariant();
        if (input.Score is { } score && score != decimal.Round(score, 2) ||
            input.MaxScore is { } max && max != decimal.Round(max, 2))
            throw new DomainException("evaluationCycle", "Les cotes et maxima acceptent deux décimales au maximum.");

        var candidate = new SchoolCycleAssessment
        {
            AcademicYearId = context.Year.Id,
            SchoolCycleId = context.Cycle.Id,
            PromotionId = context.Promotion.Id,
            EnrollmentId = enrollmentId,
            SchoolCyclePeriodId = period.Id,
            CurriculumItemId = input.CurriculumItemId,
            EvaluationCode = code,
            Title = (input.Title ?? string.Empty).Trim(),
            Observation = string.IsNullOrWhiteSpace(input.Observation) ? null : input.Observation.Trim(),
            Score = input.Score,
            MaxScore = input.MaxScore,
            Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(),
            Mode = period.Mode,
        };
        SchoolCycleAssessmentRules.Validate(context.Cycle, period,
            context.Promotion, context.Enrollment, subject, candidate);

        var existing = await db.SchoolCycleAssessments.FirstOrDefaultAsync(
            a => a.EnrollmentId == enrollmentId &&
                 a.SchoolCyclePeriodId == period.Id && a.EvaluationCode == code, ct);
        SchoolCycleAssessment saved;
        if (existing is null)
        {
            if (input.ExpectedVersion is not null)
                throw new ConflictException("Évaluation introuvable : actualisez la feuille.");
            db.SchoolCycleAssessments.Add(candidate);
            saved = candidate;
        }
        else
        {
            if (input.ExpectedVersion != existing.Version)
                throw new ConflictException("Cette évaluation a changé depuis votre dernière lecture : actualisez la feuille.");
            existing.CurriculumItemId = candidate.CurriculumItemId;
            existing.Title = candidate.Title;
            existing.Observation = candidate.Observation;
            existing.Score = candidate.Score;
            existing.MaxScore = candidate.MaxScore;
            existing.Comment = candidate.Comment;
            saved = existing;
        }
        audit.Record(existing is null ? "evaluation.cycle.creee" : "evaluation.cycle.modifiee",
            "EvaluationCycle", saved.Id.ToString(),
            $"Évaluation {code} : {context.Cycle.Name}, {context.Promotion.Name}, {period.Code}.");
        await db.SaveChangesAsync(ct);
        return ToDto(saved, period.Code);
    }, ct);

    private static SchoolCycleAssessmentItemDto ToDto(SchoolCycleAssessment item, string periodCode) =>
        new(item.Id, item.SchoolCyclePeriodId, periodCode,
            item.EvaluationCode, item.Title, item.Mode.ToString(),
            item.CurriculumItemId, item.Observation,
            item.Score, item.MaxScore, item.Comment, item.Version);
}
