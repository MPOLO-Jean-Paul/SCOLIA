using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>Un homonyme existe : l'utilisateur doit confirmer qu'il s'agit d'une autre personne.</summary>
public sealed class DuplicateStudentException(IReadOnlyList<DuplicateCandidate> candidates)
    : DomainException("doublon", "Un apprenant portant la même identité existe déjà. Vérifiez qu'il ne s'agit pas de la même personne.")
{
    public IReadOnlyList<DuplicateCandidate> Candidates { get; } = candidates;
}

public sealed class StudentService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, AccountService accounts)
{
    private static readonly (EnrollmentStatus, string)[] StatusLabels =
        [(EnrollmentStatus.Diplome, "Diplômé"), (EnrollmentStatus.Transfere, "Transféré")];

    public async Task<PagedResult<StudentListItem>> SearchAsync(string? search, Guid? promotionId, string? status, Guid? yearId, int page, int pageSize, CancellationToken ct = default)
    {
        var year = await YearContext.GetAsync(db, yearId, ct);
        pageSize = Math.Clamp(pageSize, 1, 500);
        page = Math.Max(1, page);
        var query = from e in db.Enrollments.AsNoTracking()
                    join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                    join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                    where e.AcademicYearId == year.Id
                    select new { e, s, p };
        if (promotionId is not null) query = query.Where(x => x.e.PromotionId == promotionId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = EnumText.Parse(status, "status", StatusLabels);
            query = query.Where(x => x.e.Status == st);
        }
        foreach (var term in TextKey.Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = term;
            query = query.Where(x => x.s.SearchText.Contains(t));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.p.Code).ThenBy(x => x.s.LastName).ThenBy(x => x.s.MiddleName).ThenBy(x => x.s.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var map = await accounts.ComputeAsync(year.Id, rows.Select(r => r.e.Id).ToList(), null, ct);
        var items = rows.Select(r =>
        {
            var account = map.TryGetValue(r.e.Id, out var a) ? a.Account : null;
            return new StudentListItem(r.s.Id, r.e.Id, r.s.Matricule, r.s.LastName, r.s.MiddleName, r.s.FirstName, r.s.Gender.ToString(), r.p.Code,
                Labels.Of(r.e.Status), r.s.GuardianPhone, account?.Balance ?? 0, account?.Overdue ?? 0);
        }).ToList();
        return new PagedResult<StudentListItem>(items, total, page, pageSize);
    }

    public async Task<StudentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Students.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Apprenant introuvable.");
        var enrollments = await (from e in db.Enrollments.AsNoTracking()
                                 join y in db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.Id
                                 join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                                 where e.StudentId == id
                                 orderby y.StartDate descending
                                 select new EnrollmentDto(e.Id, y.Id, y.Label, p.Id, p.Code, p.Name, e.EnrolledOn, Labels.Of(e.Status), e.ExitDate, e.ExitReason, e.IsReEnrollment))
                                 .ToListAsync(ct);
        return new StudentDto(s.Id, s.Matricule, s.LastName, s.MiddleName, s.FirstName, s.Gender.ToString(), s.BirthDate, s.BirthPlace, s.Nationality,
            s.Address, s.Phone, s.Email, s.GuardianName, s.GuardianRelation, s.GuardianPhone, s.GuardianPhone2, s.Notes, s.Photo is { Length: > 0 },
            enrollments, s.Version);
    }

