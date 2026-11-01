using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Scolia.Tests;

/// <summary>
/// Envoi WhatsApp face à un faux serveur Meta : SCOLIA dépose le PDF puis envoie le message exactement comme le
/// décrit la documentation de l'API WhatsApp Cloud, et rapporte fidèlement un refus de Meta.
/// </summary>
public sealed class WhatsAppSenderTests : IDisposable
{
    private readonly FakeMetaServer meta = new();
    private readonly ScoliaApp app = new();

    public WhatsAppSenderTests() => app.Settings["Scolia:WhatsApp:Adresse"] = meta.Address;

    public void Dispose()
    {
        app.Dispose();
        meta.Dispose();
    }

    private async Task<HttpClient> ConfigureAsync(object extra)
    {
        var admin = await app.InstalledAdmin();
        var settings = new Dictionary<string, object?>
        {
            ["enabled"] = true, ["time"] = "18:00", ["recipients"] = new[] { "0812345678" },
            ["whatsAppPhoneNumberId"] = "1098765432", ["whatsAppToken"] = "jeton-meta-de-test",
        };
        foreach (var p in extra.GetType().GetProperties()) settings[p.Name] = p.GetValue(extra);
        var saved = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", settings);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        return admin;
    }

    [Fact]
    public async Task Avec_modele_le_pdf_est_depose_puis_envoye_en_en_tete_du_modele()
    {
        var admin = await ConfigureAsync(new { whatsAppTemplate = "rapport_journalier", whatsAppLanguage = "fr", whatsAppApiVersion = "v24.0" });
        var sent = await admin.PostAsync("/api/rapports/journalier/envoyer", null);
        var body = await sent.Content.ReadAsStringAsync();
        Assert.True(sent.IsSuccessStatusCode, body);
        Assert.Contains("envoyé par WhatsApp à 1 numéro(s)", body);

        Assert.Equal(2, meta.Requests.Count);
        var upload = meta.Requests[0];
        Assert.Equal("/v24.0/1098765432/media", upload.Path);
        Assert.Equal("Bearer jeton-meta-de-test", upload.Authorization);
        Assert.Contains("name=messaging_product", upload.Body);
        Assert.Contains("whatsapp", upload.Body);
        Assert.Contains("application/pdf", upload.Body);
        Assert.Contains("%PDF", upload.Body);

        var message = meta.Requests[1];
        Assert.Equal("/v24.0/1098765432/messages", message.Path);
        Assert.Equal("Bearer jeton-meta-de-test", message.Authorization);
        // Numéro local 0812345678 → format international 243812345678.
        Assert.Contains("\"to\":\"243812345678\"", message.Body);
        Assert.Contains("\"type\":\"template\"", message.Body);
        Assert.Contains("\"name\":\"rapport_journalier\"", message.Body);
        Assert.Contains("\"code\":\"fr\"", message.Body);
        Assert.Contains("\"type\":\"header\"", message.Body);
        Assert.Contains("\"id\":\"media-123\"", message.Body);
        Assert.Contains("rapport-journalier-", message.Body);
    }

    [Fact]
    public async Task Sans_modele_ni_version_le_document_part_directement_avec_la_version_du_compte()
    {
        var admin = await ConfigureAsync(new { whatsAppApiVersion = "" });
        var sent = await admin.PostAsync("/api/rapports/journalier/envoyer", null);
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        Assert.Equal("/1098765432/media", meta.Requests[0].Path);
        Assert.Equal("/1098765432/messages", meta.Requests[1].Path);
        Assert.Contains("\"type\":\"document\"", meta.Requests[1].Body);
        Assert.Contains("\"caption\":\"Rapport journalier du", meta.Requests[1].Body);
    }

    [Fact]
    public async Task Un_refus_de_meta_est_rapporte_avec_son_message()
    {
        meta.RefuseMessages = true;
        var admin = await ConfigureAsync(new { whatsAppTemplate = "rapport_journalier" });
        var sent = await admin.PostAsync("/api/rapports/journalier/envoyer", null);
        var body = await sent.Content.ReadAsStringAsync();
        Assert.True(sent.IsSuccessStatusCode, body);
        Assert.Contains("1 refus", body);
        Assert.Contains("Template name does not exist", body);
    }

    [Fact]
    public async Task Une_version_mal_ecrite_est_refusee()
    {
        var admin = await app.InstalledAdmin();
        var refused = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages",
            new { enabled = false, time = "18:00", recipients = new[] { "0812345678" }, whatsAppApiVersion = "24" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}

/// <summary>Faux serveur Meta : il répond comme l'API WhatsApp Cloud et garde chaque requête reçue.</summary>
public sealed class FakeMetaServer : IDisposable
{
    public sealed record Request(string Method, string Path, string? Authorization, string Body);
    private readonly HttpListener listener = new();
    public List<Request> Requests { get; } = [];
    public string Address { get; }
    public bool RefuseMessages { get; set; }

    public FakeMetaServer()
    {
        var port = FreePort();
        Address = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(Address);
        listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) { return; }
            using var reader = new StreamReader(context.Request.InputStream, Encoding.Latin1);
            var body = await reader.ReadToEndAsync();
            var path = context.Request.Url!.AbsolutePath;
            lock (Requests) Requests.Add(new Request(context.Request.HttpMethod, path, context.Request.Headers["Authorization"], body));
            var (status, json) = path.EndsWith("/media", StringComparison.Ordinal)
                ? (200, "{\"id\":\"media-123\"}")
                : RefuseMessages
                    ? (404, "{\"error\":{\"message\":\"(#132001) Template name does not exist in the translation\",\"type\":\"OAuthException\",\"code\":132001}}")
                    : (200, "{\"messaging_product\":\"whatsapp\",\"contacts\":[{\"input\":\"243812345678\",\"wa_id\":\"243812345678\"}],\"messages\":[{\"id\":\"wamid.TEST\"}]}");
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        listener.Stop();
        listener.Close();
    }
}
