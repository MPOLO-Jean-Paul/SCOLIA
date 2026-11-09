using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scolia.Api.Security;
using Scolia.Domain.Cloud;
using Scolia.Domain.Common;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Backup;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Api.Cloud;

/// <summary>Opération reçue en ligne et refusée par le serveur : gardée pour le responsable, jamais perdue en silence.</summary>
public sealed record CloudRefusal(DateTimeOffset At, string Label, string Path, string User, string Reason);

/// <summary>État de la continuité en ligne, sans aucune clé.</summary>
public sealed record CloudStatus(bool Registered, bool Enabled, bool RecoveryConfirmed, string? KeyFingerprint, DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastBackupAt, long LastBackupBytes, DateTimeOffset? LastDrainAt, int Applied, int Refused,
    string? LastError, DateTimeOffset? LastErrorAt, IReadOnlyList<CloudRefusal> Refusals, bool ActivatedByOwner = false);

/// <summary>Copie en ligne vue par le serveur ; <c>SameKey</c> faux : il faut le code de secours de l'école.</summary>
public sealed record CloudBackupDto(Guid Id, DateTimeOffset CreatedAt, long SizeBytes, string KeyFingerprint, bool SameKey);

/// <summary>Jumelage de l'ordinateur principal avec le D1 scolaire : seulement un état
/// technique. Les paiements et les comptes ne sont jamais validés dans D1.</summary>
public sealed record CloudMirrorPairStatus(bool Paired, bool CloudAuthoritative);


/// <summary>
/// Continuité en ligne du serveur scolaire, entièrement automatique une fois activée par le responsable :
/// <list type="bullet">
/// <item>sauvegarde : toutes les heures si des données ont changé (et au moins une fois par jour), une copie
/// cohérente de la base est compressée, chiffrée avec la clé de l'école et envoyée à la console JP-Services (R2) ;</item>
/// <item>relève : toutes les deux minutes, le serveur relève la boîte de dépôt où les appareils ont laissé les
/// opérations faites pendant sa coupure, les applique avec ses propres règles et publie leur issue.</item>
/// </list>
/// La clé ne quitte jamais l'école ; le code de secours permet de restaurer sur un nouvel ordinateur.
/// </summary>
public sealed partial class CloudContinuityService : BackgroundService
{
    public static readonly TimeSpan Cycle = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BackupEvery = TimeSpan.FromHours(1);
    public static readonly TimeSpan BackupAtLeast = TimeSpan.FromHours(24);
    public static readonly TimeSpan DelegationLifetime = TimeSpan.FromDays(30);
    public const long MaxUploadBytes = 90L * 1024 * 1024;
    public const long MaxDatabaseBytes = 1024L * 1024 * 1024;
    private const int MaxRefusals = 100;
    private const int MailboxPage = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record State(bool Enabled, string? DataKey, string? DepositKey, string? DepositToken, DateTimeOffset? ActivatedAt,
        bool RecoveryConfirmed, DateTimeOffset? LastBackupAt, long LastBackupRevision, string? LastBackupEpoch, long LastBackupBytes,
        DateTimeOffset? LastDrainAt, int Applied, int Refused, string? LastError, DateTimeOffset? LastErrorAt, List<CloudRefusal> Refusals,
        bool ActivatedByOwner = false, bool DeclinedBySchool = false,
        bool MirrorPaired = false, string? LastCloudBackupId = null,
        string? LastCloudBackupSha256 = null, string? MirroredCloudBackupId = null,
        string? PendingMirrorBackupId = null, string? PendingMirrorPayload = null,
        long LastRemoteMirrorCursor = 0);

    private sealed record Delegation(Guid U, string S, long E);

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IDataProtector protector;
    private readonly IDataProtector delegations;
    private readonly SchoolLicenseService license;
    private readonly BackupService backups;
    private readonly DataRevision revision;
    private readonly CloudDispatcher dispatcher;
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<CloudContinuityService> logger;
    private readonly string path;
    private readonly bool corrupted;
    private State state;
    private volatile bool backupRequested;

