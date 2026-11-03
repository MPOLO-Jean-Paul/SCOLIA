using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// L'appareil invité ne reçoit jamais le mot de passe de l'administrateur.
/// Il conserve son propre jeton, protégé par le Keystore Android ou DPAPI Windows.
/// </summary>
public static class DevicePairingStore
{
    public sealed record Credential(Guid GrantId, string DeviceSecret, DateTime ExpiresUtc, string UserName);

    private static string PathFor(Uri server)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.AbsoluteUri)));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SCOLIA", "postes-autorises", key + ".enc");
    }

    public static Credential? Read(Uri server)
    {
        try
        {
            var path = PathFor(server);
            if (!File.Exists(path)) return null;
            var plain = SecretStore.Unprotect(File.ReadAllBytes(path));
            var credentials = JsonSerializer.Deserialize<Credential>(plain);
            return credentials is { ExpiresUtc: var expires } && expires > DateTime.UtcNow ? credentials : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            CryptographicException or JsonException) { return null; }
    }

    public static void Save(Uri server, Credential credentials) =>
        SecretStore.Save(PathFor(server), JsonSerializer.SerializeToUtf8Bytes(credentials));

    public static void Forget(Uri server)
    {
        try
        {
            var path = PathFor(server);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
