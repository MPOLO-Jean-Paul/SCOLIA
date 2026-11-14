using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Résultats officiels d'une classe du primaire ou du secondaire. Exige un règlement
/// validé, un calendrier conforme à ce règlement et le maximum de chaque branche.
/// Lecture seule : aucune cote n'est modifiée ici.
/// </summary>
public sealed class EpstResultsService(IAppDbContext db)
{
    public async Task<EpstClassResultsDto> ClassAsync(Guid promotionId, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("resultatsEpst", "Les résultats par classe concernent le primaire et le secondaire.");
        var classroom = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId, ct)
            ?? throw new NotFoundException("Classe introuvable.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == classroom.AcademicYearId, ct);
        var cycle = classroom.SchoolCycleId is { } cycleId
            ? await db.SchoolCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cycleId && c.SchoolId == school.Id, ct)
            : null;
        if (cycle is null)
            throw new DomainException("resultatsEpst", "Cette classe n'est rattachée à aucun cycle scolaire.");
        if (cycle.Kind == SchoolCycleKind.Maternelle)
            throw new DomainException("resultatsEpst", "La maternelle utilise un carnet d'observations, sans points ni place.");

        var scope = AcademicRegulationRules.ScopeOf(cycle.Kind);
        var regulation = await AcademicRegulationService.RequireValidatedAsync(db, year.Id, scope, ct);

        var periods = await db.SchoolCyclePeriods.AsNoTracking()
            .Where(p => p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id)
            .OrderBy(p => p.Order).ToListAsync(ct);
        var subjects = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.PromotionId == classroom.Id && i.AcademicYearId == year.Id &&
                        i.Kind == CurriculumItemKind.AnnualSubject && i.IsActive)
            .ToListAsync(ct);
        var withoutMax = subjects.Where(s => s.PeriodMaxScore is not > 0).Select(s => s.Name).Order().ToList();
        if (withoutMax.Count > 0)
            throw new DomainException("resultatsEpst",
                "Indiquez le maximum par période des branches : " + string.Join(", ", withoutMax) + ".");

        var enrollments = await db.Enrollments.AsNoTracking().Include(e => e.Student)
            .Where(e => e.PromotionId == classroom.Id && e.AcademicYearId == year.Id &&
                        e.Status != EnrollmentStatus.Sorti && e.Status != EnrollmentStatus.Transfere)
            .ToListAsync(ct);
        var subjectIds = subjects.Select(s => s.Id).ToHashSet();
        var scores = await db.SchoolCycleAssessments.AsNoTracking()
            .Where(a => a.PromotionId == classroom.Id && a.AcademicYearId == year.Id &&
                        a.SchoolCycleId == cycle.Id && a.CurriculumItemId != null && a.MaxScore != null)
            .Select(a => new { a.EnrollmentId, SubjectId = a.CurriculumItemId!.Value, a.SchoolCyclePeriodId, a.Score, a.MaxScore })
            .ToListAsync(ct);

        var result = EpstResultsCalculator.Compute(regulation,
            periods.Select(p => new EpstPeriodSpec(p.Id, p.Code, p.TermCode, p.Order, p.IsExam)).ToList(),
            subjects.Select(s => new EpstSubjectSpec(s.Id, s.Code, s.Name, s.PeriodMaxScore!.Value)).ToList(),
            enrollments.Select(e => e.Id).ToList(),
            scores.Where(s => subjectIds.Contains(s.SubjectId))
                .Select(s => new EpstScore(s.EnrollmentId, s.SubjectId, s.SchoolCyclePeriodId, s.Score, s.MaxScore!.Value))
                .ToList());

        var conduct = await db.EpstConductMarks.AsNoTracking()
            .Where(c => c.PromotionId == classroom.Id && c.AcademicYearId == year.Id).ToListAsync(ct);
        var workPeriods = result.Periods.Where(p => !p.IsExam).ToList();
        IReadOnlyList<EpstConductCellDto> ConductOf(Guid enrollmentId) => workPeriods.Select(p =>
        {
            var mark = conduct.FirstOrDefault(c => c.EnrollmentId == enrollmentId && c.SchoolCyclePeriodId == p.PeriodId);
            return new EpstConductCellDto(p.PeriodId, p.Code, mark?.Application, mark?.Conduct);
        }).ToList();

        static EpstCellDto Cell(EpstCell c) => new(c.Points, c.Max);
        var byId = enrollments.ToDictionary(e => e.Id);
        var pupils = result.Pupils.Select(p =>
        {
            var e = byId[p.EnrollmentId];
            var s = e.Student!;
            return new EpstPupilResultDto(e.Id, s.Id, s.Matricule, s.FullName,
                p.Subjects.Select(x => new EpstSubjectLineDto(x.SubjectId, x.Code, x.Name,
                    x.Periods.Select(Cell).ToList(), x.Terms.Select(Cell).ToList(),
                    x.Total, x.Max, x.Percent, x.Failed)).ToList(),
                p.PeriodTotals.Select(Cell).ToList(), p.TermTotals.Select(Cell).ToList(),
                p.Total, p.Max, p.Percent, p.MissingCells, p.FailedSubjects,
                regulation.ShowRank ? p.Rank : null,
                regulation.ShowRank ? p.TermRanks : p.TermRanks.Select(_ => (int?)null).ToList(),
                ConductOf(e.Id));
        })
        // Ordre du palmarès : place, puis nom ; les élèves incomplets à la fin.
        .OrderBy(p => p.Rank ?? int.MaxValue).ThenBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return new EpstClassResultsDto(classroom.Id, classroom.Name, cycle.Name, scope.ToString(), year.Label,
            regulation.ShowRank, regulation.PassPercent, regulation.SubjectPassPercent, pupils.Count,
            result.Terms.Select(t => new EpstTermColumnDto(t.TermCode, t.Periods.Select(p => p.Code).ToList())).ToList(),
            result.Periods.Select(p => new EpstPeriodColumnDto(p.PeriodId, p.Code, p.TermCode, p.IsExam)).ToList(),
            result.Subjects.Select(s => new EpstSubjectColumnDto(s.SubjectId, s.Code, s.Name, s.PeriodMax)).ToList(),
            pupils, regulation.ValidatedByName, regulation.ValidatedAtUtc);
    }

    /// <summary>Bulletin d'un élève : mêmes calculs que la classe, pour que la place soit exacte.</summary>
    public async Task<(EpstClassResultsDto Class, EpstPupilResultDto Pupil)> PupilAsync(Guid enrollmentId,
        CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enrollmentId, ct)
            ?? throw new NotFoundException("Inscription introuvable.");
        var classResults = await ClassAsync(enrollment.PromotionId, ct);
        var pupil = classResults.Pupils.FirstOrDefault(p => p.EnrollmentId == enrollmentId)
            ?? throw new DomainException("resultatsEpst", "Cet élève n'est plus inscrit dans la classe.");
        return (classResults, pupil);
    }

    /// <summary>Données d'impression des bulletins officiels, pour toute la classe ou un seul élève.</summary>
    public async Task<EpstBulletinPrintDto> PrintAsync(Guid promotionId, Guid? enrollmentId, CancellationToken ct = default)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var results = await ClassAsync(promotionId, ct);
        var pupils = enrollmentId is { } one
            ? results.Pupils.Where(p => p.EnrollmentId == one).ToList()
            : results.Pupils.OrderBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (pupils.Count == 0)
            throw new DomainException("resultatsEpst", "Aucun élève à imprimer dans cette classe.");
        return new EpstBulletinPrintDto(school.Name, school.Address, school.City,
            school.CardMinistry, school.CardDivision, school.Logo, results, pupils);
    }

}

