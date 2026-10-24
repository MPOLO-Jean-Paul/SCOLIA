using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Cours, évaluations, notes, résultats, présences et discipline.</summary>
public sealed class AcademicService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user, IDocumentService documents)
{
    private static readonly (AssessmentKind, string)[] KindLabels = [(AssessmentKind.TravauxPratiques, "TP"), (AssessmentKind.TravauxPratiques, "Travaux pratiques")];
    private static readonly (AttendanceStatus, string)[] AttendanceLabels =
        [(AttendanceStatus.Present, "Présent"), (AttendanceStatus.AbsenceJustifiee, "Justifié"), (AttendanceStatus.AbsenceJustifiee, "Absence justifiée")];
    private static readonly (IncidentSeverity, string)[] SeverityLabels = [(IncidentSeverity.Mineur, "Mineure"), (IncidentSeverity.Moyen, "Moyenne")];

    // ---------- Évaluations et notes ----------
    public async Task<IReadOnlyList<AssessmentDto>> AssessmentsAsync(Guid courseId, CancellationToken ct = default)
    {
        var list = await db.Assessments.AsNoTracking().Where(a => a.CourseId == courseId).OrderBy(a => a.Term).ThenBy(a => a.Date).ToListAsync(ct);
        var ids = list.Select(a => a.Id).ToList();
        var counts = await db.Grades.AsNoTracking().Where(g => ids.Contains(g.AssessmentId) && g.Score != null).GroupBy(g => g.AssessmentId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return list.Select(a => ToDto(a, counts.GetValueOrDefault(a.Id))).ToList();
    }

    private static AssessmentDto ToDto(Assessment a, int graded) => new(a.Id, a.CourseId, a.Title, a.Kind.ToString(), a.Term, a.Date, a.MaxScore, a.Weight, graded);

    public Task<AssessmentDto> UpsertAssessmentAsync(Guid? id, AssessmentUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Title) && input.Title.Length <= 120, "title", "Intitulé obligatoire.")
            .Require(input.MaxScore is > 0 and <= 1000, "maxScore", "Note maximale invalide.")
            .Require(input.Weight is > 0 and <= 100, "weight", "Poids invalide.")
            .Require(input.Term is >= 1 and <= 4, "term", "Période (semestre ou trimestre) de 1 à 4.")
            .ThrowIfInvalid();
        if (!await db.Courses.AnyAsync(c => c.Id == input.CourseId, ct)) throw new NotFoundException("Cours introuvable.");
        var assessment = id is null ? new Assessment() : await db.Assessments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Évaluation introuvable.");
        if (id is not null && input.MaxScore < assessment.MaxScore && await db.Grades.AnyAsync(g => g.AssessmentId == assessment.Id && g.Score > input.MaxScore, ct))
            throw new DomainException("maxScore", "Des notes dépassent la nouvelle note maximale.");
        assessment.CourseId = input.CourseId; assessment.Title = input.Title.Trim(); assessment.Kind = EnumText.Parse(input.Kind, "kind", KindLabels);
        assessment.Term = input.Term; assessment.Date = input.Date; assessment.MaxScore = input.MaxScore; assessment.Weight = input.Weight;
        if (id is null) db.Assessments.Add(assessment);
        audit.Record(id is null ? "evaluation.creee" : "evaluation.modifiee", "Evaluation", assessment.Id.ToString(), $"Évaluation « {assessment.Title} ».");
        await db.SaveChangesAsync(ct);
        return ToDto(assessment, 0);
    }, ct);

    public async Task<GradeSheetDto> GradeSheetAsync(Guid assessmentId, CancellationToken ct = default)
    {
        var a = await db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assessmentId, ct) ?? throw new NotFoundException("Évaluation introuvable.");
        var course = await db.Courses.AsNoTracking().FirstAsync(c => c.Id == a.CourseId, ct);
        var students = await StudentsOfAsync(course.PromotionId, ct);
        var grades = await db.Grades.AsNoTracking().Where(g => g.AssessmentId == assessmentId).ToDictionaryAsync(g => g.EnrollmentId, ct);
        var rows = students.Select(s => new GradeRow(s.EnrollmentId, s.Matricule, s.FullName, grades.GetValueOrDefault(s.EnrollmentId)?.Score, grades.GetValueOrDefault(s.EnrollmentId)?.Comment)).ToList();
        return new GradeSheetDto(ToDto(a, rows.Count(r => r.Score != null)), rows);
    }

    public Task<int> SaveGradesAsync(Guid assessmentId, IReadOnlyList<GradeInput> input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var a = await db.Assessments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == assessmentId, ct) ?? throw new NotFoundException("Évaluation introuvable.");
        var course = await db.Courses.AsNoTracking().FirstAsync(c => c.Id == a.CourseId, ct);
        var allowed = (await StudentsOfAsync(course.PromotionId, ct)).Select(s => s.EnrollmentId).ToHashSet();
        var existing = await db.Grades.Where(g => g.AssessmentId == assessmentId).ToDictionaryAsync(g => g.EnrollmentId, ct);
        var v = new ValidationBuilder();
        foreach (var g in input)
        {
            v.Require(allowed.Contains(g.EnrollmentId), "grades", "Un apprenant n'appartient pas à cette promotion.");
            v.Require(g.Score is null || (g.Score >= 0 && g.Score <= a.MaxScore), "grades", $"Les notes doivent être comprises entre 0 et {a.MaxScore}.");
        }
        v.ThrowIfInvalid();
        var changed = 0;
        foreach (var g in input)
        {
            if (existing.TryGetValue(g.EnrollmentId, out var grade))
            {
                if (grade.Score == g.Score && grade.Comment == g.Comment) continue;
                grade.Score = g.Score; grade.Comment = g.Comment;
            }
            else
            {
                if (g.Score is null && string.IsNullOrWhiteSpace(g.Comment)) continue;
                db.Grades.Add(new Grade { AssessmentId = assessmentId, EnrollmentId = g.EnrollmentId, Score = g.Score, Comment = g.Comment });
            }
            changed++;
        }
        audit.Record("notes.saisies", "Evaluation", assessmentId.ToString(), $"{changed} note(s) enregistrée(s) pour « {a.Title} » ({course.Number}).");
        await db.SaveChangesAsync(ct);
        return changed;
    }, ct);

    private sealed record EnrolledStudent(Guid EnrollmentId, string Matricule, string FullName);

    private async Task<List<EnrolledStudent>> StudentsOfAsync(Guid promotionId, CancellationToken ct) =>
        (await (from e in db.Enrollments.AsNoTracking()
                join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                where e.PromotionId == promotionId && (e.Status == EnrollmentStatus.Actif || e.Status == EnrollmentStatus.Suspendu)
                orderby s.LastName, s.MiddleName, s.FirstName
                select new { e.Id, s.Matricule, s.LastName, s.MiddleName, s.FirstName }).ToListAsync(ct))
        .Select(x => new EnrolledStudent(x.Id, x.Matricule, $"{x.LastName} {x.MiddleName} {x.FirstName}")).ToList();

    /// <summary>Grille de résultats : moyenne par cours, moyenne générale, rang et décision.</summary>
    public async Task<ResultsDto> ResultsAsync(Guid promotionId, int? term, CancellationToken ct = default)
    {
        var promotion = await db.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == promotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
        var courses = await db.Courses.AsNoTracking().Where(c => c.PromotionId == promotionId && c.IsActive).OrderBy(c => c.Number).ToListAsync(ct);
        var courseIds = courses.Select(c => c.Id).ToList();
        var assessments = await db.Assessments.AsNoTracking().Where(a => courseIds.Contains(a.CourseId) && (term == null || a.Term == term)).ToListAsync(ct);
        var assessmentIds = assessments.Select(a => a.Id).ToList();
        var grades = await db.Grades.AsNoTracking().Where(g => assessmentIds.Contains(g.AssessmentId)).ToListAsync(ct);
        var students = await StudentsOfAsync(promotionId, ct);
        var gradeMap = grades.ToDictionary(g => (g.AssessmentId, g.EnrollmentId));
        var rows = students.Select(s =>
        {
            var averages = courses.Select(c => GradeCalculator.CourseAverage(assessments.Where(a => a.CourseId == c.Id)
                .Select(a => new GradeCalculator.ScoredAssessment(gradeMap.GetValueOrDefault((a.Id, s.EnrollmentId))?.Score, a.MaxScore, a.Weight)))).ToList();
            var general = GradeCalculator.GeneralAverage(averages.Select((avg, i) => (avg, courses[i].Coefficient)));
            return new ResultRow(s.EnrollmentId, s.Matricule, s.FullName, averages, general, null, GradeCalculator.Decision(general));
        }).ToList();
        var ranked = rows.Where(r => r.Average != null).OrderByDescending(r => r.Average).ToList();
        var ranks = new Dictionary<Guid, int>();
        for (var i = 0; i < ranked.Count; i++)
            ranks[ranked[i].EnrollmentId] = i > 0 && ranked[i].Average == ranked[i - 1].Average ? ranks[ranked[i - 1].EnrollmentId] : i + 1;
        rows = rows.Select(r => r with { Rank = ranks.TryGetValue(r.EnrollmentId, out var rank) ? rank : null })
            .OrderBy(r => r.Rank ?? int.MaxValue).ThenBy(r => r.FullName).ToList();
        var withAverage = rows.Where(r => r.Average != null).ToList();
        return new ResultsDto($"{promotion.Code} — {promotion.Name}", term, courses.Select(c => c.Name).ToList(), courses.Select(c => c.Coefficient).ToList(), rows,
            withAverage.Count == 0 ? null : decimal.Round(withAverage.Average(r => r.Average!.Value), 2), withAverage.Count(r => r.Average >= 10));
    }

    public async Task<byte[]> ReportCardPdfAsync(Guid promotionId, Guid enrollmentId, int? term, CancellationToken ct = default)
    {
        var results = await ResultsAsync(promotionId, term, ct);
        var row = results.Rows.FirstOrDefault(r => r.EnrollmentId == enrollmentId) ?? throw new NotFoundException("Apprenant absent de cette promotion.");
        var school = await YearContext.SchoolAsync(db, ct);
        var promotion = await db.Promotions.AsNoTracking().FirstAsync(p => p.Id == promotionId, ct);
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == promotion.AcademicYearId, ct);
        var lines = results.Courses.Select((c, i) => (c, results.Coefficients[i], row.CourseAverages[i], 20m)).ToList();
        return documents.ReportCard(new ReportCardDocument(PaymentService.Header(school), row.FullName, row.Matricule, results.Promotion + (term is null ? "" : $" · Période {term}"),
            year.Label, lines, row.Average, row.Rank, results.Rows.Count, row.Decision));
    }

    // ---------- Présences ----------
    public async Task<AttendanceSheetDto> AttendanceSheetAsync(Guid promotionId, DateOnly date, CancellationToken ct = default)
    {
        var students = await StudentsOfAsync(promotionId, ct);
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var records = await db.Attendance.AsNoTracking().Where(a => a.Date == date && ids.Contains(a.EnrollmentId)).ToDictionaryAsync(a => a.EnrollmentId, ct);
        return new AttendanceSheetDto(promotionId, date, students.Select(s =>
        {
            var r = records.GetValueOrDefault(s.EnrollmentId);
            return new AttendanceRow(s.EnrollmentId, s.Matricule, s.FullName, r is null ? null : Labels.Of(r.Status), r?.Note);
        }).ToList());
    }

    public Task<int> SaveAttendanceAsync(Guid promotionId, DateOnly date, IReadOnlyList<AttendanceInput> input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (date > clock.Today) throw new DomainException("date", "Impossible de saisir l'appel d'une date future.");
        var allowed = (await StudentsOfAsync(promotionId, ct)).Select(s => s.EnrollmentId).ToHashSet();
        var ids = input.Select(i => i.EnrollmentId).ToList();
        var existing = await db.Attendance.Where(a => a.Date == date && ids.Contains(a.EnrollmentId)).ToDictionaryAsync(a => a.EnrollmentId, ct);
        foreach (var i in input)
        {
            if (!allowed.Contains(i.EnrollmentId)) throw new DomainException("attendance", "Un apprenant n'appartient pas à cette promotion.");
            var status = EnumText.Parse(i.Status, "attendance", AttendanceLabels);
            if (existing.TryGetValue(i.EnrollmentId, out var record)) { record.Status = status; record.Note = i.Note; }
            else db.Attendance.Add(new AttendanceRecord { EnrollmentId = i.EnrollmentId, Date = date, Status = status, Note = i.Note });
        }
        audit.Record("presences.saisies", "Presence", promotionId.ToString(), $"Appel du {date:dd/MM/yyyy} : {input.Count} apprenant(s).");
        await db.SaveChangesAsync(ct);
        return input.Count;
    }, ct);

    public async Task<IReadOnlyList<AttendanceSummary>> AttendanceSummaryAsync(Guid promotionId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var students = await StudentsOfAsync(promotionId, ct);
        var ids = students.Select(s => s.EnrollmentId).ToList();
        var records = await db.Attendance.AsNoTracking().Where(a => ids.Contains(a.EnrollmentId) && a.Date >= from && a.Date <= to).ToListAsync(ct);
        return students.Select(s =>
        {
            var mine = records.Where(r => r.EnrollmentId == s.EnrollmentId).ToList();
            var present = mine.Count(r => r.Status == AttendanceStatus.Present);
            var late = mine.Count(r => r.Status == AttendanceStatus.Retard);
            return new AttendanceSummary(s.EnrollmentId, s.Matricule, s.FullName, present, mine.Count(r => r.Status == AttendanceStatus.Absent), late,
                mine.Count(r => r.Status == AttendanceStatus.AbsenceJustifiee), mine.Count == 0 ? 100 : decimal.Round((present + late) * 100m / mine.Count, 1));
        }).OrderBy(s => s.Rate).ThenBy(s => s.FullName).ToList();
    }

    // ---------- Discipline ----------
    public async Task<IReadOnlyList<IncidentDto>> IncidentsAsync(Guid? promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var q = from i in db.Incidents.AsNoTracking()
                join e in db.Enrollments.AsNoTracking() on i.EnrollmentId equals e.Id
                join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                where e.AcademicYearId == year.Id && (promotionId == null || e.PromotionId == promotionId)
                orderby i.Date descending
                select new IncidentDto(i.Id, e.Id, s.Matricule, s.LastName + " " + s.MiddleName + " " + s.FirstName, p.Code, i.Date, i.Category, i.Description, i.Sanction,
                    i.Severity.ToString(), i.RecordedByName);
        return await q.Take(500).ToListAsync(ct);
    }

    public Task<IncidentDto> CreateIncidentAsync(IncidentCreate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        new ValidationBuilder()
            .Require(!string.IsNullOrWhiteSpace(input.Category) && input.Category.Length <= 80, "category", "Catégorie obligatoire.")
            .Require(!string.IsNullOrWhiteSpace(input.Description) && input.Description.Length <= 2000, "description", "Description obligatoire.")
            .ThrowIfInvalid();
        var enrollment = await db.Enrollments.Include(e => e.Student).FirstOrDefaultAsync(e => e.Id == input.EnrollmentId, ct) ?? throw new NotFoundException("Inscription introuvable.");
        var incident = new DisciplineIncident
        {
            EnrollmentId = enrollment.Id, Date = input.Date ?? clock.Today, Category = input.Category.Trim(), Description = input.Description.Trim(),
            Sanction = string.IsNullOrWhiteSpace(input.Sanction) ? null : input.Sanction.Trim(), Severity = EnumText.Parse(input.Severity, "severity", SeverityLabels),
            RecordedByName = user.Name,
        };
        db.Incidents.Add(incident);
        audit.Record("discipline.incident", "Incident", incident.Id.ToString(), $"Incident « {incident.Category} » pour {enrollment.Student!.Matricule}.");
        await db.SaveChangesAsync(ct);
        return (await IncidentsAsync(null, ct)).First(i => i.Id == incident.Id);
    }, ct);
}
