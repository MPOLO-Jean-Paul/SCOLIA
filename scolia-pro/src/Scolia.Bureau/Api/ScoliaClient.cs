using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Scolia.Bureau.Api;

/// <summary>Client HTTP du serveur SCOLIA : session par cookie et en-tête anti-CSRF, comme l'interface web.</summary>
public sealed class ScoliaClient : IDisposable
{
    private CookieContainer cookies = new();
    private HttpClient http;

    public ScoliaClient(Uri server)
    {
        Server = server;
        http = Create(server);
    }

    public Uri Server { get; private set; }

    /// <summary>Change de serveur (ce poste ou celui de l'établissement) : nouvelle connexion, sans session ouverte.</summary>
    public void Retarget(Uri server)
    {
        if (server == Server) return;
        var previous = http;
        cookies = new CookieContainer();
        http = Create(server);
        Server = server;
        previous.Dispose();
    }

    private HttpClient Create(Uri server)
    {
        var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        var client = new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Add("X-Scolia", "1");
        return client;
    }

    public async Task<SessionDto> LoginAsync(string userName, string password, CancellationToken ct = default) =>
        await SendAsync<SessionDto>(HttpMethod.Post, "api/session", new { userName, password }, ct);

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await SendAsync(HttpMethod.Delete, "api/session", null, ct); }
        finally { cookies.GetAllCookies().Cast<Cookie>().ToList().ForEach(c => c.Expired = true); }
    }

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("api/sante", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public async Task<SessionDto?> CurrentSessionAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/session", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return await response.Content.ReadFromJsonAsync<SessionDto>(Json.Options, ct);
    }

    /// <summary>Lit une ressource JSON.</summary>
    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, ct);

    /// <summary>Envoie une requête avec un corps JSON et lit la réponse.</summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    /// <summary>Envoie une requête sans lire de réponse.</summary>
    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(method, path, body, ct);
    }

    /// <summary>Télécharge un document (PDF, Excel, CSV, sauvegarde) servi par l'API.</summary>
    public async Task<byte[]> DownloadAsync(string path, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Demande un document (PDF) et lit l'en-tête X-Cartes-Ecartees : le nombre d'éléments écartés.</summary>
    public async Task<(byte[] Bytes, int Skipped)> PostDocumentAsync(string path, object body, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(HttpMethod.Post, path, body, ct);
        var skipped = response.Headers.TryGetValues("X-Cartes-Ecartees", out var values)
            ? int.Parse(values.Single(), System.Globalization.CultureInfo.InvariantCulture) : 0;
        return (await response.Content.ReadAsByteArrayAsync(ct), skipped);
    }

    /// <summary>Envoie des fichiers en multipart (import Excel, logo).</summary>
    public async Task<T> UploadAsync<T>(HttpMethod method, string path, IEnumerable<UploadedFile> files, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        foreach (var file in files) content.Add(new ByteArrayContent(file.Data), file.Field, file.FileName);
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    /// <summary>Construit une chaîne de requête. Les valeurs vides sont omises.</summary>
    public static string Query(params (string Key, object? Value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            var text = value switch
            {
                null => null,
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (!string.IsNullOrWhiteSpace(text)) parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
        }
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get) request.Content = JsonContent.Create(body ?? new { }, options: Json.Options);
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var error = await ToException(response);
        response.Dispose();
        throw error;
    }

    private static async Task<ApiException> ToException(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } text)
            {
                var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                // Connexion verrouillée : le serveur donne le temps restant pour le compte à rebours.
                if (doc.RootElement.TryGetProperty("retryAfterSeconds", out var retry) && retry.TryGetInt32(out var seconds))
                    return new ApiException(text, status) { Code = code, RetryAfterSeconds = seconds };
                // Validation : le premier message de champ précise ce qui est incorrect.
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                    foreach (var field in errors.EnumerateObject())
                        if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                            return new ApiException($"{text} {field.Value[0].GetString()}", status);
                return new ApiException(text, status);
            }
        }
        catch (JsonException) { }
        return new ApiException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Vous n'avez pas les droits nécessaires pour cette action.",
            HttpStatusCode.Unauthorized => "Votre session a expiré. Reconnectez-vous.",
            (HttpStatusCode)429 => "Trop de tentatives. Patientez une minute.",
            _ => $"Erreur {status}",
        }, status);
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Fichier joint à un envoi multipart.</summary>
public sealed record UploadedFile(string Field, string FileName, byte[] Data);

/// <summary>Erreur renvoyée par le serveur (format RFC 9457), message lisible en français.</summary>
public sealed class ApiException(string message, int status = 0) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; init; }
    public int? RetryAfterSeconds { get; init; }
}
