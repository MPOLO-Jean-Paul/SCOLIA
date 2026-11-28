using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Dtos;
using Scolia.Domain.Security;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Security;

public static class SessionFactory
{
    public static ClaimsPrincipal Principal(AppUser user, SchoolManagementMode managementMode)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ScoliaClaims.UserName, user.UserName),
            new(ScoliaClaims.Role, user.Role.ToString()),
            new(ScoliaClaims.Stamp, user.SecurityStamp),
            new(ScoliaClaims.PermissionVersion, "2"),
            new(ScoliaClaims.ManagementMode, managementMode.ToString()),
        };
        claims.AddRange(Permissions.For(user, managementMode).Select(p => new Claim(ScoliaClaims.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static async Task<ClaimsPrincipal> PrincipalAsync(ScoliaDbContext db, AppUser user, CancellationToken ct = default)
    {
        var mode = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc)
            .Select(s => (SchoolManagementMode?)s.ManagementMode).FirstOrDefaultAsync(ct) ?? SchoolManagementMode.Mixte;
        return Principal(user, mode);
    }

    public static async Task SignInAsync(HttpContext http, AppUser user)
    {
        var db = http.RequestServices.GetRequiredService<ScoliaDbContext>();
        var principal = await PrincipalAsync(db, user, http.RequestAborted);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    /// <summary>Le ticket d'un appareil lié porte un identifiant révocable et est lié à l'en-tête de ce poste.</summary>
    public static async Task SignInDeviceAsync(HttpContext http, AppUser user, Guid grantId, string deviceId)
    {
        var db = http.RequestServices.GetRequiredService<ScoliaDbContext>();
        var principal = await PrincipalAsync(db, user, http.RequestAborted);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("scolia:device-grant", grantId.ToString()));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("scolia:device-id", deviceId));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    public static async Task<SessionDto> DescribeAsync(ScoliaDbContext db, AppUser user, CancellationToken ct)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => y.Label).FirstOrDefaultAsync(ct);
        var mode = school?.ManagementMode ?? SchoolManagementMode.Mixte;
        return new SessionDto(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), Permissions.Label(user), Permissions.For(user, mode),
            user.MustChangePassword, school?.Name ?? "SCOLIA", school?.ShortName, year, school?.CurrencySymbol ?? "FC", school?.PhoneCountryCode ?? "243", school?.InstitutionType ?? "ItmIm", mode);
    }

    /// <summary>Chaque requête vérifie que le compte est actif et que son empreinte de sécurité n'a pas changé
    /// (mot de passe réinitialisé, rôle modifié, compte désactivé) : la session est alors fermée immédiatement.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(ScoliaClaims.Stamp);
        if (!Guid.TryParse(id, out var userId) || stamp is null) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<ScoliaDbContext>();
        var current = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, context.HttpContext.RequestAborted);
        // Révoquer les anciens cookies attribués par QR avant cette correction.
        // Une session utilisateur n'est désormais valable qu'après authentification.
        if (current is null || !current.IsActive || current.SecurityStamp != stamp
            || context.Principal?.HasClaim(c => c.Type == "scolia:device-grant") == true)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        // Une mise à jour de l'application renouvelle les droits des anciens cookies valides.
        // Un changement de mode révoque, lui, leurs empreintes et exige une nouvelle authentification.
        var expected = await PrincipalAsync(db, current, context.HttpContext.RequestAborted);
        var principal = context.Principal!;
        var actualPermissions = principal.FindAll(ScoliaClaims.Permission).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        if (principal.FindFirstValue(ScoliaClaims.PermissionVersion) != "2"
            || principal.FindFirstValue(ScoliaClaims.ManagementMode) != expected.FindFirstValue(ScoliaClaims.ManagementMode)
            || !actualPermissions.SetEquals(expected.FindAll(ScoliaClaims.Permission).Select(c => c.Value)))
        {
            context.ReplacePrincipal(expected);
            context.ShouldRenew = true;
        }
    }
}
