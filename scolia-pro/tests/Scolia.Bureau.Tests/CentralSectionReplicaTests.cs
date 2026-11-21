using System.Net;
using System.Text;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class CentralSectionReplicaTests
{
    private const string Jwt = "header.payload.signature_example";
    private const string School = "school-a";
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return reply(request, cancellationToken);
        }
    }
    private static HttpResponseMessage Json(HttpStatusCode status, string data) => new(status)
    {
        Content = new StringContent(data, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task File_locale_survit_a_une_coupure_et_aux_nouvelles_instances()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-queue-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            string operation;
            using (var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((_, _) => throw new HttpRequestException("connexion coupée"))))
            {
                operation = await replica.QueueUpsertAsync(
                    id, "INF", "Sciences infirmières", true);
                Assert.NotEmpty(operation);
                Assert.Equal(1, replica.Local().PendingCount);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    replica.QueueUpsertAsync(id, "INF", "Autre", true));
                var response = await replica.SyncAsync(
                    new Uri("https://central.example/"), Jwt);
                Assert.True(response.Offline);
                Assert.Equal(1, response.PendingCount);
            }
            using var next = new CentralSectionReplica(store, store.User, School);
            var saved = next.Local();
            Assert.Equal(1, saved.PendingCount);
            Assert.Equal("Sciences infirmières", Assert.Single(saved.Sections).Name);
            Assert.True(Assert.Single(saved.Sections).IsPending);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Reponse_metier_applied_puis_evenement_d1_valide_confirment_sqlite()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-sync-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var count = 0;
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler(async (request, ct) =>
                {
                    Assert.Equal("central.example", request.RequestUri?.Host);
                    Assert.Equal("Bearer " + Jwt, request.Headers.Authorization?.ToString());
                    count++;
                    if (request.Method == HttpMethod.Post)
                    {
                        Assert.Equal(1, count);
                        var json = await request.Content!.ReadAsStringAsync(ct);
                        using var body = System.Text.Json.JsonDocument.Parse(json);
                        Assert.Equal("INF", body.RootElement.GetProperty("code").GetString());
                        Assert.Equal(0, body.RootElement.GetProperty("expected_revision").GetInt64());
                        Assert.Equal(id.ToString("D"), body.RootElement.GetProperty("section_id").GetString());
                        return Json(HttpStatusCode.Created,
                            "{\"operation_id\":\"" + body.RootElement.GetProperty("operation_id").GetString() +
                            "\",\"section_id\":\"" + id.ToString("D") +
                            "\",\"status\":\"applied\",\"revision\":11,\"replayed\":false}");
                    }
                    Assert.Equal(2, count);
                    Assert.Contains("after=0", request.RequestUri?.Query);
                    return Json(HttpStatusCode.OK,
                        "{\"events\":[{\"revision\":11,\"section_id\":\"" + id.ToString("D") +
                        "\",\"code\":\"INF\",\"name\":\"Sciences infirmières\"," +
                        "\"is_active\":1,\"created_at\":1234}]," +
                        "\"next\":11,\"has_more\":false}");
                }));
            await replica.QueueUpsertAsync(id, "INF", "Sciences infirmières", true);
            var synced = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
            Assert.False(synced.Offline);
            Assert.False(synced.HasConflict);
            Assert.Equal(0, synced.PendingCount);
            Assert.Equal(11, synced.Revision);
            var section = Assert.Single(synced.Sections);
            Assert.False(section.IsPending);
            Assert.Equal("INF", section.Code);
            Assert.Equal(2, count);
            Assert.Equal(11, replica.Local().Revision);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Reponse_409_conserve_l_operation_et_signale_un_conflit()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-conflict-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((_, _) => Task.FromResult(
                    Json(HttpStatusCode.Conflict, """{"error":"stale_section_revision"}"""))));
            await replica.QueueUpsertAsync(Guid.NewGuid(), "ACC", "Accoucheuse", true);
            var conflict = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
            Assert.True(conflict.HasConflict);
            Assert.Equal(1, conflict.PendingCount);
            Assert.Equal(1, replica.Local().PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Changement_de_compte_et_origine_Http_ne_livrent_ni_jeton_ni_cache()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-account-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School);
            store.User = "ENSEIGNANT";
            Assert.Throws<InvalidOperationException>(() => replica.Local());
            await Assert.ThrowsAsync<ArgumentException>(() => replica.SyncAsync(
                new Uri("http://central.example/"), Jwt));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