    private void Apply(Student s, StudentUpsert input)
    {
        var v = new ValidationBuilder()
            .Require(IdentityRules.IsValidName(input.LastName), "lastName", "Nom obligatoire : lettres, espaces, apostrophes ou tirets.")
            .Require(IdentityRules.IsValidName(input.MiddleName), "middleName", "Post-nom obligatoire : lettres, espaces, apostrophes ou tirets.")
            .Require(IdentityRules.IsValidName(input.FirstName), "firstName", "Prénom obligatoire : lettres, espaces, apostrophes ou tirets.")
            // Comme dans SCOLIA V79 : date et lieu de naissance, et le contact du parent qui sert aux relances.
            .Require(input.BirthDate is not null, "birthDate", "Date de naissance obligatoire.")
            .Require(input.BirthDate is null || (input.BirthDate <= clock.Today && input.BirthDate > clock.Today.AddYears(-100)), "birthDate", "Date de naissance invalide.")
            .Require(!string.IsNullOrWhiteSpace(input.BirthPlace), "birthPlace", "Lieu de naissance obligatoire.")
            .Require(!string.IsNullOrWhiteSpace(input.GuardianPhone), "guardianPhone", "Contact parent obligatoire : 10 chiffres, par exemple 0812345678.")
            .Require(input.Email is null || input.Email.Length == 0 || (input.Email.Contains('@') && input.Email.Length <= 160), "email", "Adresse e-mail invalide.");
        Gender gender = Gender.M;
        try { gender = EnumText.Parse<Gender>(input.Gender, "gender", [(Gender.M, "Masculin"), (Gender.F, "Féminin")]); }
        catch (DomainException) { v.Add("gender", "Choisissez le sexe."); }
        string? phone = null, guardianPhone = null, guardianPhone2 = null;
        try { phone = IdentityRules.NormalizePhone(input.Phone); } catch (DomainException e) { v.Add("phone", e.Message); }
        try { guardianPhone = IdentityRules.NormalizePhone(input.GuardianPhone); } catch (DomainException e) { v.Add("guardianPhone", e.Message); }
        try { guardianPhone2 = IdentityRules.NormalizePhone(input.GuardianPhone2); } catch (DomainException e) { v.Add("guardianPhone2", e.Message); }
        if (guardianPhone is not null && guardianPhone == guardianPhone2) v.Add("guardianPhone2", "Le contact parent 2 reprend le contact 1 : laissez-le vide ou saisissez un autre numéro.");
        v.ThrowIfInvalid();

        s.LastName = input.LastName.Trim().ToUpperInvariant();
        s.MiddleName = input.MiddleName.Trim().ToUpperInvariant();
        s.FirstName = TextKey.ProperName(input.FirstName);
        s.Gender = gender;
        s.BirthDate = input.BirthDate;
        s.BirthPlace = Clean(input.BirthPlace);
        s.Nationality = Clean(input.Nationality);
        s.Address = Clean(input.Address);
        s.Phone = phone;
        s.Email = Clean(input.Email);
        s.GuardianName = Clean(input.GuardianName);
        s.GuardianRelation = Clean(input.GuardianRelation);
        s.GuardianPhone = guardianPhone;
        s.GuardianPhone2 = guardianPhone2;
        s.Notes = Clean(input.Notes);
        s.RefreshKeys();
    }

    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(string lastName, string middleName, string firstName, Guid? excludeId, CancellationToken ct = default)
    {
        var key = Student.BuildIdentityKey(lastName, middleName, firstName);
        var candidates = await db.Students.AsNoTracking().Where(s => s.IdentityKey == key && s.Id != excludeId).ToListAsync(ct);
        var result = new List<DuplicateCandidate>();
        foreach (var c in candidates)
        {
            var code = await (from e in db.Enrollments.AsNoTracking()
                              join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                              join y in db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.Id
                              where e.StudentId == c.Id
                              orderby y.StartDate descending
                              select p.Code).FirstOrDefaultAsync(ct);
            result.Add(new DuplicateCandidate(c.Id, c.Matricule, c.FullName, code));
        }
        return result;
    }

