using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Domain.Common;
using Scolia.Domain.Security;

namespace Scolia.Application.Services;

/// <summary>Contrôle les droits persistés au moment de l'écriture, après l'attente éventuelle du verrou.</summary>
internal static class EffectivePermissions
{
    public static async Task<bool> AllowsAsync(IAppDbContext db, ICurrentUser current, string permission, CancellationToken ct)
    {
        if (!current.HasPermission(permission) || current.Id is not { } id) return false;
        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        if (actor is null || current.SecurityStamp is { } stamp && actor.SecurityStamp != stamp) return false;
        var mode = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).Select(s => s.ManagementMode).FirstOrDefaultAsync(ct);
        return Permissions.For(actor, mode).Contains(permission);
    }

    public static async Task RequireAsync(IAppDbContext db, ICurrentUser current, string permission, CancellationToken ct)
    {
        if (!await AllowsAsync(db, current, permission, ct))
            throw new ForbiddenException("Cette opération n'est pas autorisée avec les droits actuels du compte. Reconnectez-vous pour les actualiser.");
    }
}
