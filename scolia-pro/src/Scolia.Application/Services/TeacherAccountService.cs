using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>
/// Comptes des enseignants, gérés par le secrétariat depuis la gestion des délibérations : fiche de l'enseignant,
/// identifiant proposé, code provisoire généré (à remplacer à la première connexion), privilège jury et activation.
/// Le code provisoire n'est montré qu'une fois, à sa création ; il n'est jamais stocké en clair.
/// </summary>
public sealed class TeacherAccountService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, StaffService staff, UserService users)
{
    private const string Letters = "abcdefghjkmnpqrstuvwxyz";
    private const string Digits = "23456789";

    /// <summary>Code provisoire lisible à dicter : quatre lettres, un tiret, quatre chiffres (sans 0, 1, i, l ni o ambigus).</summary>
    public static string NewProvisionalCode() =>
        RandomNumberGenerator.GetString(Letters, 4) + "-" + RandomNumberGenerator.GetString(Digits, 4);

    /// <summary>Identifiant proposé « prénom.nom », en minuscules sans accents ni espaces.</summary>
    public static string SuggestUserName(string? firstName, string? lastName)
    {
        static string Clean(string? value)
        {
            var decomposed = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
                else if ((c is ' ' or '-' or '\'') && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
            }
            return builder.ToString().Trim('-');
        }
        var first = Clean((firstName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault());
        var last = Clean(lastName);
        var name = string.Join('.', new[] { first, last }.Where(p => p.Length > 0));
        if (name.Length > 36) name = name[..36].TrimEnd('.', '-');
        return name.Length >= 3 ? name : (name + ".ens").TrimStart('.');
    }

    public async Task<IReadOnlyList<TeacherAccountDto>> ListAsync(CancellationToken ct = default)
    {
        var year = await YearContext.CurrentAsync(db, ct);
        var accounts = await db.Users.AsNoTracking().Where(u => u.Role == Role.Enseignant && u.StaffMemberId != null).ToListAsync(ct);
        var linked = accounts.Select(a => a.StaffMemberId!.Value).ToHashSet();
        var courses = await (from a in db.CourseAssignments.AsNoTracking()
                             join c in db.Courses.AsNoTracking() on a.CourseId equals c.Id
                             where a.EndedOn == null && c.AcademicYearId == year.Id && c.IsActive
                             group a by a.TeacherId into g
                             select new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        // Écoles et universités : branches et EC attribués au programme de l'année, comptés comme cours.
        var curriculum = await (from a in db.CurriculumAssignments.AsNoTracking()
                                join i in db.CurriculumItems.AsNoTracking() on a.CurriculumItemId equals i.Id
                                where a.EndedOn == null && i.AcademicYearId == year.Id && i.IsActive
                                group a by a.TeacherId into g
                                select new { g.Key, Count = g.Count() }).ToListAsync(ct);
        foreach (var c in curriculum) courses[c.Key] = courses.GetValueOrDefault(c.Key) + c.Count;
        var members = await db.Staff.AsNoTracking().OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToListAsync(ct);
        var now = clock.UtcNow;
        return members
            .Where(m => TeachingRules.IsAvailableDuring(m, year) || linked.Contains(m.Id) || courses.ContainsKey(m.Id))
            .Select(m =>
            {
                // Le compte actif prime ; sinon le dernier compte désactivé, que l'on peut réactiver.
                var account = accounts.Where(a => a.StaffMemberId == m.Id).OrderByDescending(a => a.IsActive).ThenByDescending(a => a.CreatedAtUtc).FirstOrDefault();
                var courseCount = courses.GetValueOrDefault(m.Id);
                return new TeacherAccountDto(m.Id, m.Number, m.FullName, m.Function, m.Phone, m.IsActive, courseCount,
                    account?.Id, account?.UserName, account is not null, account?.IsActive ?? false, account?.MustChangePassword ?? false,
                    account?.LastLoginUtc, account?.LockoutEndUtc is { } end && end > now, account?.JuryAccess ?? false,
                    courseCount > 0 || account is not null || TeachingFunction(m.Function));
            }).ToList();
    }

    /// <summary>Fonction d'enseignement (enseignant, professeur, formateur, encadreur, chargé de cours…).</summary>
    public static bool TeachingFunction(string? function)
    {
        var key = TextKey.Normalize(function);
        return new[] { "ENSEIGN", "PROFESS", "FORMAT", "ENCADR", "CHARGE DE COURS", "TITULAIRE", "MONITEUR", "INSTRUCT" }
            .Any(k => key.Contains(k, StringComparison.Ordinal));
    }

    /// <summary>
    /// Ouvre le compte d'un enseignant : identifiant choisi ou proposé, code provisoire généré. Un compte désactivé
    /// de la même fiche est réactivé avec un nouveau code plutôt que dupliqué.
    /// </summary>
    public Task<TeacherCredentialsDto> IssueAsync(TeacherAccountRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var member = await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == input.StaffMemberId, ct)
            ?? throw new NotFoundException("Fiche de l'enseignant introuvable.");
        var existing = await db.Users.AsNoTracking().Where(u => u.StaffMemberId == member.Id && u.Role == Role.Enseignant)
            .OrderByDescending(u => u.IsActive).ThenByDescending(u => u.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (existing is { IsActive: true })
            throw new ConflictException($"{member.FullName} a déjà le compte « {existing.UserName} ». Utilisez « Nouveau code provisoire » s'il a oublié son code.");
        var code = NewProvisionalCode();
        if (existing is not null)
        {
            await users.UpdateAsync(existing.Id, new UserUpdate(member.FullName, nameof(Role.Enseignant), true, member.Id, input.JuryAccess), ct);
            await users.ResetPasswordAsync(existing.Id, code, ct);
            audit.Record("enseignant.compte.reactive", "Utilisateur", existing.Id.ToString(), $"Compte {existing.UserName} de {member.FullName} réactivé avec un code provisoire.");
            await db.SaveChangesAsync(ct);
            return new TeacherCredentialsDto(member.Id, existing.Id, member.FullName, existing.UserName, code);
        }
        var userName = string.IsNullOrWhiteSpace(input.UserName)
            ? await FreeUserNameAsync(SuggestUserName(member.FirstName, member.LastName), ct)
            : input.UserName.Trim().ToLowerInvariant();
        var created = await users.CreateAsync(new UserCreate(userName, member.FullName, nameof(Role.Enseignant), code, member.Id, input.JuryAccess), ct);
        return new TeacherCredentialsDto(member.Id, created.Id, member.FullName, created.UserName, code);
    }, ct);

    /// <summary>Nouvel enseignant en une étape : fiche du personnel (fonction « Enseignant » par défaut) et, au choix, son compte.</summary>
    public Task<NewTeacherResult> CreateTeacherAsync(NewTeacherRequest input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var function = string.IsNullOrWhiteSpace(input.Function) ? "Enseignant" : input.Function.Trim();
        var member = await staff.UpsertAsync(null, new StaffUpsert(input.LastName ?? string.Empty, input.MiddleName ?? string.Empty, input.FirstName ?? string.Empty,
            string.IsNullOrWhiteSpace(input.Gender) ? "M" : input.Gender, function, input.Phone, input.Email, clock.Today, null, 0m, true), ct);
        var account = input.CreateAccount ? await IssueAsync(new TeacherAccountRequest(member.Id, input.UserName, input.JuryAccess), ct) : null;
        return new NewTeacherResult(member.Id, member.Number, string.Join(' ', new[] { member.LastName, member.MiddleName, member.FirstName }.Where(s => !string.IsNullOrWhiteSpace(s))), account);
    }, ct);

