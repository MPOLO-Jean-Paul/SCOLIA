using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class TailscaleHttpsServeTests
{
    private const string Dns = "scolia.ecole.ts.net";
    private const string Status = """
        {"BackendState":"Running","Self":{"DNSName":"scolia.ecole.ts.net.","TailscaleIPs":["100.100.12.13"]}}
        """;
    private const string ActiveServe = """
        {"TCP":{"443":{"HTTPS":true}},"Web":{"scolia.ecole.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:5080"}}}}}
        """;

    [Fact]
    public void Verifie_le_nom_tailscale_du_poste_avant_publication()
    {
        Assert.Equal(Dns, TailscaleHttpsServe.ParseOwnDnsName(Status));
        Assert.Null(TailscaleHttpsServe.ParseOwnDnsName(
            """{"BackendState":"Stopped","Self":{"DNSName":"scolia.ecole.ts.net."}}"""));
        Assert.Null(TailscaleHttpsServe.ParseOwnDnsName(
            """{"BackendState":"Running","Self":{"DNSName":"scolia.exemple.com."}}"""));
        Assert.Null(TailscaleHttpsServe.ParseOwnDnsName(
            """{"BackendState":"Running","Self":{"DNSName":"-malveillant.ts.net."}}"""));
        Assert.Null(TailscaleHttpsServe.ParseOwnDnsName("unjson"));
    }

    [Fact]
    public void Ne_modifie_pas_un_autre_serveur_publie_sur_https_443()
    {
        Assert.Equal(TailscaleHttpsServe.PortState.Available,
            TailscaleHttpsServe.Inspect("{}", Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.AlreadyScolia,
            TailscaleHttpsServe.Inspect(ActiveServe, Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.Occupied,
            TailscaleHttpsServe.Inspect(ActiveServe, Dns, 5081));
        Assert.Equal(TailscaleHttpsServe.PortState.Occupied,
            TailscaleHttpsServe.Inspect(
                """{"TCP":{"443":{"HTTPS":true}},"Web":{"scolia.ecole.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:9000"}}}}}""",
                Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.Occupied,
            TailscaleHttpsServe.Inspect(
                """{"Services":{"svc:autre":{"TCP":{"443":{"HTTPS":true}}}}}""",
                Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.Occupied,
            TailscaleHttpsServe.Inspect(
                """{"TCP":{"443":{"HTTPS":true}},"AllowFunnel":{"scolia.ecole.ts.net:443":true},"Web":{"scolia.ecole.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:5080"}}}}}""",
                Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.Unknown,
            TailscaleHttpsServe.Inspect(
                """{"AllowFunnel":"invalid"}""", Dns, 5080));
        Assert.Equal(TailscaleHttpsServe.PortState.Unknown,
            TailscaleHttpsServe.Inspect("erreur", Dns, 5080));
    }

    [Fact]
    public void Le_partage_https_ne_fonctionne_pas_comme_un_endpoint_public()
    {
        Assert.False(TailscaleEndpoint.TryParse("https://google.com", 5080, out _));
        Assert.False(TailscaleEndpoint.TryParse("https://100.100.12.13:443", 5080, out _));
        Assert.False(TailscaleEndpoint.TryParse("https://scolia.ecole.ts.net.evil.test", 5080, out _));
        Assert.True(TailscaleEndpoint.TryParse("https://scolia.ecole.ts.net", 5080, out var link));
        Assert.NotNull(link);
        Assert.Equal("https", link.Scheme);
        Assert.Equal(443, link.Port);
    }

    [Fact]
    public void Le_code_qr_transporte_https_sans_le_degrader_en_http()
    {
        var code = PairingCode.ForThisServer("Ecole", "Serveur",
            ["http://192.168.1.20:5080", "https://scolia.ecole.ts.net/"], null);
        var qr = code.Text();
        Assert.Contains("h=scolia.ecole.ts.net", qr);
        Assert.DoesNotContain("scolia.ecole.ts.net:443", qr);
        Assert.True(PairingCode.TryParse(qr, out var parsed, out _));
        Assert.NotNull(parsed);
        Assert.Equal("https", parsed.TailnetAddresses.First().Scheme);
        Assert.Equal("scolia.ecole.ts.net", parsed.TailnetAddresses.First().Host);
        Assert.Single(parsed.LanAddresses);

        Assert.False(PairingCode.TryParse(
            "scolia://relier?v=1&e=Ecole&h=google.com&a=192.168.1.20:5080", out _, out _));
        Assert.False(PairingCode.TryParse(
            "scolia://relier?v=1&e=Ecole&h=scolia.ecole.ts.net.evil.test&a=192.168.1.20:5080", out _, out _));
    }

    [Fact]
    public void Ancien_qr_http_reste_compatible()
    {
        Assert.True(PairingCode.TryParse(
            "scolia://relier?v=1&e=Ecole&a=192.168.1.20:5080,100.100.12.13:5080",
            out var old, out _));
        Assert.NotNull(old);
        Assert.Equal(2, old.Addresses.Count);
        Assert.All(old.Addresses, address => Assert.Equal("http", address.Scheme));
    }
}
