using System.Security.Cryptography;
using System.Text;
using Scolia.Domain.Common;

namespace Scolia.Infrastructure.Backup;

/// <summary>
/// Format portable chiffré SCOLIA : AES-256-GCM + PBKDF2-SHA256. L'en-tête
/// de version est authentifié et toute modification du fichier est rejetée.
/// Un mot de passe est demandé séparément du fichier chiffré.
/// </summary>
public static class BackupProtection
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SCOLIA-ENC-V1");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 600_000;
    // Limite de sécurité pour éviter une pression mémoire excessive sur un téléphone serveur.
    public const int MaximumPlainBytes = 48 * 1024 * 1024;
    private static int HeaderSize => Magic.Length + SaltSize + NonceSize + TagSize;

    public static byte[] Encrypt(ReadOnlySpan<byte> plain, string passphrase)
    {
        ValidatePassphrase(passphrase);
        if (plain.Length is 0 or > MaximumPlainBytes)
            throw new DomainException("sauvegardes", "Cette sauvegarde dépasse la taille autorisée pour un export chiffré (48 Mio).");

        var result = new byte[HeaderSize + plain.Length];
        var header = result.AsSpan(0, Magic.Length);
        Magic.AsSpan().CopyTo(header);
        var salt = result.AsSpan(Magic.Length, SaltSize);
        var nonce = result.AsSpan(Magic.Length + SaltSize, NonceSize);
        var tag = result.AsSpan(Magic.Length + SaltSize + NonceSize, TagSize);
        var ciphertext = result.AsSpan(HeaderSize);
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, ciphertext, tag, Magic);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return result;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> protectedBytes, string passphrase)
    {
        ValidatePassphrase(passphrase);
        if (protectedBytes.Length <= HeaderSize || protectedBytes.Length - HeaderSize > MaximumPlainBytes
            || !protectedBytes[..Math.Min(protectedBytes.Length, Magic.Length)].SequenceEqual(Magic))
            throw new DomainException("sauvegardes", "Archive chiffrée SCOLIA invalide, incomplète ou trop volumineuse.");

        var salt = protectedBytes.Slice(Magic.Length, SaltSize);
        var nonce = protectedBytes.Slice(Magic.Length + SaltSize, NonceSize);
        var tag = protectedBytes.Slice(Magic.Length + SaltSize + NonceSize, TagSize);
        var cipher = protectedBytes[HeaderSize..];
        var plain = new byte[cipher.Length];

        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new DomainException("sauvegardes", "Mot de passe erroné ou fichier chiffré modifié : importation refusée.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
            throw new DomainException("sauvegardes", "Utilisez une phrase secrète d'au moins 12 caractères pour protéger la sauvegarde.");
    }
}
