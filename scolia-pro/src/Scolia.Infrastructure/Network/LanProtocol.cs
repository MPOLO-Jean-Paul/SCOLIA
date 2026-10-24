using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Scolia.Network;

/// <summary>
/// Découverte de SCOLIA sur le réseau local, sur le principe de la version 79 : un poste envoie une demande en
/// diffusion UDP, chaque serveur répond avec son établissement et son port. Aucune adresse n'est à saisir.
/// Ce fichier est partagé par le serveur et par l'application des postes.
/// </summary>
public static class LanProtocol
{
    /// <summary>Port UDP de la découverte. La version 79 utilise 48629 : les deux peuvent cohabiter sur un poste.</summary>
    public const int DiscoveryPort = 48630;

    private const string Prefix = "SCOLIA_PRO_DISCOVER_V1|";
    private const int NonceLength = 32;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Demande de découverte, avec un jeton aléatoire que la réponse doit reprendre.</summary>
    public static (byte[] Bytes, string Nonce) NewRequest()
    {
        var nonce = Guid.NewGuid().ToString("N");
        return (Encoding.ASCII.GetBytes(Prefix + nonce), nonce);
    }

    /// <summary>Jeton d'une demande bien formée, ou null : toute autre trame est ignorée.</summary>
    public static string? ParseRequest(ReadOnlySpan<byte> data)
    {
        if (data.Length != Prefix.Length + NonceLength) return null;
        var text = Encoding.ASCII.GetString(data);
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var nonce = text[Prefix.Length..];
        return nonce.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? nonce : null;
    }

    public static byte[] Reply(LanAnnouncement announcement) => JsonSerializer.SerializeToUtf8Bytes(announcement, Json);

    /// <summary>Réponse d'un serveur, si elle correspond à la demande envoyée ; sinon null.</summary>
    public static LanAnnouncement? ParseReply(ReadOnlySpan<byte> data, string expectedNonce)
    {
        if (data.Length is 0 or > 2048) return null;
        try
        {
            var announcement = JsonSerializer.Deserialize<LanAnnouncement>(data, Json);
            return announcement is not null && announcement.Nonce == expectedNonce && announcement.Port is > 0 and < 65536 ? announcement : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Adresse du réseau de l'établissement : privée, de liaison locale ou de ce poste. Internet n'est jamais servi.</summary>
    public static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    /// <summary>
    /// Poste autorisé à découvrir le serveur : adresse privée, ou adresse du même sous-réseau que l'une des cartes
    /// du serveur (comme la version 79), pour les réseaux d'établissement qui n'utilisent pas les plages privées.
    /// </summary>
    public static bool IsNeighbour(IPAddress address)
    {
        if (IsLocal(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var remote = address.GetAddressBytes();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var local = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                // Un masque nul couvrirait Internet entier : il est ignoré.
                if (mask.All(b => b == 0)) continue;
                var same = true;
                for (var i = 0; i < 4 && same; i++) same = (local[i] & mask[i]) == (remote[i] & mask[i]);
                if (same) return true;
            }
        }
        return false;
    }
}

/// <summary>Réponse d'un serveur SCOLIA à une demande de découverte.</summary>
public sealed record LanAnnouncement(string Nonce, string SchoolId, string School, string Machine, int Port, string Version);
