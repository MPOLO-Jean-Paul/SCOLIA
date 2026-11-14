using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Students;

namespace Scolia.Application.Services;

/// <summary>
/// Accès en ligne des étudiants d'une université : le secrétariat crée l'accès (identifiant = matricule,
/// code provisoire montré une fois) ; l'étudiant ne lit que ses propres résultats publiés par le jury.
/// </summary>
public sealed class StudentAccountService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IPasswordService passwords)
{
    private async Task RequireUniversityAsync(CancellationToken ct)
    {
        var school = await YearContext.SchoolAsync(db, ct);
        if (InstitutionProfiles.Canonicalize(school.InstitutionType) != InstitutionProfiles.SuperieurLmd)
            throw new DomainException("compteEtudiant", "Les accès étudiants concernent les universités et instituts supérieurs.");
    }

    public async Task<IReadOnlyList<StudentAccountDto>> ListAsync(Guid? promotionId, CancellationToken ct = default)
    {
        await RequireUniversityAsync(ct);
        var year = await YearContext.CurrentAsync(db, ct);
        var rows = await (from e in db.Enrollments.AsNoTracking()
                          join s in db.Students.AsNoTracking() on e.StudentId equals s.Id
                          join p in db.Promotions.AsNoTracking() on e.PromotionId equals p.Id
                          where e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif
                              && (promotionId == null || e.PromotionId == promotionId)
                          select new { e.Id, Student = s, Promotion = p.Name }).ToListAsync(ct);
        var ids = rows.Select(r => r.Student.Id).ToList();
        var accounts = (await db.Users.AsNoTracking().Where(u => u.Role == Role.Etudiant && u.StudentId != null && ids.Contains(u.StudentId.Value))
            .ToListAsync(ct)).GroupBy(u => u.StudentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.IsActive).First());
        return rows.OrderBy(r => r.Promotion).ThenBy(r => r.Student.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Select(r =>
            {
                accounts.TryGetValue(r.Student.Id, out var u);
                return new StudentAccountDto(r.Student.Id, r.Id, r.Student.Matricule, r.Student.FullName, r.Promotion,
                    u?.Id, u?.UserName, u?.IsActive ?? false, u?.MustChangePassword ?? false, u?.LastLoginUtc);
            }).ToList();
    }

