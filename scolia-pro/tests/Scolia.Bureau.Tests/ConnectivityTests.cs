using Scolia.Bureau.Api;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Une coupure Tailscale ne doit pas laisser l'indicateur réseau au vert.</summary>
public class ConnectivityTests
{
    [Fact]
    public async Task Sonde_en_echec_passe_hors_ligne_et_notifie_le_client()
    {
        // Port non écouté : aucun service externe ni donnée scolaire n'est contacté.
        using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/"));
        var changes = new List<bool>();
        api.ConnectivityChanged += changes.Add;

        Assert.True(api.IsOnline);
        Assert.False(await api.IsRunningAsync());
        Assert.False(api.IsOnline);
        Assert.Equal(new[] { false }, changes);

        Assert.False(await api.IsRunningAsync());
        Assert.Single(changes);
    }

    [Fact]
    public async Task Annulation_volontaire_de_la_sonde_ne_signale_pas_une_panne_VPN()
    {
        using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(await api.IsRunningAsync(cancellation.Token));
        Assert.True(api.IsOnline);
    }
}
