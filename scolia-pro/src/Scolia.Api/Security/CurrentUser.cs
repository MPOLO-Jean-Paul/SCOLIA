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
}
