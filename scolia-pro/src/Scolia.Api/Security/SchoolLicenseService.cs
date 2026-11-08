using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Scolia.Infrastructure;

namespace Scolia.Api.Security;

/// <summary>État commercial du serveur SCOLIA. Jamais de données scolaires
/// ni d'identités individuelles dans le service propriétaire distant.</summary>
public sealed class SchoolLicenseService : BackgroundService
{
    public const string ConsentVersion = "2026-11-08";
    public const int MaximumReportQueue = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IDataProtector protector;
    private readonly ILogger<SchoolLicenseService> logger;
    private readonly string path;
    private readonly string? configuredOwnerUrl;
    private StoredLicense state;
    private bool corrupted;

    private sealed record Registration(string ServiceUrl, string SchoolId, string Token,
        string SchoolName, string DeviceName, DateTimeOffset AcceptedAt);
    private sealed record ReportEvent(string EventId, string Event, string InstallationId,
        string ServerDeviceName, DateTimeOffset OccurredAt);
    private sealed record StoredLicense(string InstallationId, Registration? Registration,
        LicensePolicy? Policy, DateTimeOffset? LastSuccessAt, List<ReportEvent> Outbox);
    private sealed record RegistrationResponse(
        [property: JsonPropertyName("school_id")] string SchoolId,
        [property: JsonPropertyName("school_token")] string SchoolToken);
    public sealed record LicensePolicy(
        [property: JsonPropertyName("license_status")] string LicenseStatus,
        [property: JsonPropertyName("access_mode")] string AccessMode,
        [property: JsonPropertyName("readonly")] bool ReadOnly,
        [property: JsonPropertyName("billing_enabled")] bool BillingEnabled,
        [property: JsonPropertyName("days_remaining")] int? DaysRemaining,
        [property: JsonPropertyName("expires_at")] long? ExpiresAt,
        [property: JsonPropertyName("issued_at")] long IssuedAt,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("offline_grace_seconds")] int OfflineGraceSeconds,
        [property: JsonPropertyName("next_check_seconds")] int NextCheckSeconds);
    public sealed record Status(bool Managed, bool OwnerConfigured, bool ReadOnly,
        string LicenseStatus, int? DaysRemaining, long? ExpiresAt,
        string Reason, bool BillingEnabled, DateTimeOffset? LastVerifiedAt);

    public SchoolLicenseService(IOptions<ScoliaStorageOptions> options,
        IDataProtectionProvider provider, IConfiguration configuration,
        ILogger<SchoolLicenseService> logger)
    {
        this.logger = logger;
        protector = provider.CreateProtector("SCOLIA.Pro.OwnerLicensing.v1");
        var directory = options.Value.DataDirectory;
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "proprietaire-licence.sec");
        var configured = configuration["Scolia:Licence:ServiceUrl"]?.Trim();
        configuredOwnerUrl = IsSecureServiceUrl(configured) ? new Uri(configured!).ToString() : null;
        try
        {
            state = File.Exists(path)
                ? JsonSerializer.Deserialize<StoredLicense>(
                    protector.Unprotect(File.ReadAllBytes(path)), JsonOptions)
                    ?? throw new InvalidDataException("État de licence manquant.")
                : new StoredLicense(Guid.NewGuid().ToString("N"), null, null, null, []);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException
            or JsonException or InvalidDataException)
        {
            logger.LogError(ex, "État de licence SCOLIA inaccessible : protection des données en lecture seule.");
            corrupted = true;
            state = new StoredLicense(Guid.NewGuid().ToString("N"), null, null, null, []);
        }
    }

    public static bool IsSecureServiceUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback ||
            uri.UserInfo.Length > 0 || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            return false;
        return uri.Host.Contains('.');
    }

    /// <summary>Le serveur scolaire, pas le poste client, fait appliquer la lecture seule.</summary>
    public Status Current
    {
        get
        {
            if (corrupted) return new Status(true, configuredOwnerUrl is not null, true,
                "verification_requise", 0, null, "licence_locale_inaccessible", true, null);
            var enrollment = state.Registration;
            if (enrollment is null)
                return new Status(false, configuredOwnerUrl is not null, false, "non_activee",
                    null, null, "facturation_inactive", false, null);
            var policy = state.Policy;
            if (policy is null)
                return new Status(true, true, false, "verification_en_attente",
                    null, null, "connexion_centrale_en_attente", false, null);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var grace = Math.Clamp(policy.OfflineGraceSeconds, 0, 7 * 86400) * 1000L;
            var previouslyAllowed = policy.ReadOnly == false && policy.BillingEnabled &&
                policy.ExpiresAt is { } end && end + grace < now;
            var locked = policy.ReadOnly || previouslyAllowed;
            var days = policy.ExpiresAt is { } endDate
                ? (int)Math.Max(0, Math.Ceiling((endDate - now) / 86_400_000d))
                : policy.DaysRemaining;
            return new Status(true, true, locked,
                locked && !policy.ReadOnly ? "expired" : policy.LicenseStatus,
                days, policy.ExpiresAt, locked && !policy.ReadOnly
                    ? "grace_hors_ligne_terminee" : policy.Reason,
                policy.BillingEnabled, state.LastSuccessAt);
        }
    }

    private void Save()
    {
        if (corrupted) throw new InvalidOperationException(
            "La licence protégée est illisible ; restaurer les clés du serveur avant de modifier les licences.");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions)));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static HttpClient Http(string url, string? token = null)
    {
        if (!IsSecureServiceUrl(url)) throw new InvalidOperationException("Fournisseur HTTPS invalide.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(12) };
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Enregistrement demandé explicitement par le responsable du serveur
    /// depuis l'écran Licence, avec une invitation JP-Services à usage unique.</summary>
    public async Task<Status> EnrollAsync(string schoolName, string invitation,
        bool consent, CancellationToken ct)
    {
        if (!consent) throw new InvalidOperationException("Accord explicite de l'école requis.");
        if (configuredOwnerUrl is null) throw new InvalidOperationException(
            "Le service de licences JP-Services n'est pas encore configuré dans la distribution officielle.");
        if (schoolName.Trim().Length < 3 || invitation.Trim().Length < 20)
            throw new ArgumentException("Nom d'école ou invitation insuffisante.");
        await gate.WaitAsync(ct);
        try
        {
            if (state.Registration is not null)
                throw new InvalidOperationException("Ce serveur possède déjà une licence.");
            using var client = Http(configuredOwnerUrl);
            var payload = new
            {
                consent = true, consent_version = ConsentVersion,
                school_name = schoolName.Trim(), server_device_name = Environment.MachineName,
                installation_id = state.InstallationId, invite_code = invitation.Trim()
            };
            using var response = await client.PostAsJsonAsync("api/v1/register", payload, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>(JsonOptions, ct);
            if (body is null || !Guid.TryParse(body.SchoolId, out _) || body.SchoolToken.Length < 32)
                throw new InvalidDataException("Réponse d'inscription du propriétaire invalide.");
            state = state with { Registration = new Registration(configuredOwnerUrl,
                body.SchoolId, body.SchoolToken, schoolName.Trim(),
                Environment.MachineName, DateTimeOffset.UtcNow) };
            Save(); // Le token reste chiffré sur le serveur scolaire ; jamais côté client.
        }
        finally { gate.Release(); }
        await RefreshAsync(ct);
        return Current;
    }

    /// <summary>File persistante et idempotente, indépendante du partage Internet.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            var registration = state.Registration;
            if (registration is null || corrupted) return;
            var pending = state.Outbox.ToList();
            pending.Add(new ReportEvent(Guid.NewGuid().ToString(), "online", state.InstallationId,
                registration.DeviceName, DateTimeOffset.UtcNow));
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
            pending = pending.Where(e => e.OccurredAt >= cutoff).TakeLast(MaximumReportQueue).ToList();
            state = state with { Outbox = pending };
            Save();
            using var client = Http(registration.ServiceUrl, registration.Token);
            foreach (var item in pending.ToArray())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var response = await client.PostAsJsonAsync("api/v1/heartbeat", new
                    {
                        event_id = item.EventId,
                        @event = item.Event,
                        installation_id = item.InstallationId,
                        server_device_name = item.ServerDeviceName,
                        occurred_at = item.OccurredAt
                    }, ct);
                    if (!response.IsSuccessStatusCode) return;
                    var license = await response.Content.ReadFromJsonAsync<LicensePolicy>(JsonOptions, ct);
                    if (license is not null && license.AccessMode is "full" or "read_only")
                    {
                        // Seule la réponse authentifiée du service propriétaire définit le mode.
                        state = state with { Policy = license, LastSuccessAt = DateTimeOffset.UtcNow };
                    }
                    state.Outbox.Remove(item);
                    Save(); // Marquer après succès, jamais avant.
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
                {
                    logger.LogWarning("Suivi des licences différé : nouvelle tentative automatique au retour d'Internet.");
                    return;
                }
            }
        }
        finally { gate.Release(); }
    }

    public async Task<JsonElement> RelayAsync(CancellationToken ct = default) =>
        await OwnerRequestAsync(HttpMethod.Get, "api/v1/server/relay-settings", null, ct);

    public async Task<JsonElement> RelayConsentAsync(bool enabled, CancellationToken ct) =>
        await OwnerRequestAsync(HttpMethod.Post, "api/v1/server/relay-consent", new
        { enabled, disclosed_to_school_admin = true }, ct);

    public async Task<JsonElement> RelayApproveAsync(long revision, CancellationToken ct) =>
        await OwnerRequestAsync(HttpMethod.Post, "api/v1/server/relay-approve", new
        { relay_revision = revision, confirm = true, disclosed_to_school_admin = true }, ct);

    private async Task<JsonElement> OwnerRequestAsync(HttpMethod method, string resource,
        object? payload, CancellationToken ct)
    {
        var registration = state.Registration ?? throw new InvalidOperationException("Enregistrement préalable requis.");
        using var client = Http(registration.ServiceUrl, registration.Token);
        using var request = new HttpRequestMessage(method, resource);
        if (payload is not null) request.Content = JsonContent.Create(payload);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Le service démarre même en l'absence de session Windows/Android ouverte :
        // suivi du SERVEUR uniquement, jamais des comptes locaux.
        try
        {
            await RefreshAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or CryptographicException
            or InvalidOperationException or JsonException)
        {
            logger.LogError(ex, "Service commercial SCOLIA suspendu : l'API scolaire reste consultable.");
        }
    }
}

/// <summary>Contrôle central de tous les postes, même si un client modifie son interface.</summary>
public static class LicenseMutationPolicy
{
    public static bool IsWrite(string method) => method is "POST" or "PUT" or "PATCH" or "DELETE";

    public static bool AllowedWhenReadOnly(string path, string method)
    {
        // Connexion, déconnexion et récupération du mot de passe restent utilisables.
        if (path == "/api/session" && method is "POST" or "DELETE") return true;
        if (method == "POST" &&
            path is "/api/session/recuperation" or "/api/session/mot-de-passe" or
                "/api/session/appareil" or "/api/session/appareil/renouveler" or
                "/api/jumelage/appareil/verifier" or "/api/jumelage/preuve-serveur")
            return true;
        // La gestion de la licence possède ses propres permissions SettingsWrite.
        return path == "/api/licence" ||
               path.StartsWith("/api/licence/", StringComparison.Ordinal);
    }
}
