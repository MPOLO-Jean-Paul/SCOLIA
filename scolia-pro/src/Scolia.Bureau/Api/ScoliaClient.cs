using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scolia.Transport;

namespace Scolia.Bureau.Api;

/// <summary>
/// Client HTTP du serveur SCOLIA : session par cookie et en-tête anti-CSRF, comme l'interface web.
/// Chaque requête présente le poste (identifiant et nom) ; chaque écriture porte une clé d'idempotence.
/// Hors ligne, les pages déjà lues sont rendues depuis l'appareil et les écritures sont mises en file,
/// puis envoyées dans l'ordre au retour de l'ordinateur principal.
/// </summary>
public sealed class ScoliaClient : IDisposable
{
    private CookieContainer cookies = new();
    private HttpClient http;
    private bool online = true;
    private RemoteLink? remoteLink;
    private readonly SemaphoreSlim flushGate = new(1, 1);
    public bool IsLongDistance => remoteLink is not null;
    public string SessionCookie => cookies.GetCookieHeader(Server);
    public void RestoreSessionCookie(string cookie) { if (!string.IsNullOrWhiteSpace(cookie)) cookies.SetCookies(Server, cookie); }

    /// <summary>Verrous d'action déclarés par l'écriture en cours : le serveur refuse si un autre poste les tient.</summary>
    private static readonly AsyncLocal<string?> CurrentLocks = new();

    /// <summary>Données gardées sur l'appareil ; null tant qu'aucune session n'est ouverte.</summary>
    public Infrastructure.OfflineStore? Offline { get; set; }

    /// <summary>L'ordinateur principal répond : faux après une coupure, jusqu'à la prochaine réponse.</summary>
    public bool IsOnline => online;

    public event Action<bool>? ConnectivityChanged;

    /// <summary>Une écriture a été gardée hors ligne, pour envoi au retour du serveur.</summary>
    public event Action<Infrastructure.OutboxItem>? Queued;