    public CloudContinuityService(IOptions<ScoliaStorageOptions> options, IDataProtectionProvider provider, SchoolLicenseService license,
        BackupService backups, DataRevision revision, CloudDispatcher dispatcher, IServiceScopeFactory scopes, ILogger<CloudContinuityService> logger)
    {
        this.license = license;
        this.backups = backups;
        this.revision = revision;
        this.dispatcher = dispatcher;
        this.scopes = scopes;
        this.logger = logger;
        protector = provider.CreateProtector("SCOLIA.Pro.Nuage.Etat.v1");
        delegations = provider.CreateProtector("SCOLIA.Pro.Nuage.Delegation.v1");
        Directory.CreateDirectory(options.Value.DataDirectory);
        path = Path.Combine(options.Value.DataDirectory, "nuage.sec");
        var empty = new State(false, null, null, null, null, false, null, -1, null, 0, null, 0, 0, null, null, []);
        try
        {
            state = File.Exists(path)
                ? JsonSerializer.Deserialize<State>(protector.Unprotect(File.ReadAllBytes(path)), Json) ?? empty
                : empty;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException)
        {
            // Clés du poste perdues : on n'écrase rien ; la reprise passe par le code de secours.
            logger.LogError(ex, "État de la sauvegarde en ligne illisible : réactivation ou code de secours nécessaire.");
            corrupted = true;
            state = empty;
        }
    }

    public CloudStatus Status
    {
        get
        {
            var s = state;
            return new CloudStatus(license.IsRegistered, s.Enabled && !corrupted, s.RecoveryConfirmed,
                s.DataKey is { } key ? CloudSeal.Fingerprint(Convert.FromBase64String(key)) : null, s.ActivatedAt,
                s.LastBackupAt, s.LastBackupBytes, s.LastDrainAt, s.Applied, s.Refused, s.LastError, s.LastErrorAt, s.Refusals,
                s.ActivatedByOwner);
        }
    }

