using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Common;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

public sealed class UserService(IAppDbContext db, IWriteGate gate, IAuditLog audit, IClock clock, IPasswordService passwords, ICurrentUser current)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly (Role, string)[] RoleLabels = [(Role.Secretariat, "Secrétariat"), (Role.Comptabilite, "Comptabilité")];

    public static UserDto ToDto(AppUser u, DateTime now) => new(u.Id, u.UserName, u.DisplayName, u.Role.ToString(), Permissions.Label(u.Role), u.IsActive,
        u.MustChangePassword, u.LastLoginUtc, u.LockoutEndUtc is not null && u.LockoutEndUtc > now);

    /// <summary>Authentifie un utilisateur. Message identique pour un identifiant inconnu ou un mot de passe faux.
    /// Les échecs sont enregistrés (transaction validée) avant le refus : le verrouillage est donc effectif.</summary>
    public async Task<AppUser> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        const string invalid = "Identifiant ou mot de passe incorrect.";
        var (user, error) = await gate.RunAsync<(AppUser?, string?)>(async () =>
        {
            var normalized = TextKey.Normalize(userName);
            var found = await db.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, ct);
            if (found is null || !found.IsActive)
            {
                _ = passwords.Hash(password ?? string.Empty); // Même durée qu'une vérification : pas d'énumération des comptes.
                return (null, invalid);
            }
            var now = clock.UtcNow;
            if (found.LockoutEndUtc is not null && found.LockoutEndUtc > now)
                return (null, $"Compte temporairement verrouillé après plusieurs erreurs. Réessayez après {found.LockoutEndUtc.Value.ToLocalTime():HH:mm}.");
            if (!passwords.Verify(found.PasswordHash, password ?? string.Empty, out var rehash))
            {
                found.FailedLoginCount++;
                audit.Record("connexion.echec", "Utilisateur", found.Id.ToString(), $"Échec de connexion pour {found.UserName} ({found.FailedLoginCount}/{MaxFailedAttempts}).");
                if (found.FailedLoginCount >= MaxFailedAttempts)
                {
                    found.LockoutEndUtc = now + LockoutDuration;
                    found.FailedLoginCount = 0;
                    audit.Record("connexion.verrouillage", "Utilisateur", found.Id.ToString(), $"Compte {found.UserName} verrouillé 15 minutes après {MaxFailedAttempts} échecs.");
                }
                await db.SaveChangesAsync(ct);
                return (null, invalid);
            }
            if (rehash) found.PasswordHash = passwords.Hash(password!);
            found.FailedLoginCount = 0;
            found.LockoutEndUtc = null;
            found.LastLoginUtc = now;
            audit.Record("connexion.reussie", "Utilisateur", found.Id.ToString(), $"Connexion de {found.UserName}.");
            await db.SaveChangesAsync(ct);
            return (found, null);
        }, ct);
        if (user is null) throw new DomainException(error!.StartsWith("Compte", StringComparison.Ordinal) ? "verrouille" : "connexion", error);
        return user;
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default) =>
        (await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct)).Select(u => ToDto(u, clock.UtcNow)).ToList();

    public Task<UserDto> CreateAsync(UserCreate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var name = (input.UserName ?? string.Empty).Trim().ToLowerInvariant();
        new ValidationBuilder()
            .Require(name.Length is >= 3 and <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'), "userName",
                "Identifiant de 3 à 40 caractères (lettres, chiffres, point, tiret).")
            .Require(!string.IsNullOrWhiteSpace(input.DisplayName) && input.DisplayName.Length <= 120, "displayName", "Nom affiché obligatoire.")
            .ThrowIfInvalid();
        PasswordPolicy.Validate(input.Password);
        var role = EnumText.Parse(input.Role, "role", RoleLabels);
        var normalized = TextKey.Normalize(name);
        if (await db.Users.AnyAsync(u => u.NormalizedUserName == normalized, ct)) throw new ConflictException("Cet identifiant existe déjà.");
        var user = new AppUser
        {
            UserName = name, NormalizedUserName = normalized, DisplayName = input.DisplayName.Trim(), Role = role,
            PasswordHash = passwords.Hash(input.Password), MustChangePassword = true,
        };
        db.Users.Add(user);
        audit.Record("utilisateur.cree", "Utilisateur", user.Id.ToString(), $"Compte {name} ({Permissions.Label(role)}) créé.");
        await db.SaveChangesAsync(ct);
        return ToDto(user, clock.UtcNow);
    }, ct);

    public Task<UserDto> UpdateAsync(Guid id, UserUpdate input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Utilisateur introuvable.");
        var role = EnumText.Parse(input.Role, "role", RoleLabels);
        if (string.IsNullOrWhiteSpace(input.DisplayName)) throw new DomainException("displayName", "Nom affiché obligatoire.");
        if (user.Role == Role.Administrateur && (role != Role.Administrateur || !input.IsActive))
        {
            var admins = await db.Users.CountAsync(u => u.Role == Role.Administrateur && u.IsActive && u.Id != id, ct);
            if (admins == 0) throw new DomainException("role", "Au moins un administrateur actif doit être conservé.");
        }
        if (id == current.Id && !input.IsActive) throw new DomainException("isActive", "Vous ne pouvez pas désactiver votre propre compte.");
        var changedSecurity = user.Role != role || user.IsActive != input.IsActive;
        user.DisplayName = input.DisplayName.Trim();
        user.Role = role;
        user.IsActive = input.IsActive;
        if (changedSecurity) user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("utilisateur.modifie", "Utilisateur", id.ToString(), $"Compte {user.UserName} : {Permissions.Label(role)}, {(input.IsActive ? "actif" : "désactivé")}.");
        await db.SaveChangesAsync(ct);
        return ToDto(user, clock.UtcNow);
    }, ct);

    public Task ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        PasswordPolicy.Validate(newPassword);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Utilisateur introuvable.");
        user.PasswordHash = passwords.Hash(newPassword);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("utilisateur.motdepasse", "Utilisateur", id.ToString(), $"Mot de passe de {user.UserName} réinitialisé par l'administrateur.");
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task<AppUser> ChangeOwnPasswordAsync(Guid id, PasswordChange input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("Utilisateur introuvable.");
        if (!passwords.Verify(user.PasswordHash, input.CurrentPassword ?? string.Empty, out _)) throw new DomainException("currentPassword", "Mot de passe actuel incorrect.");
        PasswordPolicy.Validate(input.NewPassword);
        if (input.NewPassword == input.CurrentPassword) throw new DomainException("newPassword", "Le nouveau mot de passe doit être différent de l'ancien.");
        user.PasswordHash = passwords.Hash(input.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("utilisateur.motdepasse", "Utilisateur", id.ToString(), $"{user.UserName} a changé son mot de passe.");
        await db.SaveChangesAsync(ct);
        return user;
    }, ct);
}

