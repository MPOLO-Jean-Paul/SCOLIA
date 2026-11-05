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
    public async Task Encrypted_archive_with_foreign_sqlite_schema_cannot_replace_scolia()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-backup-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var foreignPath = Path.Combine(folder, "foreign.db");
            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = foreignPath, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE ForeignRecords (Id INTEGER PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }
            var encrypted = BackupProtection.Encrypt(await File.ReadAllBytesAsync(foreignPath), Passphrase);
            var options = Options.Create(new ScoliaStorageOptions { DataDirectory = folder });
            var backups = new BackupService(options, NullLogger<BackupService>.Instance);
            await Assert.ThrowsAsync<DomainException>(() => backups.ImportEncryptedAsync(encrypted, Passphrase));
            Assert.Empty(backups.List()); // L'archive rejetée ne doit pas survivre sous forme de sauvegarde.
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
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
                cmd.CommandText = """
                    CREATE TABLE Etablissement (Id INTEGER PRIMARY KEY);
                    CREATE TABLE Apprenants (Id INTEGER PRIMARY KEY);
                    CREATE TABLE Inscriptions (Id INTEGER PRIMARY KEY);
                    CREATE TABLE Recus (Id INTEGER PRIMARY KEY);
                    CREATE TABLE Dossiers (Id INTEGER PRIMARY KEY, Valeur TEXT NOT NULL);
                    INSERT INTO Dossiers (Valeur) VALUES ('demo');
                    """;
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
