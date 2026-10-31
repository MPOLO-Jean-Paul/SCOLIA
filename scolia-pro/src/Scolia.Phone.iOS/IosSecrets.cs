using System.Security.Cryptography;
using Foundation;
using Scolia.Bureau.Infrastructure;
using Security;

namespace Scolia.Phone.iOS;

/// <summary>
/// Protection locale sur iPhone : la clé AES qui chiffre les données de SCOLIA (session hors ligne, liaison au serveur)
/// vit dans le trousseau iOS, propre à cet appareil, et non dans un fichier à côté des données.
/// Si le trousseau est indisponible (application installée sans les droits nécessaires), SCOLIA garde sa
/// protection par fichier privé de l'application.
/// </summary>
public static class IosSecrets
{
    private const string Service = "com.jpservices.scolia.ios.local";
    private const string Account = "scolia-local-v1";
    private static readonly object Gate = new();
    private static byte[]? key;

    public static void Initialize()
    {
        try
        {
            if (Key() is null) return;
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
        {
            return;
        }
        SecretStore.NativeProtect = Encrypt;
        SecretStore.NativeUnprotect = Decrypt;
    }

    private static byte[]? Key()
    {
        lock (Gate)
        {
            if (key is not null) return key;
            var query = new SecRecord(SecKind.GenericPassword) { Service = Service, Account = Account };
            var data = SecKeyChain.QueryAsData(query, false, out var status);
            if (status == SecStatusCode.Success && data is not null && (int)data.Length == 32)
                return key = data.ToArray();

            var fresh = RandomNumberGenerator.GetBytes(32);
            var record = new SecRecord(SecKind.GenericPassword)
            {
                Service = Service,
                Account = Account,
                ValueData = NSData.FromArray(fresh),
                // Disponible après le premier déverrouillage, jamais copié sur un autre appareil ni dans une sauvegarde.
                Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly,
            };
            var added = SecKeyChain.Add(record);
            if (added == SecStatusCode.Success) return key = fresh;
            if (added == SecStatusCode.DuplicateItem)
            {
                data = SecKeyChain.QueryAsData(query, false, out status);
                if (status == SecStatusCode.Success && data is not null && (int)data.Length == 32) return key = data.ToArray();
            }
            return null;
        }
    }

    // Même enveloppe que sur les autres plateformes : nonce (12), étiquette (16), données chiffrées.
    private static byte[] Encrypt(byte[] data)
    {
        using var aes = new AesGcm(Key() ?? throw new CryptographicException("Trousseau iOS indisponible."), 16);
        var result = new byte[28 + data.Length];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        aes.Encrypt(result.AsSpan(0, 12), data, result.AsSpan(28), result.AsSpan(12, 16));
        return result;
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (data.Length < 28) throw new CryptographicException("Enveloppe locale invalide.");
        using var aes = new AesGcm(Key() ?? throw new CryptographicException("Trousseau iOS indisponible."), 16);
        var clear = new byte[data.Length - 28];
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), clear);
        return clear;
    }
}
