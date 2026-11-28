using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JPServices.Owner;

/// <summary>
/// Identifiants de la console native. Le jeton de service Cloudflare Access ouvre la porte
/// (Access vérifie l'identifiant et le secret avant le Worker), le jeton propriétaire autorise
/// ensuite chaque appel : il faut toujours les deux, comme dans la console web.
/// </summary>
public sealed record OwnerCredentials(string AccessClientId, string AccessClientSecret, string OwnerToken)
{
    public bool IsComplete =>
        AccessClientId.Trim().Length >= 8 && AccessClientSecret.Trim().Length >= 16 && OwnerToken.Trim().Length >= 16;

    /// <summary>Identifiant raccourci pour l'affichage ; jamais le secret.</summary>
    public string Hint => AccessClientId.Length <= 10 ? AccessClientId : AccessClientId[..10] + "…";
}

/// <summary>Erreur lisible renvoyée à l'écran, sans jamais citer un secret.</summary>
public sealed class OwnerApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Client de l'API propriétaire du Worker (routes /api/v1/owner/*).</summary>
public sealed class OwnerApi : IDisposable
{
    public static readonly Uri DefaultBase = new("https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/");

    private readonly HttpClient _http;
    private readonly OwnerCredentials _credentials;

    public OwnerApi(OwnerCredentials credentials, Uri? baseAddress = null, HttpMessageHandler? handler = null)
    {
        _credentials = credentials;
        // Pas de redirection suivie : une redirection vers la page de connexion Access signifie
        // que le jeton de service n'est pas accepté, et le secret ne doit partir nulle part ailleurs.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            BaseAddress = baseAddress ?? DefaultBase,
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SCOLIA-Console/" + OwnerInfo.Version);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public Task<JsonElement> SummaryAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/summary", ct);
    public Task<JsonElement> SchoolsAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/schools", ct);
    public Task<JsonElement> SchoolAsync(string id, CancellationToken ct = default) =>
        GetAsync("api/v1/owner/schools/" + CheckedId(id), ct);
    public Task<JsonElement> ServersAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/servers", ct);
    public Task<JsonElement> RelaysAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/relays", ct);
    public Task<JsonElement> AuditAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/audit", ct);
    public Task<JsonElement> StorageAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/settings/storage", ct);
    public Task<JsonElement> DiscoverTurnAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/relays/cloudflare", ct);
    public Task<JsonElement> NetlifyAsync(CancellationToken ct = default) => GetAsync("api/v1/owner/relays/netlify", ct);

    public Task<JsonElement> ConfigureStorageAsync(string account, string bucket, string accessKey, string secret,
        CancellationToken ct = default) => SendAsync(HttpMethod.Put, "api/v1/owner/settings/storage",
        new { account_id = account.Trim(), bucket = bucket.Trim(), access_key_id = accessKey.Trim(), secret_access_key = secret.Trim() }, ct);

    public Task<JsonElement> RemoveStorageAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/v1/owner/settings/storage", null, ct);

    public Task<JsonElement> RequestCloudForAllAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/owner/cloud/request-all", new { }, ct);

    public Task<JsonElement> ConfigureNetlifyAsync(string label, string address, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/v1/owner/relays/netlify",
            new { label = label.Trim(), base_url = address.Trim(), enabled = false, confirm_experimental = true }, ct);

    public Task<JsonElement> TestNetlifyAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/owner/relays/netlify/test", new { }, ct);

