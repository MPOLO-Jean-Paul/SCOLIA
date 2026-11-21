using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Scolia.Domain.Cloud;

namespace Scolia.Bureau.Api;

/// <summary>
/// Relève en ligne : quand l'ordinateur principal ne répond pas, chaque opération gardée sur l'appareil
/// est aussi déposée, chiffrée pour le serveur, dans la boîte de l'établissement (console JP-Services).
/// L'opération est alors en sécurité même si l'appareil est perdu ; le serveur l'applique à son retour et
/// l'appareil, informé de l'issue, ne la renvoie pas une seconde fois.
/// </summary>
public sealed partial class ScoliaClient
{
    private static readonly HttpClient CloudHttp = new(new SocketsHttpHandler
    {
        UseCookies = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(8),
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    private readonly SemaphoreSlim depositGate = new(1, 1);

    // La sonde d'un poste Windows peut rester active alors que son ordinateur
    // principal est éteint. Retenter le dépôt cloud sans monopoliser le réseau,
    // ni déclencher un dépôt à chaque battement de l'interface.
    private long lastCloudProbeDepositTicks;

    private void RetryCloudDepositWhileHostUnavailable()
    {
        if (Offline is not { User.Length: > 0 } store) return;
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref lastCloudProbeDepositTicks);
        if (now - previous < TimeSpan.FromMinutes(2).Ticks ||
            Interlocked.CompareExchange(ref lastCloudProbeDepositTicks, now, previous) != previous)
            return;
        if (store.Outbox().Any(item => !item.Deposited))
            DepositSoon();
    }

    /// <summary>Dépôt en arrière-plan juste après la mise en file : la saisie n'attend jamais Internet.</summary>
    private void DepositSoon()
    {
        // Ne pas démarrer de travail de fond lorsque ce compte ne possède
        // pas d'autorisation cloud utilisable. Cela évite les lectures différées
        // d'une base locale qui pourrait déjà avoir été fermée et accélère
        // les opérations hors ligne sans liaison cloud activée.
        var store = Offline;
        var user = store?.User ?? string.Empty;
        if (store is null || user.Length == 0 || !Usable(ReadAccess(store, user))) return;
        _ = Task.Run(async () =>
        {
            try { await DepositPendingAsync(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.WriteLine($"SCOLIA : dépôt en ligne différé ({ex.GetType().Name}).");
            }
        });
    }

    /// <summary>Nombre d'opérations de ce compte déjà en sécurité en ligne.</summary>
    public int DepositedCount => Offline is { } store && store.User.Length > 0 ? store.Outbox().Count(i => i.Deposited) : 0;

    /// <summary>Après une connexion au serveur : renouvelle l'accès de dépôt du compte (30 jours).</summary>
    public async Task RefreshCloudAccessAsync(CancellationToken ct = default)
    {
        var store = Offline;
        var user = store?.User ?? string.Empty;
        if (store is null || user.Length == 0 || !IsOnline) return;
        try
        {
            // Lecture directe, hors cache des pages : l'accès n'est gardé qu'à un seul endroit, chiffré.
            using var response = await http.GetAsync("api/nuage/depot", ct);
            if (!response.IsSuccessStatusCode) return;
            var access = await response.Content.ReadFromJsonAsync<CloudDepositAccess>(Json.Options, ct);
            if (access is null) return;
            await Task.Run(() => store.SaveCloudAccessFor(user, JsonSerializer.Serialize(access, Json.Options)), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
    }

    private static CloudDepositAccess? ReadAccess(Infrastructure.OfflineStore store, string user)
    {
        try
        {
            return store.ReadCloudAccessFor(user) is { Length: > 0 } text
                ? JsonSerializer.Deserialize<CloudDepositAccess>(text, Json.Options) : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool Usable(CloudDepositAccess? access) =>
        access is { Enabled: true, ServiceUrl.Length: > 0, DepositToken.Length: > 0, DepositKey.Length: > 0, Delegation.Length: > 0 }
        && (access.ExpiresUtc is null || access.ExpiresUtc > DateTime.UtcNow)
        && Uri.TryCreate(access.ServiceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Dépose, dans l'ordre, les opérations du compte pas encore en sécurité en ligne. S'arrête à la première
    /// difficulté pour ne jamais inverser l'ordre ; tout reste gardé sur l'appareil.
    /// </summary>
    public async Task<int> DepositPendingAsync(CancellationToken ct = default)
    {
        var store = Offline;
        var user = store?.User ?? string.Empty;
        if (store is null || user.Length == 0) return 0;
        var access = ReadAccess(store, user);
        if (!Usable(access)) return 0;
        if (!await depositGate.WaitAsync(0, ct)) return 0;
        try
        {
            var key = Convert.FromBase64String(access!.DepositKey!);
            var target = new Uri(new Uri(access.ServiceUrl!), "api/v1/device/mailbox");
            var deposited = 0;
            foreach (var item in store.OutboxFor(user).Where(i => !i.Deposited).OrderBy(i => i.CreatedAt))
            {
                var operation = new CloudOperation(item.Key, item.Method, item.Path, item.Body, item.Label,
                    access.Delegation!, item.CreatedAt.ToUniversalTime(), Infrastructure.DeviceIdentity.Id);
                var payload = Base64Url.EncodeToString(CloudSeal.Seal(JsonSerializer.SerializeToUtf8Bytes(operation, Json.Options), key, CloudSeal.OperationContext));
                using var request = new HttpRequestMessage(HttpMethod.Post, target)
                {
                    Content = JsonContent.Create(new
                    {
                        op_id = item.Key, device_id = Infrastructure.DeviceIdentity.Id,
                        sequence = new DateTimeOffset(item.CreatedAt.ToUniversalTime()).ToUnixTimeMilliseconds(), payload,
                    }),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.DepositToken);
                using var response = await CloudHttp.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) break;
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                // Déjà appliquée par le serveur (dépôt précédent) : elle quitte la file.
                if (body.TryGetProperty("outcome", out var outcome) && outcome.GetString() == "applied")
                    await Task.Run(() => store.SentFor(user, item.Id), ct);
                else
                    await Task.Run(() => store.MarkDepositedFor(user, item.Id), ct);
                deposited++;
            }
            return deposited;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException
            or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException or InvalidDataException) { return 0; }
        finally { depositGate.Release(); }
    }

    /// <summary>
    /// Demande l'issue des opérations déposées : celles que le serveur a déjà appliquées quittent la file
    /// (aucun renvoi en double). Une opération refusée reste : son renvoi direct montrera le motif.
    /// </summary>
    public async Task<int> ReconcileCloudAsync(CancellationToken ct = default)
    {
        var store = Offline;
        var user = store?.User ?? string.Empty;
        if (store is null || user.Length == 0) return 0;
        var access = ReadAccess(store, user);
        if (!Usable(access)) return 0;
        try
        {
            var deposited = store.OutboxFor(user).Where(i => i.Deposited).Take(200).ToList();
            if (deposited.Count == 0) return 0;
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(access!.ServiceUrl!), "api/v1/device/mailbox/outcomes"))
            {
                Content = JsonContent.Create(new { op_ids = deposited.Select(i => i.Key).Distinct().ToArray() }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access.DepositToken);
            using var response = await CloudHttp.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.OK) return 0;
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var outcomes = body.GetProperty("outcomes");
            var done = 0;
            foreach (var item in deposited)
                if (outcomes.TryGetProperty(item.Key, out var value) && value.GetString() == "applied")
                {
                    await Task.Run(() => store.SentFor(user, item.Id), ct);
                    done++;
                }
            return done;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
            or KeyNotFoundException or InvalidDataException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Point d'entrée partagé par SCOLIA Windows et Android pour la future
    /// activation centrale. Nécessite une origine HTTPS approuvée, une
    /// invitation délivrée par le secrétariat et le mot de passe du titulaire.
    /// La confirmation n'est PAS une session et n'autorise PAS de données.
    /// </summary>
    public async Task<CentralPairingResult> PairCentralDeviceAsync(
        Uri trustedCentralOrigin, Guid invitationId, string token,
        string userName, string password, CancellationToken ct = default)
    {
        using var central = new CentralPairingClient();
        return await central.PairAsync(trustedCentralOrigin, invitationId,
            token, userName, password, Infrastructure.DeviceIdentity.Id, ct);
    }

    /// <summary>
    /// Consulte les métadonnées D1 depuis Windows ou Android. Cette route ne
    /// contient pas encore les données scolaires et n'avance aucun checkpoint.
    /// Le JWT fourni doit être une session centrale et NON le JWT de jumelage.
    /// </summary>
    public async Task<CentralJournalPage> ReadCentralJournalPreviewAsync(
        Uri trustedCentralOrigin, string centralSessionJwt,
        long afterRevision = 0, CancellationToken ct = default)
    {
        using var journal = new CentralJournalClient();
        return await journal.ReadMetadataAsync(trustedCentralOrigin,
            centralSessionJwt, afterRevision, ct: ct);
    }

    /// <summary>Session de 15 minutes obtenue par la clé de l'appareil déjà
    /// appairé, sans stocker de JWT ni réenvoyer un mot de passe.</summary>
    public async Task<CentralSession> OpenCentralSessionAsync(
        Uri trustedCentralOrigin, string schoolId, string userName,
        CancellationToken ct = default)
    {
        using var central = new CentralPairingClient();
        return await central.OpenSessionAsync(trustedCentralOrigin, schoolId,
            userName, Infrastructure.DeviceIdentity.Id, ct);
    }

    /// <summary>
    /// Parcours complet d'identité native : ouvrir une session par ECDSA,
    /// puis lire UNIQUEMENT le journal informatif D1. Aucun accusé de
    /// réception ni écriture scolaire n'est exécuté.
    /// </summary>
    public async Task<CentralJournalPage> CheckCentralJournalAsync(
        Uri trustedCentralOrigin, string schoolId, string userName,
        long afterRevision = 0, CancellationToken ct = default)
    {
        var session = await OpenCentralSessionAsync(
            trustedCentralOrigin, schoolId, userName, ct);
        return await ReadCentralJournalPreviewAsync(
            trustedCentralOrigin, session.AccessToken, afterRevision, ct);
    }

    /// <summary>Restaurer la session centrale à partir de l'école mémorisée
    /// sous une enveloppe protégée par le système, sans mot de passe.</summary>
    public async Task<CentralSession> OpenSavedCentralSessionAsync(
        Uri trustedCentralOrigin, string userName, CancellationToken ct = default)
    {
        using var central = new CentralPairingClient();
        return await central.OpenSavedSessionAsync(trustedCentralOrigin, userName,
            Infrastructure.DeviceIdentity.Id, ct);
    }

    /// <summary>Enregistre une modification de section dans SQLite, sans
    /// aucune attente du réseau. La clé d'idempotence est créée une seule fois
    /// et reste persistante jusqu'à confirmation métier D1.</summary>
    public async Task<string> QueueCentralSectionAsync(
        Uri trustedCentralOrigin, string userName, Guid sectionId, string code,
        string name, bool active, CancellationToken ct = default)
    {
        if (Offline is not { } store || store.User != userName)
            throw new InvalidOperationException("Le cache du compte connecté n'est pas disponible.");
        using var central = new CentralPairingClient();
        var schoolId = central.ReadSavedSchoolId(trustedCentralOrigin,
            userName, Infrastructure.DeviceIdentity.Id) ??
            throw new InvalidOperationException("Cet appareil n'est pas jumelé à l'école centrale.");
        using var replica = new CentralSectionReplica(store, userName, schoolId, trustedCentralOrigin);
        var operation = await replica.QueueUpsertAsync(sectionId, code, name, active, ct);
        // Une écriture locale est terminée avant tout déclenchement réseau.
        // Sur Android, WorkManager reprend la file lorsque le réseau revient.
        try { Infrastructure.Platform.RequestBackgroundSync?.Invoke(); }
        catch (Exception ex) when (ex is InvalidOperationException or
            PlatformNotSupportedException )
        { System.Diagnostics.Trace.WriteLine("Déclenchement différé indisponible."); }
        return operation;
    }

    /// <summary>
    /// Synchronisation facultative de l'origine réellement approuvée par le
    /// titulaire et ce poste. Fonctionne avec D1 même si l'ancien serveur PC
    /// ne répond plus ; ne synchronise que les sections du pilote.
    /// Null signifie qu'aucun jumelage central n'a été activé.
    /// </summary>
    public async Task<CentralSectionLocalState?> SyncActiveCentralSectionsAsync(
        string userName, CancellationToken ct = default)
    {
        if (Offline is not { } store ||
            !string.Equals(store.User, userName, StringComparison.Ordinal))
            throw new InvalidOperationException("Session locale différente du compte demandé.");
        using var central = new CentralPairingClient();
        var origin = central.ReadActiveOrigin(userName, Infrastructure.DeviceIdentity.Id);
        if (origin is null) return null;
        return await SyncCentralSectionsAsync(origin, userName, ct);
    }

    /// <summary>
    /// Reprend les sections mises en attente puis leur journal, sans mot de
    /// passe ni clé stockée dans D1. Une coupure réseau conserve SQLite.
    /// Seules les sections/option sont couvertes : les autres fonctionnalités
    /// scolaires et financières conservent leur circuit serveur existant.
    /// </summary>
    public async Task<CentralSectionLocalState> SyncCentralSectionsAsync(
        Uri trustedCentralOrigin, string userName, CancellationToken ct = default)
    {
        if (Offline is not { } store || store.User != userName)
            throw new InvalidOperationException("Le cache du compte connecté n'est pas disponible.");
        using var central = new CentralPairingClient();
        var schoolId = central.ReadSavedSchoolId(trustedCentralOrigin,
            userName, Infrastructure.DeviceIdentity.Id) ??
            throw new InvalidOperationException("Cet appareil n'est pas jumelé à l'école centrale.");
        using var replica = new CentralSectionReplica(store, userName, schoolId, trustedCentralOrigin);
        try
        {
            var session = await central.OpenSavedSessionAsync(
                trustedCentralOrigin, userName, Infrastructure.DeviceIdentity.Id, ct);
            return await replica.CatchUpAsync(trustedCentralOrigin, session.AccessToken, ct: ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return replica.Local(); // réseau absent : état SQLite inchangé.
        }
    }
}
