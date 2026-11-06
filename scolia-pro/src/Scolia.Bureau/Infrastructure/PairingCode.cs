using System.Net;
using System.Net.Sockets;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Code de liaison montré en QR par le serveur de l'établissement, ordinateur ou téléphone. Il dit à l'autre appareil
/// comment le joindre : les adresses locales et, si activée, l'invitation Internet Render.
/// Il ne contient aucun mot de passe : l'accord du serveur et la connexion au compte restent demandés.
/// </summary>
public sealed record PairingCode(string School, string Machine, IReadOnlyList<Uri> Addresses, string? Invitation = null, string? AccessTicket = null)
{
    public const string Prefix = "scolia://relier?";
    private const int MaxLength = 2048;
    // Limiter la densité du QR affiché : une adresse Wi-Fi et les premières
    // voies privées suffisent, plutôt que six adresses illisibles en photo.
    private const int MaxAddresses = 3;

    /// <summary>Adresses du Wi-Fi de l'établissement, essayées en premier.</summary>
    public IEnumerable<Uri> LanAddresses => Addresses.Where(a => !TailscaleEndpoint.IsTailnetAddress(a));

    /// <summary>Adresses du réseau privé Tailscale, essayées ensuite.</summary>
    public IEnumerable<Uri> TailnetAddresses => Addresses.Where(TailscaleEndpoint.IsTailnetAddress);

    public bool HasInvitation => !string.IsNullOrEmpty(Invitation);

    /// <summary>Texte du QR : court, lisible par n'importe quel lecteur, sans secret.</summary>
    public string Text()
    {
        var parts = new List<string>
        {
            "v=1",
            "e=" + Uri.EscapeDataString(School),
            "m=" + Uri.EscapeDataString(Machine),
        };
        // Le format v1 historique ne sait pas transporter un schema. Ne jamais
        // transformer une URL HTTPS en HTTP au moment de dessiner le QR.
        var clear = Addresses.Where(a => a.Scheme == Uri.UriSchemeHttp).Take(MaxAddresses).ToArray();
        if (clear.Length > 0)
            parts.Add("a=" + string.Join(',', clear.Select(a => $"{Host(a)}:{a.Port}")));
        // Extension compatible v1 : une adresse HTTPS Tailnet strictement valide.
        // Les anciens clients ignorent "h" ; les nouveaux privilegient cette voie.
        var secure = Addresses.FirstOrDefault(a => a.Scheme == Uri.UriSchemeHttps
            && a.Port == 443 && TailscaleEndpoint.IsSecureTailnetDns(a.Host));
        if (secure is not null) parts.Add("h=" + Uri.EscapeDataString(secure.Host));
        if (HasInvitation) parts.Add("l=" + Uri.EscapeDataString(Invitation!));
        if (!string.IsNullOrWhiteSpace(AccessTicket)) parts.Add("p=" + Uri.EscapeDataString(AccessTicket));
        return Prefix + string.Join('&', parts);
    }

    /// <summary>
    /// Lit un code scanné ou collé. Accepte aussi une invitation Internet seule (scolia-link:…).
    /// Toute adresse hors du réseau local ou de Tailscale est refusée : un QR trouvé ailleurs ne peut pas
    /// envoyer cet appareil vers un serveur inconnu sur Internet.
    /// </summary>
    public static bool TryParse(string? text, out PairingCode? code, out string error)
    {
        code = null;
        error = "Ce code QR n'est pas un code de liaison SCOLIA.";
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength) return false;

        if (text.StartsWith(Scolia.Transport.RemoteLink.Prefix, StringComparison.Ordinal))
        {
            code = new PairingCode("Établissement", string.Empty, [], text);
            error = string.Empty;
            return true;
        }
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text[Prefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=');
            if (cut <= 0) continue;
            try { values[pair[..cut]] = Uri.UnescapeDataString(pair[(cut + 1)..]); }
            catch (UriFormatException) { return false; }
        }
        if (!values.TryGetValue("v", out var version) || version != "1")
        {
            error = "Ce code vient d'une autre version de SCOLIA. Mettez les deux appareils à jour.";
            return false;
        }

