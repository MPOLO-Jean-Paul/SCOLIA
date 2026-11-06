using Scolia.Transport;

namespace Scolia.Bureau.Tests;

/// <summary>Un réseau privé n'est pas obligatoire pour rejoindre le relais Render.</summary>
public sealed class RenderRemoteLinkTests
{
    [Fact]
    public void Le_relais_Render_est_pret_sans_edition_manuelle_de_l_adresse()
    {
        var link = RemoteLink.Create(RemoteLink.DefaultRelay);
        Assert.Equal("wss", new Uri(link.Relay).Scheme);
        Assert.Equal("scolia-pro-relay.onrender.com", new Uri(link.Relay).Host);
        Assert.True(link.ExpiresUtc > DateTime.UtcNow.AddDays(360));
        Assert.False(RemoteLink.Parse(link.Invitation()).CanHost);
        Assert.Equal(link.Room, RemoteLink.Parse(link.Invitation()).Room);
    }

    [Fact]
    public void Les_anciennes_invitations_de_trente_jours_restent_compatibles()
    {
        var link = RemoteLink.Create(RemoteLink.DefaultRelay);
        var legacy = link with { ExpiresUtc = DateTime.UtcNow.AddDays(20) };
        Assert.Equal(legacy.Room, RemoteLink.Parse(legacy.Invitation()).Room);
    }

    [Fact]
    public void Une_invitation_expiree_ou_un_relais_non_chiffre_est_refuse()
    {
        var link = RemoteLink.Create(RemoteLink.DefaultRelay);
        Assert.Throws<FormatException>(() => (link with { ExpiresUtc = DateTime.UtcNow.AddSeconds(-5) }).Validate());
        Assert.Throws<FormatException>(() => (link with { Relay = "ws://example.com/" }).Validate());
        Assert.Throws<FormatException>(() => (link with { Relay = "https://example.com/" }).Validate());
    }
}
