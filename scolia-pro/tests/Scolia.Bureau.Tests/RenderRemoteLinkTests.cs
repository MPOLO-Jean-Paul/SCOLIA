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
    [Fact]
    public void Deux_relais_pour_une_salle_ne_changent_ni_les_cles_ni_les_appareils()
    {
        var primary = "wss://scolia-pro-relay.example.workers.dev/";
        var host = RemoteLink.Create(primary) with { BackupRelay = RemoteLink.DefaultRelay };
        host.Validate();
        var cloudflarePhone = RemoteLink.Parse(host.Invitation());
        var renderPhone = cloudflarePhone.Via(RemoteLink.DefaultRelay);

        Assert.Equal(RemoteLink.DefaultRelay, cloudflarePhone.EffectiveBackupRelay);
        Assert.Equal(host.Room, renderPhone.Room);
        Assert.Equal(host.ClientToken, renderPhone.ClientToken);
        Assert.Equal(host.Key, renderPhone.Key);
        Assert.Equal(host.VirtualServer, renderPhone.VirtualServer);
        Assert.Null(renderPhone.HostToken);
        Assert.Equal(primary, host.Relay);
    }

    [Fact]
    public void Ancien_jumelage_Cloudflare_peut_utiliser_Render_sans_rotation()
    {
        var oldCloudflare = RemoteLink.Create("wss://ancien-relay.example.workers.dev/");
        Assert.Equal(RemoteLink.DefaultRelay, RemoteLink.Parse(oldCloudflare.Invitation()).EffectiveBackupRelay);
        var backup = oldCloudflare.Via(RemoteLink.DefaultRelay);
        Assert.Equal(oldCloudflare.Room, backup.Room);
        Assert.Equal(oldCloudflare.Key, backup.Key);
    }

    [Fact]
    public void Un_secours_non_chiffre_ou_identique_au_principal_est_refuse()
    {
        var host = RemoteLink.Create(RemoteLink.DefaultRelay);
        Assert.Throws<FormatException>(() => (host with { BackupRelay = "ws://example.com/" }).Validate());
        Assert.Throws<FormatException>(() => (host with { BackupRelay = host.Relay }).Validate());
    }

}