public sealed class AuditService(IAppDbContext db)
{
    /// <summary>Export CSV (séparateur point-virgule, comme Excel en français) de toutes les entrées filtrées, dans l'ordre chronologique.</summary>
    public async Task<string> ExportCsvAsync(string? search, string? entityType, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var q = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (from is not null) { var f = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.AtUtc >= f); }
        if (to is not null) { var t = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.AtUtc < t); }
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Summary.Contains(search) || a.UserName.Contains(search) || a.Action.Contains(search));
        var rows = await q.OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.AtUtc, a.UserName, a.Action, a.EntityType, a.EntityId, a.Summary, a.IpAddress })
            .ToListAsync(ct);
        static string Cell(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var sb = new System.Text.StringBuilder();
        sb.Append("Numéro;Date UTC;Utilisateur;Action;Type;Identifiant;Résumé;Adresse IP\r\n");
        foreach (var r in rows)
            sb.Append(r.Id).Append(';').Append(Cell(r.AtUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))).Append(';')
              .Append(Cell(r.UserName)).Append(';').Append(Cell(r.Action)).Append(';').Append(Cell(r.EntityType)).Append(';')
              .Append(Cell(r.EntityId)).Append(';').Append(Cell(r.Summary)).Append(';').Append(Cell(r.IpAddress)).Append("\r\n");
        return sb.ToString();
    }

    public async Task<PagedResult<AuditDto>> ListAsync(string? search, string? entityType, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (from is not null) { var f = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.AtUtc >= f); }
        if (to is not null) { var t = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); q = q.Where(a => a.AtUtc < t); }
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Summary.Contains(search) || a.UserName.Contains(search) || a.Action.Contains(search));
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 500);
        page = Math.Max(1, page);
        var items = await q.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditDto(a.Id, a.AtUtc, a.UserName, a.Action, a.EntityType, a.EntityId, a.Summary, a.IpAddress)).ToListAsync(ct);
        return new PagedResult<AuditDto>(items, total, page, pageSize);
    }

    /// <summary>Recalcule toute la chaîne d'empreintes : détecte une modification ou suppression d'entrée.</summary>
    public async Task<AuditVerification> VerifyAsync(CancellationToken ct = default)
    {
        var previous = string.Empty;
        long count = 0;
        long lastId = 0;
        await foreach (var entry in db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            count++;
            if (entry.PreviousHash != previous || entry.ComputeHash() != entry.Hash)
                return new AuditVerification(false, count, entry.Id, $"Rupture détectée à l'entrée n° {entry.Id} : le journal a été modifié en dehors de l'application.");
            if (lastId != 0 && entry.Id != lastId + 1)
                return new AuditVerification(false, count, entry.Id, $"Entrée(s) manquante(s) avant le n° {entry.Id}.");
            previous = entry.Hash;
            lastId = entry.Id;
        }
        return new AuditVerification(true, count, null, $"Journal intègre : {count} entrée(s) vérifiée(s).");
    }
}

public sealed class SearchService(IAppDbContext db)
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? text, CancellationToken ct = default)
    {
        var terms = TextKey.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];
        var students = db.Students.AsNoTracking().AsQueryable();
        foreach (var term in terms) { var t = term; students = students.Where(s => s.SearchText.Contains(t)); }
        var hits = (await students.OrderBy(s => s.LastName).Take(8).ToListAsync(ct))
            .Select(s => new SearchHit("Apprenant", $"{s.FullName}", $"Matricule {s.Matricule}", $"/apprenants/{s.Id}")).ToList();
        var number = string.Join(' ', terms);
        var receipts = await db.Receipts.AsNoTracking().Where(r => r.Number.Contains(number)).OrderByDescending(r => r.Date).Take(5)
            .Select(r => new { r.Id, r.Number, r.Date, r.Amount }).ToListAsync(ct);
        hits.AddRange(receipts.Select(r => new SearchHit("Reçu", r.Number, $"{r.Date:dd/MM/yyyy} · {r.Amount:N0}", $"/paiements/recus/{r.Id}")));
        return hits;
    }
}
