using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Lecture de la page du dépôt officiel de Tailscale : bon paquet, plus récente version, empreinte.</summary>
public class TailscalePackagesTests
{
    private const string Index = """
        <a href="tailscale-android-universal-1.98.2.apk">tailscale-android-universal-1.98.2.apk</a>
        <a href="tailscale-android-universal-1.104.4.apk">tailscale-android-universal-1.104.4.apk</a>
        <a href="tailscale-android-universal-1.104.4.apk.sha256">sha256</a>
        <a href="tailscale-setup-1.104.4.exe">tailscale-setup-1.104.4.exe</a>
        <a href="tailscale-setup-1.96.0.exe">tailscale-setup-1.96.0.exe</a>
        <a href="tailscale-setup-full-1.104.4.exe">tailscale-setup-full-1.104.4.exe</a>
        """;

    [Fact]
    public void Android_prend_la_plus_recente_application_universelle()
    {
        var package = TailscalePackages.Latest(Index, android: true)!;
        Assert.Equal("tailscale-android-universal-1.104.4.apk", package.FileName);
        Assert.Equal("https://pkgs.tailscale.com/stable/tailscale-android-universal-1.104.4.apk", package.Url);
    }

    [Fact]
    public void Windows_prend_le_plus_recent_installateur()
    {
        // 1.104 est plus récent que 1.96 : la comparaison est numérique, pas alphabétique.
        Assert.Equal("tailscale-setup-1.104.4.exe", TailscalePackages.Latest(Index, android: false)!.FileName);
    }

    [Fact]
    public void Une_page_sans_paquet_ne_donne_rien() => Assert.Null(TailscalePackages.Latest("<html></html>", android: true));

    [Theory]
    [InlineData("3B7F0A2C9D1E4F5A6B7C8D9E0F1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A  tailscale.apk", "3b7f0a2c9d1e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a")]
    [InlineData("pas d'empreinte", null)]
    public void L_empreinte_publiee_est_lue(string text, string? expected) => Assert.Equal(expected, TailscalePackages.ParseSha256(text));
}
