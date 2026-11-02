using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Opt-in local, un compte par appareil et serveur ; jamais d'empreinte ni de mot
/// de passe stocké ici. L'authentification réelle reste celle de l'API SCOLIA.
/// </summary>
public static class BiometricAccess
{
    private sealed record Enrollment(string Server, string UserName);
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SCOLIA", "biometrie.compte");

    private sealed record Sealed(string UserName, string Secret);

    /// <summary>
    /// Ce que la puce de sécurité du téléphone garde sous l'empreinte : le compte et son code, ensemble. Une fois
    /// l'empreinte validée, SCOLIA se connecte comme avec le clavier : la biométrie marche aussi après une
    /// déconnexion, et le serveur vérifie toujours le code.
    /// </summary>
    public static string Seal(string userName, string secret) =>
        JsonSerializer.Serialize(new Sealed(userName.Trim(), secret));

    /// <summary>Lit ce que l'empreinte a rendu ; rien si ce n'est pas un accès SCOLIA complet.</summary>
    public static (string UserName, string Secret)? Open(string? sealedAccess)
    {
        if (string.IsNullOrEmpty(sealedAccess)) return null;
        try
        {
            return JsonSerializer.Deserialize<Sealed>(sealedAccess) is { UserName.Length: > 0, Secret.Length: > 0 } value
                ? (value.UserName, value.Secret) : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool AccountMatches(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    public static void Enable(Uri server, string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("Compte obligatoire.", nameof(userName));
        var enrollment = new Enrollment(ServerKey(server), userName.Trim());
        SecretStore.Save(FilePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(enrollment)));
    }

    public static string? ReadAccount(Uri server)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var bytes = SecretStore.Unprotect(File.ReadAllBytes(FilePath));
            var enrollment = JsonSerializer.Deserialize<Enrollment>(bytes);
            return enrollment is { UserName.Length: > 0 }
                && string.Equals(enrollment.Server, ServerKey(server), StringComparison.Ordinal)
                    ? enrollment.UserName : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                    or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static void Disable()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    private static string ServerKey(Uri uri) =>
        $"{uri.Scheme.ToLowerInvariant()}://{uri.Host.ToLowerInvariant()}:{uri.Port}";
}
