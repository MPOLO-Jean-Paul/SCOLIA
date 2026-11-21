using System.Net;
using System.Text;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Tests;

public sealed class CentralJournalClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reply(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string text) => new(status)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json"),
    };
    private const string Jwt = "sessionheader.sessionpayload.sessionsignaturetest";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Lecture_D1_authenticated_retourne_uniquement_metadonnees_ordonnee()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("central-staging.example", request.RequestUri?.Host);
            Assert.Equal("/v1/sync/events", request.RequestUri?.AbsolutePath);
            Assert.Equal("?after=4&limit=5", request.RequestUri?.Query);
            Assert.Equal("Bearer " + Jwt, request.Headers.Authorization?.ToString());
            return Json(HttpStatusCode.OK,
                "{\"events\":[{\"revision\":5,\"entity_kind\":\"cours\"," +
                "\"entity_id\":\"c-1\",\"change_kind\":\"upsert\"," +
                "\"payload_sha256\":\"" + Hash + "\"}]," +
                "\"next\":5,\"has_more\":false,\"includes_payload\":false}");
        });
        using var client = new CentralJournalClient(handler);
        var result = await client.ReadMetadataAsync(new Uri("https://central-staging.example/"), Jwt, 4, 5);
        Assert.Single(result.Events);
        Assert.Equal("cours", result.Events[0].EntityKind);
        Assert.Equal(5, result.NextRevision);
        Assert.False(result.HasMore);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Retour_non_ordonne_ou_avec_donnees_metier_est_refuse()
    {
        foreach (var result in new[]
        {
            "{\"events\":[],\"next\":3,\"has_more\":false,\"includes_payload\":false}",
            "{\"events\":[],\"next\":5,\"has_more\":false,\"includes_payload\":true}",
            "{\"events\":[{\"revision\":5,\"entity_kind\":\"cours\",\"entity_id\":\"c-1\"," +
                "\"change_kind\":\"upsert\",\"payload_sha256\":\"" + Hash + "\"}," +
                "{\"revision\":5,\"entity_kind\":\"cours\",\"entity_id\":\"c-2\"," +
                "\"change_kind\":\"upsert\",\"payload_sha256\":\"" + Hash + "\"}]," +
                "\"next\":5,\"has_more\":false,\"includes_payload\":false}",
        })
        {
            using var client = new CentralJournalClient(new Handler(_ => Json(HttpStatusCode.OK, result)));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.ReadMetadataAsync(new Uri("https://central-staging.example/"), Jwt, 4));
        }
    }

    [Fact]
    public async Task JWT_manquant_et_refus_de_droits_n_ouvrent_pas_le_journal()
    {
        var handler = new Handler(_ => Json(HttpStatusCode.Forbidden, "{}"));
        using var client = new CentralJournalClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ReadMetadataAsync(new Uri("https://central-staging.example/"),
                "une-preuve-de-jumelage"));
        Assert.Equal(0, handler.Calls);
        var error = await Assert.ThrowsAsync<CentralPairingException>(() =>
            client.ReadMetadataAsync(new Uri("https://central-staging.example/"), Jwt));
        Assert.Equal(403, error.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Origin_Http_ne_peut_recevoir_jamais_un_bearer()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("Fuite de JWT"));
        using var client = new CentralJournalClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ReadMetadataAsync(new Uri("http://central-staging.example/"), Jwt));
        Assert.Equal(0, handler.Calls);
    }
}
