using System.Net;
using System.Net.Sockets;
using System.Text;
using Scolia.Network;

namespace Scolia.Tests;

/// <summary>Découverte du serveur sur le réseau local : réponse réelle par UDP, silence quand le réseau est fermé.</summary>
public sealed class LanDiscoveryTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    /// <summary>Envoie la demande plusieurs fois : le serveur ouvre son écoute juste après son démarrage.</summary>
    private static async Task<LanAnnouncement?> AskAsync(int port, int attempts)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var (request, nonce) = LanProtocol.NewRequest();
        for (var i = 0; i < attempts; i++)
        {
            await udp.SendAsync(request, new IPEndPoint(IPAddress.Loopback, port));
            using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            try
            {
                var received = await udp.ReceiveAsync(wait.Token);
                if (LanProtocol.ParseReply(received.Buffer, nonce) is { } found) return found;
            }
            catch (OperationCanceledException) { }
        }
        return null;
    }

    [Fact]
    public async Task The_server_answers_with_its_school_and_port_when_the_network_is_open()
    {
        var port = FreeUdpPort();
        app.Settings["Scolia:Reseau:AccesReseauLocal"] = "true";
        app.Settings["Scolia:Reseau:PortDecouverte"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        app.Settings["Scolia:Reseau:Port"] = "5099";
        await app.InstalledAdmin();

        var found = await AskAsync(port, 25);

        Assert.NotNull(found);
        Assert.Equal("Institut de test", found.School);
        Assert.Equal(5099, found.Port);
        Assert.True(Guid.TryParse(found.SchoolId, out _));
        Assert.False(string.IsNullOrWhiteSpace(found.Machine));
    }

    [Fact]
    public async Task The_server_stays_silent_when_the_network_is_closed()
    {
        var port = FreeUdpPort();
        app.Settings["Scolia:Reseau:PortDecouverte"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await app.InstalledAdmin();

        Assert.Null(await AskAsync(port, 3));
    }

    [Fact]
    public void Malformed_requests_and_foreign_replies_are_ignored()
    {
        var (request, nonce) = LanProtocol.NewRequest();
        Assert.Equal(nonce, LanProtocol.ParseRequest(request));
        Assert.Null(LanProtocol.ParseRequest(Encoding.ASCII.GetBytes("SCOLIA_PRO_DISCOVER_V1|pas-un-jeton")));
        Assert.Null(LanProtocol.ParseRequest(Encoding.ASCII.GetBytes("SCOLIA|1|" + nonce)));

        var reply = LanProtocol.Reply(new LanAnnouncement(nonce, Guid.NewGuid().ToString(), "École", "PC-1", 5080, "1.2.0"));
        Assert.NotNull(LanProtocol.ParseReply(reply, nonce));
        Assert.Null(LanProtocol.ParseReply(reply, Guid.NewGuid().ToString("N")));
        Assert.Null(LanProtocol.ParseReply(Encoding.UTF8.GetBytes("{pas du json"), nonce));
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.20.3.4", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.32.0.1", false)]
    public void Only_the_school_network_is_served(string address, bool local) =>
        Assert.Equal(local, LanProtocol.IsLocal(IPAddress.Parse(address)));
}
