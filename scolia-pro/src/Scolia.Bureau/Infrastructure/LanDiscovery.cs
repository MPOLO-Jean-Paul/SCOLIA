using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Scolia.Network;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Serveur SCOLIA trouvé sur le réseau de l'établissement.</summary>
public sealed record DiscoveredServer(string SchoolId, string School, string Machine, string Version, Uri Address)
{
    public string Detail => $"Ordinateur {Machine} · {Address.Host} · version {Version}";
}

/// <summary>
/// Recherche des serveurs SCOLIA du réseau local : une demande en diffusion sur chaque carte réseau, puis les réponses
/// reçues pendant un court délai. Aucune adresse n'est à connaître ni à saisir.
/// </summary>
public static class LanDiscovery
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMilliseconds(1600);

    public static async Task<IReadOnlyList<DiscoveredServer>> FindAsync(TimeSpan? wait = null, CancellationToken ct = default)
    {
        var (request, nonce) = LanProtocol.NewRequest();
        var found = new Dictionary<string, DiscoveredServer>();
        try
        {
            using var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            foreach (var target in Targets())
            {
                try { await socket.SendAsync(request, new IPEndPoint(target, LanProtocol.DiscoveryPort), ct); }
                catch (SocketException) { }
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(wait ?? DefaultWait);
            while (true)
            {
                var received = await socket.ReceiveAsync(limit.Token);
                if (LanProtocol.ParseReply(received.Buffer, nonce) is not { } a) continue;
                var address = new Uri($"http://{received.RemoteEndPoint.Address.MapToIPv4()}:{a.Port}/");
                // Un serveur répond une fois par carte réseau qui l'atteint : on le garde une seule fois.
                found.TryAdd($"{a.SchoolId}|{a.Machine}", new DiscoveredServer(a.SchoolId, a.School, a.Machine, a.Version, address));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (SocketException) { }
        return found.Values.OrderBy(s => s.School, StringComparer.CurrentCulture).ThenBy(s => s.Machine, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Diffusion générale, plus la diffusion propre à chaque réseau du poste (certains routeurs ne relaient que celle-ci).</summary>
    private static IEnumerable<IPAddress> Targets()
    {
        yield return IPAddress.Broadcast;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null) continue;
                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(broadcast);
            }
        }
    }
}
