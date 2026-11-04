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
    public async Task Lecture_hors_ligne_depuis_le_cache_est_immediate_et_par_compte()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-offline-fast", Guid.NewGuid().ToString("N"));
        var server = new Uri("http://127.0.0.1:1/");
        try
        {
            var store = new Scolia.Bureau.Infrastructure.OfflineStore(server, directory) { User = "secretariat" };
            store.SavePage("api/rapports/tableau-de-bord", """{"value":131}""");
            using var api = new ScoliaClient(server) { Offline = store };
            Assert.False(await api.IsRunningAsync());

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var content = await api.GetAsync<System.Text.Json.JsonElement>("api/rapports/tableau-de-bord");
            watch.Stop();

            Assert.Equal(131, content.GetProperty("value").GetInt32());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Chargement du cache trop lent : {watch.Elapsed}");
            Assert.False(api.IsOnline);
            store.User = "comptabilite";
            await Assert.ThrowsAsync<OfflineException>(() =>
                api.GetAsync<System.Text.Json.JsonElement>("api/rapports/tableau-de-bord"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Page_non_cachable_hors_ligne_echoue_sans_attente_reseau()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-offline-fast", Guid.NewGuid().ToString("N"));
        var server = new Uri("http://127.0.0.1:1/");
        try
        {
            using var api = new ScoliaClient(server)
            {
                Offline = new Scolia.Bureau.Infrastructure.OfflineStore(server, directory) { User = "secretariat" }
            };
            Assert.False(await api.IsRunningAsync());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<OfflineException>(() =>
                api.GetAsync<System.Text.Json.JsonElement>("api/session"));
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Ecriture_hors_ligne_reste_durable_sans_attendre_le_reseau()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-offline-fast", Guid.NewGuid().ToString("N"));
        var server = new Uri("http://127.0.0.1:1/");
        try
        {
            var store = new Scolia.Bureau.Infrastructure.OfflineStore(server, directory) { User = "secretariat" };
            using var api = new ScoliaClient(server) { Offline = store };
            Assert.False(await api.IsRunningAsync());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<OfflineQueuedException>(() =>
                api.SendAsync(HttpMethod.Post, "api/depenses",
                    new { amount = 5000m, reason = "Fournitures" }));
            watch.Stop();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            var entry = Assert.Single(store.Outbox());
            Assert.Equal("api/depenses", entry.Path);
            Assert.NotEmpty(entry.Key);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Connexion_avec_le_dernier_compte_hors_ligne_ne_tente_pas_une_longue_requete()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-offline-fast", Guid.NewGuid().ToString("N"));
        var server = new Uri("http://127.0.0.1:1/");
        try
        {
            var store = new Scolia.Bureau.Infrastructure.OfflineStore(server, directory) { User = "secretariat" };
            var session = new SessionDto(Guid.NewGuid(), "secretariat", "Secrétariat", "Secretariat", "Secrétariat",
                [], false, "Institut", null, null, "FC", "243");
            store.SaveCredential("secretariat", "Secretariat1", session);
            using var api = new ScoliaClient(server) { Offline = store };
            Assert.False(await api.IsRunningAsync());

            var login = new Scolia.Bureau.ViewModels.LoginViewModel(api)
            {
                UserName = "secretariat",
                Password = "Secretariat1"
            };
            var opened = new TaskCompletionSource<SessionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            login.LoggedIn += result => opened.TrySetResult(result);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            login.Submit.Execute(null);
            var restored = await opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            watch.Stop();

            Assert.Equal("secretariat", restored.UserName);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Connexion hors ligne lente : {watch.Elapsed}");
            Assert.False(api.IsOnline);
            Assert.Contains("hors ligne", login.Notice ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Logo_local_s_affiche_sans_attendre_un_serveur_injoignable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-offline-fast", Guid.NewGuid().ToString("N"));
        var server = new Uri("http://127.0.0.1:1/");
        try
        {
            var store = new Scolia.Bureau.Infrastructure.OfflineStore(server, directory) { User = "secretariat" };
            var logo = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
            store.SavePage("api/etablissement/logo", Convert.ToBase64String(logo));
            using var api = new ScoliaClient(server) { Offline = store };
            Assert.False(await api.IsRunningAsync());
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var localLogo = await api.DownloadAsync("api/etablissement/logo");
            watch.Stop();
            Assert.Equal(logo, localLogo);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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
