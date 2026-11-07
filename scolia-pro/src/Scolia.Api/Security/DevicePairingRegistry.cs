using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scolia.Api.Security;

/// <summary>Invitations courtes et appareils explicitement autorisés par la session du serveur.</summary>
public sealed class DevicePairingRegistry
{
    private sealed record Invitation(Guid Owner, string Stamp, DateTime ExpiresUtc);
    private sealed record DeviceGrant(Guid Id, Guid Owner, string Stamp, string DeviceId, string DeviceName,
        string SecretHash, DateTime ExpiresUtc, bool Revoked = false);
    private readonly Dictionary<string, Invitation> invitations = new(StringComparer.Ordinal);
    private readonly List<DeviceGrant> grants;
    // Nonces vus pendant la fenêtre de validité de deux minutes : une requête
    // capturée ne doit jamais être acceptée une deuxième fois.
    private readonly Dictionary<string, long> remoteNonces = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly string file;
    // Une autorisation est révocable à tout moment ; chaque usage valide prolonge
    // sa durée sans copier le mot de passe du compte de l'établissement.
    public const int DeviceValidityDays = 180; // Compatibilité des anciennes versions ; l'accès n'expire plus automatiquement.
    public const int RenewalWindowDays = 30;

    public DevicePairingRegistry(string dataDirectory)
    {
        file = Path.Combine(dataDirectory, "cles", "appareils-autorises.json");
        grants = File.Exists(file)
            ? JsonSerializer.Deserialize<List<DeviceGrant>>(File.ReadAllText(file)) ?? []
            : [];
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool Matches(string received, string expectedHash)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(received));
        var right = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    public (string Token, DateTime ExpiresUtc) Invite(Guid owner, string stamp)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            foreach (var expired in invitations.Where(x => x.Value.ExpiresUtc <= now).Select(x => x.Key).ToArray())
                invitations.Remove(expired);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var expires = now.AddMinutes(5);
            invitations[Hash(token)] = new Invitation(owner, stamp, expires);
            return (token, expires);
        }
    }

    public (Guid Owner, string Stamp, Guid GrantId, string Secret, DateTime ExpiresUtc)? Redeem(
        string? invitationToken, string? deviceId, string? name)
    {
        if (string.IsNullOrWhiteSpace(invitationToken) || invitationToken.Length > 100
            || string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64) return null;
        lock (gate)
        {
            var key = Hash(invitationToken.Trim());
            if (!invitations.TryGetValue(key, out var invitation) || invitation.ExpiresUtc <= DateTime.UtcNow)
                return null;
            // Une invitation ne peut donner lieu qu'à une seule liaison, y compris lors de requêtes simultanées.
            invitations.Remove(key);
            var id = Guid.NewGuid();
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = DateTime.MaxValue;
            grants.RemoveAll(x => x.DeviceId == deviceId);
            grants.Add(new DeviceGrant(id, invitation.Owner, invitation.Stamp, deviceId,
                string.IsNullOrWhiteSpace(name) ? "Appareil SCOLIA" : name.Trim()[..Math.Min(name.Trim().Length, 80)],
                Hash(secret), expires));
            Save();
            return (invitation.Owner, invitation.Stamp, id, secret, expires);
        }
    }

    /// <summary>Un promoteur déjà authentifié peut mémoriser SON propre appareil.
    /// Aucun droit supplémentaire n'est accordé : le jeton reprend exactement son compte.</summary>
    public (Guid GrantId, string Secret, DateTime ExpiresUtc)? EnrollOwnDevice(
        Guid owner, string stamp, string? deviceId, string? name)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64) return null;
        lock (gate)
        {
            var id = Guid.NewGuid();
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expires = DateTime.MaxValue;
            grants.RemoveAll(x => x.DeviceId == deviceId);
            grants.Add(new DeviceGrant(id, owner, stamp, deviceId,
                string.IsNullOrWhiteSpace(name) ? "Appareil promoteur" : name.Trim()[..Math.Min(name.Trim().Length, 80)],
                Hash(secret), expires));
            Save();
            return (id, secret, expires);
        }
    }

    public (Guid Owner, string Stamp)? Authorize(Guid grantId, string? deviceId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret) || secret.Length > 128) return null;
        lock (gate)
        {
            var grant = grants.FirstOrDefault(x => x.Id == grantId && !x.Revoked
                && x.DeviceId == deviceId);
            return grant is not null && Matches(secret, grant.SecretHash) ? (grant.Owner, grant.Stamp) : null;
        }
    }

    /// <summary>
    /// Reconnaît une autorisation persistante et migre les anciennes échéances.
    /// Le même appareil et le même secret restent nécessaires ; une autorisation
    /// retirée ne peut jamais être réactivée.
    /// </summary>
    public (Guid Owner, string Stamp, DateTime ExpiresUtc)? AuthorizeAndRefresh(
        Guid grantId, string? deviceId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
            return null;
        lock (gate)
        {
            var index = grants.FindIndex(x => x.Id == grantId && x.DeviceId == deviceId
                && !x.Revoked);
            if (index < 0 || !Matches(secret, grants[index].SecretHash)) return null;
            if (grants[index].ExpiresUtc != DateTime.MaxValue)
            {
                // Migration des appareils autorisés avant la suppression de l'expiration :
                // seule une révocation explicite peut désormais interrompre le jumelage.
                grants[index] = grants[index] with { ExpiresUtc = DateTime.MaxValue };
                Save();
            }
            var authorized = grants[index];
            return (authorized.Owner, authorized.Stamp, authorized.ExpiresUtc);
        }
    }

    /// <summary>
    /// Le serveur prouve qu'il possède le secret du jumelage, SANS recevoir
    /// le secret ni une session du téléphone. Indispensable avant de suivre
    /// une nouvelle adresse IP annoncée en UDP (réponse LAN non authentifiée).
    /// </summary>
    public string? ProveIdentity(Guid grantId, string? deviceId, string? nonce)
    {
        if (string.IsNullOrWhiteSpace(deviceId) ||
            nonce is null || nonce.Length != 48 || !nonce.All(char.IsAsciiHexDigit))
            return null;
        lock (gate)
        {
            var grant = grants.FirstOrDefault(x => x.Id == grantId &&
                x.DeviceId == deviceId && !x.Revoked);
            if (grant is null) return null;
            var key = Convert.FromHexString(grant.SecretHash);
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(nonce)));
        }
    }

    /// <summary>
    /// La liaison chiffrée n'accorde aucun accès par elle-même : chaque requête
    /// distante doit être signée par l'appareil toujours présent dans le registre.
    /// Une révocation bloque ainsi aussi les anciens cookies utilisateur.
    /// </summary>
    public bool VerifyRemoteProof(HttpContext http)
    {
        var grantText = http.Request.Headers["X-Scolia-Grant"].ToString();
        var deviceId = http.Request.Headers["X-Scolia-Poste"].ToString();
        var timestamp = http.Request.Headers["X-Scolia-Device-Time"].ToString();
        var nonce = http.Request.Headers["X-Scolia-Device-Nonce"].ToString();
        var proofText = http.Request.Headers["X-Scolia-Device-Proof"].ToString();
        if (!Guid.TryParseExact(grantText, "N", out var id) ||
            string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64 ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            nonce.Length != 32 || !nonce.All(char.IsAsciiHexDigit) ||
            proofText.Length != 64 || !proofText.All(char.IsAsciiHexDigit))
            return false;
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (seconds < nowSeconds - 120 || seconds > nowSeconds + 120) return false;
        lock (gate)
        {
            var grant = grants.FirstOrDefault(x => x.Id == id && x.DeviceId == deviceId && !x.Revoked);
            if (grant is null) return false;
            var message = $"{id:N}\n{deviceId}\n{timestamp}\n{nonce}\n{http.Request.Method}\n{http.Request.Path}{http.Request.QueryString}";
            var key = Convert.FromHexString(grant.SecretHash);
            var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message));
            var received = Convert.FromHexString(proofText);
            if (!CryptographicOperations.FixedTimeEquals(expected, received)) return false;
            var replayKey = $"{id:N}:{nonce}";
            if (remoteNonces.ContainsKey(replayKey)) return false;
            if (remoteNonces.Count >= 4096)
            {
                foreach (var expired in remoteNonces.Where(x => x.Value < seconds - 120)
                    .Select(x => x.Key).ToArray())
                    remoteNonces.Remove(expired);
                if (remoteNonces.Count >= 8192) return false;
            }
            remoteNonces[replayKey] = seconds;
            return true;
        }
    }

    public bool IsActive(Guid grantId, Guid owner, string? deviceId)
    {
        lock (gate) return grants.Any(x => x.Id == grantId && x.Owner == owner
            && x.DeviceId == deviceId && !x.Revoked);
    }

    public IReadOnlyList<AuthorizedDevice> List(Guid owner)
    {
        lock (gate) return grants.Where(x => x.Owner == owner && !x.Revoked)
            .Select(x => new AuthorizedDevice(x.Id, x.DeviceName, x.ExpiresUtc, x.Revoked)).ToArray();
    }

    /// <summary>La gestion des appareils est commune à tous les administrateurs
    /// autorisés de l'établissement, pas seulement à l'auteur du QR.</summary>
    public IReadOnlyList<AuthorizedDevice> ListAll()
    {
        lock (gate) return grants.Where(x => !x.Revoked)
            .Select(x => new AuthorizedDevice(x.Id, x.DeviceName, x.ExpiresUtc, false)).ToArray();
    }

    public bool RevokeAny(Guid grantId)
    {
        lock (gate)
        {
            var index = grants.FindIndex(x => x.Id == grantId && !x.Revoked);
            if (index < 0) return false;
            var deviceId = grants[index].DeviceId;
            grants.RemoveAll(x => x.DeviceId == deviceId);
            Save();
            return true;
        }
    }

    public bool Revoke(Guid grantId, Guid owner)
    {
        lock (gate)
        {
            var index = grants.FindIndex(x => x.Id == grantId && x.Owner == owner && !x.Revoked);
            if (index < 0) return false;
            // Suppression immédiate du registre persistant et de la liste affichée.
            // L'ancien secret ne sera plus accepté lors d'une reprise réseau.
            grants.RemoveAt(index);
            Save();
            return true;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
                JsonSerializer.Serialize(stream, grants);
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record AuthorizedDevice(Guid Id, string Name, DateTime ExpiresUtc, bool Revoked);
public sealed record PairingInvitation(string Token, DateTime ExpiresUtc);
public sealed record PairingAccept(string InvitationToken, string DeviceId, string DeviceName);
public sealed record DeviceLoginRequest(Guid GrantId, string DeviceId, string Secret);
public sealed record DeviceProofRequest(Guid GrantId, string DeviceId, string Nonce);
public sealed record DeviceProofReply(string Proof);
public sealed record DeviceSelfRegistration(Guid GrantId, string DeviceSecret, DateTime ExpiresUtc);
public sealed record DeviceRenewedSession(object Session, DateTime ExpiresUtc);
public sealed record PairingAccepted(Guid GrantId, string DeviceSecret, DateTime ExpiresUtc, string SchoolName);
public sealed record DeviceLinkVerified(DateTime ExpiresUtc);
