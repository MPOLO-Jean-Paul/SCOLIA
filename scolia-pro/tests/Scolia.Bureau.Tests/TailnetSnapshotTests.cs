using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class TailnetSnapshotTests
{
    [Theory]
    [InlineData("100.101.150.25", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.100.100.100", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("100.101.150.25:5080", false)]
    [InlineData(null, false)]
    public void Plage_Tailscale_cgnat_est_verifiee(string? address, bool expected)
        => Assert.Equal(expected, TailnetSnapshot.IsCandidateTailscaleIp(address));

    [Fact]
    public void Ip_sur_vpn_ne_prouve_pas_identite_Tailscale()
    {
        var state = new TailnetSnapshot(true, true, "100.101.150.25");
        Assert.True(state.LikelyConnected);
        Assert.Contains("100.101.150.25", state.Summary);
        Assert.Contains("non certifiée", state.Summary);
    }

    [Fact]
    public void Application_installee_sans_vpn_ne_recommande_plus_reinstallation()
    {
        var state = new TailnetSnapshot(true, false, null);
        Assert.False(state.LikelyConnected);
        Assert.Contains("installé", state.Summary);
        Assert.Contains("aucun VPN", state.Summary);
    }

    [Fact]
    public void Vpn_sans_paquet_identifiable_reste_incertain()
    {
        var state = new TailnetSnapshot(false, true, "100.101.150.25");
        Assert.False(state.LikelyConnected);
        Assert.Contains("n'a pas pu être confirmé", state.Summary);
    }
}