        var addresses = new List<Uri>();
        if (values.TryGetValue("h", out var httpsHost))
        {
            if (httpsHost.Contains(':') || httpsHost.Contains('/') || httpsHost.Contains('\\')
                || !TailscaleEndpoint.IsSecureTailnetDns(httpsHost)
                || !Uri.TryCreate($"https://{httpsHost}/", UriKind.Absolute, out var secure)
                || secure.Port != 443 || secure.UserInfo.Length > 0 || secure.Query.Length > 0)
            {
                error = "L'adresse HTTPS du code QR est invalide : aucune connexion HTTP de remplacement n'a ete lancee.";
                return false;
            }
            addresses.Add(secure);
        }
        if (values.TryGetValue("a", out var list))
            foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(MaxAddresses))
                if (ToAddress(item) is { } uri && !addresses.Contains(uri)) addresses.Add(uri);

        var invitation = values.TryGetValue("l", out var link) && link.StartsWith(Scolia.Transport.RemoteLink.Prefix, StringComparison.Ordinal) ? link : null;
        // Un code d'accès malformé ne doit pas être discrètement rétrogradé en simple liaison réseau.
        var accessTicket = values.GetValueOrDefault("p");
        if (accessTicket is not null && (accessTicket.Length != 48 || !accessTicket.All(char.IsAsciiHexDigit)))
        {
            error = "L'invitation de cet appareil est invalide. Générez un nouveau code sur le serveur.";
            return false;
        }
        if (addresses.Count == 0 && invitation is null)
        {
            error = "Ce code ne contient aucune adresse utilisable sur le réseau de l'établissement.";
            return false;
        }

        code = new PairingCode(
            Clean(values.GetValueOrDefault("e"), "Établissement"),
            Clean(values.GetValueOrDefault("m"), string.Empty),
            addresses, invitation, accessTicket);
        error = string.Empty;
        return true;
    }

    /// <summary>Code de ce serveur : ses adresses actuelles et, si elle est active, son invitation Internet.</summary>
    public static PairingCode ForThisServer(string school, string machine, IEnumerable<string> addresses, string? invitation)
    {
        var found = new List<Uri>();
        foreach (var text in addresses)
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp
                && !TailscaleEndpoint.IsTailnetAddress(uri)
                && IsAllowed(uri.Host) && !found.Contains(uri)) found.Add(uri);
        // Les nouvelles invitations ne publient plus d'adresse de réseau VPN.
        // La lecture des anciens QR reste prise en charge pour proposer un renouvellement.
        var ordered = found.Take(MaxAddresses).ToList();
        return new PairingCode(school, machine, ordered, invitation);
    }

    private static Uri? ToAddress(string item)
    {
        if (!Uri.TryCreate("http://" + item, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return null;
        if (uri.AbsolutePath != "/" || uri.UserInfo.Length > 0 || uri.Query.Length > 0) return null;
        if (uri.IsDefaultPort && !item.EndsWith(":80", StringComparison.Ordinal)) uri = new UriBuilder("http", uri.Host, PosteAdmin.DefaultPort).Uri;
        return IsAllowed(uri.Host) ? new UriBuilder("http", uri.Host, uri.Port).Uri : null;
    }

    /// <summary>Réseau local privé (10.x, 172.16–31.x, 192.168.x) ou Tailscale (100.64–127.x, nom .ts.net).</summary>
    public static bool IsAllowed(string host)
    {
        host = host.Trim('[', ']');
        if (TailscaleEndpoint.IsTailnetHost(host)) return true;
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        if (TailscaleEndpoint.IsTailnetIp(ip)) return true;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    private static string Host(Uri uri) => uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host.Trim('[', ']')}]" : uri.Host;

    private static string Clean(string? text, string fallback)
    {
        var value = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return value.Length == 0 ? fallback : value.Length > 120 ? value[..120] : value;
    }
}
