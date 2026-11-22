using System.Net;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class CentralAcademicCatalogReplicaTests
{
    private const string School = "school-a";
    private const string Jwt = "header.payload.signature_example";
    private static readonly Uri Origin = new("https://central.example/");
    private static readonly string YearId = Guid.NewGuid().ToString("D");
    private static readonly string PromotionId = Guid.NewGuid().ToString("D");
    private static readonly string SectionId = Guid.NewGuid().ToString("D");

    private sealed class Handler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken ct)
        {
            Calls++;
            return reply(request, ct);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string YearEvent(long revision, string id, string label = "2026-2027") =>
        JsonSerializer.Serialize(new
        {
            revision, academic_year_id = id, label, start_date = "2026-09-01",
            end_date = "2027-07-31", status = "open",
        });

    private static string PromotionEvent(long revision, string id, string year,
        string? section = null) => JsonSerializer.Serialize(new
        {
            revision, promotion_id = id, academic_year_id = year,
            section_id = section, code = "INF1", name = "Premiere infirmiere",
            is_active = 1,
        });

    [Fact]
    public async Task Annees_lues_et_cachees_dans_SQLite_restent_disponibles_hors_ligne()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-years-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            var requests = new Handler((request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("Bearer " + Jwt, request.Headers.Authorization?.ToString());
                Assert.Equal("central.example", request.RequestUri?.Host);
                Assert.Contains("v1/catalog/years?after=0&limit=50",
                    request.RequestUri?.ToString(), StringComparison.Ordinal);
                return Task.FromResult(Json("{\"events\":[" + YearEvent(4, YearId) +
                    "],\"next\":4,\"has_more\":false}"));
            });
            using (var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years, requests))
            {
                var first = await replica.SyncAsync(Origin, Jwt);
                Assert.False(first.Offline);
                Assert.Equal(4, first.Revision);
                Assert.Empty(first.Promotions);
                Assert.Equal("2026-2027", Assert.Single(first.Years).Label);
                Assert.Equal(1, requests.Calls);
            }
            using var offline = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years,
                new Handler((_, _) => throw new HttpRequestException("Connexion indisponible")));
            var previous = offline.Local();
            Assert.True(previous.Offline);
            Assert.Equal(4, previous.Revision);
            Assert.Equal(YearId, Assert.Single(previous.Years).Id);
            var noNetwork = await offline.SyncAsync(Origin, Jwt);
            Assert.True(noNetwork.Offline);
            Assert.Equal(4, noNetwork.Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Promotions_reprises_sur_deux_pages_sans_les_melanger_avec_les_annees()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-promotions-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            var second = Guid.NewGuid().ToString("D");
            var handler = new Handler((request, _) =>
            {
                if (request.RequestUri?.Query.Contains("after=0", StringComparison.Ordinal) == true)
                    return Task.FromResult(Json("{\"events\":[" +
                        PromotionEvent(3, PromotionId, YearId, SectionId) +
                        "],\"next\":3,\"has_more\":true}"));
                Assert.Contains("after=3", request.RequestUri?.Query);
                return Task.FromResult(Json("{\"events\":[" +
                    PromotionEvent(6, second, YearId) +
                    "],\"next\":6,\"has_more\":false}"));
            });
            using (var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Promotions, handler))
            {
                var state = await replica.SyncAsync(Origin, Jwt);
                Assert.False(state.Offline);
                Assert.False(state.HasMorePages);
                Assert.Equal(6, state.Revision);
                Assert.Equal(2, state.Promotions.Count);
                Assert.Contains(state.Promotions, p => p.SectionId == SectionId);
                Assert.Contains(state.Promotions, p => p.SectionId is null);
                Assert.Empty(state.Years);
                Assert.Equal(2, handler.Calls);
            }
            using var local = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Promotions);
            Assert.Equal(2, local.Local().Promotions.Count);
            using var differentSchool = new CentralAcademicCatalogReplica(store,
                store.User, "school-b", Origin, CentralAcademicCatalogKind.Promotions);
            Assert.Empty(differentSchool.Local().Promotions);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Page_invalide_ne_fait_pas_avancer_le_curseur_SQLite()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-catalog-invalid-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            using var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years,
                new Handler((_, _) => Task.FromResult(Json(
                    "{\"events\":[" + YearEvent(2, YearId) +
                    "],\"next\":3,\"has_more\":false}"))));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                replica.SyncAsync(Origin, Jwt));
            Assert.Equal(0, replica.Local().Revision);
            Assert.Empty(replica.Local().Years);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Champ_JSON_absent_ne_detruit_pas_le_cache_et_une_reprise_valide_reussit()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-catalog-parse-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            var calls = 0;
            var handler = new Handler((_, _) =>
            {
                calls++;
                if (calls == 1)
                    return Task.FromResult(Json(
                        "{\"events\":[{\"revision\":1,\"academic_year_id\":\"" +
                        YearId + "\",\"label\":12}],\"next\":1,\"has_more\":false}"));
                return Task.FromResult(Json("{\"events\":[" + YearEvent(1, YearId) +
                    "],\"next\":1,\"has_more\":false}"));
            });
            using var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years, handler);
            await Assert.ThrowsAsync<InvalidDataException>(
                () => replica.SyncAsync(Origin, Jwt));
            Assert.Equal(0, replica.Local().Revision);
            Assert.Empty(replica.Local().Years);
            var recovered = await replica.SyncAsync(Origin, Jwt);
            Assert.False(recovered.Offline);
            Assert.Equal(1, recovered.Revision);
            Assert.Single(recovered.Years);
            Assert.Equal(2, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Changement_de_compte_bloque_la_lecture_meme_apres_HTTP()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-catalog-user-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            using var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years,
                new Handler((_, _) =>
                {
                    store.User = "ENSEIGNANT";
                    return Task.FromResult(Json("{\"events\":[" +
                        YearEvent(1, YearId) +
                        "],\"next\":1,\"has_more\":false}"));
                }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                replica.SyncAsync(Origin, Jwt));
            Assert.Throws<InvalidOperationException>(() => replica.Local());
            using var teacher = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Years);
            Assert.Empty(teacher.Local().Years);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Autorisation_centrale_retiree_ne_contourne_pas_les_controles()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-catalog-revoked-" +
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example/"), root)
                { User = "SECRETARIAT" };
            using var replica = new CentralAcademicCatalogReplica(store, store.User,
                School, Origin, CentralAcademicCatalogKind.Promotions,
                new Handler((_, _) => Task.FromResult(Json(
                    "{\"error\":\"forbidden\"}", HttpStatusCode.Forbidden))));
            await Assert.ThrowsAsync<CentralPairingException>(() =>
                replica.SyncAsync(Origin, Jwt));
            Assert.Empty(replica.Local().Promotions);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                replica.SyncAsync(new Uri("https://another.example/"), Jwt));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
