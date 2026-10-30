using System.Net;
using System.Net.Sockets;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Adresse de liaison privée Tailscale (transport réseau uniquement).
/// Aucun jeton Tailscale n'est stocké : l'utilisateur installe et autorise le VPN séparément.
/// Le serveur SCOLIA conserve sa propre authentification et ses permissions métier.
/// </summary>
public static class TailscaleEndpoint
{
    public static bool TryParse(string? address, int defaultPort, out Uri? endpoint)
    {
        endpoint = null;
        var candidate = address?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Contains(' ') || candidate.Contains('\\'))
            return false;
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "http://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp || uri.Host.Length == 0
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/" || !IsTailnetHost(uri.Host))
            return false;
        var port = uri.IsDefaultPort ? defaultPort : uri.Port;
        if (port is < 1 or > 65535) return false;
        endpoint = new UriBuilder(Uri.UriSchemeHttp, uri.Host, port, "/").Uri;
        return true;
    }

    public static bool IsTailnetHost(string host)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            // Plage Tailscale IPv4, soumise aux règles d'accès de la tailnet.
            var octets = ip.GetAddressBytes();
            return octets[0] == 100 && octets[1] is >= 64 and <= 127;
        }
        // Nom MagicDNS complet du tailnet, sans supposer qu'un simple nom DNS est approuvé.
        return host.Length > ".ts.net".Length && host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)
            && host[..^".ts.net".Length].Split('.').All(x => x.Length is > 0 and <= 63
                && x.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    public static bool IsTailnetAddress(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && IsTailnetHost(uri.Host);
}