    /// <summary>Nouveau code provisoire pour un compte enseignant (code oublié, compte verrouillé) : à remplacer à la prochaine connexion.</summary>
    public Task<TeacherCredentialsDto> ResetAsync(Guid userId, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var account = await TeacherAccountAsync(userId, ct);
        if (!account.IsActive) throw new DomainException("compte", "Ce compte est désactivé : réactivez-le d'abord.");
        var code = NewProvisionalCode();
        await users.ResetPasswordAsync(account.Id, code, ct);
        return new TeacherCredentialsDto(account.StaffMemberId!.Value, account.Id, account.DisplayName, account.UserName, code);
    }, ct);

    /// <summary>Active ou désactive le compte, accorde ou retire le privilège jury. Les cotes déjà encodées restent.</summary>
    public Task<TeacherAccountDto> UpdateAsync(Guid userId, TeacherAccountUpdate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var account = await TeacherAccountAsync(userId, ct);
        await users.UpdateAsync(account.Id, new UserUpdate(account.DisplayName, nameof(Role.Enseignant), input.IsActive, account.StaffMemberId, input.JuryAccess), ct);
        return (await ListAsync(ct)).First(t => t.UserId == account.Id || t.StaffMemberId == account.StaffMemberId);
    }, ct);

    private async Task<AppUser> TeacherAccountAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("Compte introuvable.");
        // Cette page ne touche qu'aux comptes enseignants : les autres se règlent dans « Utilisateurs ».
        if (account.Role != Role.Enseignant || account.StaffMemberId is null)
            throw new DomainException("compte", "Ce compte n'est pas un compte enseignant : gérez-le dans « Utilisateurs ».");
        return account;
    }

    private async Task<string> FreeUserNameAsync(string wanted, CancellationToken ct)
    {
        var candidate = wanted;
        for (var n = 2; ; n++)
        {
            var key = TextKey.Normalize(candidate);
            if (!await db.Users.AnyAsync(u => u.NormalizedUserName == key, ct)) return candidate;
            candidate = $"{wanted}{n.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