    private void Save(State next)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(next, Json)));
            File.Move(temp, path, true);
            state = next;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void Fail(string message)
    {
        logger.LogWarning("Continuité en ligne : {Message}", message);
        try { Save(state with { LastError = message, LastErrorAt = DateTimeOffset.UtcNow }); }
        catch (IOException) { }
    }

    private static string Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ---------- Activation par le responsable (une seule fois) ----------

    /// <param name="byOwner">Vrai : décision de JP-Services reçue au contrôle de licence ; faux : décision du responsable de l'école,
    /// qui prime toujours (un arrêt par l'école n'est jamais annulé par la console).</param>
    public async Task<CloudStatus> EnableAsync(bool enabled, bool byOwner, CancellationToken ct)
    {
        if (!license.IsRegistered)
            throw new DomainException("nuage", "Inscrivez d'abord ce serveur auprès de JP-Services (Licence) : la sauvegarde en ligne passe par sa console.");
        await gate.WaitAsync(ct);
        try
        {
            if (!enabled)
            {
                using var off = license.OwnerClient(TimeSpan.FromSeconds(30));
                using var stopped = await off.PostAsJsonAsync("api/v1/server/cloud/enable", new { enabled = false, disclosed_to_school_admin = true }, ct);
                await EnsureSuccessAsync(stopped, ct);
                Save(state with
                {
                    Enabled = false, DepositToken = null, ActivatedByOwner = false,
                    DeclinedBySchool = !byOwner || state.DeclinedBySchool,
                });
                return Status;
            }
            // Une réactivation garde la clé : les anciennes copies restent lisibles avec le même code de secours.
            var dataKey = state.DataKey ?? Convert.ToBase64String(CloudSeal.NewKey());
            var depositKey = state.DepositKey ?? Convert.ToBase64String(CloudSeal.NewKey());
            using var client = license.OwnerClient(TimeSpan.FromSeconds(30));
            using var response = await client.PostAsJsonAsync("api/v1/server/cloud/enable", new
            {
                enabled = true, disclosed_to_school_admin = true,
                key_fingerprint = CloudSeal.Fingerprint(Convert.FromBase64String(dataKey)),
            }, ct);
            await EnsureSuccessAsync(response, ct);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            var token = body.TryGetProperty("deposit_token", out var t) ? t.GetString() : null;
            if (token is not { Length: >= 32 }) throw new DomainException("nuage", "Réponse de la console JP-Services incomplète : réessayez.");
            Save(state with
            {
                Enabled = true, DataKey = dataKey, DepositKey = depositKey, DepositToken = token,
                ActivatedAt = state.ActivatedAt ?? DateTimeOffset.UtcNow, LastError = null, LastErrorAt = null,
                ActivatedByOwner = byOwner, DeclinedBySchool = false,
            });
            backupRequested = true;
            return Status;
        }
        finally { gate.Release(); }
    }

    /// <summary>Code de secours de l'école (affiché au responsable, à noter et ranger hors de l'ordinateur).</summary>
    public string RecoveryCode()
    {
        if (state.DataKey is not { } key) throw new DomainException("nuage", "Activez d'abord la sauvegarde en ligne.");
        return CloudSeal.RecoveryCode(Convert.FromBase64String(key));
    }

    public CloudStatus ConfirmRecovery()
    {
        if (state.DataKey is null) throw new DomainException("nuage", "Activez d'abord la sauvegarde en ligne.");
        Save(state with { RecoveryConfirmed = true });
        return Status;
    }

    /// <summary>
    /// Appairage opt-in du serveur physique au journal D1 scolaire séparé.
    /// Le serveur garde la clé de l'école ; seule son empreinte est transmise.
    /// Cette étape ne copie aucune table ni aucune identité personnelle.
    /// </summary>
    public async Task<CloudMirrorPairStatus> PairD1Async(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = state;
            if (!license.IsRegistered || corrupted || !current.Enabled || current.DataKey is null)
                throw new DomainException("nuage", "Activez d'abord la continuité chiffrée de cet établissement.");
            if (!current.RecoveryConfirmed)
                throw new DomainException("nuage", "Confirmez d'abord la conservation du code de secours de l'école.");
            using var client = license.OwnerClient(TimeSpan.FromSeconds(30));
            using var response = await client.PostAsJsonAsync("api/v1/server/cloud/mirror/pair", new
            {
                installation_hash = license.InstallationFingerprint,
                key_fingerprint = CloudSeal.Fingerprint(Convert.FromBase64String(current.DataKey)),
                consent_confirmed = true,
            }, ct);
            await EnsureSuccessAsync(response, ct);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (!body.TryGetProperty("paired", out var paired) || paired.ValueKind != JsonValueKind.True)
                throw new DomainException("nuage", "Jumelage D1 non confirmé par Cloudflare.");
            Save(state with { MirrorPaired = true });
            backupRequested = true;
            return new CloudMirrorPairStatus(true, false);
        }
        finally { gate.Release(); }
    }

    // ---------- Sauvegarde ----------

    private bool BackupDue(DateTimeOffset now)
    {
        var s = state;
        if (backupRequested || s.LastBackupAt is null) return true;
        var changed = s.LastBackupEpoch != revision.Epoch || s.LastBackupRevision != revision.Value;
        return (changed && now - s.LastBackupAt >= BackupEvery) || now - s.LastBackupAt >= BackupAtLeast;
    }

    /// <summary>Copie cohérente, compressée, chiffrée par l'école, envoyée et vérifiée par son empreinte.</summary>
    public async Task<CloudStatus> BackupNowAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var s = state;
            if (!s.Enabled || s.DataKey is null || corrupted) throw new DomainException("nuage", "La sauvegarde en ligne n'est pas activée.");
            var markRevision = revision.Value;
            var markEpoch = revision.Epoch;
            var snapshot = await backups.SnapshotAsync(ct);
            byte[] sealedBytes;
            try
            {
                var plain = await File.ReadAllBytesAsync(snapshot, ct);
                try
                {
                    var compressed = CloudSeal.Compress(plain);
                    sealedBytes = CloudSeal.Seal(compressed, Convert.FromBase64String(s.DataKey), CloudSeal.BackupContext);
                    CryptographicOperations.ZeroMemory(compressed);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            finally { File.Delete(snapshot); }
            if (sealedBytes.Length > MaxUploadBytes)
                throw new DomainException("nuage", "Base trop volumineuse pour l'envoi en ligne gratuit (90 Mo compressés).");
            var fingerprint = CloudSeal.Fingerprint(Convert.FromBase64String(s.DataKey));
            using var client = license.OwnerClient(TimeSpan.FromMinutes(10));
            using var content = new ByteArrayContent(sealedBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var checksum = Hex(sealedBytes);
            using var response = await client.PutAsync($"api/v1/server/cloud/backups?sha256={checksum}&key={fingerprint}", content, ct);
            await EnsureSuccessAsync(response, ct);
            var confirmation = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (!confirmation.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var backupId))
                throw new DomainException("nuage", "Sauvegarde R2 reçue, mais identité de sauvegarde absente.");
            backupRequested = false;
            Save(state with
            {
                LastBackupAt = DateTimeOffset.UtcNow, LastBackupRevision = markRevision, LastBackupEpoch = markEpoch,
                LastBackupBytes = sealedBytes.Length, LastError = null, LastErrorAt = null,
                LastCloudBackupId = backupId.ToString("D"), LastCloudBackupSha256 = checksum,
            });
            logger.LogInformation("Sauvegarde en ligne envoyée ({Size} octets chiffrés).", sealedBytes.Length);
            return Status;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<CloudBackupDto>> ListAsync(CancellationToken ct)
    {
        using var client = license.OwnerClient(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync("api/v1/server/cloud/backups", ct);
        await EnsureSuccessAsync(response, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var mine = state.DataKey is { } key ? CloudSeal.Fingerprint(Convert.FromBase64String(key)) : null;
        return body.GetProperty("backups").EnumerateArray().Select(b => new CloudBackupDto(
            Guid.Parse(b.GetProperty("id").GetString()!), DateTimeOffset.FromUnixTimeMilliseconds(b.GetProperty("created_at").GetInt64()),
            b.GetProperty("size_bytes").GetInt64(), b.GetProperty("key_fingerprint").GetString() ?? string.Empty,
            mine is not null && b.GetProperty("key_fingerprint").GetString() == mine)).ToList();
    }

    /// <summary>
    /// Ramène une copie en ligne parmi les sauvegardes locales (jamais de restauration automatique).
    /// Sur un nouvel ordinateur, le code de secours de l'école la déchiffre et devient la clé de ce serveur.
    /// </summary>
    public async Task<BackupInfo> RecoverAsync(Guid id, string? recoveryCode, CancellationToken ct)
    {
        using var client = license.OwnerClient(TimeSpan.FromMinutes(10));
        using var response = await client.GetAsync($"api/v1/server/cloud/backups/{id}", ct);
        await EnsureSuccessAsync(response, ct);
        var sealedBytes = await response.Content.ReadAsByteArrayAsync(ct);
        var expected = response.Headers.TryGetValues("X-Scolia-Sha256", out var hash) ? hash.FirstOrDefault() : null;
        if (expected is null || !string.Equals(expected, Hex(sealedBytes), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("nuage", "Copie en ligne incomplète ou altérée pendant le téléchargement : réessayez.");
        var fingerprint = response.Headers.TryGetValues("X-Scolia-Key", out var keys) ? keys.FirstOrDefault() : null;
        byte[] key;
        var fromCode = !string.IsNullOrWhiteSpace(recoveryCode);
        if (fromCode) key = CloudSeal.ParseRecoveryCode(recoveryCode);
        else if (state.DataKey is { } local) key = Convert.FromBase64String(local);
        else throw new DomainException("codeSecours", "Saisissez le code de secours de l'école pour déchiffrer cette copie.");
        if (fingerprint is not null && fingerprint != CloudSeal.Fingerprint(key))
            throw new DomainException("codeSecours", fromCode
                ? "Ce code de secours ne correspond pas à cette copie."
                : "Cette copie a été chiffrée avec une autre clé (ancien serveur) : saisissez le code de secours de l'école.");
        var compressed = CloudSeal.Open(sealedBytes, key, CloudSeal.BackupContext);
        var plain = CloudSeal.Decompress(compressed, MaxDatabaseBytes);
        try
        {
            var info = await backups.ImportPlainAsync(plain, "enligne", ct);
            // Nouveau serveur (sauvegarde en ligne pas encore active) : il adopte la clé de l'école,
            // pour que le même code de secours reste valable pour toutes les copies.
            if (fromCode && !state.Enabled)
            {
                await gate.WaitAsync(ct);
                try { Save(state with { DataKey = Convert.ToBase64String(key), RecoveryConfirmed = true }); }
                finally { gate.Release(); }
            }
            return info;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(compressed);
        }
    }

    // ---------- Relève : opérations déposées pendant une coupure ----------

    /// <summary>Accès de dépôt pour un compte connecté : jeton de dépôt, clé de dépôt et délégation signée (30 jours).</summary>
    public CloudDepositAccess DepositAccess(AppUser user)
    {
        var s = state;
        if (!s.Enabled || corrupted || s.DepositKey is null || s.DepositToken is null || license.OwnerServiceUrl is null)
            return new CloudDepositAccess(false, null, null, null, null, null);
        var expires = DateTimeOffset.UtcNow + DelegationLifetime;
        var proof = delegations.Protect(JsonSerializer.SerializeToUtf8Bytes(new Delegation(user.Id, user.SecurityStamp, expires.ToUnixTimeSeconds()), Json));
        return new CloudDepositAccess(true, license.OwnerServiceUrl, s.DepositToken, s.DepositKey, Base64Url.EncodeToString(proof), expires.UtcDateTime);
    }

    /// <summary>
    /// Relève la boîte de dépôt et applique chaque opération comme si son auteur l'envoyait lui-même :
    /// mêmes droits, mêmes règles, même clé d'idempotence (aucun doublon quand l'appareil la renverra).
    /// S'arrête sans rien perdre si le serveur ne peut pas écrire (licence, panne) : la boîte reste pleine.
    /// </summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var s = state;
            if (!s.Enabled || s.DepositKey is null || corrupted) return 0;
            var depositKey = Convert.FromBase64String(s.DepositKey);
            var applied = 0;
            using var client = license.OwnerClient(TimeSpan.FromSeconds(30));
            for (var page = 0; page < 20; page++)
            {
                using var response = await client.GetAsync($"api/v1/server/cloud/mailbox?limit={MailboxPage}", ct);
                await EnsureSuccessAsync(response, ct);
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                var operations = body.GetProperty("operations").EnumerateArray().ToList();
                var outcomes = new List<object>();
                var refusals = new List<CloudRefusal>();
                var stop = false;
                foreach (var item in operations)
                {
                    var opId = item.GetProperty("op_id").GetString()!;
                    var (outcome, refusal) = await ApplyAsync(item.GetProperty("payload").GetString() ?? string.Empty, opId, depositKey, ct);
                    if (outcome is null) { stop = true; break; }
                    outcomes.Add(new { op_id = opId, outcome });
                    if (refusal is not null) refusals.Add(refusal);
                    else applied++;
                }
                if (outcomes.Count > 0)
                {
                    using var ack = await client.PostAsJsonAsync("api/v1/server/cloud/mailbox/ack", new { outcomes }, ct);
                    await EnsureSuccessAsync(ack, ct);
                }
                var current = state;
                Save(current with
                {
                    LastDrainAt = DateTimeOffset.UtcNow,
                    Applied = current.Applied + outcomes.Count - refusals.Count,
                    Refused = current.Refused + refusals.Count,
                    Refusals = [.. refusals.Concat(current.Refusals).Take(MaxRefusals)],
                });
                if (stop || operations.Count < MailboxPage) break;
            }
            return applied;
        }
        finally { gate.Release(); }
    }

    /// <summary>Issue d'une opération : « applied », « refused » (avec motif) ou null (réessayer plus tard).</summary>
    private async Task<(string? Outcome, CloudRefusal? Refusal)> ApplyAsync(string payload, string opId, byte[] depositKey, CancellationToken ct)
    {
        CloudOperation? operation;
        try
        {
            var clear = CloudSeal.Open(Base64Url.DecodeFromChars(payload), depositKey, CloudSeal.OperationContext);
            operation = JsonSerializer.Deserialize<CloudOperation>(clear, Json);
        }
        catch (Exception ex) when (ex is DomainException or FormatException or JsonException)
        {
            return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, "Opération illisible", "—", "—", "Chiffrement ou format invalide : ignorée."));
        }
        if (operation is null || operation.Key != opId)
            return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, "Opération incohérente", "—", "—", "Identifiant différent de celui du dépôt : ignorée."));
        var label = string.IsNullOrWhiteSpace(operation.Label) ? "Opération" : operation.Label[..Math.Min(80, operation.Label.Length)];
        if (!CloudOperationRules.Queueable(operation.Method, operation.Path))
            return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, label, operation.Path, "—", "Cette opération ne peut pas être faite hors ligne."));

        Guid userId;
        string userName;
        try
        {
            var proof = JsonSerializer.Deserialize<Delegation>(delegations.Unprotect(Base64Url.DecodeFromChars(operation.Delegation)), Json)
                ?? throw new JsonException();
            if (proof.E < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, label, operation.Path, "—", "Autorisation de l'appareil expirée : l'opération repartira de l'appareil à sa reconnexion."));
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == proof.U, ct);
            if (user is null || !user.IsActive || user.SecurityStamp != proof.S)
                return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, label, operation.Path, user?.UserName ?? "—",
                    "Compte désactivé ou modifié depuis (mot de passe, rôle) : opération non appliquée."));
            userId = user.Id;
            userName = user.UserName;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, label, operation.Path, "—", "Autorisation invalide : opération ignorée."));
        }

        try
        {
            var (status, message) = await dispatcher.SendAsync(userId, operation, ct);
            if (status is >= 200 and < 300) return ("applied", null);
            if (status == (int)HttpStatusCode.Locked || status >= 500) return (null, null);
            return ("refused", new CloudRefusal(DateTimeOffset.UtcNow, label, operation.Path, userName, message));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    // ---------- Boucle automatique ----------

    /// <summary>
    /// Un cycle complet, sans intervention : suit la décision de JP-Services (activation proposée depuis la console,
    /// jamais contre un arrêt décidé par l'école), relève les opérations en ligne, puis sauvegarde si c'est l'heure.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (!license.IsRegistered || corrupted) return;
        var current = state;
        try
        {
            if (license.CloudRequested && !current.Enabled && !current.DeclinedBySchool)
            {
                await EnableAsync(true, byOwner: true, ct);
                logger.LogInformation("Sauvegarde en ligne activée à la demande de JP-Services (clé de l'école générée sur ce serveur).");
            }
            else if (!license.CloudRequested && current.Enabled && current.ActivatedByOwner)
                await EnableAsync(false, byOwner: true, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or JsonException or IOException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) return;
            Fail("activation différée (" + Describe(ex) + ").");
            return;
        }
        if (!state.Enabled) return;
        try { await DrainAsync(ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or JsonException or IOException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) return;
            Fail("relève différée (" + Describe(ex) + ").");
        }
        if (BackupDue(DateTimeOffset.UtcNow))
        {
            try { await BackupNowAsync(ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or JsonException or IOException or InvalidOperationException
                or Microsoft.Data.Sqlite.SqliteException)
            {
                if (ct.IsCancellationRequested) return;
                Fail("sauvegarde différée (" + Describe(ex) + "), nouvel essai automatique.");
            }
        }
        // D1 conserve un journal chiffré des versions de la base sauvegardées dans R2.
        // La relève des écritures hors ligne vers SQLite est réalisée ci-dessus.
        if (state.MirrorPaired)
        {
            try { await SyncMirrorAsync(ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DomainException or JsonException or IOException or InvalidOperationException or CryptographicException)
            {
                if (ct.IsCancellationRequested) return;
                Fail("journal D1 différé (" + Describe(ex) + "), nouvel essai automatique.");
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Cycle);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException or TaskCanceledException => "Internet ou console JP-Services injoignable",
        DomainException d => d.Message,
        _ => ex.GetType().Name,
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? detail = null;
        try { detail = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct)).GetProperty("error").GetString(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NotSupportedException) { }
        throw new DomainException("nuage", detail ?? $"Console JP-Services : refus {(int)response.StatusCode}.");
    }
}
