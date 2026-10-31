using System.Security.Claims;
using Scolia.Application.Abstractions;

namespace Scolia.Api.Security;

public static class ScoliaClaims
{
    public const string Permission = "scolia:perm";
    public const string Stamp = "scolia:stamp";
    public const string Role = "scolia:role";
    public const string UserName = "scolia:login";
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string Name => Principal?.Identity?.IsAuthenticated == true ? Principal.FindFirstValue(ClaimTypes.Name) ?? "?" : "système";

    public bool HasPermission(string permission) => Principal?.HasClaim(ScoliaClaims.Permission, permission) == true;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? DeviceId => LiveHeaders.DeviceId(accessor.HttpContext);

    public string? Device => LiveHeaders.Device(accessor.HttpContext);
}

/// <summary>
/// En-têtes par lesquels chaque poste se présente : un identifiant stable, et un nom lisible encodé pour l'URL
/// (les en-têtes HTTP n'acceptent pas les accents).
/// </summary>
public static class LiveHeaders
{
    public const string DeviceIdHeader = "X-Scolia-Poste";
    public const string DeviceHeader = "X-Scolia-Appareil";
    public const string LockHeader = "X-Scolia-Verrou";

    public static string? DeviceId(HttpContext? http) =>
        http?.Request.Headers[DeviceIdHeader].ToString() is { Length: > 0 and <= 64 } id ? id : null;

    public static string? Device(HttpContext? http)
    {
        if (http?.Request.Headers[DeviceHeader].ToString() is not { Length: > 0 } raw) return null;
        var name = Uri.UnescapeDataString(raw).Trim();
        return name.Length > 80 ? name[..80] : name;
    }
}
