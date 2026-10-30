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

    /// <summary>
    /// Résolution DNS défensive : un nom .ts.net n'est utilisable que s'il mène à
    /// des adresses du réseau Tailscale. Évite d'envoyer une session SCOLIA à un
    /// serveur public si le DNS n'est pas celui du tailnet connecté.
    /// </summary>
    public static async Task<bool> ResolvesPrivatelyAsync(Uri uri, CancellationToken ct = default)
    {
        if (!IsTailnetAddress(uri)) return false;
        if (IPAddress.TryParse(uri.Host, out var literal)) return IsTailnetIp(literal);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
            return addresses.Length > 0 && addresses.All(IsTailnetIp);
        }
        catch (SocketException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public static bool IsTailnetIp(IPAddress ip)
    {
        var address = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return address[0] == 100 && address[1] is >= 64 and <= 127;
        // Tailscale IPv6 ULA fd7a:115c:a1e0::/48 (MagicDNS peut publier A et AAAA).
        return ip.AddressFamily == AddressFamily.InterNetworkV6
            && address[0] == 0xfd && address[1] == 0x7a
            && address[2] == 0x11 && address[3] == 0x5c
            && address[4] == 0xa1 && address[5] == 0xe0;
    }

    public static bool IsTailnetAddress(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp && IsTailnetHost(uri.Host);
}
