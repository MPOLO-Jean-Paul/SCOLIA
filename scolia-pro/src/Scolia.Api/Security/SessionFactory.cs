using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Dtos;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Security;

public static class SessionFactory
{
    public static ClaimsPrincipal Principal(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ScoliaClaims.UserName, user.UserName),
            new(ScoliaClaims.Role, user.Role.ToString()),
            new(ScoliaClaims.Stamp, user.SecurityStamp),
        };
        claims.AddRange(Permissions.For(user).Select(p => new Claim(ScoliaClaims.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static Task SignInAsync(HttpContext http, AppUser user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(user), new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });

    /// <summary>Le ticket d'un appareil lié porte un identifiant révocable et est lié à l'en-tête de ce poste.</summary>
    public static Task SignInDeviceAsync(HttpContext http, AppUser user, Guid grantId, string deviceId)
    {
        var principal = Principal(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("scolia:device-grant", grantId.ToString()));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("scolia:device-id", deviceId));
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = false, AllowRefresh = true });
    }

    public static async Task<SessionDto> DescribeAsync(ScoliaDbContext db, AppUser user, CancellationToken ct)
    {
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.IsCurrent).Select(y => y.Label).FirstOrDefaultAsync(ct);
        return new SessionDto(user.Id, user.UserName, user.DisplayName, user.Role.ToString(), Permissions.Label(user), Permissions.For(user),
            user.MustChangePassword, school?.Name ?? "SCOLIA", school?.ShortName, year, school?.CurrencySymbol ?? "FC", school?.PhoneCountryCode ?? "243");
    }

    /// <summary>Chaque requête vérifie que le compte est actif et que son empreinte de sécurité n'a pas changé
    /// (mot de passe réinitialisé, rôle modifié, compte désactivé) : la session est alors fermée immédiatement.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(ScoliaClaims.Stamp);
        if (!Guid.TryParse(id, out var userId) || stamp is null) { context.RejectPrincipal(); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<ScoliaDbContext>();
        var current = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.SecurityStamp, u.IsActive }).FirstOrDefaultAsync();
        var invalid = current is null || !current.IsActive || current.SecurityStamp != stamp;
        // Révoquer les anciens cookies attribués par QR avant cette correction.
        // Une session utilisateur n'est désormais valable qu'après authentification.
        if (context.Principal?.HasClaim(c => c.Type == "scolia:device-grant") == true)
            invalid = true;
        if (invalid)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
