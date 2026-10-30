using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le mode Tailscale n'accepte pas d'URL Internet publique ni d'identifiants dans l'adresse.</summary>
public class TailscaleEndpointTests
{
    [Theory]
    [InlineData("100.64.0.1", "100.64.0.1", 5080)]
    [InlineData("100.127.255.254:5081", "100.127.255.254", 5081)]
    [InlineData("http://100.101.102.103:5080", "100.101.102.103", 5080)]
    [InlineData("scolia.equipe.ts.net", "scolia.equipe.ts.net", 5080)]
    public void Accepte_une_adresse_privee_Tailscale(string source, string host, int port)
    {
        Assert.True(TailscaleEndpoint.TryParse(source, 5080, out var uri));
        Assert.NotNull(uri);
        Assert.Equal(host, uri.Host);
        Assert.Equal(port, uri.Port);
        Assert.True(TailscaleEndpoint.IsTailnetAddress(uri));
    }

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.254", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("fd7a:115c:a1e0::12", true)]
    [InlineData("fd7a:115c:a1e1::12", false)]
    [InlineData("2001:db8::1", false)]
    public void Le_reseau_prive_est_valide_en_IPv4_et_IPv6(string address, bool expected)
        => Assert.Equal(expected, TailscaleEndpoint.IsTailnetIp(System.Net.IPAddress.Parse(address)));

    [Fact]
    public async Task La_verification_des_adresses_litterales_ne_depend_pas_du_DNS()
    {
        Assert.True(await TailscaleEndpoint.ResolvesPrivatelyAsync(new Uri("http://100.100.100.100:5080")));
        Assert.False(await TailscaleEndpoint.ResolvesPrivatelyAsync(new Uri("http://192.168.1.1:5080")));
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("localhost")]
    [InlineData("google.com")]
    [InlineData("scolia.ts.net.evil.org")]
    [InlineData("https://100.100.100.100:5080")]
    [InlineData("http://admin:secret@100.100.100.100:5080")]
    [InlineData("http://100.100.100.100:5080/api/installation")]
    [InlineData("http://100.100.100.100:5080/?key=secret")]
    public void Refuse_les_adresses_non_privees_ou_ambiguës(string source)
    {
        Assert.False(TailscaleEndpoint.TryParse(source, 5080, out _));
    }
}
