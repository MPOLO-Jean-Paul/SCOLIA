using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Attribution des branches EPST et des éléments constitutifs LMD aux enseignants, et saisie
/// des notes par l'enseignant attribué. Le secrétariat garde l'accès à toutes les feuilles.
/// Les cours ITM et leurs attributions ne sont pas concernés.
/// </summary>
public sealed class CurriculumTeachingService(IAppDbContext db, IWriteGate gate, IAuditLog audit, ICurrentUser user, IClock clock)
{
    private async Task<(SchoolProfile School, AcademicYear Year)> ContextAsync(CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        var profile = InstitutionProfiles.Canonicalize(school.InstitutionType);
        if (profile == InstitutionProfiles.ItmIm)
            throw new DomainException("attribution", "Les ITM/IM attribuent leurs cours dans Enseignants et cours › Attribution des cours.");
        return (school, await YearContext.CurrentAsync(db, ct));
    }

    private static bool Assignable(CurriculumItem i) =>
        i.IsActive && i.Kind is CurriculumItemKind.AnnualSubject or CurriculumItemKind.TeachingElement;

    // ------------------------------------------------------------- secrétariat

    public async Task<IReadOnlyList<CurriculumTeachingItemDto>> ItemsAsync(Guid? teacherId = null, CancellationToken ct = default)
    {
        var (_, year) = await ContextAsync(ct);
        var items = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.AcademicYearId == year.Id && i.IsActive &&
                        (i.Kind == CurriculumItemKind.AnnualSubject || i.Kind == CurriculumItemKind.TeachingElement))
            .ToListAsync(ct);
        var ueIds = items.Where(i => i.ParentItemId != null).Select(i => i.ParentItemId!.Value).Distinct().ToList();
        var ues = await db.CurriculumItems.AsNoTracking().Where(i => ueIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct);
        var promotions = await db.Promotions.AsNoTracking().Where(p => p.AcademicYearId == year.Id)
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var current = await (from a in db.CurriculumAssignments.AsNoTracking()
                             join s in db.Staff.AsNoTracking() on a.TeacherId equals s.Id
                             where a.EndedOn == null && itemIds.Contains(a.CurriculumItemId)
                             select new { a.CurriculumItemId, s.Id, s.LastName, s.MiddleName, s.FirstName }).ToListAsync(ct);
        var byItem = current.ToDictionary(x => x.CurriculumItemId,
            x => (x.Id, Name: string.Join(' ', new[] { x.LastName, x.MiddleName, x.FirstName }.Where(n => !string.IsNullOrWhiteSpace(n)))));
        return items
            .Where(i => teacherId is null || byItem.TryGetValue(i.Id, out var t) && t.Id == teacherId)
            .OrderBy(i => promotions.GetValueOrDefault(i.PromotionId), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Semester).ThenBy(i => i.Code)
            .Select(i =>
            {
                var has = byItem.TryGetValue(i.Id, out var teacher);
                return new CurriculumTeachingItemDto(i.Id, i.PromotionId, promotions.GetValueOrDefault(i.PromotionId) ?? "",
                    i.Kind == CurriculumItemKind.AnnualSubject ? "Branche" : "EC", i.Code, i.Name, i.Semester.ToString(),
                    i.ParentItemId is { } ue ? ues.GetValueOrDefault(ue) : null,
                    has ? teacher.Id : null, has ? teacher.Name : null);
            }).ToList();
    }

    public async Task<IReadOnlyList<CurriculumTeacherDto>> TeachersAsync(CancellationToken ct = default)
    {
        var (_, year) = await ContextAsync(ct);
        var staff = await db.Staff.AsNoTracking().Where(s => s.IsActive).ToListAsync(ct);
        return staff.Where(s => TeachingRules.IsAvailableDuring(s, year))
            .Select(s => new CurriculumTeacherDto(s.Id, s.FullName)).OrderBy(s => s.FullName).ToList();
    }

    private async Task AssignCoreAsync(CurriculumItem item, Domain.Staff.StaffMember teacher, bool confirmReplace, CancellationToken ct)
    {
        var current = await db.CurriculumAssignments.FirstOrDefaultAsync(a => a.CurriculumItemId == item.Id && a.EndedOn == null, ct);
        if (current?.TeacherId == teacher.Id) return;
        if (current is not null)
        {
            if (!confirmReplace)
                throw new ConflictException($"« {item.Name} » est déjà attribué : confirmez la réattribution, l'historique est conservé.");
            current.EndedOn = clock.Today;
            current.EndReason = "Réattribué";
        }
        db.CurriculumAssignments.Add(new CurriculumAssignment
        {
            CurriculumItemId = item.Id, TeacherId = teacher.Id, StartedOn = clock.Today, AssignedByName = user.Name,
        });
        audit.Record("programme.attribue", "Programme", item.Id.ToString(), $"{item.Code} « {item.Name} » attribué à {teacher.FullName}.");
    }

    private async Task<Domain.Staff.StaffMember> TeacherAsync(Guid id, AcademicYear year, CancellationToken ct)
    {
        var teacher = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Enseignant introuvable.");
        if (!TeachingRules.IsAvailableDuring(teacher, year))
            throw new DomainException("attribution", "Cet enseignant n'est pas en activité pendant l'année : complétez sa fiche du personnel.");
        return teacher;
    }

    public Task<CurriculumTeachingItemDto> AssignAsync(Guid itemId, CurriculumAssignInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (_, year) = await ContextAsync(ct);
        var item = await db.CurriculumItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.AcademicYearId == year.Id, ct)
            ?? throw new NotFoundException("Branche ou élément constitutif introuvable pour l'année courante.");
        if (!Assignable(item))
            throw new DomainException("attribution", "Seules les branches et les éléments constitutifs actifs s'attribuent.");
        await AssignCoreAsync(item, await TeacherAsync(input.TeacherId, year, ct), input.ConfirmReplace, ct);
        await db.SaveChangesAsync(ct);
        return (await ItemsAsync(null, ct)).First(i => i.ItemId == itemId);
    }, ct);

    /// <summary>Titulaire de classe (primaire) : toutes les branches actives de la classe.</summary>
    public Task<int> AssignClassAsync(Guid promotionId, CurriculumAssignInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (_, year) = await ContextAsync(ct);
        var teacher = await TeacherAsync(input.TeacherId, year, ct);
        var items = await db.CurriculumItems.AsNoTracking()
            .Where(i => i.PromotionId == promotionId && i.AcademicYearId == year.Id && i.IsActive &&
                        i.Kind == CurriculumItemKind.AnnualSubject).ToListAsync(ct);
        if (items.Count == 0) throw new DomainException("attribution", "Cette classe n'a aucune branche active.");
        foreach (var item in items) await AssignCoreAsync(item, teacher, input.ConfirmReplace, ct);
        await db.SaveChangesAsync(ct);
        return items.Count;
    }, ct);

    public Task<CurriculumTeachingItemDto> UnassignAsync(Guid itemId, CurriculumUnassignInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        await ContextAsync(ct);
        var current = await db.CurriculumAssignments.FirstOrDefaultAsync(a => a.CurriculumItemId == itemId && a.EndedOn == null, ct)
            ?? throw new DomainException("attribution", "Cet élément n'a pas d'enseignant attribué.");
        current.EndedOn = clock.Today;
        current.EndReason = string.IsNullOrWhiteSpace(input.Reason) ? "Retiré" : input.Reason.Trim();
        audit.Record("programme.retrait", "Programme", itemId.ToString(), "Attribution retirée : " + current.EndReason);
        await db.SaveChangesAsync(ct);
        return (await ItemsAsync(null, ct)).First(i => i.ItemId == itemId);
    }, ct);

    // ------------------------------------------------------------- enseignant

    /// <summary>Fiche du personnel de l'enseignant connecté, code provisoire remplacé.</summary>
    private async Task<Guid> MyStaffIdAsync(CancellationToken ct)
    {
        var id = user.Id ?? throw new DomainException("session", "Session expirée : reconnectez-vous.");
        var account = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new { u.StaffMemberId, u.MustChangePassword }).FirstOrDefaultAsync(ct);
        if (account is { MustChangePassword: true })
            throw new ForbiddenException("Votre code d'accès est provisoire : choisissez votre propre code avant de saisir des notes.");
        return account?.StaffMemberId
            ?? throw new ForbiddenException("Ce compte enseignant n'est relié à aucune fiche du personnel : demandez au secrétariat de le compléter.");
    }

    public async Task<IReadOnlyList<CurriculumTeachingItemDto>> MineAsync(CancellationToken ct = default) =>
        await ItemsAsync(await MyStaffIdAsync(ct), ct);

    /// <summary>Le secrétariat saisit toutes les notes ; un enseignant, seulement celles des éléments qui lui sont attribués.</summary>
    public async Task EnsureCanEncodeAsync(Guid itemId, CancellationToken ct)
    {
        if (user.HasPermission(Permissions.AcademicsWrite)) return;
        if (!user.HasPermission(Permissions.MarksEncode))
            throw new ForbiddenException("Vous n'avez pas le droit de saisir des notes.");
        var staff = await MyStaffIdAsync(ct);
        if (!await db.CurriculumAssignments.AnyAsync(a => a.CurriculumItemId == itemId && a.TeacherId == staff && a.EndedOn == null, ct))
            throw new ForbiddenException("Cet élément ne vous est pas attribué : demandez au secrétariat de vous l'attribuer.");
    }

    // ------------------------------------------------------------- feuille EPST

    private async Task<(CurriculumItem Subject, Promotion Class, SchoolCycle Cycle, AcademicYear Year, SchoolCyclePeriod Period)>
        SheetContextAsync(Guid subjectId, Guid periodId, CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (!InstitutionProfiles.UsesSchoolCycles(school.InstitutionType))
            throw new DomainException("evaluationCycle", "Les feuilles de branches concernent le primaire et le secondaire.");
        var subject = await db.CurriculumItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == subjectId && i.Kind == CurriculumItemKind.AnnualSubject, ct)
            ?? throw new NotFoundException("Branche introuvable.");
        var classroom = await db.Promotions.AsNoTracking().FirstAsync(p => p.Id == subject.PromotionId, ct);
        var cycle = await db.SchoolCycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classroom.SchoolCycleId, ct)
            ?? throw new DomainException("evaluationCycle", "Cette classe n'est rattachée à aucun cycle.");
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == classroom.AcademicYearId, ct);
        var period = await db.SchoolCyclePeriods.AsNoTracking().FirstOrDefaultAsync(p =>
            p.Id == periodId && p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id, ct)
            ?? throw new NotFoundException("Période introuvable pour cette classe.");
        return (subject, classroom, cycle, year, period);
    }

    /// <summary>Périodes du cycle de la classe d'une branche, pour la feuille de l'enseignant.</summary>
    public async Task<IReadOnlyList<SchoolPeriodDto>> EpstPeriodsAsync(Guid subjectId, CancellationToken ct = default)
    {
        var subject = await db.CurriculumItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == subjectId && i.Kind == CurriculumItemKind.AnnualSubject, ct)
            ?? throw new NotFoundException("Branche introuvable.");
        await EnsureCanEncodeAsync(subject.Id, ct);
        var classroom = await db.Promotions.AsNoTracking().FirstAsync(p => p.Id == subject.PromotionId, ct);
        var periods = await db.SchoolCyclePeriods.AsNoTracking()
            .Where(p => p.SchoolCycleId == classroom.SchoolCycleId && p.AcademicYearId == classroom.AcademicYearId)
            .OrderBy(p => p.Order).ToListAsync(ct);
        return periods.Select(p => new SchoolPeriodDto(p.Id, p.Code, p.Name, p.TermCode, p.Order, p.Mode.ToString(),
            p.StartDate, p.EndDate, p.IsExam)).ToList();
    }

    private static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public async Task<EpstMarkSheetDto> EpstSheetAsync(Guid subjectId, Guid periodId, string? code, CancellationToken ct = default)
    {
        var (subject, classroom, cycle, year, period) = await SheetContextAsync(subjectId, periodId, ct);
        await EnsureCanEncodeAsync(subject.Id, ct);
        var evaluation = NormalizeCode(string.IsNullOrWhiteSpace(code) ? (period.IsExam ? "EXAMEN" : "INTERRO1") : code);
        var entries = await db.SchoolCycleAssessments.AsNoTracking()
            .Where(a => a.PromotionId == classroom.Id && a.SchoolCyclePeriodId == period.Id && a.CurriculumItemId == subject.Id)
            .ToListAsync(ct);
        var enrollments = await db.Enrollments.AsNoTracking().Include(e => e.Student)
            .Where(e => e.PromotionId == classroom.Id && e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif)
            .ToListAsync(ct);
        string? lockReason = null;
        try
        {
            if (year.Status != AcademicYearStatus.Open || !year.IsCurrent)
                throw new DomainException("evaluationCycle", "L'année n'est pas ouverte.");
            await EpstDeliberationService.EnsureMarksEditableAsync(db, classroom.Id, year.Id, ct);
        }
        catch (DomainException e) { lockReason = e.Message; }
        var current = entries.Where(e => e.EvaluationCode == evaluation).ToList();
        var first = current.FirstOrDefault();
        return new EpstMarkSheetDto(subject.Id, subject.Name, classroom.Name, period.Id, period.Code, period.IsExam,
            evaluation, first?.Title ?? (period.IsExam ? "Examen" : "Interrogation"),
            first?.MaxScore ?? (period.IsExam && subject.PeriodMaxScore is { } m ? m * 2 : subject.PeriodMaxScore ?? 10),
            lockReason is null, lockReason,
            entries.Select(e => e.EvaluationCode).Distinct().Order().ToList(),
            enrollments.OrderBy(e => e.Student!.FullName, StringComparer.CurrentCultureIgnoreCase)
                .Select(e => new EpstMarkSheetLineDto(e.Id, e.Student!.Matricule, e.Student.FullName,
                    current.FirstOrDefault(a => a.EnrollmentId == e.Id)?.Score)).ToList());
    }

    public Task<EpstMarkSheetDto> SaveEpstSheetAsync(Guid subjectId, EpstMarkSheetInput input, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        var (subject, classroom, cycle, year, period) = await SheetContextAsync(subjectId, input.PeriodId, ct);
        await EnsureCanEncodeAsync(subject.Id, ct);
        if (year.Status != AcademicYearStatus.Open || !year.IsCurrent)
            throw new DomainException("evaluationCycle", "Les notes se saisissent pendant l'année courante ouverte.");
        await EpstDeliberationService.EnsureMarksEditableAsync(db, classroom.Id, year.Id, ct);
        var code = NormalizeCode(input.EvaluationCode);
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.PromotionId == classroom.Id && e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif)
            .ToDictionaryAsync(e => e.Id, ct);
        var existing = await db.SchoolCycleAssessments
            .Where(a => a.PromotionId == classroom.Id && a.SchoolCyclePeriodId == period.Id &&
                        a.CurriculumItemId == subject.Id && a.EvaluationCode == code).ToListAsync(ct);
        var count = 0;
        foreach (var line in input.Lines ?? [])
        {
            if (!enrollments.TryGetValue(line.EnrollmentId, out var enrollment))
                throw new DomainException("evaluationCycle", "Un élève de la feuille n'est pas inscrit dans cette classe.");
            var row = existing.FirstOrDefault(a => a.EnrollmentId == line.EnrollmentId);
            if (line.Score is null && row is null) continue;
            if (row is null)
            {
                row = new SchoolCycleAssessment
                {
                    AcademicYearId = year.Id, SchoolCycleId = cycle.Id, PromotionId = classroom.Id, EnrollmentId = enrollment.Id,
                    SchoolCyclePeriodId = period.Id, CurriculumItemId = subject.Id, EvaluationCode = code,
                    Mode = SchoolCycleAssessmentMode.Evaluation,
                };
                db.SchoolCycleAssessments.Add(row);
                existing.Add(row);
            }
            row.Title = (input.Title ?? string.Empty).Trim();
            row.Score = line.Score;
            row.MaxScore = input.MaxScore;
            SchoolCycleAssessmentRules.Validate(cycle, period, classroom, enrollment, subject, row);
            count++;
        }
        audit.Record("cycle.feuille.saisie", "MatiereCycle", subject.Id.ToString(),
            $"{classroom.Name} · {subject.Code} · {period.Code} · {code} : {count} cote(s) saisie(s) par {user.Name}.");
        await db.SaveChangesAsync(ct);
        return await EpstSheetAsync(subjectId, input.PeriodId, code, ct);
    }, ct);
}
