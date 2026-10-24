using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Staff;

namespace Scolia.Application.Services;

/// <summary>
/// Cours par promotion, attributions aux enseignants avec historique, et charges pédagogiques (version 79).
/// Tout se fait sur l'année scolaire courante ; une année clôturée reste consultable.
/// </summary>
public sealed class TeachingService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, ICurrentUser user)
{
    public async Task<IReadOnlyList<CourseDto>> CoursesAsync(Guid promotionId, bool includeArchived, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var courses = await db.Courses.AsNoTracking()
            .Where(c => c.PromotionId == promotionId && c.AcademicYearId == year.Id && (includeArchived || c.IsActive))
            .OrderBy(c => c.Number).ToListAsync(ct);
        return await DescribeAsync(courses, ct);
    }

    /// <summary>Enseignants proposés pour une attribution : leur période d'activité recoupe l'année courante.</summary>
    public async Task<IReadOnlyList<TeacherChoiceDto>> AvailableTeachersAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        return (await db.Staff.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.LastName).ToListAsync(ct))
            .Where(t => TeachingRules.IsAvailableDuring(t, year))
            .Select(t => new TeacherChoiceDto(t.Id, t.FullName, t.Function)).ToList();
    }

    /// <summary>Premier numéro libre proposé pour un nouveau cours de la promotion.</summary>
    public async Task<int> NextNumberAsync(Guid promotionId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var used = await db.Courses.AsNoTracking().Where(c => c.PromotionId == promotionId && c.AcademicYearId == year.Id).Select(c => c.Number).ToListAsync(ct);
        return TeachingRules.NextFreeNumber(used);
    }

    public Task<CourseDto> UpsertCourseAsync(Guid? id, CourseUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var number = TeachingRules.Number(input.Number);
        var name = TeachingRules.Name(input.Name);
        var hours = TeachingRules.Positive(input.Hours, "Le volume horaire");
        var maxScore = TeachingRules.Positive(input.MaxScore, "La cotation maximale");
        var coefficient = TeachingRules.Coefficient(input.Coefficient);

        Course course;
        if (id is null)
        {
            var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == input.PromotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
            if (!promotion.IsActive) throw new DomainException("cours", "Les cours se créent dans une promotion active.");
            course = new Course { AcademicYearId = year.Id, PromotionId = promotion.Id };
            db.Courses.Add(course);
        }
        else
        {
            course = await CourseInYearAsync(id.Value, year, ct);
            // La promotion d'un cours enregistré est fixe : on crée le cours dans une autre classe si besoin.
            if (course.PromotionId != input.PromotionId)
                throw new DomainException("cours", "La promotion d'un cours enregistré est fixe : créez le cours dans une autre classe si nécessaire.");
        }

        await EnsureUniqueAsync(course, number, name, ct);
        course.Number = number;
        course.Name = name;
        course.Hours = hours;
        course.MaxScore = maxScore;
        course.Coefficient = coefficient;
        audit.Record(id is null ? "cours.cree" : "cours.modifie", "Cours", course.Id.ToString(), $"Cours {number} « {name} » ({hours} h, cotation {maxScore}).");
        await db.SaveChangesAsync(ct);
        return (await DescribeAsync([course], ct)).Single();
    }, ct);

    /// <summary>Archive un cours ou le réactive. Un cours ne s'archive qu'après le retrait de son attribution.</summary>
    public Task<CourseDto> SetArchivedAsync(Guid id, bool archived, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var course = await CourseInYearAsync(id, year, ct);
        if (archived)
        {
            if (await db.CourseAssignments.AnyAsync(a => a.CourseId == id && a.EndedOn == null, ct))
                throw new ConflictException("Retirez d'abord l'attribution de ce cours, puis archivez-le. L'historique est conservé.");
            course.IsActive = false;
        }
        else
        {
            await EnsureUniqueAsync(course, course.Number, course.Name, ct);
            course.IsActive = true;
        }
        audit.Record(archived ? "cours.archive" : "cours.reactive", "Cours", id.ToString(), $"Cours {course.Number} « {course.Name} » {(archived ? "archivé" : "réactivé")}.");
        await db.SaveChangesAsync(ct);
        return (await DescribeAsync([course], ct)).Single();
    }, ct);

    /// <summary>Attribue le cours. Réattribuer remplace l'enseignant après confirmation ; l'ancien reste dans l'historique.</summary>
    public Task<CourseDto> AssignAsync(Guid courseId, AssignmentRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var course = await CourseInYearAsync(courseId, year, ct);
        if (!course.IsActive) throw new DomainException("cours", "Un cours archivé ne s'attribue pas. Réactivez-le d'abord.");
        var teacher = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.TeacherId, ct) ?? throw new NotFoundException("Enseignant introuvable.");
        if (!TeachingRules.IsAvailableDuring(teacher, year))
            throw new DomainException("cours", "Cet enseignant n'est pas en activité pendant l'année scolaire. Complétez sa fiche de personnel.");

        var current = await db.CourseAssignments.FirstOrDefaultAsync(a => a.CourseId == courseId && a.EndedOn == null, ct);
        if (current is not null && current.TeacherId == teacher.Id) return (await DescribeAsync([course], ct)).Single();
        if (current is not null)
        {
            if (!input.ConfirmReplace)
                throw new ConflictException("Ce cours est déjà attribué à un autre enseignant. Confirmez la réattribution : l'attribution précédente reste dans l'historique.");
            current.EndedOn = clock.Today;
            current.EndReason = "Réattribué";
        }
        db.CourseAssignments.Add(new CourseAssignment { CourseId = courseId, TeacherId = teacher.Id, StartedOn = clock.Today, AssignedByName = user.Name });
        audit.Record("cours.attribue", "Cours", courseId.ToString(), $"Cours {course.Number} « {course.Name} » attribué à {teacher.FullName}.");
        await db.SaveChangesAsync(ct);
        return (await DescribeAsync([course], ct)).Single();
    }, ct);

    /// <summary>Retire l'attribution en cours. L'historique reste consultable et le cours redevient disponible.</summary>
    public Task<CourseDto> UnassignAsync(Guid courseId, CourseArchiveRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var year = await OpenYearAsync(ct);
        var course = await CourseInYearAsync(courseId, year, ct);
        var current = await db.CourseAssignments.FirstOrDefaultAsync(a => a.CourseId == courseId && a.EndedOn == null, ct)
            ?? throw new DomainException("cours", "Ce cours n'a pas d'enseignant attribué.");
        current.EndedOn = clock.Today;
        current.EndReason = string.IsNullOrWhiteSpace(input.Reason) ? "Retiré" : input.Reason.Trim();
        audit.Record("cours.retrait", "Cours", courseId.ToString(), $"Attribution retirée du cours {course.Number} « {course.Name} ».");
        await db.SaveChangesAsync(ct);
        return (await DescribeAsync([course], ct)).Single();
    }, ct);

    /// <summary>Toutes les attributions d'un cours, de la plus récente à la plus ancienne.</summary>
    public async Task<IReadOnlyList<AssignmentHistoryDto>> HistoryAsync(Guid courseId, CancellationToken ct = default)
    {
        var rows = await (from a in db.CourseAssignments.AsNoTracking()
                          join s in db.Staff.AsNoTracking() on a.TeacherId equals s.Id
                          where a.CourseId == courseId
                          orderby a.StartedOn descending, a.CreatedAtUtc descending
                          select new { a, s }).ToListAsync(ct);
        return rows.Select(r => new AssignmentHistoryDto(r.a.Id, r.s.FullName, r.a.StartedOn, r.a.EndedOn, r.a.EndReason, r.a.AssignedByName, r.a.EndedOn is null)).ToList();
    }

    /// <summary>Charge prévue de chaque enseignant disponible sur l'année, y compris ceux sans cours (0 heure).</summary>
    public async Task<IReadOnlyList<TeacherChargeDto>> ChargesAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var teachers = (await db.Staff.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct)).Where(t => TeachingRules.IsAvailableDuring(t, year)).ToList();
        var lines = await CurrentLinesAsync(year, ct);
        return teachers.Select(t =>
        {
            var mine = lines.Where(l => l.TeacherId == t.Id).ToList();
            return new TeacherChargeDto(t.Id, t.FullName, mine.Count, mine.Select(l => l.PromotionId).Distinct().Count(), mine.Sum(l => l.Hours));
        }).OrderBy(c => c.Name).ToList();
    }

    /// <summary>Détail d'un enseignant : chaque cours attribué et son volume.</summary>
    public async Task<IReadOnlyList<TeacherCourseLineDto>> TeacherCoursesAsync(Guid teacherId, CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var lines = await CurrentLinesAsync(year, ct);
        return lines.Where(l => l.TeacherId == teacherId)
            .OrderBy(l => l.PromotionCode).ThenBy(l => l.Number)
            .Select(l => new TeacherCourseLineDto(l.CourseId, l.PromotionCode, l.Number, l.Name, l.Hours, l.MaxScore)).ToList();
    }

    private async Task<List<CurrentLine>> CurrentLinesAsync(AcademicYear year, CancellationToken ct) =>
        await (from a in db.CourseAssignments.AsNoTracking()
               join c in db.Courses.AsNoTracking() on a.CourseId equals c.Id
               join p in db.Promotions.AsNoTracking() on c.PromotionId equals p.Id
               where a.EndedOn == null && c.IsActive && c.AcademicYearId == year.Id
               select new CurrentLine(a.TeacherId, c.Id, c.PromotionId, p.Code, c.Number, c.Name, c.Hours, c.MaxScore)).ToListAsync(ct);

    private sealed record CurrentLine(Guid TeacherId, Guid CourseId, Guid PromotionId, string PromotionCode, int Number, string Name, decimal Hours, decimal MaxScore);

    private async Task<AcademicYear> OpenYearAsync(CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        if (year.Status == AcademicYearStatus.Closed)
            throw new ConflictException("L'année est clôturée : les cours et les attributions se consultent sans modification.");
        return year;
    }

    private async Task<Course> CourseInYearAsync(Guid id, AcademicYear year, CancellationToken ct)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Cours introuvable.");
        if (course.AcademicYearId != year.Id) throw new DomainException("cours", "Ce cours appartient à une autre année scolaire.");
        return course;
    }

    /// <summary>Numéro unique dans la promotion, archives comprises ; nom unique parmi les cours actifs (casse et accents ignorés).</summary>
    private async Task EnsureUniqueAsync(Course course, int number, string name, CancellationToken ct)
    {
        if (await db.Courses.AnyAsync(c => c.PromotionId == course.PromotionId && c.AcademicYearId == course.AcademicYearId && c.Number == number && c.Id != course.Id, ct))
            throw new ConflictException("Ce numéro de cours est déjà utilisé dans la promotion, archives comprises.");
        var key = TextKey.Normalize(name);
        var activeNames = await db.Courses.AsNoTracking()
            .Where(c => c.PromotionId == course.PromotionId && c.AcademicYearId == course.AcademicYearId && c.IsActive && c.Id != course.Id)
            .Select(c => c.Name).ToListAsync(ct);
        if (activeNames.Any(n => TextKey.Normalize(n) == key))
            throw new ConflictException("Un cours actif porte déjà ce nom dans cette classe.");
    }

    private async Task<List<CourseDto>> DescribeAsync(IReadOnlyCollection<Course> courses, CancellationToken ct)
    {
        var ids = courses.Select(c => c.Id).ToList();
        var promotionIds = courses.Select(c => c.PromotionId).Distinct().ToList();
        var codes = await db.Promotions.AsNoTracking().Where(p => promotionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var counts = await db.Assessments.AsNoTracking().Where(a => ids.Contains(a.CourseId)).GroupBy(a => a.CourseId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var current = await db.CourseAssignments.AsNoTracking().Where(a => a.EndedOn == null && ids.Contains(a.CourseId))
            .Select(a => new { a.CourseId, a.TeacherId }).ToDictionaryAsync(a => a.CourseId, a => a.TeacherId, ct);
        var teacherIds = current.Values.Distinct().ToList();
        var teachers = await db.Staff.AsNoTracking().Where(s => teacherIds.Contains(s.Id)).ToListAsync(ct);
        var names = teachers.ToDictionary(s => s.Id, s => s.FullName);
        return courses.Select(c =>
        {
            Guid? teacher = current.TryGetValue(c.Id, out var t) ? t : null;
            return new CourseDto(c.Id, c.PromotionId, codes.GetValueOrDefault(c.PromotionId) ?? string.Empty, c.Number, c.Name, c.Coefficient, c.Hours, c.MaxScore,
                teacher, teacher is { } id ? names.GetValueOrDefault(id) : null, c.IsActive, counts.GetValueOrDefault(c.Id));
        }).ToList();
    }
}
