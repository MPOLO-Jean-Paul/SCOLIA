using Scolia.Transport;

namespace Scolia.Bureau.Tests;

/// <summary>Cloudflare prioritaire et Render de secours sans modifier les droits des appareils.</summary>
public sealed class RenderRemoteLinkTests
{
    [Fact]
    public void Le_relais_Cloudflare_est_prioritaire_et_Render_secours_automatique()
    {
        var link = RemoteLink.Create(RemoteLink.DefaultRelay);
        Assert.Equal(RemoteLink.CloudflareRelay, link.Relay);
        Assert.Equal(RemoteLink.RenderRelay, link.EffectiveBackupRelay);
        Assert.Equal("wss", new Uri(link.Relay).Scheme);
        Assert.Equal("relay.jp-services.org", new Uri(link.Relay).Host);
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
        var host = RemoteLink.Create(primary) with { BackupRelay = RemoteLink.RenderRelay };
        host.Validate();
        var cloudflarePhone = RemoteLink.Parse(host.Invitation());
        var renderPhone = cloudflarePhone.Via(RemoteLink.RenderRelay);

        Assert.Equal(RemoteLink.RenderRelay, cloudflarePhone.EffectiveBackupRelay);
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
        Assert.Equal(RemoteLink.RenderRelay, RemoteLink.Parse(oldCloudflare.Invitation()).EffectiveBackupRelay);
        var backup = oldCloudflare.Via(RemoteLink.RenderRelay);
        Assert.Equal(oldCloudflare.Room, backup.Room);
        Assert.Equal(oldCloudflare.Key, backup.Key);
    }

    [Fact]
    public void Ancien_jumelage_Render_ne_change_jamais_de_cles_ni_de_fournisseur_sans_accord()
    {
        var oldRender = RemoteLink.Create(RemoteLink.RenderRelay);
        var phone = RemoteLink.Parse(oldRender.Invitation());
        Assert.Equal(RemoteLink.RenderRelay, phone.Relay);
        Assert.Null(phone.EffectiveBackupRelay);
        Assert.Null(phone.HostToken);

        var approved = oldRender with { Relay = RemoteLink.CloudflareRelay, BackupRelay = RemoteLink.RenderRelay };
        approved.Validate();
        Assert.Equal(oldRender.Room, approved.Room);
        Assert.Equal(oldRender.ClientToken, approved.ClientToken);
        Assert.Equal(oldRender.Key, approved.Key);
        Assert.Equal(oldRender.VirtualServer, approved.VirtualServer);
        Assert.Equal(RemoteLink.RenderRelay, RemoteLink.Parse(approved.Invitation()).EffectiveBackupRelay);
        Assert.Equal(RemoteLink.RenderRelay, RemoteLink.Parse(approved.Invitation()).Via(RemoteLink.RenderRelay).Relay);
    }

    [Fact]
    public void Un_secours_non_chiffre_ou_identique_au_principal_est_refuse()
    {
        var host = RemoteLink.Create(RemoteLink.DefaultRelay);
        Assert.Throws<FormatException>(() => (host with { BackupRelay = "ws://example.com/" }).Validate());
        Assert.Throws<FormatException>(() => (host with { BackupRelay = host.Relay }).Validate());
    }

}
