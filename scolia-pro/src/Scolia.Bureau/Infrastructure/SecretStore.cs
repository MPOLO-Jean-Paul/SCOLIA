using System.Security.Cryptography;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Enveloppes locales protégées par le système (Keystore Android ou DPAPI Windows).</summary>
public static class SecretStore
{
    public static Func<byte[], byte[]>? NativeProtect { get; set; }
    public static Func<byte[], byte[]>? NativeUnprotect { get; set; }
    private static readonly object Gate = new();

    public static byte[] Protect(byte[] data)
    {
        if (NativeProtect is { } protect) return protect(data);
        if (OperatingSystem.IsWindows()) return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Portable(data, true);
    }

    public static byte[] Unprotect(byte[] data)
    {
        if (NativeUnprotect is { } unprotect) return unprotect(data);
        if (OperatingSystem.IsWindows()) return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        return Portable(data, false);
    }

    // Tests/Linux : clé privée au compte système, hors des archives de données.
    // Cette solution ne prétend pas offrir un Keystore matériel sur Linux.
    private static byte[] Portable(byte[] data, bool encrypt)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "protection.key");
        byte[] key;
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                using var created = new FileStream(path, new FileStreamOptions
                { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
                created.Write(RandomNumberGenerator.GetBytes(32));
            }
            key = File.ReadAllBytes(path);
        }
        using var aes = new AesGcm(key, 16);
        if (encrypt)
        {
            var result = new byte[28 + data.Length];
            RandomNumberGenerator.Fill(result.AsSpan(0, 12));
            aes.Encrypt(result.AsSpan(0, 12), data, result.AsSpan(28), result.AsSpan(12, 16));
            return result;
        }
        if (data.Length < 28) throw new CryptographicException("Enveloppe locale invalide.");
        var clear = new byte[data.Length - 28];
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), clear);
        return clear;
    }

    public static void Save(string path, byte[] clear)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, Protect(clear)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
