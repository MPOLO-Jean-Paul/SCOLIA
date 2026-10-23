using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Scolia.Tests;

/// <summary>Serveur SCOLIA complet en mémoire, avec une base SQLite temporaire propre à chaque test.</summary>
public sealed class ScoliaApp : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "scolia-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Scolia:Stockage:DataDirectory", DataDirectory);
        builder.UseSetting("Scolia:OuvrirNavigateur", "false");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Scolia", "1");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<JsonElement> Ok(this Task<HttpResponseMessage> call)
    {
        var response = await call;
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.RequestUri} : {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> Fail(this Task<HttpResponseMessage> call)
    {
        var response = await call;
        var text = await response.Content.ReadAsStringAsync();
        Assert.False(response.IsSuccessStatusCode, $"Succès inattendu : {text}");
        return (response.StatusCode, text.Length == 0 || text[0] != '{' ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    public static Task<HttpResponseMessage> Post(this HttpClient client, string url, object body) => client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> Put(this HttpClient client, string url, object body) => client.PutAsJsonAsync(url, body, Json);

    public static string Year(DateOnly today)
    {
        var first = today.Month >= 8 ? today.Year : today.Year - 1;
        return $"{first}-{first + 1}";
    }

    /// <summary>Installe un établissement (avec ou sans données de démonstration) et renvoie un client administrateur connecté.</summary>
    public static async Task<HttpClient> InstalledAdmin(this ScoliaApp app, bool demo = false)
    {
        var client = app.Client();
        await client.Post("/api/installation", new
        {
            schoolName = "Institut de test", shortName = "IT", motto = "Test", city = "Kinshasa", phone = "0810000000", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Year(DateOnly.FromDateTime(DateTime.Now)), startMonth = 9, monthCount = 10, adminUserName = "admin", adminDisplayName = "Administrateur",
            adminPassword = "Scolia2026!", loadDemoData = demo,
        }).Ok();
        return client;
    }

    public static async Task<HttpClient> Login(this ScoliaApp app, string user, string password)
    {
        var client = app.Client();
        await client.Post("/api/session", new { userName = user, password }).Ok();
        return client;
    }
}