    /// <summary>Déclare un verrou d'action pour les écritures faites jusqu'à la fin du bloc.</summary>
    public static IDisposable UnderLock(string key)
    {
        var previous = CurrentLocks.Value;
        CurrentLocks.Value = previous is null ? Uri.EscapeDataString(key) : $"{previous},{Uri.EscapeDataString(key)}";
        return new Restore(() => CurrentLocks.Value = previous);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private void SetOnline(bool value)
    {
        if (online == value) return;
        online = value;
        ConnectivityChanged?.Invoke(value);
    }

    public ScoliaClient(Uri server)
    {
        Server = server;
        http = Create(server);
    }

    public Uri Server { get; private set; }

    /// <summary>Change de serveur (ce poste ou celui de l'établissement) : nouvelle connexion, sans session ouverte.</summary>
    public void Retarget(Uri server)
    {
        if (server == Server && remoteLink is null) return;
        remoteLink = null;
        // Un changement d'établissement ne doit jamais réutiliser le cache ou les opérations de l'ancien serveur.
        Offline = null;
        var previous = http;
        cookies = new CookieContainer();
        http = Create(server);
        Server = server;
        previous.Dispose();
    }

    public void UseRemote(RemoteLink link)
    {
        link.Validate();
        var previous = http;
        remoteLink = link;
        Offline = null;
        cookies = new CookieContainer();
        Server = link.VirtualServer;
        http = Create(Server);
        previous.Dispose();
    }

    // La rapidité des écrans ne doit pas dépendre du délai global des écritures (60 s).
    // La sonde du serveur relance ensuite la connexion sans bloquer la navigation.
    private TimeSpan ReadTimeout => IsLongDistance ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(4);
    private TimeSpan ProbeTimeout => IsLongDistance ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(3);
    private bool CanUseOfflineCache => Offline is { } store && !string.IsNullOrWhiteSpace(store.User);

    private HttpClient Create(Uri server)
    {
        // Délai de connexion court : un ordinateur principal éteint est détecté en quelques secondes, pas en vingt.
        HttpMessageHandler handler = remoteLink is { } link ? new RemoteHttpHandler(link, cookies)
            : new SocketsHttpHandler { CookieContainer = cookies, UseCookies = true, ConnectTimeout = TimeSpan.FromSeconds(4) };
        var client = new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.Add("X-Scolia", "1");
        client.DefaultRequestHeaders.Add("X-Scolia-Poste", Infrastructure.DeviceIdentity.Id);
        client.DefaultRequestHeaders.Add("X-Scolia-Appareil", Uri.EscapeDataString(Infrastructure.DeviceIdentity.Name));
        return client;
    }

    /// <summary>Client sans délai global, pour le flux en direct qui reste ouvert.</summary>
    public HttpClient CreateStreamClient()
    {
        HttpMessageHandler handler = remoteLink is { } link ? new RemoteHttpHandler(link, cookies)
            : new SocketsHttpHandler { CookieContainer = cookies, UseCookies = true, ConnectTimeout = TimeSpan.FromSeconds(4) };
        var client = new HttpClient(handler) { BaseAddress = Server, Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Add("X-Scolia", "1");
        client.DefaultRequestHeaders.Add("X-Scolia-Poste", Infrastructure.DeviceIdentity.Id);
        client.DefaultRequestHeaders.Add("X-Scolia-Appareil", Uri.EscapeDataString(Infrastructure.DeviceIdentity.Name));
        return client;
    }

    public async Task<SessionDto> LoginAsync(string userName, string password, CancellationToken ct = default) =>
        await SendAsync<SessionDto>(HttpMethod.Post, "api/session", new { userName, password }, ct);

    public sealed record PairedSession(SessionDto Session, Guid GrantId, string DeviceSecret, DateTime ExpiresUtc);

    /// <summary>Un QR explicitement créé par le serveur donne accès au compte lié, sans communiquer son mot de passe.</summary>
    public async Task<SessionDto> AcceptDeviceInvitationAsync(string invitationToken, CancellationToken ct = default)
    {
        var linked = await SendAsync<PairedSession>(HttpMethod.Post, "api/jumelage/accepter", new
        {
            invitationToken, deviceId = Infrastructure.DeviceIdentity.Id, deviceName = Infrastructure.DeviceIdentity.Name
        }, ct);
        Infrastructure.DevicePairingStore.Save(Server, new Infrastructure.DevicePairingStore.Credential(
            linked.GrantId, linked.DeviceSecret, linked.ExpiresUtc, linked.Session.UserName));
        return linked.Session;
    }

    /// <summary>Renouvelle une session reconnue sur le serveur, avec un secret propre à cet appareil.</summary>
    public async Task<SessionDto?> RestoreDeviceSessionAsync(CancellationToken ct = default)
    {
        var stored = Infrastructure.DevicePairingStore.Read(Server);
        if (stored is null) return null;
        try
        {
            return await SendAsync<SessionDto>(HttpMethod.Post, "api/session/appareil",
                new { grantId = stored.GrantId, deviceId = Infrastructure.DeviceIdentity.Id, secret = stored.DeviceSecret }, ct);
        }
        catch (ApiException ex) when (ex.Status is 401 or 403 or 404)
        {
            Infrastructure.DevicePairingStore.Forget(Server);
            return null;
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try { await SendAsync(HttpMethod.Delete, "api/session", null, ct); }
        finally
        {
            cookies.GetAllCookies().Cast<Cookie>().ToList().ForEach(c => c.Expired = true);
            Offline?.ClearBackgroundSession();
        }
    }

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            // Une sonde ne doit jamais attendre les 60 secondes du délai d'une écriture.
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(ProbeTimeout);
            using var response = await http.GetAsync("api/sante", probe.Token);
            // Une sonde négative doit être annoncée à la synchronisation :
            // autrement le poste reste affiché « en ligne » après une coupure VPN.
            SetOnline(response.IsSuccessStatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { SetOnline(false); return false; }
        catch (TaskCanceledException) { if (!ct.IsCancellationRequested) SetOnline(false); return false; }
    }

    public async Task<SessionDto?> CurrentSessionAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/session", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return await response.Content.ReadFromJsonAsync<SessionDto>(Json.Options, ct);
    }

    /// <summary>
    /// Lit une ressource JSON. En ligne, la réponse est gardée sur l'appareil ; hors ligne, la dernière version
    /// gardée est rendue, ce qui permet de consulter les fiches, les reçus et les listes sans réseau.
    /// </summary>
    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sourceStore = Offline;
        var sourceUser = sourceStore?.User ?? string.Empty;
        var canCache = Cacheable(path) && sourceUser.Length > 0;

        // CIRCUIT HORS LIGNE : une fois la coupure connue, aucun écran n'attend de
        // nouveau le DNS, un VPN ou un serveur absent. La boucle de synchronisation
        // contrôle la reprise indépendamment, toutes les 5 secondes.
        if (!IsOnline && CanUseOfflineCache)
        {
            if (canCache && Offline!.ReadPage(path) is { } local)
                return JsonSerializer.Deserialize<T>(local.Json, Json.Options)!;
            throw new OfflineException("Cette rubrique n'a pas encore de données sur cet appareil. Reconnectez-vous à l'ordinateur principal pour la consulter.");
        }

        string text;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(ReadTimeout);
            using var response = await SendRawAsync(HttpMethod.Get, path, null, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NoContent) return default!;
            text = await response.Content.ReadAsStringAsync(deadline.Token);
            // Une lecture réussie reste réutilisable au prochain démarrage hors ligne.
            if (canCache && sourceStore is not null)
                await Task.Run(() => sourceStore.SavePageFor(sourceUser, path, text), ct);
        }
        catch (Exception ex) when (IsDisconnected(ex, ct))
        {
            SetOnline(false);
            if (canCache && Offline!.ReadPage(path) is { } saved) text = saved.Json;
            else throw new OfflineException("Ordinateur principal injoignable : cette rubrique n'a pas encore été chargée sur cet appareil.");
        }
        return JsonSerializer.Deserialize<T>(text, Json.Options)!;
    }

