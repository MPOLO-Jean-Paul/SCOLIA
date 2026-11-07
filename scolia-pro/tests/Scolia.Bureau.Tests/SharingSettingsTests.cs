using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

/// <summary>Paramètres de partage : la synthèse dit clairement si d'autres appareils peuvent joindre le serveur.</summary>
[Collection("Plateforme")]
public sealed class SharingSettingsTests
{
    [Fact]
    public void Un_serveur_ferme_le_dit_et_propose_d_ouvrir_le_wifi_ou_internet()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        var poste = new PosteViewModel(shell);
        Assert.True(poste.IsServerComputer);
        Assert.True(poste.ShowTitle);
        if (!poste.LanEnabled && !poste.Remote.IsActive)
        {
            Assert.Equal("Ce serveur n'est pas partagé", poste.AccessTitle);
            Assert.Contains("Wi-Fi", poste.AccessSummary, StringComparison.Ordinal);
            Assert.Contains("Internet", poste.AccessSummary, StringComparison.Ordinal);
            Assert.Equal("Activer l'accès par Internet", poste.Remote.EnableLabel);
            Assert.Equal("Désactivé", poste.Remote.StatusTitle);
        }
        // Sans session, la liste des appareils n'est ni demandée ni proposée.
        Assert.False(poste.CanManageDevices);
        Assert.Equal("Aucun appareil autorisé", poste.DevicesState);
        Assert.DoesNotContain("Render", poste.ShowCodeLine, StringComparison.Ordinal);
    }

    [Fact]
    public void La_page_reseau_ne_repete_pas_le_titre_poste_et_reseau()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        var hub = new NetworkHubPage(shell);
        Assert.False(hub.Connection.ShowTitle);
        Assert.Equal("Réseau et postes", hub.Title);
    }
}