    private async Task<StudentCredentialsDto> IssueCoreAsync(Student student, CancellationToken ct)
    {
        var existing = await db.Users.Where(u => u.Role == Role.Etudiant && u.StudentId == student.Id)
            .OrderByDescending(u => u.IsActive).FirstOrDefaultAsync(ct);
        if (existing is { IsActive: true })
            throw new ConflictException($"{student.FullName} a déjà l'accès « {existing.UserName} » : utilisez « Nouveau code provisoire ».");
        var code = TeacherAccountService.NewProvisionalCode();
        if (existing is not null)
        {
            existing.IsActive = true;
            existing.PasswordHash = passwords.Hash(code);
            existing.MustChangePassword = true;
            existing.FailedLoginCount = 0;
            existing.LockoutEndUtc = null;
            existing.SecurityStamp = Guid.NewGuid().ToString("N");
            audit.Record("etudiant.acces.reactive", "Utilisateur", existing.Id.ToString(), $"Accès {existing.UserName} de {student.FullName} réactivé.");
            return new StudentCredentialsDto(student.Id, existing.Id, student.FullName, student.Matricule, existing.UserName, code);
        }
        var userName = student.Matricule.Trim().ToLowerInvariant();
        if (userName.Length is < 3 or > 40 || !userName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new DomainException("compteEtudiant", $"Le matricule {student.Matricule} ne peut pas servir d'identifiant.");
        var normalized = TextKey.Normalize(userName);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, ct))
            throw new ConflictException($"L'identifiant « {userName} » est déjà utilisé par un autre compte.");
        var user = new AppUser
        {
            UserName = userName, NormalizedUserName = normalized, DisplayName = student.FullName, Role = Role.Etudiant,
            PasswordHash = passwords.Hash(code), MustChangePassword = true, StudentId = student.Id,
        };
        db.Users.Add(user);
        audit.Record("etudiant.acces.cree", "Utilisateur", user.Id.ToString(), $"Accès étudiant {userName} créé pour {student.FullName}.");
        return new StudentCredentialsDto(student.Id, user.Id, student.FullName, student.Matricule, userName, code);
    }

    private async Task<Student> EnrolledStudentAsync(Guid studentId, CancellationToken ct)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == studentId, ct)
            ?? throw new NotFoundException("Étudiant introuvable.");
        if (!await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.AcademicYearId == year.Id && e.Status == EnrollmentStatus.Actif, ct))
            throw new DomainException("compteEtudiant", "Seul un étudiant inscrit cette année reçoit un accès.");
        return student;
    }

    public Task<StudentCredentialsDto> IssueAsync(Guid studentId, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        await RequireUniversityAsync(ct);
        var credentials = await IssueCoreAsync(await EnrolledStudentAsync(studentId, ct), ct);
        await db.SaveChangesAsync(ct);
        return credentials;
    }, ct);

    /// <summary>Crée les accès manquants de toute une promotion ; renvoie les codes provisoires à remettre.</summary>
    public Task<IReadOnlyList<StudentCredentialsDto>> IssuePromotionAsync(Guid promotionId, CancellationToken ct = default) =>
        gate.RunAsync(async () =>
    {
        await RequireUniversityAsync(ct);
        var list = await ListAsync(promotionId, ct);
        var result = new List<StudentCredentialsDto>();
        foreach (var row in list.Where(r => !r.Active))
            result.Add(await IssueCoreAsync(await db.Students.AsNoTracking().FirstAsync(s => s.Id == row.StudentId, ct), ct));
        await db.SaveChangesAsync(ct);
        return (IReadOnlyList<StudentCredentialsDto>)result;
    }, ct);

    private async Task<AppUser> AccountAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == Role.Etudiant, ct)
        ?? throw new NotFoundException("Accès étudiant introuvable.");

    public Task<StudentCredentialsDto> ResetAsync(Guid userId, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        await RequireUniversityAsync(ct);
        var account = await AccountAsync(userId, ct);
        if (!account.IsActive) throw new DomainException("compteEtudiant", "Cet accès est désactivé : réactivez-le d'abord.");
        var student = await db.Students.AsNoTracking().FirstAsync(s => s.Id == account.StudentId, ct);
        var code = TeacherAccountService.NewProvisionalCode();
        account.PasswordHash = passwords.Hash(code);
        account.MustChangePassword = true;
        account.FailedLoginCount = 0;
        account.LockoutEndUtc = null;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("etudiant.acces.code", "Utilisateur", account.Id.ToString(), $"Nouveau code provisoire pour {account.UserName}.");
        await db.SaveChangesAsync(ct);
        return new StudentCredentialsDto(student.Id, account.Id, student.FullName, student.Matricule, account.UserName, code);
    }, ct);

    public Task<bool> SetActiveAsync(Guid userId, StudentAccountStatusInput input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        await RequireUniversityAsync(ct);
        var account = await AccountAsync(userId, ct);
        account.IsActive = input.Active;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record(input.Active ? "etudiant.acces.active" : "etudiant.acces.desactive", "Utilisateur", account.Id.ToString(),
            $"Accès {account.UserName} {(input.Active ? "activé" : "désactivé")}.");
        await db.SaveChangesAsync(ct);
        return account.IsActive;
    }, ct);
}

