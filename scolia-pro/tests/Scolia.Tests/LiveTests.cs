using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

/// <summary>
/// Travail à plusieurs postes : le direct annonce les écritures des autres, la présence et les verrous d'action,
/// et une écriture rejouée après une coupure n'est appliquée qu'une fois.
/// </summary>
public sealed class LiveTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static HttpClient As(HttpClient client, string deviceId, string device)
    {
        client.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        client.DefaultRequestHeaders.Add("X-Scolia-Appareil", Uri.EscapeDataString(device));
        return client;
    }

    private static object Promotion(string code) => new
    {
        code, name = "Promotion " + code, level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
    };

    /// <summary>Lit le flux en direct jusqu'à l'événement attendu, ou échoue au bout de dix secondes.</summary>
    private static async Task<JsonElement> NextEvent(StreamReader stream, string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string? current = null;
        while (true)
        {
            var line = await stream.ReadLineAsync(timeout.Token) ?? throw new InvalidOperationException("Flux fermé.");
            if (line.StartsWith("event: ", StringComparison.Ordinal)) current = line[7..];
            else if (line.StartsWith("data: ", StringComparison.Ordinal) && current == kind) return JsonDocument.Parse(line[6..]).RootElement.Clone();
        }
    }

    [Fact]
    public async Task A_write_on_one_device_is_announced_to_the_others_with_who_and_where()
    {
        var desk = As(await app.InstalledAdmin(), "poste-bureau", "PC Secrétariat");
        var phone = As(await app.Login("admin", "Scolia2026!"), "poste-telephone", "Téléphone Samsung");

        using var response = await phone.GetAsync("/api/direct", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var stream = new StreamReader(await response.Content.ReadAsStreamAsync());
        var state = await NextEvent(stream, "etat");
        Assert.True(state.TryGetProperty("revision", out _));

        await desk.Post("/api/etablissement/promotions", Promotion("DIR1")).Ok();

        var change = await NextEvent(stream, "donnees");
        Assert.Equal("poste-bureau", change.GetProperty("deviceId").GetString());
        Assert.Equal("PC Secrétariat", change.GetProperty("device").GetString());
        Assert.Equal("Administrateur", change.GetProperty("user").GetString());
        Assert.Contains("DIR1", change.GetProperty("summary").GetString());
        Assert.True(change.GetProperty("revision").GetInt64() > state.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task Presence_lists_the_connected_devices_and_their_page()
    {
        var desk = As(await app.InstalledAdmin(), "poste-bureau", "PC Secrétariat");
        var phone = As(await app.Login("admin", "Scolia2026!"), "poste-telephone", "Téléphone");

        await desk.Post("/api/direct/presence", new { page = "Encaisser", activity = "Encaissement de MUKENDI", locks = Array.Empty<string>() }).Ok();
        var seen = await phone.Post("/api/direct/presence", new { page = "Accueil" }).Ok();

        var devices = seen.GetProperty("presence").EnumerateArray().ToDictionary(p => p.GetProperty("deviceId").GetString()!, p => p);
        Assert.Equal(2, devices.Count);
        Assert.Equal("Encaisser", devices["poste-bureau"].GetProperty("page").GetString());
        Assert.Equal("Encaissement de MUKENDI", devices["poste-bureau"].GetProperty("activity").GetString());

        await desk.DeleteAsync("/api/direct/presence");
        var after = await phone.GetAsync("/api/direct/etat").Ok();
        Assert.Single(after.GetProperty("presence").EnumerateArray());
    }

    [Fact]
    public async Task An_action_reserved_by_one_device_is_refused_to_the_others_until_released()
    {
        var desk = As(await app.InstalledAdmin(), "poste-bureau", "PC Secrétariat");
        var phone = As(await app.Login("admin", "Scolia2026!"), "poste-telephone", "Téléphone");
        var key = "encaissement:inscription-42";

        await desk.Post("/api/direct/verrous", new { key, label = "Encaissement de MUKENDI" }).Ok();
        // Le même poste peut redemander son verrou : il le prolonge.
        await desk.Post("/api/direct/verrous", new { key, label = "Encaissement de MUKENDI" }).Ok();

        var (status, body) = await phone.Post("/api/direct/verrous", new { key, label = "Encaissement de MUKENDI" }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("PC Secrétariat", body.GetProperty("title").GetString());
        Assert.Equal("verrou", body.GetProperty("code").GetString());

        // Une écriture faite sous ce verrou par l'autre poste est refusée par le serveur lui-même.
        phone.DefaultRequestHeaders.Add("X-Scolia-Verrou", Uri.EscapeDataString(key));
        var (writeStatus, _) = await phone.Post("/api/etablissement/promotions", Promotion("VER1")).Fail();
        Assert.Equal(HttpStatusCode.Conflict, writeStatus);
        phone.DefaultRequestHeaders.Remove("X-Scolia-Verrou");

        await desk.DeleteAsync($"/api/direct/verrous?cle={Uri.EscapeDataString(key)}");
        await phone.Post("/api/direct/verrous", new { key, label = "Encaissement de MUKENDI" }).Ok();
    }

    [Fact]
    public async Task A_write_replayed_with_the_same_key_is_applied_only_once()
    {
        var admin = As(await app.InstalledAdmin(), "poste-telephone", "Téléphone");
        var key = Guid.NewGuid().ToString("N");

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/etablissement/promotions") { Content = System.Net.Http.Json.JsonContent.Create(Promotion("REJ1"), options: Http.Json) };
        first.Headers.Add("Idempotency-Key", key);
        var created = await admin.SendAsync(first);
        Assert.True(created.IsSuccessStatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        // Le téléphone n'a pas reçu la réponse (coupure) : il renvoie la même opération au retour du réseau.
        using var again = new HttpRequestMessage(HttpMethod.Post, "/api/etablissement/promotions") { Content = System.Net.Http.Json.JsonContent.Create(Promotion("REJ1"), options: Http.Json) };
        again.Headers.Add("Idempotency-Key", key);
        var replayed = await admin.SendAsync(again);
        Assert.True(replayed.IsSuccessStatusCode);
        Assert.True(replayed.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(id, JsonDocument.Parse(await replayed.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString());

        var promotions = await admin.GetAsync("/api/etablissement/promotions").Ok();
        Assert.Single(promotions.EnumerateArray(), p => p.GetProperty("code").GetString() == "REJ1");
    }
}
