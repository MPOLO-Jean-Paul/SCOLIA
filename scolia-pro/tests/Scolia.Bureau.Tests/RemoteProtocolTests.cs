using System.Security.Cryptography;
using System.Text;
using Scolia.Transport;

namespace Scolia.Bureau.Tests;

public sealed class RemoteProtocolTests
{
    [Fact]
    public void Invitation_never_discloses_host_identity_and_requires_secure_relay()
    {
        var link = RemoteLink.Create("wss://relay.example/");
        var joined = RemoteLink.Parse(link.Invitation());
        Assert.False(joined.CanHost);
        Assert.Null(joined.HostToken);
        Assert.Equal(link.Key, joined.Key);
        Assert.Equal(link.Room, joined.Room);
        Assert.Throws<FormatException>(() => RemoteLink.Create("ws://relay.example/"));
        Assert.Throws<FormatException>(() => RemoteLink.Create("https://relay.example/"));
        Assert.Throws<FormatException>(() => RemoteLink.Parse((link with { ExpiresUtc = DateTime.UtcNow.AddSeconds(-1) }).Invitation()));
    }

    [Fact]
    public void Renewal_revokes_the_previous_secret_without_stranding_the_local_queue_identity()
    {
        var original = RemoteLink.Create("wss://relay.example/");
        var renewed = original.Renew("wss://another-relay.example/");
        Assert.Equal(original.VirtualServer, renewed.VirtualServer);
        Assert.Equal(original.HostToken, renewed.HostToken);
        Assert.NotEqual(original.ClientToken, renewed.ClientToken);
        Assert.NotEqual(original.Key, renewed.Key);
    }

    [Fact]
    public void Ciphertext_is_bound_to_request_direction_sequence_epoch_and_key()
    {
        var link = RemoteLink.Create("wss://relay.example/");
        var epoch = RandomNumberGenerator.GetBytes(32);
        var id = Guid.NewGuid();
        var clear = Encoding.UTF8.GetBytes("paiement confidentiel 55000 FC");
        var packet = RelayWire.Seal(link, epoch, id, 0, false, clear);
        Assert.Equal(clear, RelayWire.Open(link, epoch, packet, id, 0, false));
        Assert.DoesNotContain("paiement", Encoding.UTF8.GetString(packet), StringComparison.Ordinal);
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link, epoch, packet, Guid.NewGuid(), 0, false));
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link, epoch, packet, id, 1, false));
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link, epoch, packet, id, 0, true));
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link, RandomNumberGenerator.GetBytes(32), packet, id, 0, false));
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link with { Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }, epoch, packet, id, 0, false));
        packet[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => RelayWire.Open(link, epoch, packet, id, 0, false));
    }
}
