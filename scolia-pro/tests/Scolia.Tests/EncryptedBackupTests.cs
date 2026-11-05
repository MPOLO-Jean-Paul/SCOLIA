using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scolia.Domain.Common;
using Scolia.Infrastructure.Backup;

namespace Scolia.Tests;

public sealed class EncryptedBackupTests
{
    private const string Passphrase = "Une phrase confidentielle de douze caracteres et plus";

    [Fact]
    public void Encrypted_backup_roundtrip_does_not_expose_sqlite_bytes()
    {
        var original = RandomNumberGenerator.GetBytes(4096);
        var secured = BackupProtection.Encrypt(original, Passphrase);
        Assert.False(secured.AsSpan().IndexOf(original) >= 0);
        Assert.Equal(original, BackupProtection.Decrypt(secured, Passphrase));
        Assert.NotEqual(Convert.ToBase64String(BackupProtection.Encrypt(original, Passphrase)),
            Convert.ToBase64String(secured)); // Nouveau sel et nonce à chaque export.
    }

    [Fact]
    public void Wrong_password_tampering_and_weak_passphrases_are_rejected()
    {
        var protectedBytes = BackupProtection.Encrypt([1, 2, 3, 4, 5], Passphrase);
        Assert.Throws<DomainException>(() => BackupProtection.Decrypt(protectedBytes, "Une autre phrase de secours"));
        var changed = (byte[])protectedBytes.Clone();
        changed[^1] ^= 0x01;
        Assert.Throws<DomainException>(() => BackupProtection.Decrypt(changed, Passphrase));
        Assert.Throws<DomainException>(() => BackupProtection.Encrypt([1, 2, 3], "faible"));
    }

    [Fact]
    public async Task Imported_encrypted_copy_is_verified_without_restoring_live_database()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-backup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var options = Options.Create(new ScoliaStorageOptions { DataDirectory = folder });
            await using (var live = new SqliteConnection(options.Value.ConnectionString))
            {
                await live.OpenAsync();
                await using var cmd = live.CreateCommand();
                cmd.CommandText = "CREATE TABLE Dossiers (Id INTEGER PRIMARY KEY, Valeur TEXT NOT NULL); INSERT INTO Dossiers (Valeur) VALUES ('demo');";
                await cmd.ExecuteNonQueryAsync();
            }

            var backups = new BackupService(options, NullLogger<BackupService>.Instance);
            var saved = await backups.CreateAsync("verification");
            var archive = await backups.ExportEncryptedAsync(saved.Name, Passphrase);
            var imported = await backups.ImportEncryptedAsync(archive, Passphrase);
            Assert.NotEqual(saved.Name, imported.Name);
            var checkedCopy = await backups.VerifyAsync(imported.Name);
            Assert.True(checkedCopy.Intact);
            Assert.True(checkedCopy.FingerprintMatches);
            Assert.True(await backups.CheckLiveDatabaseAsync()); // Importer ne restaure pas.
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
