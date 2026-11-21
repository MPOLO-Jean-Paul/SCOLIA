using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Api;

/// <summary>
/// Première intégration native Windows/Android du jumelage D1 sécurisé.
/// ATTENTION : jumeler un appareil ne délivre PAS encore de session scolaire.
/// La cible HTTPS est configurée par l'administrateur et n'est jamais lue dans le QR.
/// </summary>
public sealed class CentralPairingClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string keyFolder;

    public CentralPairingClient(HttpMessageHandler? handler = null, string? keyFolder = null)
    {
        this.keyFolder = keyFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SCOLIA", "central-keys");
        http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public static Uri RequireTrustedHttps(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 ||
            endpoint.Fragment.Length > 0 || endpoint.AbsolutePath != "/")
            throw new ArgumentException("Le serveur central doit être une origine HTTPS sans identifiant ni chemin.", nameof(endpoint));
        return endpoint;
    }

    /// <summary>
    /// Permet d'achever le jumelage QR par authentification réelle du compte
    /// destinataire et preuve de possession de la clé privée ECDSA P-256.
    /// Aucun jeton de session ou de synchronisation n'est créé par cette route.
    /// </summary>
    public async Task<CentralPairingResult> PairAsync(
        Uri trustedCentralOrigin, Guid invitationId, string invitationToken,
        string userName, string password, string deviceId,
        CancellationToken ct = default)
    {
        var origin = RequireTrustedHttps(trustedCentralOrigin);
        if (string.IsNullOrWhiteSpace(invitationToken) ||
            !System.Text.RegularExpressions.Regex.IsMatch(invitationToken, "^[A-Za-z0-9_-]{43}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(userName, "^[A-Za-z0-9._-]{3,40}$") ||
            string.IsNullOrEmpty(password) ||
            Encoding.UTF8.GetByteCount(password) > 512 ||
            !System.Text.RegularExpressions.Regex.IsMatch(deviceId, "^[A-Za-z0-9._:-]{1,128}$"))
            throw new ArgumentException("Invitation, compte ou appareil invalide.");

        ct.ThrowIfCancellationRequested();
        using var signingKey = LoadOrCreateDeviceKey(origin, deviceId, userName, keyFolder);
        var parameters = signingKey.ExportParameters(false);
        var publicKey = new
        {
            crv = "P-256",
            kty = "EC",
            x = Base64Url(parameters.Q.X!),
            y = Base64Url(parameters.Q.Y!),
        };
        var canonicalKey = JsonSerializer.Serialize(publicKey);
        var thumbprint = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey)));
        var id = invitationId.ToString("D");

        using var proofRequest = new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "v1/identity/pairing-proof"))
        {
            Content = JsonContent.Create(new
            {
                invitation_id = id,
                user_name = userName,
                password,
                device_id = deviceId,
                device_key_thumbprint = thumbprint,
            }),
        };
        using var proofResponse = await http.SendAsync(proofRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (proofResponse.StatusCode != HttpStatusCode.OK)
            throw new CentralPairingException((int)proofResponse.StatusCode,
                "La vérification du compte a été refusée ou différée.");
        var proofJson = await ReadSmallJsonAsync(proofResponse, ct);
        if (!proofJson.TryGetProperty("pairing_proof", out var signed) ||
            signed.ValueKind != JsonValueKind.String ||
            signed.GetString() is not { Length: > 30 } jwt ||
            jwt.Length > 4096 || jwt.Count(c => c == '.') != 2 ||
            proofJson.GetProperty("session_issued").GetBoolean())
            throw new InvalidDataException("La preuve d'identité du serveur est invalide.");

        var challenge = Encoding.UTF8.GetBytes($"SCOLIA-PAIR-V1\n{id}\n{deviceId}\n{invitationToken}");
        var signature = signingKey.SignData(challenge, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        using var acceptRequest = new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "v1/pair/accept"))
        {
            Content = JsonContent.Create(new
            {
                invitation_id = id,
                token = invitationToken,
                device_id = deviceId,
                public_key_jwk = publicKey,
                device_signature = Base64Url(signature),
            }),
        };
        acceptRequest.Headers.TryAddWithoutValidation("X-Scolia-Account-Proof", "Bearer " + jwt);
        using var accepted = await http.SendAsync(acceptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (accepted.StatusCode != HttpStatusCode.Created)
            throw new CentralPairingException((int)accepted.StatusCode,
                "Le jumelage de l'appareil a été refusé ou différé.");
        var outcome = await ReadSmallJsonAsync(accepted, ct);
        if (!outcome.TryGetProperty("approved", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !outcome.TryGetProperty("device_id", out var device) || device.GetString() != deviceId ||
            !outcome.TryGetProperty("session_issued", out var sessionIssued) ||
            sessionIssued.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("La confirmation de l'appareil est invalide.");

        if (!outcome.TryGetProperty("school_id", out var school) ||
            school.GetString() is not { Length: > 0 and <= 128 } schoolId ||
            !ValidIdentifier(schoolId))
            throw new InvalidDataException("L'établissement n'est pas identifié dans la confirmation.");
        // L'école approuvée est conservée sous une enveloppe locale protégée.
        // Le mot de passe et les JWT temporaires ne sont jamais sauvegardés.
        var registration = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { school_id = schoolId }));
        try { SecretStore.Save(DevicePath(origin, deviceId, userName, keyFolder, ".meta"), registration); }
        finally { CryptographicOperations.ZeroMemory(registration); }
        // Le prochain travail de fond retrouve uniquement l'origine réellement
        // approuvée par le secrétariat, sans la déduire d'un QR non vérifié.
        SelectApprovedOrigin(origin, userName, deviceId);
        return new CentralPairingResult(deviceId, Approved: true, SessionIssued: false, schoolId);
    }

    /// <summary>
    /// Authentification de l'appareil déjà jumelé. La clé privée ne quitte
    /// jamais le stockage protégé, le mot de passe n'est pas renvoyé. Les JWT
    /// restent seulement en mémoire pour une durée maximale de 15 minutes.
    /// </summary>
    public async Task<CentralSession> OpenSessionAsync(
        Uri trustedCentralOrigin, string schoolId, string userName,
        string deviceId, CancellationToken ct = default)
    {
        var origin = RequireTrustedHttps(trustedCentralOrigin);
        if (!ValidIdentifier(schoolId) || !ValidIdentifier(deviceId) ||
            string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("L'établissement, le compte ou l'appareil est invalide.");

        // Ne surtout pas créer une nouvelle clé en silence : un appareil dont
        // la clé a disparu doit être réautorisé par le secrétariat.
        using var key = LoadOrCreateDeviceKey(origin, deviceId, userName, keyFolder,
            requireExisting: true);
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var message = Encoding.UTF8.GetBytes(
            $"SCOLIA-SESSION-V1\n{schoolId}\n{deviceId}\n{nonce}\n{issuedAt}");
        var signature = Base64Url(key.SignData(message, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "v1/identity/session"))
        {
            Content = JsonContent.Create(new
            {
                school_id = schoolId,
                device_id = deviceId,
                nonce,
                issued_at = issuedAt,
                signature,
            }),
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new CentralPairingException((int)response.StatusCode,
                "Session centrale refusée : vérifiez l'appareil et l'autorisation du secrétariat.");

        var result = await ReadSmallJsonAsync(response, ct);
        if (!result.TryGetProperty("access_token", out var token) ||
            token.GetString() is not { Length: > 30 and <= 4096 } jwt ||
            !ValidJwt(jwt) ||
            !result.TryGetProperty("expires_in", out var time) ||
            !time.TryGetInt32(out var seconds) || seconds is < 1 or > 900 ||
            !result.TryGetProperty("school_id", out var school) ||
            school.GetString() != schoolId ||
            !result.TryGetProperty("device_id", out var device) ||
            device.GetString() != deviceId)
            throw new InvalidDataException("La session centrale retournée est incohérente.");

        return new CentralSession(jwt, DateTimeOffset.UtcNow.AddSeconds(seconds), schoolId, deviceId);
    }

    /// <summary>
    /// Restaurer l'école approuvée depuis le stockage chiffré Android/Windows,
    /// sans accepter une adresse cloud fournie par un QR arbitraire.
    /// </summary>
    public string? ReadSavedSchoolId(Uri trustedOrigin, string userName, string deviceId)
    {
        var origin = RequireTrustedHttps(trustedOrigin);
        if (!ValidIdentifier(deviceId) || string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Appareil ou compte invalide.");
        var path = DevicePath(origin, deviceId, userName, keyFolder, ".meta");
        if (!File.Exists(path)) return null;
        var clear = SecretStore.Unprotect(File.ReadAllBytes(path));
        try
        {
            using var doc = JsonDocument.Parse(clear);
            if (!doc.RootElement.TryGetProperty("school_id", out var item) ||
                item.GetString() is not { Length: > 0 and <= 128 } schoolId ||
                !ValidIdentifier(schoolId))
                throw new InvalidDataException("L'inscription centrale locale est invalide.");
            return schoolId;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private static string ActiveOriginPath(string userName, string deviceId, string folder)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            userName.ToUpperInvariant() + "|" + deviceId)));
        return Path.Combine(folder, "active-origin-" + fingerprint + ".enc");
    }

    /// <summary>
    /// Choisir explicitement une origine centrale déjà approuvée sur CET
    /// appareil pour CE compte. Une origine inconnue ne peut pas devenir la
    /// destination des écritures de fond, même si son HTTPS est valide.
    /// </summary>
    public void SelectApprovedOrigin(Uri trustedOrigin, string userName, string deviceId)
    {
        var origin = RequireTrustedHttps(trustedOrigin);
        if (string.IsNullOrWhiteSpace(userName) || !ValidIdentifier(deviceId) ||
            ReadSavedSchoolId(origin, userName, deviceId) is null)
            throw new InvalidOperationException(
                "Seul un serveur central déjà jumelé et approuvé peut être activé.");
        var registration = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { origin = origin.AbsoluteUri }));
        try { SecretStore.Save(ActiveOriginPath(userName, deviceId, keyFolder), registration); }
        finally { CryptographicOperations.ZeroMemory(registration); }
    }

    /// <summary>
    /// Origine centrale active, mémorisée par utilisateur/appareil sous une
    /// enveloppe chiffrée. Aucun mot de passe ni JWT n'est stocké. L'ancienne
    /// métadonnée approuvée doit encore exister, sinon aucun envoi n'est permis.
    /// </summary>
    public Uri? ReadActiveOrigin(string userName, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(userName) || !ValidIdentifier(deviceId))
            throw new ArgumentException("Compte ou appareil invalide.");
        var path = ActiveOriginPath(userName, deviceId, keyFolder);
        if (!File.Exists(path)) return null;
        var clear = SecretStore.Unprotect(File.ReadAllBytes(path));
        try
        {
            using var doc = JsonDocument.Parse(clear);
            if (!doc.RootElement.TryGetProperty("origin", out var value) ||
                value.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var address))
                throw new InvalidDataException("Origine centrale mémorisée invalide.");
            var origin = RequireTrustedHttps(address);
            if (ReadSavedSchoolId(origin, userName, deviceId) is null)
                throw new InvalidDataException(
                    "L'origine centrale active n'a plus d'autorisation locale.");
            return origin;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    /// <summary>Retire la destination des synchronisations futures, sans
    /// effacer les opérations SQLite ni les clés de jumelage existantes.</summary>
    public void DisableActiveOrigin(string userName, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(userName) || !ValidIdentifier(deviceId))
            throw new ArgumentException("Compte ou appareil invalide.");
        var path = ActiveOriginPath(userName, deviceId, keyFolder);
        if (File.Exists(path)) File.Delete(path);
    }

    public Task<CentralSession> OpenSavedSessionAsync(
        Uri trustedOrigin, string userName, string deviceId, CancellationToken ct = default)
    {
        var schoolId = ReadSavedSchoolId(trustedOrigin, userName, deviceId) ??
            throw new InvalidOperationException("Aucun jumelage central confirmé sur cet appareil.");
        return OpenSessionAsync(trustedOrigin, schoolId, userName, deviceId, ct);
    }

    private static bool ValidIdentifier(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-');

    private static bool ValidJwt(string token) =>
        token.Count(c => c == '.') == 2 &&
        token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static Task<JsonElement> ReadSmallJsonAsync(
        HttpResponseMessage response, CancellationToken ct) =>
        CentralBoundedJson.ReadAsync(response, 8192, ct);

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DevicePath(Uri origin, string deviceId, string userName,
        string folder, string extension)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            origin.AbsoluteUri + "|" + deviceId + "|" + userName.ToUpperInvariant())));
        return Path.Combine(folder, name + extension);
    }

    private static ECDsa LoadOrCreateDeviceKey(Uri origin, string deviceId, string userName, string folder,
        bool requireExisting = false)
    {
        // Le nom du fichier est anonyme, unique à l'origine, l'appareil et le
        // compte. Le contenu est protégé par DPAPI Windows / Keystore Android.
        var path = DevicePath(origin, deviceId, userName, folder, ".key");
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            if (File.Exists(path))
            {
                var protectedKey = File.ReadAllBytes(path);
                var clear = SecretStore.Unprotect(protectedKey);
                try { key.ImportPkcs8PrivateKey(clear, out _); }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            else
            {
                if (requireExisting)
                    throw new InvalidOperationException("Cet appareil doit être jumelé par le secrétariat.");
                var raw = key.ExportPkcs8PrivateKey();
                try { SecretStore.Save(path, raw); }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public void Dispose() => http.Dispose();
}

public sealed record CentralPairingResult(string DeviceId, bool Approved, bool SessionIssued,
    string? SchoolId = null);

/// <summary>Jeton de courte durée en mémoire ; jamais inscrit dans SQLite.</summary>
public sealed record CentralSession(string AccessToken, DateTimeOffset ExpiresAt,
    string SchoolId, string DeviceId);

public sealed class CentralPairingException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