/// <summary>Consultation par l'étudiant connecté de ses seuls résultats publiés.</summary>
public sealed class StudentPortalService(IAppDbContext db, ICurrentUser user, LmdResultsService results)
{
    private async Task<Student> MeAsync(CancellationToken ct)
    {
        var id = user.Id ?? throw new DomainException("session", "Session expirée : reconnectez-vous.");
        var account = await db.Users.AsNoTracking().Where(u => u.Id == id && u.Role == Role.Etudiant && u.IsActive)
            .Select(u => new { u.StudentId, u.MustChangePassword }).FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Cet accès n'est pas un accès étudiant.");
        if (account.MustChangePassword)
            throw new ForbiddenException("Votre code est provisoire : choisissez votre propre code (Compte › Sécurité du compte) avant de consulter vos résultats.");
        return await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == account.StudentId, ct)
            ?? throw new ForbiddenException("Dossier étudiant introuvable.");
    }

    private static (string Stage, string Label, Func<LmdDeliberation, DateTime?> At)? Published(LmdSessionStatus status) => status switch
    {
        LmdSessionStatus.Cloturee => ("Definitif", "Résultats définitifs de l'année", d => d.ClosedAtUtc),
        LmdSessionStatus.PremiereSessionCloturee => ("PremiereSession", "Résultats de la première session", d => d.FirstSessionClosedAtUtc),
        LmdSessionStatus.S1Publie => ("S1", "Résultats partiels du premier semestre", d => d.S1PublishedAtUtc),
        _ => null,
    };

    public async Task<StudentPortalDto> MineAsync(CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var enrollments = await (from e in db.Enrollments.AsNoTracking()
                                 join d in db.LmdDeliberations.AsNoTracking() on e.PromotionId equals d.PromotionId
                                 join y in db.AcademicYears.AsNoTracking() on e.AcademicYearId equals y.Id
                                 where e.StudentId == me.Id && d.AcademicYearId == e.AcademicYearId && d.Status != LmdSessionStatus.Ouverte
                                 orderby y.StartDate descending
                                 select new { e.Id, e.PromotionId, Deliberation = d }).ToListAsync(ct);
        var list = new List<StudentPublishedResultDto>();
        foreach (var e in enrollments)
        {
            if (Published(e.Deliberation.Status) is not { } stage) continue;
            var promotion = await results.ResultsAsync(e.PromotionId, stage.Stage, ct);
            var mine = promotion.Students.FirstOrDefault(s => s.EnrollmentId == e.Id);
            if (mine is null) continue;
            list.Add(new StudentPublishedResultDto(e.Id, promotion.PromotionName, promotion.Level, promotion.Filiere,
                promotion.YearLabel, stage.Stage, stage.Label, stage.At(e.Deliberation), promotion.MarkScale,
                promotion.Students.Count, mine with { Jury = null, JuryReason = null }));
        }
        var debts = (await results.DebtsAsync(me.Id, ct)).ToList();
        return new StudentPortalDto(me.FullName, me.Matricule, list, debts,
            list.Count == 0 ? "Aucun résultat n'est encore publié par le jury pour vos inscriptions." : null);
    }

    /// <summary>Relevé PDF de l'étudiant connecté, pour une étape déjà publiée seulement.</summary>
    public async Task<LmdTranscriptPrintDto> TranscriptAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var enrollment = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == enrollmentId && e.StudentId == me.Id, ct)
            ?? throw new NotFoundException("Inscription introuvable.");
        var deliberation = await db.LmdDeliberations.AsNoTracking()
            .FirstOrDefaultAsync(d => d.PromotionId == enrollment.PromotionId && d.AcademicYearId == enrollment.AcademicYearId, ct);
        if (deliberation is null || Published(deliberation.Status) is not { } stage)
            throw new DomainException("deliberationLmd", "Aucun résultat publié pour cette inscription.");
        var print = await results.TranscriptAsync(enrollment.PromotionId, stage.Stage, enrollment.Id, ct);
        // Seule la page de l'étudiant est imprimée ; l'effectif sert au rang « 3/45 ».
        return print with { Students = print.Students.Select(s => s with { Jury = null, JuryReason = null }).ToList() };
    }
}
