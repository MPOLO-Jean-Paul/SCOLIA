using System.Net;
using System.Net.Sockets;
using System.Text;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Une limitation de débit, une redirection Access ou une indisponibilité
/// provisoire ne doit JAMAIS transférer une écriture locale vers les conflits.
/// Les tests utilisent uniquement un serveur HTTP loopback temporaire.
/// </summary>
public sealed class OfflineTransientSyncTests
{
    [Theory]
    [InlineData(302)] // Redirection Cloudflare Access / changement de route.
    [InlineData(408)] // Délai de requête dépassé.
    [InlineData(425)] // Too Early.
    [InlineData(429)] // Quota Cloudflare momentanément atteint.
    [InlineData(503)] // Panne de service temporaire.
    public async Task Reponse_temporaire_conserve_la_meme_operation_apres_redemarrage(int status)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new Uri($"http://127.0.0.1:{port}/");
        var root = Path.Combine(Path.GetTempPath(), "scolia-retry", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new OfflineStore(server, root) { User = "secretariat" };
            var item = new OutboxItem
            {
                User = "secretariat",
                Key = Guid.NewGuid().ToString("N"),
                Path = "api/paiements",
                Method = "POST",
                Body = """{"montant":25000}"""
            };
            store.Enqueue(item);

            using var api = new ScoliaClient(server) { Offline = store };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serverTask = RespondOnceAsync(listener, status, timeout.Token);
            var result = await api.FlushAsync(timeout.Token);
            await serverTask;

            Assert.Equal(0, result.Sent);
            Assert.Equal(0, result.Refused);
            Assert.Empty(store.Conflicts());
            Assert.Equal(item.Key, Assert.Single(store.Outbox()).Key);

            // Le journal SQLite chiffré garde la même clé d'idempotence après
            // la fermeture du client : aucune opération n'est perdue ni dupliquée.
            var reopened = new OfflineStore(server, root) { User = "secretariat" };
            Assert.Equal(item.Key, Assert.Single(reopened.Outbox()).Key);
            Assert.Empty(reopened.Conflicts());
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(302)]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    public async Task Ecriture_immediate_temporairement_refusee_reste_persistante(int status)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new Uri($"http://127.0.0.1:{port}/");
        var root = Path.Combine(Path.GetTempPath(), "scolia-retry", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new OfflineStore(server, root) { User = "secretariat" };
            using var api = new ScoliaClient(server) { Offline = store };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serverTask = RespondOnceAsync(listener, status, timeout.Token);

            await Assert.ThrowsAsync<OfflineQueuedException>(() =>
                api.SendAsync(HttpMethod.Post, "api/depenses",
                    new { amount = 25000m, reason = "Achat de fournitures" }, timeout.Token));
            await serverTask;

            Assert.Empty(store.Conflicts());
            var queued = Assert.Single(store.Outbox());
            Assert.Equal("api/depenses", queued.Path);
            Assert.False(string.IsNullOrWhiteSpace(queued.Key));

            // La réponse n'a pas validé l'écriture ; même après redémarrage,
            // le compte pourra la renvoyer sous le même identifiant.
            var reopened = new OfflineStore(server, root) { User = "secretariat" };
            Assert.Equal(queued.Key, Assert.Single(reopened.Outbox()).Key);
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(200, 1, 0)]
    [InlineData(422, 0, 1)]
    public async Task Accuse_valide_supprime_la_file_et_refus_metier_devient_conflit(
        int status, int expectedSent, int expectedRefused)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = new Uri($"http://127.0.0.1:{port}/");
        var root = Path.Combine(Path.GetTempPath(), "scolia-retry", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new OfflineStore(server, root) { User = "secretariat" };
            store.Enqueue(new OutboxItem
            {
                User = "secretariat",
                Key = "operation-validee-ou-refusee",
                Path = "api/paiements",
                Body = "{}"
            });

            using var api = new ScoliaClient(server) { Offline = store };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var serverTask = RespondOnceAsync(listener, status, timeout.Token);
            var result = await api.FlushAsync(timeout.Token);
            await serverTask;

            Assert.Equal(expectedSent, result.Sent);
            Assert.Equal(expectedRefused, result.Refused);
            Assert.Empty(store.Outbox());
            Assert.Equal(expectedRefused, store.Conflicts().Count);
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RespondOnceAsync(TcpListener listener, int status, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 }) { }

        var location = status == 302 ? "Location: /access/login\\r\\n" : string.Empty;
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Test\\r\\n{location}Content-Length: 0\\r\\nConnection: close\\r\\n\\r\\n");
        await stream.WriteAsync(response, cancellationToken);
    }
}
