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
    public async Task Confirmation_d1_puis_coupure_preserve_la_section_validee_sur_disque()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-ack-" + Guid.NewGuid().ToString("N"));
        var section = Guid.NewGuid();
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var calls = 0;
            using (var replica = new CentralSectionReplica(store, store.User, School,
                new Handler(async (request, ct) =>
                {
                    calls++;
                    if (request.Method == HttpMethod.Post)
                    {
                        using var requestJson = System.Text.Json.JsonDocument.Parse(
                            await request.Content!.ReadAsStringAsync(ct));
                        var op = requestJson.RootElement.GetProperty("operation_id").GetString();
                        return Json(HttpStatusCode.Created,
                            "{\"operation_id\":\"" + op + "\",\"section_id\":\"" +
                            section.ToString("D") +
                            "\",\"status\":\"applied\",\"revision\":15,\"replayed\":false}");
                    }
                    throw new HttpRequestException("Réseau interrompu après l'encaissement D1");
                })))
            {
                await replica.QueueUpsertAsync(section, "INF", "Sciences infirmières", true);
                var incomplete = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
                Assert.True(incomplete.Offline);
                Assert.Equal(0, incomplete.PendingCount);
                Assert.Equal(0, incomplete.Revision); // le journal n'a pas été lu
                var confirmed = Assert.Single(incomplete.Sections);
                Assert.Equal("INF", confirmed.Code);
                Assert.Equal(15, confirmed.Revision);
                Assert.False(confirmed.IsPending);
                Assert.Equal(2, calls);
            }
            using var reopened = new CentralSectionReplica(store, store.User, School);
            var saved = reopened.Local();
            Assert.DoesNotContain(saved.Sections, x => x.IsPending);
            Assert.Equal(0, saved.PendingCount);
            Assert.Equal(15, Assert.Single(saved.Sections).Revision);
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
            Assert.True(replica.Local().HasConflict);
            Assert.Equal(1, replica.Local().PendingCount);

            // Après redémarrage, conserver l'opération en conflit sans la
            // renvoyer indéfiniment. Seule une lecture du journal est admise.
            var posts = 0;
            using var reopened = new CentralSectionReplica(store, store.User, School,
                new Handler((request, _) =>
                {
                    if (request.Method == HttpMethod.Post) posts++;
                    Assert.Equal(HttpMethod.Get, request.Method);
                    return Task.FromResult(Json(HttpStatusCode.OK,
                        """{"events":[],"next":0,"has_more":false}"""));
                }));
            Assert.True(reopened.Local().HasConflict);
            var replay = await reopened.SyncAsync(new Uri("https://central.example/"), Jwt);
            Assert.Equal(0, posts);
            Assert.True(replay.HasConflict);
            Assert.Equal(1, replay.PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Historique_plus_ancien_ne_remplace_pas_une_section_deja_confirmee_apres_coupure()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-history-" +
            Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var gets = 0;
            using (var replica = new CentralSectionReplica(store, store.User, School,
                new Handler(async (request, ct) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        using var body = System.Text.Json.JsonDocument.Parse(
                            await request.Content!.ReadAsStringAsync(ct));
                        return Json(HttpStatusCode.Created,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                operation_id = body.RootElement.GetProperty("operation_id").GetString(),
                                section_id = id.ToString("D"),
                                status = "applied",
                                revision = 70,
                            }));
                    }
                    gets++;
                    if (gets == 1)
                        return Json(HttpStatusCode.OK,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                events = new[]
                                {
                                    new
                                    {
                                        revision = 15,
                                        section_id = id.ToString("D"),
                                        code = "OLD",
                                        name = "Ancien libellé",
                                        is_active = 1,
                                        created_at = 1000,
                                    },
                                },
                                next = 15,
                                has_more = true,
                            }));
                    throw new HttpRequestException("Coupure avant lecture des révisions récentes");
                })))
            {
                await replica.QueueUpsertAsync(id, "INF", "Nouvelle section", true);
                var result = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
                Assert.True(result.Offline);
                Assert.Equal(0, result.PendingCount);
                Assert.Equal(15, result.Revision);
                var current = Assert.Single(result.Sections);
                Assert.Equal("INF", current.Code);
                Assert.Equal("Nouvelle section", current.Name);
                Assert.Equal(70, current.Revision);
                Assert.Equal(2, gets);
            }

            using var reopened = new CentralSectionReplica(store, store.User, School);
            var saved = Assert.Single(reopened.Local().Sections);
            Assert.Equal("INF", saved.Code);
            Assert.Equal(70, saved.Revision);
            Assert.False(saved.IsPending);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Plus_de_huit_lots_signale_la_reprise_et_conserve_le_curseur()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-pages-" +
            Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var count = 0;
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((request, _) =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Contains("after=" + count, request.RequestUri?.Query);
                    count++;
                    return Task.FromResult(Json(HttpStatusCode.OK,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            events = new[]
                            {
                                new
                                {
                                    revision = count,
                                    section_id = id.ToString("D"),
                                    code = "INF",
                                    name = "Sciences infirmières",
                                    is_active = 1,
                                },
                            },
                            next = count,
                            has_more = count < 9,
                        })));
                }));

            var first = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
            Assert.False(first.Offline);
            Assert.True(first.HasMorePages);
            Assert.Equal(8, first.Revision);
            Assert.Equal(8, count);
            Assert.Equal(8, replica.Local().Revision);

            var second = await replica.SyncAsync(new Uri("https://central.example/"), Jwt);
            Assert.False(second.Offline);
            Assert.False(second.HasMorePages);
            Assert.Equal(9, second.Revision);
            Assert.Equal(9, count);
            Assert.Equal(9, Assert.Single(second.Sections).Revision);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Rattrapage_automatique_charge_les_pages_suivantes_sans_repartir_de_zero()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-catchup-" +
            Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var calls = 0;
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((request, _) =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal("?after=" + calls + "&limit=50",
                        request.RequestUri?.Query);
                    calls++;
                    return Task.FromResult(Json(HttpStatusCode.OK,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            events = new[]
                            {
                                new
                                {
                                    revision = calls,
                                    section_id = id.ToString("D"),
                                    code = "INF",
                                    name = "Sciences infirmières",
                                    is_active = 1,
                                },
                            },
                            next = calls,
                            has_more = calls < 10,
                        })));
                }));

            var limited = await replica.CatchUpAsync(
                new Uri("https://central.example/"), Jwt, maxPasses: 1);
            Assert.True(limited.HasMorePages);
            Assert.False(limited.Offline);
            Assert.Equal(8, limited.Revision);
            Assert.Equal(8, calls);

            var completed = await replica.CatchUpAsync(
                new Uri("https://central.example/"), Jwt);
            Assert.False(completed.HasMorePages);
            Assert.False(completed.Offline);
            Assert.Equal(10, completed.Revision);
            Assert.Equal(10, calls);
            Assert.Equal(10, Assert.Single(replica.Local().Sections).Revision);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Pagination_sans_progression_est_refusee_sans_avancer_le_curseur()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-empty-page-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
                    """{"events":[],"next":0,"has_more":true}"""))));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                replica.SyncAsync(new Uri("https://central.example/"), Jwt));
            Assert.Equal(0, replica.Local().Revision);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changement_de_compte_pendant_attente_reseau_ne_retourne_pas_ancien_cache(
        bool connexionInterrompue)
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-user-switch-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var replica = new CentralSectionReplica(store, store.User, School,
                new Handler((_, _) =>
                {
                    started.TrySetResult(true);
                    return response.Task;
                }));
            var operation = replica.SyncAsync(new Uri("https://central.example/"), Jwt);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            store.User = "ENSEIGNANT";
            if (connexionInterrompue)
                response.SetException(new HttpRequestException("Réseau absent"));
            else
                response.SetResult(Json(HttpStatusCode.ServiceUnavailable,
                    """{"error":"unavailable"}"""));
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await operation);
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