    /// <summary>Envoie une requête avec un corps JSON et lit la réponse.</summary>
    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        if (method == HttpMethod.Get) return await GetAsync<T>(path, ct);
        using var response = await WriteAsync(method, path, body, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    /// <summary>Envoie une requête sans lire de réponse.</summary>
    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await WriteAsync(method, path, body, ct);
    }

    /// <summary>
    /// Écriture : envoyée tout de suite si l'ordinateur principal répond. Sinon, une opération du quotidien
    /// (inscription, encaissement, dépense, présences, notes, discipline) est gardée pour envoi au retour ;
    /// les autres demandent le serveur et sont refusées avec une explication.
    /// </summary>
    private async Task<HttpResponseMessage> WriteAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var key = Guid.NewGuid().ToString("N");
        if (method == HttpMethod.Post && (path is "api/paiements" or "api/paiements/lot") && body is not null)
        {
            var payload = JsonSerializer.SerializeToNode(body, Json.Options)!.AsObject();
            key = payload["idempotencyKey"]?.GetValue<string>() is { Length: > 0 } previousKey ? previousKey : key;
            payload["idempotencyKey"] = key;
            body = payload;
        }
        var sourceStore = Offline;
        var sourceUser = sourceStore?.User ?? string.Empty;
        Infrastructure.OutboxItem? durable = null;
        if (sourceStore is not null && sourceUser.Length > 0 && Queueable(method, path))
        {
            durable = new Infrastructure.OutboxItem
            {
                Key = key, Method = method.Method, Path = path,
                Body = JsonSerializer.Serialize(body ?? new { }, Json.Options),
                Label = Describe(method, path), User = sourceUser,
            };
            // Enregistrement durable AVANT l'envoi. En cas de changement de
            // compte pendant l'attente, la source et son identité sont fixées.
            await Task.Run(() => sourceStore.Enqueue(durable), ct);
        }
        // Même principe que pour la lecture : lorsqu'une coupure est confirmée,
        // aucune validation ne doit attendre inutilement une nouvelle requête réseau.
        // L'opération a déjà été journalisée avec sa clé d'idempotence.
        if (!IsOnline && CanUseOfflineCache)
        {
            if (durable is null)
                throw new OfflineException("Cette action nécessite l'ordinateur principal. Reconnectez-vous pour la terminer.");
            Queued?.Invoke(durable);
            throw new OfflineQueuedException($"{durable.Label} : enregistré sur cet appareil. Envoi automatique au retour de l'appareil principal.");
        }
        try
        {
            var response = await SendRawAsync(method, path, body, ct, key);
            if (durable is not null && sourceStore is not null)
                await Task.Run(() => sourceStore.SentFor(sourceUser, durable.Id), ct);
            return response;
        }
        catch (Exception ex) when (IsDisconnected(ex, ct))
        {
            SetOnline(false);
            if (durable is null) throw new OfflineException("Appareil principal injoignable : cette action demande la connexion. Elle sera possible dès son retour.");
            Queued?.Invoke(durable);
            throw new OfflineQueuedException($"{durable.Label} : enregistré sur cet appareil. Envoi automatique au retour de l'appareil principal.");
        }
        catch (ApiException ex)
        {
            if (durable is not null && sourceStore is not null && ex.Status != 401)
                sourceStore.RefusedFor(sourceUser, durable.Id, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Envoie les opérations gardées hors ligne, dans l'ordre où elles ont été faites. Chacune garde sa clé :
    /// le serveur ne l'applique qu'une fois, même si une première tentative avait abouti sans réponse.
    /// Une opération refusée (le mois a été payé entre-temps sur un autre poste) passe en conflit, avec le motif.
    /// </summary>
    public async Task<(int Sent, int Refused)> FlushAsync(CancellationToken ct = default)
    {
        if (Offline is null) return (0, 0);
        await flushGate.WaitAsync(ct);
        try
        {
        int sent = 0, refused = 0;
        var flushingStore = Offline;
        var flushingUser = flushingStore.User;
        foreach (var item in flushingStore.Outbox())
        {
            if (!string.Equals(flushingStore.User, flushingUser, StringComparison.Ordinal))
                break; // Un autre compte a pris l'interface pendant l'envoi.
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(item.Method), item.Path)
                {
                    Content = new StringContent(item.Body, System.Text.Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Idempotency-Key", item.Key);
                using var response = await http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    flushingStore.SentFor(flushingUser, item.Id);
                    sent++;
                    continue;
                }
                if ((int)response.StatusCode >= 500) break;
                var error = await ToException(response);
                // Session expirée : l'opération attend la reconnexion, elle n'est pas en conflit.
                if (error.Status == 401) break;
                flushingStore.RefusedFor(flushingUser, item.Id, error.Message);
                refused++;
            }
            catch (Exception ex) when (IsDisconnected(ex, ct))
            {
                SetOnline(false);
                break;
            }
        }
        if (sent > 0) SetOnline(true);
        return (sent, refused);
        }
        finally { flushGate.Release(); }
    }

    /// <summary>Coupure de l'ordinateur principal ou du Wi-Fi, par opposition à un refus du serveur.</summary>
    private static bool IsDisconnected(Exception ex, CancellationToken ct) => ex switch
    {
        HttpRequestException => true,
        TaskCanceledException when !ct.IsCancellationRequested => true,
        ApiException { Status: 502 or 503 or 504 } => true,
        _ => false,
    };

    /// <summary>Pages gardées sur l'appareil : les données métier, pas le direct ni la santé du serveur.</summary>
    private static bool Cacheable(string path) =>
        path.StartsWith("api/", StringComparison.Ordinal)
        && !path.StartsWith("api/direct", StringComparison.Ordinal)
        && !path.StartsWith("api/revision", StringComparison.Ordinal)
        && !path.StartsWith("api/sante", StringComparison.Ordinal)
        && !path.StartsWith("api/session", StringComparison.Ordinal);

    /// <summary>
    /// Écritures du quotidien qui peuvent attendre le retour du serveur. Les aperçus, les exports, les sauvegardes,
    /// les effacements et les réglages de l'établissement demandent le serveur, tout de suite.
    /// </summary>
    public static bool Queueable(HttpMethod method, string path)
    {
        if (method != HttpMethod.Post && method != HttpMethod.Put && method.Method != "PATCH") return false;
        // Les annulations de reçus, effacements et opérations irréversibles restent
        // à confirmer auprès du serveur : pas de validation fictive hors ligne.
        if (new[] { "annulation", "effacement", "supprimer", "revoquer", "export", "apercu" }
            .Any(term => path.Contains(term, StringComparison.OrdinalIgnoreCase))) return false;
        string[] daily =
        [
            "api/apprenants", "api/inscriptions", "api/paiements", "api/depenses", "api/scolarite/presences",
            "api/scolarite/discipline", "api/scolarite/evaluations", "api/personnel",
        ];
        return daily.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string Describe(HttpMethod method, string path) => path switch
    {
        _ when path.StartsWith("api/paiements/lot", StringComparison.Ordinal) => "Encaissement par lot",
        _ when path.Contains("annulation", StringComparison.Ordinal) => "Annulation",
        _ when path.StartsWith("api/paiements", StringComparison.Ordinal) => "Encaissement",
        _ when path.StartsWith("api/apprenants", StringComparison.Ordinal) && method == HttpMethod.Post => "Inscription",
        _ when path.StartsWith("api/apprenants", StringComparison.Ordinal) || path.StartsWith("api/inscriptions", StringComparison.Ordinal) => "Dossier de l'apprenant",
        _ when path.StartsWith("api/depenses", StringComparison.Ordinal) => "Dépense",
        _ when path.StartsWith("api/scolarite/presences", StringComparison.Ordinal) => "Appel des présences",
        _ when path.StartsWith("api/scolarite/discipline", StringComparison.Ordinal) => "Discipline",
        _ when path.StartsWith("api/scolarite/evaluations", StringComparison.Ordinal) => "Notes",
        _ when path.StartsWith("api/personnel", StringComparison.Ordinal) => "Pointage",
        _ => "Opération",
    };

    /// <summary>Télécharge un document (PDF, Excel, CSV, sauvegarde) servi par l'API.</summary>
    public async Task<byte[]> DownloadAsync(string path, CancellationToken ct = default)
    {
        // Le logo de l'établissement est gardé sur l'appareil : il reste affiché
        // dès la connexion hors ligne, sans essayer un téléchargement de 60 s.
        var keep = path == "api/etablissement/logo";
        var logoStore = Offline;
        var logoUser = logoStore?.User ?? string.Empty;
        ct.ThrowIfCancellationRequested();
        if (!IsOnline && CanUseOfflineCache)
        {
            if (keep && Offline!.ReadPage(path) is { } cached)
                return Convert.FromBase64String(cached.Json);
            throw new OfflineException(keep
                ? "Le logo de l'établissement n'a pas encore été enregistré sur cet appareil."
                : "L'export ou le document demandé exige la connexion au serveur.");
        }

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (keep) deadline.CancelAfter(ReadTimeout);
            using var response = await http.GetAsync(path, deadline.Token);
            if (!response.IsSuccessStatusCode) throw await ToException(response);
            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (keep && logoStore is not null && logoUser.Length > 0)
                logoStore.SavePageFor(logoUser, path, Convert.ToBase64String(bytes));
            return bytes;
        }
        catch (Exception ex) when (keep && IsDisconnected(ex, ct))
        {
            SetOnline(false);
            if (Offline?.ReadPage(path) is { } saved)
                return Convert.FromBase64String(saved.Json);
            throw new OfflineException("Le logo de l'établissement n'a pas encore été enregistré sur cet appareil.");
        }
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
    public Task<T> UploadAsync<T>(HttpMethod method, string path, IEnumerable<UploadedFile> files, CancellationToken ct = default) =>
        UploadFormAsync<T>(method, path, files, null, ct);

    /// <summary>Import de documents protégés : la phrase secrète va dans le corps POST, jamais l'URL.</summary>
    public Task<T> UploadWithFieldsAsync<T>(HttpMethod method, string path, IEnumerable<UploadedFile> files,
        IReadOnlyDictionary<string, string> fields, CancellationToken ct = default) =>
        UploadFormAsync<T>(method, path, files, fields, ct);

    private async Task<T> UploadFormAsync<T>(HttpMethod method, string path, IEnumerable<UploadedFile> files,
        IReadOnlyDictionary<string, string>? fields, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        if (fields is not null)
            foreach (var pair in fields) content.Add(new StringContent(pair.Value), pair.Key);
        foreach (var file in files)
        {
            // Le type du fichier accompagne l'envoi : le serveur et les journaux savent ce qu'ils reçoivent.
            var part = new ByteArrayContent(file.Data);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MediaTypeOf(file.FileName));
            content.Add(part, file.Field, file.FileName);
        }
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    private static string MediaTypeOf(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".db" or ".sqlite" => "application/vnd.sqlite3",
        _ => "application/octet-stream",
    };

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

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body ?? new { }, options: Json.Options);
            request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
            if (CurrentLocks.Value is { } locks) request.Headers.Add("X-Scolia-Verrou", locks);
        }
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            SetOnline(true);
            return response;
        }
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

/// <summary>Ordinateur principal injoignable pour une action qui le demande : le message dit quoi faire.</summary>
public sealed class OfflineException(string message) : ApiException(message, 0);

/// <summary>Écriture gardée sur l'appareil pendant une coupure : ce n'est pas une erreur, l'envoi suivra.</summary>
public sealed class OfflineQueuedException(string message) : Exception(message);

/// <summary>Erreur renvoyée par le serveur (format RFC 9457), message lisible en français.</summary>
public class ApiException(string message, int status = 0) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; init; }
    public int? RetryAfterSeconds { get; init; }
}
