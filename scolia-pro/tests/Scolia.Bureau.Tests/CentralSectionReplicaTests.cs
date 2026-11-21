using System.Net;
using System.Text;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class CentralSectionReplicaTests
{
    private const string Jwt = "header.payload.signature_example";
    private const string School = "school-a";
    private static readonly Uri CentralOrigin = new("https://central.example/");
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

    // StreamContent ne peut pas fournir de Content-Length quand CanSeek=false.
    // Les limites de lecture doivent donc fonctionner sur un vrai flux HTTP
    // fragmenté, pas seulement sur une déclaration d'en-tête fiable.
    private sealed class NoLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static HttpResponseMessage Chunked(HttpStatusCode status, string data) =>
        new(status)
        {
            Content = new StreamContent(new NoLengthStream(Encoding.UTF8.GetBytes(data))),
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
            using (var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using var next = new CentralSectionReplica(store, store.User, School, CentralOrigin);
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using (var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using var reopened = new CentralSectionReplica(store, store.User, School, CentralOrigin);
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using var reopened = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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

            // Une nouvelle coupure ne fait pas disparaître un conflit
            // déjà confirmé dans la copie SQLite locale.
            using var disconnected = new CentralSectionReplica(store, store.User,
                School, CentralOrigin, new Handler((request, _) =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    return Task.FromResult(Json(HttpStatusCode.ServiceUnavailable,
                        """{"error":"temporarily_unavailable"}"""));
                }));
            var offline = await disconnected.SyncAsync(CentralOrigin, Jwt);
            Assert.True(offline.Offline);
            Assert.True(offline.HasConflict);
            Assert.Equal(1, offline.PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Abandon_manuel_du_conflit_n_efface_pas_un_autre_journal()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-conflict-discard-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((_, _) => Task.FromResult(Json(
                    HttpStatusCode.Conflict, """{"error":"stale_section_revision"}"""))));
            var section = Guid.NewGuid();
            await replica.QueueUpsertAsync(section, "INF", "Infirmière", true);
            var conflicted = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.True(conflicted.HasConflict);
            await replica.DiscardConflictedSectionAsync(section);
            Assert.Equal(0, replica.Local().PendingCount);
            Assert.False(replica.Local().HasConflict);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => replica.DiscardConflictedSectionAsync(section));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Reprise_manuelle_du_conflit_genere_une_nouvelle_cle_et_un_accord_d1()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-conflict-retry-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var section = Guid.NewGuid();
            var postCount = 0;
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler(async (request, ct) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        postCount++;
                        using var body = System.Text.Json.JsonDocument.Parse(
                            await request.Content!.ReadAsStringAsync(ct));
                        if (postCount == 1)
                            return Json(HttpStatusCode.Conflict,
                                """{"error":"duplicate_section_code_or_operation"}""");
                        Assert.Equal("ACC2", body.RootElement.GetProperty("code").GetString());
                        Assert.Equal(0, body.RootElement.GetProperty("expected_revision").GetInt64());
                        return Json(HttpStatusCode.Created,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                operation_id = body.RootElement.GetProperty("operation_id").GetString(),
                                section_id = section.ToString("D"),
                                status = "applied",
                                revision = 9,
                            }));
                    }
                    Assert.Equal(HttpMethod.Get, request.Method);
                    if (postCount == 1)
                        return Json(HttpStatusCode.OK,
                            """{"events":[],"next":0,"has_more":false}""");
                    return Json(HttpStatusCode.OK,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            events = new[]
                            {
                                new
                                {
                                    revision = 9,
                                    section_id = section.ToString("D"),
                                    code = "ACC2",
                                    name = "Accoucheuse 2",
                                    is_active = 1,
                                },
                            },
                            next = 9,
                            has_more = false,
                        }));
                }));
            var original = await replica.QueueUpsertAsync(
                section, "ACC", "Accoucheuse", true);
            Assert.True((await replica.SyncAsync(CentralOrigin, Jwt)).HasConflict);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                replica.RetryConflictedSectionAsync(
                    section, "ACC", "Accoucheuse", true));

            var retried = await replica.RetryConflictedSectionAsync(
                section, "ACC2", "Accoucheuse 2", true);
            Assert.NotEqual(original, retried);
            Assert.Equal(1, replica.Local().PendingCount);
            Assert.False(replica.Local().HasConflict);
            Assert.Equal("ACC2", Assert.Single(replica.Local().Sections).Code);

            var synced = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.False(synced.Offline);
            Assert.False(synced.HasConflict);
            Assert.Equal(0, synced.PendingCount);
            Assert.Equal(9, synced.Revision);
            Assert.Equal("ACC2", Assert.Single(synced.Sections).Code);
            Assert.Equal(2, postCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Lot_D1_sans_Content_Length_trop_grand_est_refuse_avant_chargement_complet()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-bounded-stream-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((request, _) =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    return Task.FromResult(Chunked(HttpStatusCode.OK,
                        "{\"events\":[\"" + new string('x', 110_000) + "\"]}"));
                }));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                replica.SyncAsync(CentralOrigin, Jwt));
            Assert.Equal(0, replica.Local().Revision);
            Assert.Empty(replica.Local().Sections);
            // Le cache reste utilisable même si le serveur répond abusivement.
            await replica.QueueUpsertAsync(Guid.NewGuid(), "INF",
                "Section locale préservée", true);
            Assert.Equal(1, replica.Local().PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Conflit_409_chunked_volumineux_ne_contamine_pas_le_cache_local()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-bounded-conflict-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((request, _) =>
                {
                    return Task.FromResult(request.Method == HttpMethod.Post
                        ? Chunked(HttpStatusCode.Conflict,
                            "{\"error\":\"" + new string('x', 4096) + "\"}")
                        : Json(HttpStatusCode.OK,
                            """{"events":[],"next":0,"has_more":false}"""));
                }));
            var id = Guid.NewGuid();
            var op = await replica.QueueUpsertAsync(id, "INF", "Infirmière", true);
            var result = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.True(result.HasConflict);
            Assert.False(result.Offline);
            Assert.Equal(1, result.PendingCount);
            var conflict = Assert.Single(replica.Conflicts());
            Assert.Equal("conflict", conflict.ConflictCode);
            Assert.Equal(op, conflict.OperationId);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Erreur_de_conflit_inconnue_ne_devient_pas_un_message_non_fiable()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-conflict-sanitize-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((request, _) => Task.FromResult(
                    request.Method == HttpMethod.Post
                        ? Json(HttpStatusCode.Conflict,
                            """{"error":"injection_non_fiable_du_serveur"}""")
                        : Json(HttpStatusCode.OK,
                            """{"events":[],"next":0,"has_more":false}"""))));
            await replica.QueueUpsertAsync(Guid.NewGuid(), "INF", "Infirmière", true);
            var result = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.True(result.HasConflict);
            Assert.Equal("conflict", Assert.Single(replica.Conflicts()).ConflictCode);
            Assert.Equal(1, replica.Local().PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Version_obsolete_exige_un_rattrapage_central_avant_nouvelle_tentative()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-stale-revision-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var section = Guid.NewGuid();
            var serverUpdated = false;
            var posts = 0;
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((request, _) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        posts++;
                        return Task.FromResult(Json(HttpStatusCode.Conflict,
                            """{"error":"stale_section_revision"}"""));
                    }
                    Assert.Equal(HttpMethod.Get, request.Method);
                    var events = serverUpdated
                        ? System.Text.Json.JsonSerializer.Serialize(new
                        {
                            events = new[]
                            {
                                new
                                {
                                    revision = 7,
                                    section_id = section.ToString("D"),
                                    code = "INF",
                                    name = "Version du serveur",
                                    is_active = 1,
                                },
                            },
                            next = 7, has_more = false,
                        })
                        : """{"events":[],"next":0,"has_more":false}""";
                    return Task.FromResult(Json(HttpStatusCode.OK, events));
                }));
            var initial = await replica.QueueUpsertAsync(section, "INF",
                "Saisie locale", true);
            var conflicted = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.True(conflicted.HasConflict);
            Assert.Equal("stale_section_revision",
                Assert.Single(replica.Conflicts()).ConflictCode);
            Assert.Equal(initial, Assert.Single(replica.Conflicts()).OperationId);

            using var reopened = new CentralSectionReplica(
                store, store.User, School, CentralOrigin);
            Assert.Equal("stale_section_revision",
                Assert.Single(reopened.Conflicts()).ConflictCode);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                replica.RetryConflictedSectionAsync(section, "INF2",
                    "Nom différent sans nouvelle version", true));

            serverUpdated = true;
            var caughtUp = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.Equal(7, caughtUp.Revision);
            var next = await replica.RetryConflictedSectionAsync(
                section, "INF2", "Nom corrigé", true);
            Assert.NotEqual(initial, next);
            Assert.Empty(replica.Conflicts());
            Assert.Equal(1, posts); // Aucune répétition du conflit D1.
            Assert.Equal(1, replica.Local().PendingCount);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Conflit_sur_une_section_n_empeche_pas_les_autres_et_le_pull_d1()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-conflict-independent-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var disputed = Guid.NewGuid();
            var accepted = Guid.NewGuid();
            var posts = 0;
            var gets = 0;
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler(async (request, ct) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        posts++;
                        using var body = System.Text.Json.JsonDocument.Parse(
                            await request.Content!.ReadAsStringAsync(ct));
                        var id = body.RootElement.GetProperty("section_id").GetString();
                        if (id == disputed.ToString("D"))
                            return Json(HttpStatusCode.Conflict,
                                """{"error":"stale_section_revision"}""");
                        Assert.Equal(accepted.ToString("D"), id);
                        return Json(HttpStatusCode.Created,
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                operation_id = body.RootElement.GetProperty("operation_id").GetString(),
                                section_id = id, status = "applied", revision = 3,
                            }));
                    }
                    Assert.Equal(HttpMethod.Get, request.Method);
                    gets++;
                    return Json(HttpStatusCode.OK,
                        System.Text.Json.JsonSerializer.Serialize(new
                        {
                            events = new[]
                            {
                                new
                                {
                                    revision = 3,
                                    section_id = accepted.ToString("D"),
                                    code = "ACC", name = "Accoucheuse", is_active = 1,
                                },
                            },
                            next = 3, has_more = false,
                        }));
                }));
            await replica.QueueUpsertAsync(disputed, "INF", "Infirmière", true);
            await replica.QueueUpsertAsync(accepted, "ACC", "Accoucheuse", true);

            var outcome = await replica.SyncAsync(CentralOrigin, Jwt);
            Assert.False(outcome.Offline);
            Assert.True(outcome.HasConflict);
            Assert.Equal(1, outcome.PendingCount);
            Assert.Equal(3, outcome.Revision);
            Assert.Equal(2, posts);
            Assert.Equal(1, gets);
            Assert.True(Assert.Single(outcome.Sections, x => x.Id == disputed.ToString("D")).IsPending);
            Assert.False(Assert.Single(outcome.Sections, x => x.Id == accepted.ToString("D")).IsPending);
            Assert.Equal(1, replica.Local().PendingCount);
            Assert.True(replica.Local().HasConflict);
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
            using (var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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

            using var reopened = new CentralSectionReplica(store, store.User, School, CentralOrigin);
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
    public async Task Saisie_pendant_une_lecture_reseau_ne_disparait_pas_du_cache_partage()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-interleave-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var firstStore = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var secondStore = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var sync = new CentralSectionReplica(firstStore, firstStore.User, School, CentralOrigin,
                new Handler((request, _) =>
                {
                    Assert.Equal(HttpMethod.Get, request.Method);
                    entered.TrySetResult(true);
                    return response.Task;
                }));
            using var editor = new CentralSectionReplica(secondStore, secondStore.User, School, CentralOrigin);
            var ongoing = sync.SyncAsync(new Uri("https://central.example/"), Jwt);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var id = Guid.NewGuid();
            await editor.QueueUpsertAsync(id, "INF", "Sciences infirmières", true);
            response.SetResult(Json(HttpStatusCode.OK,
                """{"events":[],"next":0,"has_more":false}"""));
            var result = await ongoing;
            Assert.False(result.Offline);
            Assert.Equal(1, result.PendingCount);
            Assert.Equal(1, sync.Local().PendingCount);
            Assert.Equal(1, editor.Local().PendingCount);
            Assert.True(Assert.Single(sync.Local().Sections).IsPending);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Deux_instances_ne_peuvent_pas_creer_deux_operations_sur_meme_section()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-atomic-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var firstStore = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var secondStore = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            using var first = new CentralSectionReplica(firstStore, firstStore.User, School, CentralOrigin);
            using var second = new CentralSectionReplica(secondStore, secondStore.User, School, CentralOrigin);
            var section = Guid.NewGuid();
            async Task<bool> TryQueue(CentralSectionReplica replica)
            {
                try
                {
                    await replica.QueueUpsertAsync(section, "INF", "Infirmière", true);
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            var outcomes = await Task.WhenAll(
                Task.Run(() => TryQueue(first)),
                Task.Run(() => TryQueue(second)));
            Assert.Single(outcomes, succeeded => succeeded);
            Assert.Equal(1, first.Local().PendingCount);
            Assert.Equal(1, second.Local().PendingCount);
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin,
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
    public async Task Retrait_du_compte_interdit_si_des_sections_centrales_sont_en_attente_ou_en_conflit()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-forget-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            store.RememberAccount("SECRETARIAT");
            var section = Guid.NewGuid();
            using var replica = new CentralSectionReplica(store, store.User, School,
                CentralOrigin, new Handler((request, _) => Task.FromResult(
                    request.Method == HttpMethod.Post
                        ? Json(HttpStatusCode.Conflict,
                            """{"error":"stale_section_revision"}""")
                        : Json(HttpStatusCode.OK,
                            """{"events":[],"next":0,"has_more":false}"""))));

            await replica.QueueUpsertAsync(section, "INF", "Infirmière", true);
            Assert.False(store.ForgetKnownAccount("SECRETARIAT"));
            Assert.Equal(1, replica.Local().PendingCount);

            Assert.True((await replica.SyncAsync(CentralOrigin, Jwt)).HasConflict);
            Assert.False(store.ForgetKnownAccount("SECRETARIAT"));
            Assert.True(replica.Local().HasConflict);

            await replica.DiscardConflictedSectionAsync(section);
            Assert.Equal(0, replica.Local().PendingCount);
            Assert.True(store.ForgetKnownAccount("SECRETARIAT"));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    [Fact]
    public async Task Preproduction_et_production_ne_partagent_pas_une_file_d_attente()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-section-origin-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), temp)
                { User = "SECRETARIAT" };
            var stagingOrigin = new Uri("https://staging.example/");
            var productionOrigin = new Uri("https://production.example/");
            using var staging = new CentralSectionReplica(store, store.User, School, stagingOrigin);
            using var production = new CentralSectionReplica(store, store.User, School, productionOrigin);
            var id = Guid.NewGuid();

            await staging.QueueUpsertAsync(id, "INF", "En attente sur staging", true);
            Assert.Equal(1, staging.Local().PendingCount);
            Assert.Empty(production.Local().Sections);
            Assert.Equal(0, production.Local().PendingCount);

            await production.QueueUpsertAsync(id, "INF", "Production distincte", true);
            Assert.Equal("En attente sur staging", Assert.Single(staging.Local().Sections).Name);
            Assert.Equal("Production distincte", Assert.Single(production.Local().Sections).Name);
            Assert.Equal(1, staging.Local().PendingCount);
            Assert.Equal(1, production.Local().PendingCount);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                staging.SyncAsync(productionOrigin, Jwt));
            Assert.Equal(1, staging.Local().PendingCount);
            Assert.Equal(1, production.Local().PendingCount);

            using var reopenedStaging = new CentralSectionReplica(
                store, store.User, School, stagingOrigin);
            using var reopenedProduction = new CentralSectionReplica(
                store, store.User, School, productionOrigin);
            Assert.Equal("En attente sur staging",
                Assert.Single(reopenedStaging.Local().Sections).Name);
            Assert.Equal("Production distincte",
                Assert.Single(reopenedProduction.Local().Sections).Name);
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
            using var replica = new CentralSectionReplica(store, store.User, School, CentralOrigin);
            store.User = "ENSEIGNANT";
            Assert.Throws<InvalidOperationException>(() => replica.Local());
            await Assert.ThrowsAsync<ArgumentException>(() => replica.SyncAsync(
                new Uri("http://central.example/"), Jwt));
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