    public Task<JsonElement> RemoveNetlifyAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, "api/v1/owner/relays/netlify", null, ct);

    public Task<JsonElement> CreateInvitationAsync(string schoolName, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/v1/owner/invitations", new { school_name = schoolName.Trim() }, ct);

    public Task<JsonElement> CreateRecoveryInvitationAsync(string schoolId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"api/v1/owner/schools/{CheckedId(schoolId)}/recovery-invitation", new { }, ct);

    /// <summary>Modification de licence : seuls les champs fournis changent (voir validateLicensePatch du Worker).</summary>
    public Task<JsonElement> UpdateLicenseAsync(string schoolId, IReadOnlyDictionary<string, object?> patch,
        CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"api/v1/owner/schools/{CheckedId(schoolId)}/license", patch, ct);

    public Task<JsonElement> RequestCloudAsync(string schoolId, bool requested, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"api/v1/owner/schools/{CheckedId(schoolId)}/cloud", new { requested }, ct);

    public Task<JsonElement> SetRelayStateAsync(string relayId, bool active, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"api/v1/owner/relays/{CheckedId(relayId)}", new { state = active ? "active" : "disabled" }, ct);

    public Task<JsonElement> CreateRelayAsync(string label, string endpoint, CancellationToken ct = default)
    {
        if (label.Trim().Length is < 3 or > 90 || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "wss" || uri.HostNameType != UriHostNameType.Dns || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new OwnerApiException("Indiquez un nom de relais et une adresse WSS publique valide.");
        return SendAsync(HttpMethod.Post, "api/v1/owner/relays", new { label = label.Trim(), endpoint_wss = uri.AbsoluteUri }, ct);
    }

    /// <summary>Réutilise le fournisseur existant ; un relais déjà actif ne change pas de révision.</summary>
    public async Task<JsonElement> UseCloudflarePrimaryAsync(CancellationToken ct = default)
    {
        const string endpoint = "wss://relay.jp-services.org/";
        var catalog = await RelaysAsync(ct);
        var chosen = OwnerText.Items(catalog, "relays").FirstOrDefault(r => OwnerText.Str(r, "url") == endpoint);
        if (OwnerText.Str(chosen, "state") == "active")
            return JsonSerializer.SerializeToElement(new { notice = "Cloudflare est déjà le relais principal. Les approbations des établissements sont conservées." });
        var id = OwnerText.Str(chosen, "id");
        if (id.Length == 0) id = OwnerText.Str(await CreateRelayAsync("SCOLIA Cloudflare (principal)", endpoint, ct), "id");
        return await SetRelayStateAsync(id, active: true, ct);
    }

    private static string CheckedId(string id) =>
        Guid.TryParse(id, out var guid) ? guid.ToString("D") : throw new OwnerApiException("Identifiant invalide.");

    private Task<JsonElement> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!_credentials.IsComplete)
            throw new OwnerApiException("Configurez d'abord l'accès dans Compte et sécurité.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("CF-Access-Client-Id", _credentials.AccessClientId.Trim());
        request.Headers.Add("CF-Access-Client-Secret", _credentials.AccessClientSecret.Trim());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.OwnerToken.Trim());
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new OwnerApiException("Le service JP-Services ne répond pas (délai dépassé)."); }
        catch (HttpRequestException)
        { throw new OwnerApiException("Service JP-Services injoignable : vérifiez la connexion Internet."); }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
                throw new OwnerApiException(
                    "Cloudflare Access a refusé le jeton de service (redirection vers la connexion). " +
                    "Vérifiez l'identifiant, le secret et la règle « Service Auth » de l'application Access.", response.StatusCode);
            var json = TryParse(text);
            if (response.IsSuccessStatusCode)
                return json ?? throw new OwnerApiException("Réponse inattendue du service JP-Services.");
            var detail = json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("error", out var e) &&
                e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            throw new OwnerApiException(status switch
            {
                401 => "Jeton propriétaire refusé. Saisissez le jeton actuel dans Compte et sécurité.",
                403 when detail is null => "Cloudflare Access a refusé l'accès : jeton de service inconnu, expiré ou non autorisé.",
                403 => detail!,
                429 => "Trop de demandes. Réessayez dans une minute.",
                >= 500 => detail ?? "Le service JP-Services rencontre une erreur. Réessayez plus tard.",
                _ => detail ?? $"Demande refusée (HTTP {status})."
            }, response.StatusCode);
        }
    }

    private static JsonElement? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    public void Dispose() => _http.Dispose();
}

public static class OwnerInfo
{
    public const string Version = "1.2.2";
}