/// <summary>Saisie de l'application et de la conduite par classe et par période de travail.</summary>
public sealed class EpstConductService(IAppDbContext db, IWriteGate gate, IAuditLog audit)
{
    public Task<int> SaveAsync(Guid promotionId, EpstConductInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("conduite", "La conduite par période concerne le primaire et le secondaire.");
        var classroom = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId, ct)
            ?? throw new NotFoundException("Classe introuvable.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == classroom.AcademicYearId, ct);
        if (year.Status != AcademicYearStatus.Open)
            throw new DomainException("conduite", "L'année n'est pas ouverte.");
        var period = await db.SchoolCyclePeriods.AsNoTracking().FirstOrDefaultAsync(p =>
            p.Id == input.PeriodId && p.AcademicYearId == year.Id && p.SchoolCycleId == classroom.SchoolCycleId, ct)
            ?? throw new NotFoundException("Période introuvable pour cette classe.");
        if (period.IsExam || period.Mode != SchoolCycleAssessmentMode.Evaluation)
            throw new DomainException("conduite", "L'application et la conduite se notent sur les périodes de travail.");
        var lines = input.Lines ?? [];
        if (lines.Select(l => l.EnrollmentId).Distinct().Count() != lines.Count)
            throw new DomainException("conduite", "Un élève apparaît deux fois dans la saisie.");
        var ids = lines.Select(l => l.EnrollmentId).ToList();
        var enrolled = await db.Enrollments.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.PromotionId == classroom.Id && e.AcademicYearId == year.Id)
            .Select(e => e.Id).ToListAsync(ct);
        if (enrolled.Count != ids.Count)
            throw new DomainException("conduite", "Un élève de la saisie n'est pas inscrit dans cette classe.");
        var existing = await db.EpstConductMarks
            .Where(c => c.SchoolCyclePeriodId == period.Id && ids.Contains(c.EnrollmentId)).ToListAsync(ct);
        foreach (var line in lines)
        {
            var application = EpstConductScale.Normalize(line.Application);
            var conductCode = EpstConductScale.Normalize(line.Conduct);
            var mark = existing.FirstOrDefault(c => c.EnrollmentId == line.EnrollmentId);
            if (mark is null)
            {
                if (application is null && conductCode is null) continue;
                db.EpstConductMarks.Add(new EpstConductMark
                {
                    AcademicYearId = year.Id, PromotionId = classroom.Id, EnrollmentId = line.EnrollmentId,
                    SchoolCyclePeriodId = period.Id, Application = application, Conduct = conductCode,
                });
            }
            else
            {
                mark.Application = application;
                mark.Conduct = conductCode;
            }
        }
        audit.Record("cycle.conduite.saisie", "Promotion", classroom.Id.ToString(),
            $"Application et conduite de {classroom.Name}, période {period.Code} : {lines.Count} élève(s).");
        await db.SaveChangesAsync(ct);
        return lines.Count;
    }, ct);
}
