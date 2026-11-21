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
        using var proofResponse = await http.SendAsync(proofRequest, ct);
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
        using var accepted = await http.SendAsync(acceptRequest, ct);
        if (accepted.StatusCode != HttpStatusCode.Created)
            throw new CentralPairingException((int)accepted.StatusCode,
                "Le jumelage de l'appareil a été refusé ou différé.");
        var outcome = await ReadSmallJsonAsync(accepted, ct);
        if (!outcome.TryGetProperty("approved", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !outcome.TryGetProperty("device_id", out var device) || device.GetString() != deviceId ||
            !outcome.TryGetProperty("session_issued", out var sessionIssued) ||
            sessionIssued.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("La confirmation de l'appareil est invalide.");

        return new CentralPairingResult(deviceId, Approved: true, SessionIssued: false);
    }

    private static async Task<JsonElement> ReadSmallJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (Encoding.UTF8.GetByteCount(text) > 8192)
            throw new InvalidDataException("La réponse de jumelage dépasse la limite autorisée.");
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("La réponse de jumelage n'est pas un objet JSON.");
        return doc.RootElement.Clone();
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ECDsa LoadOrCreateDeviceKey(Uri origin, string deviceId, string userName, string folder)
    {
        // Le nom du fichier est anonyme, unique à l'origine, l'appareil et le
        // compte. Le contenu est protégé par DPAPI Windows / Keystore Android.
        var keyName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            origin.AbsoluteUri + "|" + deviceId + "|" + userName.ToUpperInvariant())));
        var path = Path.Combine(folder, keyName + ".key");
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

public sealed record CentralPairingResult(string DeviceId, bool Approved, bool SessionIssued);

public sealed class CentralPairingException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
