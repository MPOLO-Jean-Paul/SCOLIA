using System.Security.Cryptography;
using System.Text;

namespace Scolia.Api.Security;

/// <summary>Code de secours à usage unique, conservé dans le répertoire privé du serveur.</summary>
public sealed class RecoveryKeyStore : IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public RecoveryKeyStore(string directory)
    {
        path = Path.Combine(directory, "cles", "code-de-secours.txt");
    }

    public void Dispose() => gate.Dispose();

    public void EnsureCreated()
    {
        if (!File.Exists(path)) SaveNewCode(false);
    }

    public async Task<bool> ValidateAndRotateAsync(string? supplied, Func<Task> restore, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            EnsureCreated();
            var expected = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(supplied) || supplied.Length > 100) return false;
            var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            var b = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.Trim().ToUpperInvariant()));
            if (!CryptographicOperations.FixedTimeEquals(a, b)) return false;
            await restore();
            SaveNewCode(true);
            return true;
        }
        finally { gate.Release(); }
    }

    private void SaveNewCode(bool replace)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var code = "SC-" + string.Join("-", Enumerable.Range(0, 6).Select(i => raw.Substring(i * 8, 8)));
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(tmp, options))
            using (var writer = new StreamWriter(stream)) writer.WriteLine(code);
            File.Move(tmp, path, replace);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

public sealed record RecoveryInput(string UserName, string RecoveryCode, string NewPassword, string Confirmation);
