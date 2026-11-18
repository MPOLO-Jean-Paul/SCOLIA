using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace JPServices.Owner.Android;

/// <summary>
/// Clé AES-256 générée dans l'Android Keystore, jamais exportable : le fichier des identifiants
/// est illisible hors de cette installation (désinstaller l'application détruit la clé).
/// </summary>
internal sealed class KeystoreProtector : ISecretProtector
{
    private const string Store = "AndroidKeyStore";
    private const string Alias = "scolia-console-acces";
    private const int IvLength = 12;

    public bool Available
    {
        get
        {
            try { return Key() is not null; }
            catch (Exception) { return false; }
        }
    }

    public byte[] Protect(byte[] clear)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, Key());
        var iv = cipher.GetIV()!;
        var body = cipher.DoFinal(clear)!;
        var result = new byte[iv.Length + body.Length];
        iv.CopyTo(result, 0);
        body.CopyTo(result, iv.Length);
        return result;
    }

    public byte[] Unprotect(byte[] sealedData)
    {
        if (sealedData.Length <= IvLength) throw new InvalidDataException();
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, sealedData[..IvLength]));
        return cipher.DoFinal(sealedData[IvLength..])!;
    }

    private static IKey Key()
    {
        var keyStore = KeyStore.GetInstance(Store)!;
        keyStore.Load(null);
        if (keyStore.GetKey(Alias, null) is { } existing) return existing;
        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Store)!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return generator.GenerateKey()!;
    }
}
