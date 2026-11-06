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
    private readonly object gate = new();
    private readonly string file;

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
            var expires = DateTime.UtcNow.AddDays(30);
            grants.RemoveAll(x => x.DeviceId == deviceId && x.Owner == invitation.Owner);
            grants.Add(new DeviceGrant(id, invitation.Owner, invitation.Stamp, deviceId,
                string.IsNullOrWhiteSpace(name) ? "Appareil SCOLIA" : name.Trim()[..Math.Min(name.Trim().Length, 80)],
                Hash(secret), expires));
            Save();
            return (invitation.Owner, invitation.Stamp, id, secret, expires);
        }
    }

    public (Guid Owner, string Stamp)? Authorize(Guid grantId, string? deviceId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(secret) || secret.Length > 128) return null;
        lock (gate)
        {
            var grant = grants.FirstOrDefault(x => x.Id == grantId && !x.Revoked
                && x.ExpiresUtc > DateTime.UtcNow && x.DeviceId == deviceId);
            return grant is not null && Matches(secret, grant.SecretHash) ? (grant.Owner, grant.Stamp) : null;
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
                x.DeviceId == deviceId && !x.Revoked && x.ExpiresUtc > DateTime.UtcNow);
            if (grant is null) return null;
            var key = Convert.FromHexString(grant.SecretHash);
            return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(nonce)));
        }
    }

    public bool IsActive(Guid grantId, Guid owner, string? deviceId)
    {
        lock (gate) return grants.Any(x => x.Id == grantId && x.Owner == owner
            && x.DeviceId == deviceId && !x.Revoked && x.ExpiresUtc > DateTime.UtcNow);
    }

    public IReadOnlyList<AuthorizedDevice> List(Guid owner)
    {
        lock (gate) return grants.Where(x => x.Owner == owner)
            .Select(x => new AuthorizedDevice(x.Id, x.DeviceName, x.ExpiresUtc, x.Revoked)).ToArray();
    }

    public bool Revoke(Guid grantId, Guid owner)
    {
        lock (gate)
        {
            var index = grants.FindIndex(x => x.Id == grantId && x.Owner == owner && !x.Revoked);
            if (index < 0) return false;
            grants[index] = grants[index] with { Revoked = true };
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
public sealed record PairingAccepted(object Session, Guid GrantId, string DeviceSecret, DateTime ExpiresUtc);
