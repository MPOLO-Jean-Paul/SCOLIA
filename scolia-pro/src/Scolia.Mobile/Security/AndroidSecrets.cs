using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Security;

/// <summary>Clé AES non exportable dans Android Keystore ; aucun secret de liaison en préférences claires.</summary>
public static class AndroidSecrets
{
    private const string Alias = "scolia-local-v1";
    private static readonly object Gate = new();
    public static void Initialize()
    {
        SecretStore.NativeProtect = bytes => Guard(() => Encrypt(bytes));
        SecretStore.NativeUnprotect = bytes => Guard(() => Decrypt(bytes));
    }

    private static byte[] Guard(Func<byte[]> operation)
    {
        try { return operation(); }
        catch (GeneralSecurityException ex)
        { throw new System.Security.Cryptography.CryptographicException("La protection locale de SCOLIA est indisponible. Les données restent conservées sur cet appareil.", ex); }
    }

    private static IKey Key()
    {
        lock (Gate)
        {
            using var store = KeyStore.GetInstance("AndroidKeyStore")!;
            store.Load(null);
            if (!store.ContainsAlias(Alias))
            {
                using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
                using var parameters = new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                    .SetBlockModes(KeyProperties.BlockModeGcm)!
                    .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
                    .SetKeySize(256)!
                    .SetRandomizedEncryptionRequired(true)!
                    .Build();
                generator.Init(parameters);
                generator.GenerateKey();
            }
            return store.GetKey(Alias, null)!;
        }
    }

    private static byte[] Encrypt(byte[] data)
    {
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var key = Key();
        cipher.Init(CipherMode.EncryptMode, key);
        var iv = cipher.GetIV()!;
        return [.. iv, .. cipher.DoFinal(data)!];
    }

    private static byte[] Decrypt(byte[] data)
    {
        if (data.Length < 28) throw new System.Security.Cryptography.CryptographicException("Secret local invalide.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        using var key = Key();
        using var parameters = new GCMParameterSpec(128, data[..12]);
        cipher.Init(CipherMode.DecryptMode, key, parameters);
        return cipher.DoFinal(data[12..])!;
    }
}
