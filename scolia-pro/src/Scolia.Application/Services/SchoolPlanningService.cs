using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>Périodes et matières annuelles créées sans altérer les historiques d'un autre cycle.</summary>
public sealed class SchoolPlanningService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    private async Task<(SchoolProfile School, SchoolCycle Cycle, AcademicYear Year)> ContextAsync(
        Guid id, CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("cycles", "Module réservé à la maternelle, au primaire et au secondaire.");
        var cycle = await db.SchoolCycles.AsNoTracking().FirstOrDefaultAsync(
            c => c.Id == id && c.SchoolId == school.Id && c.IsActive, ct)
            ?? throw new NotFoundException("Cycle scolaire actif introuvable.");
        return (school, cycle, await YearContext.CurrentAsync(db, ct));
    }

    private async Task<(SchoolProfile School, SchoolCycle Cycle, AcademicYear Year, Promotion Class)> ClassAsync(
        Guid id, CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("cycles", "Module réservé aux cycles scolaires.");
        var year = await YearContext.CurrentAsync(db, ct);
        var classroom = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == id && p.AcademicYearId == year.Id && p.IsActive, ct)
            ?? throw new NotFoundException("Classe active introuvable.");
        var cycle = await db.SchoolCycles.AsNoTracking().FirstOrDefaultAsync(
            c => classroom.SchoolCycleId == c.Id && c.SchoolId == school.Id && c.IsActive, ct)
            ?? throw new DomainException("cycles", "Classe sans cycle scolaire actif.");
        return (school, cycle, year, classroom);
    }

    private static SchoolPeriodDto ToDto(SchoolCyclePeriod p) =>
        new(p.Id, p.Code, p.Name, p.TermCode, p.Order, p.Mode.ToString(), p.StartDate, p.EndDate, p.IsExam);
    private static AnnualSubjectDto ToDto(CurriculumItem i) =>
        new(i.Id, i.PromotionId, i.Code, i.Name, i.Coefficient, i.Hours, i.IsActive, i.PeriodMaxScore);

    public async Task<IReadOnlyList<SchoolPeriodDto>> PeriodsAsync(Guid cycleId, CancellationToken ct = default)
    {
        var (_, cycle, year) = await ContextAsync(cycleId, ct);
        var periods = await db.SchoolCyclePeriods.AsNoTracking()
            .Where(p => p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id)
            .OrderBy(p => p.Order).ToListAsync(ct);
        return periods.Select(ToDto).ToList();
    }

    public Task<SchoolPeriodDto> AddPeriodAsync(Guid cycleId, SchoolPeriodCreate input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (_, cycle, year) = await ContextAsync(cycleId, ct);
        if (year.Status != AcademicYearStatus.Open)
            throw new DomainException("cycles", "L'année clôturée ne peut pas être modifiée.");
        var periods = await db.SchoolCyclePeriods.AsNoTracking()
            .Where(p => p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id)
            .OrderBy(p => p.Order).ToListAsync(ct);
        var cycleClasses = db.Promotions.Where(p => p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id)
            .Select(p => p.Id);
        if (await db.EpstDeliberations.AnyAsync(d => cycleClasses.Contains(d.PromotionId) &&
                d.Status != EpstSessionStatus.Ouverte, ct))
            throw new DomainException("cycles", "Une délibération de ce cycle est clôturée : le calendrier est figé.");
        var next = new SchoolCyclePeriod
        {
            SchoolCycleId = cycle.Id, AcademicYearId = year.Id,
            Code = (input.Code ?? string.Empty).Trim().ToUpperInvariant(),
            Name = (input.Name ?? string.Empty).Trim(),
            TermCode = (input.TermCode ?? string.Empty).Trim().ToUpperInvariant(),
            Order = periods.Count + 1,
            Mode = cycle.Kind == SchoolCycleKind.Maternelle
                ? SchoolCycleAssessmentMode.Observation : SchoolCycleAssessmentMode.Evaluation,
            IsExam = input.IsExam,
            StartDate = input.StartDate, EndDate = input.EndDate,
        };
        SchoolCycleRules.ValidateCalendar(cycle, year, [..periods, next]);
        db.SchoolCyclePeriods.Add(next);
        audit.Record("cycle.periode.creee", "PeriodeCycle", next.Id.ToString(),
            $"{(next.IsExam ? "Examen" : "Période")} {next.Code} : cycle {cycle.Name}. Aucun historique modifié.");
        await db.SaveChangesAsync(ct);
        return ToDto(next);
    }, ct);

    public async Task<IReadOnlyList<AnnualSubjectDto>> SubjectsAsync(Guid promotionId, CancellationToken ct = default)
    {
        var (_, _, year, classroom) = await ClassAsync(promotionId, ct);
        var items = await db.CurriculumItems.AsNoTracking().Where(i =>
            i.AcademicYearId == year.Id && i.PromotionId == classroom.Id &&
            i.Kind == CurriculumItemKind.AnnualSubject)
            .OrderBy(i => i.Name).ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public Task<AnnualSubjectDto> AddSubjectAsync(Guid promotionId, AnnualSubjectCreate input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var (school, cycle, year, classroom) = await ClassAsync(promotionId, ct);
        if (year.Status != AcademicYearStatus.Open)
            throw new DomainException("programme", "Le programme d'une année clôturée ne peut pas changer.");
        await EpstDeliberationService.EnsureMarksEditableAsync(db, classroom.Id, year.Id, ct);
        var current = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.AcademicYearId == year.Id && i.PromotionId == classroom.Id).ToListAsync(ct);
        var item = new CurriculumItem
        {
            AcademicYearId = year.Id, PromotionId = classroom.Id,
            Kind = CurriculumItemKind.AnnualSubject, Semester = CourseSemester.Annuel,
            Code = (input.Code ?? string.Empty).Trim().ToUpperInvariant(),
            Name = (input.Name ?? string.Empty).Trim(),
            Coefficient = input.Coefficient, Hours = input.Hours, Credits = 0,
            PeriodMaxScore = input.PeriodMaxScore,
        };
        CurriculumStructureRules.ValidateForSchoolCycle(school, cycle, year, [..current, item]);
        db.CurriculumItems.Add(item);
        audit.Record("cycle.matiere.creee", "MatiereCycle", item.Id.ToString(),
            $"Matière {item.Code} : classe {classroom.Name}, cycle {cycle.Name}.");
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }, ct);

    /// <summary>Maximum d'une branche par période. Modifiable tant que l'année est ouverte.</summary>
    public Task<AnnualSubjectDto> SetSubjectMaxAsync(Guid subjectId, AnnualSubjectMaxUpdate input,
        CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var item = await db.CurriculumItems.FirstOrDefaultAsync(
            i => i.Id == subjectId && i.Kind == CurriculumItemKind.AnnualSubject, ct)
            ?? throw new NotFoundException("Branche introuvable.");
        var (school, cycle, year, classroom) = await ClassAsync(item.PromotionId, ct);
        if (year.Status != AcademicYearStatus.Open || item.AcademicYearId != year.Id)
            throw new DomainException("programme", "Le programme d'une année clôturée ne peut pas changer.");
        await EpstDeliberationService.EnsureMarksEditableAsync(db, classroom.Id, year.Id, ct);
        var previous = item.PeriodMaxScore;
        item.PeriodMaxScore = input.PeriodMaxScore;
        var current = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.AcademicYearId == year.Id && i.PromotionId == classroom.Id && i.Id != item.Id)
            .ToListAsync(ct);
        CurriculumStructureRules.ValidateForSchoolCycle(school, cycle, year, [..current, item]);
        audit.Record("cycle.matiere.maximum", "MatiereCycle", item.Id.ToString(),
            $"Maximum par période de {item.Code} ({classroom.Name}) : {previous?.ToString() ?? "aucun"} → " +
            $"{item.PeriodMaxScore?.ToString() ?? "aucun"}.");
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }, ct);
}