    public Task<StudentDto> CreateAsync(StudentCreate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var student = new Student();
        Apply(student, input.Identity);
        var year = await YearContext.CurrentAsync(db, ct);
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == input.PromotionId && p.AcademicYearId == year.Id, ct)
            ?? throw new DomainException("promotionId", "Choisissez une promotion de l'année scolaire courante.");
        if (!promotion.IsActive) throw new DomainException("promotionId", "Cette promotion est désactivée.");
        await EnsureCapacityAsync(promotion, ct);
        if (!input.ConfirmDuplicate)
        {
            var duplicates = await FindDuplicatesAsync(student.LastName, student.MiddleName, student.FirstName, null, ct);
            if (duplicates.Count > 0) throw new DuplicateStudentException(duplicates);
        }
        var prefix = promotion.Code;
        var taken = await db.Students.Where(s => s.Matricule.StartsWith(prefix)).Select(s => s.Matricule).ToListAsync(ct);
        student.Matricule = MatriculeGenerator.Next(prefix, taken);
        student.RefreshKeys();
        db.Students.Add(student);
        var enrollment = new Enrollment
        {
            StudentId = student.Id, AcademicYearId = year.Id, PromotionId = promotion.Id, EnrolledOn = input.EnrolledOn ?? clock.Today,
        };
        db.Enrollments.Add(enrollment);
        audit.Record("apprenant.inscrit", "Apprenant", student.Id.ToString(), $"{student.Matricule} {student.FullName} inscrit en {promotion.Code} ({year.Label}).");
        await db.SaveChangesAsync(ct);
        return await GetAsync(student.Id, ct);
    }, ct);

    private async Task EnsureCapacityAsync(Promotion promotion, CancellationToken ct)
    {
        if (promotion.Capacity is null) return;
        var count = await db.Enrollments.CountAsync(e => e.PromotionId == promotion.Id && e.Status == EnrollmentStatus.Actif, ct);
        if (count >= promotion.Capacity) throw new DomainException("promotionId", $"La promotion {promotion.Code} est complète ({promotion.Capacity} places).");
    }

    public Task<StudentDto> UpdateAsync(Guid id, StudentUpsert input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Apprenant introuvable.");
        if (input.Version is not null && input.Version != student.Version)
            throw new ConflictException("Ce dossier a été modifié sur un autre poste. Rechargez-le avant d'enregistrer.");
        var before = $"{student.FullName} | {student.GuardianPhone}";
        Apply(student, input);
        audit.Record("apprenant.modifie", "Apprenant", id.ToString(), $"Dossier {student.Matricule} modifié.", new { avant = before, apres = $"{student.FullName} | {student.GuardianPhone}" });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }, ct);

    public Task SetPhotoAsync(Guid id, byte[]? content, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        if (content is { Length: > 1_500_000 }) throw new DomainException("photo", "La photo ne doit pas dépasser 1,5 Mo.");
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Apprenant introuvable.");
        student.Photo = content;
        audit.Record("apprenant.photo", "Apprenant", id.ToString(), $"Photo de {student.Matricule} {(content is null ? "retirée" : "mise à jour")}.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public async Task<byte[]?> GetPhotoAsync(Guid id, CancellationToken ct = default) =>
        await db.Students.AsNoTracking().Where(s => s.Id == id).Select(s => s.Photo).FirstOrDefaultAsync(ct);

    public Task<StudentDto> EnrollAsync(Guid studentId, EnrollRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Apprenant introuvable.");
        var promotion = await db.Promotions.FirstOrDefaultAsync(p => p.Id == input.PromotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
        if (await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.AcademicYearId == promotion.AcademicYearId, ct))
            throw new ConflictException("Cet apprenant est déjà inscrit pour cette année scolaire.");
        await EnsureCapacityAsync(promotion, ct);
        var hadPrevious = await db.Enrollments.AnyAsync(e => e.StudentId == studentId, ct);
        db.Enrollments.Add(new Enrollment
        {
            StudentId = studentId, AcademicYearId = promotion.AcademicYearId, PromotionId = promotion.Id,
            EnrolledOn = input.EnrolledOn ?? clock.Today, IsReEnrollment = hadPrevious,
        });
        audit.Record("apprenant.reinscrit", "Apprenant", studentId.ToString(), $"{student.Matricule} inscrit en {promotion.Code}.");
        await db.SaveChangesAsync(ct);
        return await GetAsync(studentId, ct);
    }, ct);

    public Task ChangeStatusAsync(Guid enrollmentId, StatusChange input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var enrollment = await db.Enrollments.Include(e => e.Student).FirstOrDefaultAsync(e => e.Id == enrollmentId, ct) ?? throw new NotFoundException("Inscription introuvable.");
        var status = EnumText.Parse(input.Status, "status", StatusLabels);
        if (status != EnrollmentStatus.Actif && string.IsNullOrWhiteSpace(input.Reason))
            throw new DomainException("reason", "Précisez le motif du changement de statut.");
        var old = enrollment.Status;
        enrollment.Status = status;
        enrollment.ExitDate = status is EnrollmentStatus.Actif or EnrollmentStatus.Suspendu ? null : input.Date ?? clock.Today;
        enrollment.ExitReason = Clean(input.Reason);
        audit.Record("inscription.statut", "Inscription", enrollmentId.ToString(),
            $"{enrollment.Student!.Matricule} : {Labels.Of(old)} → {Labels.Of(status)}.", new { motif = input.Reason });
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    /// <summary>Change la promotion d'une inscription. Les paiements déjà affectés sont reportés sur la
    /// rubrique équivalente (même mois ou même frais) de la nouvelle promotion : rien n'est perdu.</summary>
    public Task ChangePromotionAsync(Guid enrollmentId, Guid promotionId, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var enrollment = await db.Enrollments.Include(e => e.Student).FirstOrDefaultAsync(e => e.Id == enrollmentId, ct) ?? throw new NotFoundException("Inscription introuvable.");
        if (enrollment.PromotionId == promotionId) return true;
        var target = await db.Promotions.Include(p => p.Fees).FirstOrDefaultAsync(p => p.Id == promotionId, ct) ?? throw new NotFoundException("Promotion introuvable.");
        if (target.AcademicYearId != enrollment.AcademicYearId) throw new DomainException("promotionId", "La nouvelle promotion doit appartenir à la même année scolaire.");
        await EnsureCapacityAsync(target, ct);
        var source = await db.Promotions.AsNoTracking().Include(p => p.Fees).FirstAsync(p => p.Id == enrollment.PromotionId, ct);
        if (await db.Sponsorships.AnyAsync(s => s.EnrollmentId == enrollmentId && s.IsActive, ct))
            throw new DomainException("promotionId", "Une prise en charge active existe : terminez-la avant de changer de promotion.");

        var allocations = await (from a in db.ReceiptAllocations
                                 join r in db.Receipts on a.ReceiptId equals r.Id
                                 where r.EnrollmentId == enrollmentId
                                 select a).ToListAsync(ct);
        var mapping = new Dictionary<Guid, FeeItem>();
        foreach (var fee in source.Fees)
        {
            var match = fee.Kind == FeeKind.Mensualite
                ? target.Fees.FirstOrDefault(f => f.Kind == FeeKind.Mensualite && f.PeriodId == fee.PeriodId)
                : target.Fees.FirstOrDefault(f => f.Kind == fee.Kind && TextKey.Normalize(f.Label) == TextKey.Normalize(fee.Label));
            if (match is not null) mapping[fee.Id] = match;
        }
        var validReceiptIds = await db.Receipts.Where(r => r.EnrollmentId == enrollmentId && r.Status == OperationStatus.Valide).Select(r => r.Id).ToListAsync(ct);
        foreach (var group in allocations.GroupBy(a => a.FeeItemId))
        {
            var label = source.Fees.FirstOrDefault(f => f.Id == group.Key)?.Label ?? "?";
            if (!mapping.TryGetValue(group.Key, out var newItem))
                throw new DomainException("promotionId", $"Le paiement « {label} » n'a pas d'équivalent dans {target.Code}. Ajoutez ce frais à la promotion ou annulez le reçu.");
            var paidValid = group.Where(a => validReceiptIds.Contains(a.ReceiptId)).Sum(a => a.Amount);
            if (paidValid > newItem.Amount)
                throw new DomainException("promotionId", $"« {label} » : {paidValid:N0} déjà versé dépasse le montant prévu en {target.Code} ({newItem.Amount:N0}).");
            foreach (var a in group) a.FeeItemId = newItem.Id;
        }
        var old = source.Code;
        enrollment.PromotionId = target.Id;
        enrollment.Student!.UpdatedAtUtc = clock.UtcNow;
        audit.Record("inscription.promotion", "Inscription", enrollmentId.ToString(), $"{enrollment.Student.Matricule} : promotion {old} → {target.Code}, paiements reportés.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Apprenant introuvable.");
        if (await db.Receipts.AnyAsync(r => r.StudentId == id, ct))
            throw new DomainException("apprenant", "Ce dossier a des reçus : il ne peut pas être supprimé. Changez son statut en « Sorti ».");
        var enrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        if (await db.Grades.AnyAsync(g => enrollmentIds.Contains(g.EnrollmentId), ct) || await db.Attendance.AnyAsync(a => enrollmentIds.Contains(a.EnrollmentId), ct))
            throw new DomainException("apprenant", "Ce dossier a des notes ou des présences : changez son statut au lieu de le supprimer.");
        var sponsorships = await db.Sponsorships.Include(s => s.Lines).Where(s => enrollmentIds.Contains(s.EnrollmentId)).ToListAsync(ct);
        db.Sponsorships.RemoveRange(sponsorships);
        db.Enrollments.RemoveRange(enrollments);
        db.Students.Remove(student);
        audit.Record("apprenant.supprime", "Apprenant", id.ToString(), $"Dossier {student.Matricule} {student.FullName} supprimé (aucune opération financière).");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    /// <summary>Groupes d'apprenants partageant la même identité normalisée.</summary>
    public async Task<IReadOnlyList<IReadOnlyList<DuplicateCandidate>>> DuplicateGroupsAsync(CancellationToken ct = default)
    {
        var keys = await db.Students.AsNoTracking().GroupBy(s => s.IdentityKey).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync(ct);
        var result = new List<IReadOnlyList<DuplicateCandidate>>();
        foreach (var key in keys)
        {
            var students = await db.Students.AsNoTracking().Where(s => s.IdentityKey == key).ToListAsync(ct);
            result.Add(students.Select(s => new DuplicateCandidate(s.Id, s.Matricule, s.FullName, null)).ToList());
        }
        return result;
    }

    // ---------- Réinscriptions ----------
    public async Task<IReadOnlyList<ReEnrollmentPlanItem>> ReEnrollmentPlanAsync(Guid targetYearId, CancellationToken ct = default)
    {
        var target = await YearContext.GetAsync(db, targetYearId, ct);
        var source = await db.AcademicYears.AsNoTracking().Where(y => y.StartDate < target.StartDate).OrderByDescending(y => y.StartDate).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("annee", "Aucune année précédente à partir de laquelle réinscrire.");
        var targetPromotions = await db.Promotions.AsNoTracking().Where(p => p.AcademicYearId == target.Id && p.IsActive).ToListAsync(ct);
        var already = await db.Enrollments.AsNoTracking().Where(e => e.AcademicYearId == target.Id).Select(e => e.StudentId).ToListAsync(ct);
        var map = await accounts.ComputeAsync(source.Id, null, null, ct);
        return map.Values.Where(x => x.Enrollment.Status == EnrollmentStatus.Actif && !already.Contains(x.Enrollment.StudentId))
            .OrderBy(x => x.Promotion.Code).ThenBy(x => x.Enrollment.Student!.LastName)
            .Select(x =>
            {
                var next = x.Promotion.NextPromotionCode is null ? null : targetPromotions.FirstOrDefault(p => p.Code == x.Promotion.NextPromotionCode)?.Code;
                return new ReEnrollmentPlanItem(x.Enrollment.StudentId, x.Enrollment.Student!.Matricule, x.Enrollment.Student.FullName, x.Promotion.Code, next,
                    x.Account.Balance, next is not null);
            }).ToList();
    }

    public Task<int> ReEnrollAsync(ReEnrollmentRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var target = await YearContext.GetAsync(db, input.TargetYearId, ct);
        var promotions = await db.Promotions.Where(p => p.AcademicYearId == target.Id).ToDictionaryAsync(p => p.Id, ct);
        var count = 0;
        foreach (var choice in input.Choices)
        {
            if (!promotions.TryGetValue(choice.PromotionId, out var promotion)) throw new DomainException("choices", "Promotion cible invalide.");
            if (await db.Enrollments.AnyAsync(e => e.StudentId == choice.StudentId && e.AcademicYearId == target.Id, ct)) continue;
            db.Enrollments.Add(new Enrollment
            {
                StudentId = choice.StudentId, AcademicYearId = target.Id, PromotionId = promotion.Id, EnrolledOn = clock.Today, IsReEnrollment = true,
            });
            count++;
        }
        audit.Record("reinscription.lot", "Inscription", null, $"{count} réinscription(s) pour {target.Label}.");
        await db.SaveChangesAsync(ct);
        return count;
    }, ct);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
