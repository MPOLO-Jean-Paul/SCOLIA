using System.Net;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Tests;

public sealed class CentralSectionClientTests
{
    private const string Jwt = "sessionheader.sessionpayload.sessionsignaturetest";
    private static readonly Uri Origin = new("https://scolia-staging.example/");
    private static readonly Guid SectionId = Guid.Parse("8048b1a5-e89b-4000-81cb-c47851adbe37");
    private static readonly Guid OperationId = Guid.Parse("90623fb8-31c0-4db0-85c8-0b49a03e4b81");

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return reply(request, ct);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task L_envoi_reutilise_une_cle_stable_et_exige_l_application_definitive()
    {
        var handler = new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/catalog/sections", request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer " + Jwt, request.Headers.Authorization?.ToString());
            using var parsed = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var json = parsed.RootElement;
            Assert.Equal(OperationId.ToString("D"), json.GetProperty("operation_id").GetString());
            Assert.Equal(SectionId.ToString("D"), json.GetProperty("section_id").GetString());
            Assert.Equal("INF", json.GetProperty("code").GetString());
            Assert.True(json.GetProperty("is_active").GetBoolean());
            Assert.Equal(0, json.GetProperty("expected_revision").GetInt32());
            return Json(HttpStatusCode.Created,
                "{\"operation_id\":\"" + OperationId + "\",\"section_id\":\"" +
                SectionId + "\",\"status\":\"applied\",\"revision\":7,\"replayed\":false}");
        });
        using var client = new CentralSectionClient(handler);
        var receipt = await client.UpsertAsync(Origin, Jwt,
            new CentralSectionChange(OperationId, SectionId, "INF", "Infirmière", true, 0));
        Assert.Equal(OperationId, receipt.OperationId);
        Assert.Equal(7, receipt.Revision);
        Assert.False(receipt.Replayed);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Une_écriture_non_confirmee_reste_non_acquittee(HttpStatusCode status)
    {
        using var client = new CentralSectionClient(new Handler((_, _) =>
            Task.FromResult(Json(status, "{\"status\":\"pending\"}"))));
        var ex = await Assert.ThrowsAsync<CentralPairingException>(() =>
            client.UpsertAsync(Origin, Jwt,
                new CentralSectionChange(OperationId, SectionId, "INF", "Infirmière", true, 0)));
        Assert.Equal((int)status, ex.Status);
    }

    [Fact]
    public async Task Une_confirmation_d_un_autre_identifiant_est_refusee()
    {
        using var client = new CentralSectionClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK,
                "{\"operation_id\":\"" + Guid.NewGuid() +
                "\",\"section_id\":\"" + SectionId +
                "\",\"status\":\"applied\",\"revision\":7,\"replayed\":true}"))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.UpsertAsync(Origin, Jwt,
                new CentralSectionChange(OperationId, SectionId, "INF", "Infirmière", true, 0)));
    }

    [Fact]
    public async Task Lecture_vérifie_ordre_codes_et_curseur()
    {
        var content = "{\"events\":[{\"revision\":12,\"section_id\":\"" +
            SectionId + "\",\"code\":\"INF\",\"name\":\"Infirmière\"," +
            "\"is_active\":1}],\"next\":12,\"has_more\":false}";
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("?after=9&limit=50", request.RequestUri?.Query);
            return Task.FromResult(Json(HttpStatusCode.OK, content));
        });
        using var client = new CentralSectionClient(handler);
        var page = await client.ReadAsync(Origin, Jwt, after: 9);
        Assert.Equal(12, page.NextRevision);
        Assert.Single(page.Events);
        Assert.Equal("INF", page.Events[0].Code);
    }

    [Fact]
    public async Task Curseur_incoherent_est_rejete_sans_ecriture_locale()
    {
        using var client = new CentralSectionClient(new Handler((_, _) =>
            Task.FromResult(Json(HttpStatusCode.OK,
                "{\"events\":[],\"next\":200,\"has_more\":false}"))));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ReadAsync(Origin, Jwt, after: 2));
    }

    [Fact]
    public async Task Aucune_requête_sur_HTTP_ou_sans_JWT_valide()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Fuite HTTPS"));
        using var client = new CentralSectionClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(
            new Uri("http://scolia-staging.example/"), Jwt));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(
            Origin, "jeton-invalide"));
        Assert.Equal(0, handler.Calls);
    }
}
