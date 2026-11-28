using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>Organisation de l'école, indépendante des préférences d'affichage de chaque appareil.</summary>
public sealed class ManagementModeService(IAppDbContext db, IWriteGate gate, IAuditLog audit,
    ICurrentUser current, IPasswordService passwords)
{
    public async Task<ManagementModeStatus> ReadAsync(CancellationToken ct = default)
    {
        await SecretaryAsync(ct);
        var school = await YearContext.SchoolAsync(db, ct);
        return new(school.ManagementMode, await db.Users.CountAsync(u => u.IsActive && u.Role == Role.Comptabilite, ct));
    }

    public Task<AppUser> ChangeAsync(ManagementModeChange input, CancellationToken ct = default) => gate.RunAsync(async () =>
    {
        var actor = await SecretaryAsync(ct);
        if (!Enum.IsDefined(input.Mode)) throw new DomainException("mode", "Choisissez la gestion mixte ou la gestion séparée.");
        if (string.IsNullOrEmpty(input.CurrentPassword) || !passwords.Verify(actor.PasswordHash, input.CurrentPassword, out _))
            throw new DomainException("currentPassword", "Le code ou mot de passe actuel est incorrect.");
        var school = await db.Schools.OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("configuration", "L'établissement n'est pas encore configuré.");
        if (school.ManagementMode == input.Mode) return actor;
        if (input.Mode == SchoolManagementMode.Separee
            && !await db.Users.AnyAsync(u => u.IsActive && u.Role == Role.Comptabilite, ct))
            throw new DomainException("comptabilite", "Créez ou activez d'abord un compte Comptabilité dans Utilisateurs pour lui confier les opérations financières.");

        var before = school.ManagementMode;
        school.ManagementMode = input.Mode;
        // Les permissions de ces deux métiers changent ; leurs anciennes délégations ne doivent pas survivre au choix.
        var affected = await db.Users.Where(u => u.Role == Role.Secretariat || u.Role == Role.Administrateur
            || u.Role == Role.Direction || u.Role == Role.Comptabilite).ToListAsync(ct);
        foreach (var user in affected) user.SecurityStamp = Guid.NewGuid().ToString("N");
        audit.Record("organisation.mode-modifie", "Etablissement", school.Id.ToString(),
            $"Type de gestion : {Label(before)} → {Label(input.Mode)}. Droits du secrétariat et de la comptabilité actualisés.");
        await db.SaveChangesAsync(ct);
        return actor;
    }, ct);

    private async Task<AppUser> SecretaryAsync(CancellationToken ct)
    {
        if (!current.HasPermission(Permissions.SettingsWrite) || current.Id is not { } id)
            throw new ForbiddenException("Le type de gestion est réservé au secrétariat.");
        var actor = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (actor is null || actor.Role is not (Role.Secretariat or Role.Administrateur or Role.Direction))
            throw new ForbiddenException("Le type de gestion est réservé au secrétariat.");
        return actor;
    }

    private static string Label(SchoolManagementMode mode) => mode == SchoolManagementMode.Separee ? "Gestion séparée" : "Gestion mixte";
}
