using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scolia.Domain.Common;

namespace Scolia.Infrastructure.Backup;

public sealed class ScoliaStorageOptions
{
    /// <summary>Dossier des données (base, sauvegardes, journaux). Par défaut : « donnees » à côté de l'exécutable.</summary>
    public string DataDirectory { get; set; } = string.Empty;
    public int BackupRetentionDays { get; set; } = 30;
    public int AutomaticBackupHours { get; set; } = 24;

    public string DatabasePath => Path.Combine(DataDirectory, "scolia.db");
    public string BackupDirectory => Path.Combine(DataDirectory, "sauvegardes");
    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Cache = SqliteCacheMode.Shared, DefaultTimeout = 30, Pooling = true, ForeignKeys = true }.ToString();
}

/// <summary>Une copie de la base. L'empreinte SHA-256 est écrite à côté de la copie, dans un fichier .sha256.</summary>
public sealed record BackupInfo(string Name, DateTime CreatedUtc, long SizeBytes, string? Sha256);

/// <summary>Résultat de la vérification d'une copie : intégrité SQLite et concordance avec son empreinte.</summary>
public sealed record BackupCheck(string Name, bool Intact, bool HasFingerprint, bool FingerprintMatches);

/// <summary>Sauvegarde à chaud cohérente (API de sauvegarde SQLite), vérification d'intégrité et restauration.</summary>
public sealed class BackupService(IOptions<ScoliaStorageOptions> options, ILogger<BackupService> logger)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private readonly ScoliaStorageOptions settings = options.Value;

    public async Task<BackupInfo> CreateAsync(string reason, CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct);
        try
        {
            EnsureBackupDirectory();
            var safeReason = new string(reason.Where(char.IsAsciiLetterOrDigit).ToArray());
            var name = $"scolia-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{(safeReason.Length == 0 ? "manuelle" : safeReason)}.db";
            var target = Path.Combine(settings.BackupDirectory, name);
            await CopyLiveDatabaseAsync(target, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var hash = await Sha256Async(target, ct);
            await File.WriteAllTextAsync(target + FingerprintExtension, $"{hash}  {name}\n", ct);
            var info = new FileInfo(target);
            logger.LogInformation("Sauvegarde créée : {Name} ({Size} octets)", name, info.Length);
            return new BackupInfo(name, info.CreationTimeUtc, info.Length, hash);
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Copie à chaud cohérente de la base en service vers <paramref name="target"/>, vérifiée.</summary>
    private async Task CopyLiveDatabaseAsync(string target, CancellationToken ct)
    {
        await using (var source = new SqliteConnection(settings.ConnectionString))
        await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString()))
        {
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
            // La copie hérite du mode WAL : on la ramène à un fichier unique, autonome et transportable.
            await using var mode = destination.CreateCommand();
            mode.CommandText = "PRAGMA journal_mode=DELETE;";
            await mode.ExecuteNonQueryAsync(ct);
        }
        if (!await IsIntactAsync(target, ct)) throw new IOException("La sauvegarde créée n'a pas passé le contrôle d'intégrité.");
    }

    /// <summary>
    /// Instantané pour la sauvegarde en ligne : une copie cohérente et vérifiée, écrite hors du dossier
    /// des sauvegardes locales (elle est chiffrée puis envoyée, et le fichier temporaire est supprimé).
    /// </summary>
    public async Task<string> SnapshotAsync(CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct);
        try
        {
            var folder = Path.Combine(settings.DataDirectory, "nuage-temp");
            Directory.CreateDirectory(folder);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            foreach (var stale in Directory.EnumerateFiles(folder, "*.db"))
                try { File.Delete(stale); } catch (IOException) { }
            var target = Path.Combine(folder, $"instantane-{Guid.NewGuid():N}.db");
            try { await CopyLiveDatabaseAsync(target, ct); }
            catch
            {
                if (File.Exists(target)) File.Delete(target);
                throw;
            }
            return target;
        }
        finally { Lock.Release(); }
    }

    /// <summary>
    /// Range une base reçue en clair (copie en ligne déchiffrée) parmi les sauvegardes locales, après contrôle
    /// d'intégrité et du schéma SCOLIA. Elle n'est jamais restaurée automatiquement.
    /// </summary>
    public async Task<BackupInfo> ImportPlainAsync(byte[] clear, string origin, CancellationToken ct = default)
    {
        if (clear.Length == 0) throw new DomainException("sauvegardes", "Copie vide : récupération refusée.");
        var safeOrigin = new string(origin.Where(char.IsAsciiLetterOrDigit).ToArray());
        string? target = null;
        await Lock.WaitAsync(ct);
        try
        {
            EnsureBackupDirectory();
            var name = $"scolia-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{(safeOrigin.Length == 0 ? "importee" : safeOrigin)}-{Guid.NewGuid():N}.db";
            target = Path.Combine(settings.BackupDirectory, name);
            await File.WriteAllBytesAsync(target, clear, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (!await IsIntactAsync(target, ct) || !await IsScoliaDatabaseAsync(target, ct))
                throw new DomainException("sauvegardes", "La copie déchiffrée est endommagée ou ne contient pas le schéma SCOLIA attendu : récupération refusée.");
            var sha = await Sha256Async(target, ct);
            await File.WriteAllTextAsync(target + FingerprintExtension, $"{sha}  {name}\n", ct);
            var info = new FileInfo(target);
            logger.LogInformation("Copie en ligne récupérée et vérifiée : {Name}", name);
            target = null;
            return new BackupInfo(name, info.LastWriteTimeUtc, info.Length, sha);
        }
        finally
        {
            if (target is not null)
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (File.Exists(target + FingerprintExtension)) File.Delete(target + FingerprintExtension);
                }
                catch (IOException) { }
            }
            Lock.Release();
        }
    }

    /// <summary>
    /// Exporte une copie vérifiée sous la forme d'une archive portable chiffrée.
    /// La phrase secrète n'est jamais stockée dans la base ni dans les journaux.
    /// </summary>
    public async Task<byte[]> ExportEncryptedAsync(string name, string passphrase, CancellationToken ct = default)
    {
        var status = await VerifyAsync(name, ct);
        if (!status.Intact || (status.HasFingerprint && !status.FingerprintMatches))
            throw new DomainException("sauvegardes", "Sauvegarde invalide ou altérée : export chiffré refusé.");

        var file = PathOf(name);
        if (!await IsScoliaDatabaseAsync(file, ct))
            throw new DomainException("sauvegardes", "Le fichier ne contient pas la structure de base SCOLIA requise.");
        if (new FileInfo(file).Length > BackupProtection.MaximumPlainBytes)
            throw new DomainException("sauvegardes", "Sauvegarde supérieure à 48 Mio : utilisez une méthode d'archivage chiffré par flux.");

        var clear = await File.ReadAllBytesAsync(file, ct);
        try { return BackupProtection.Encrypt(clear, passphrase); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    /// <summary>
    /// Déchiffre dans une NOUVELLE sauvegarde temporaire, vérifie SQLite,
    /// puis rend la copie disponible à la restauration ordinaire, jamais automatique.
    /// </summary>
    public async Task<BackupInfo> ImportEncryptedAsync(byte[] archive, string passphrase, CancellationToken ct = default)
    {
        var clear = BackupProtection.Decrypt(archive, passphrase);
        string? target = null;
        var locked = false;
        try
        {
            await Lock.WaitAsync(ct);
            locked = true;
            EnsureBackupDirectory();
            var name = $"scolia-{DateTime.UtcNow:yyyyMMdd-HHmmss}-importee-{Guid.NewGuid():N}.db";
            target = Path.Combine(settings.BackupDirectory, name);
            await File.WriteAllBytesAsync(target, clear, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (!await IsIntactAsync(target, ct) || !await IsScoliaDatabaseAsync(target, ct))
                throw new DomainException("sauvegardes", "Le fichier déchiffré est endommagé ou ne contient pas le schéma SCOLIA attendu : import refusé.");

            var sha = await Sha256Async(target, ct);
            await File.WriteAllTextAsync(target + FingerprintExtension, $"{sha}  {name}\n", ct);
            var info = new FileInfo(target);
            logger.LogInformation("Sauvegarde chiffrée importée et vérifiée : {Name}", name);
            return new BackupInfo(name, info.LastWriteTimeUtc, info.Length, sha);
        }
        catch
        {
            if (target is not null)
            {
                if (File.Exists(target)) File.Delete(target);
                if (File.Exists(target + FingerprintExtension)) File.Delete(target + FingerprintExtension);
            }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (locked) Lock.Release();
        }
    }

    private void EnsureBackupDirectory()
    {
        Directory.CreateDirectory(settings.BackupDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(settings.BackupDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(settings.BackupDirectory)) return [];
        return new DirectoryInfo(settings.BackupDirectory).GetFiles("scolia-*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, f.LastWriteTimeUtc, f.Length, ReadFingerprint(f.FullName))).ToList();
    }

    public string PathOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.StartsWith("scolia-", StringComparison.Ordinal) || !name.EndsWith(".db", StringComparison.Ordinal))
            throw new FileNotFoundException("Sauvegarde introuvable.");
        var path = Path.Combine(settings.BackupDirectory, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Sauvegarde introuvable.");
        return path;
    }

    /// <summary>Vérifie une copie : contrôle SQLite et concordance avec l'empreinte enregistrée à sa création.</summary>
    public async Task<BackupCheck> VerifyAsync(string name, CancellationToken ct = default)
    {
        var path = PathOf(name);
        var intact = await IsIntactAsync(path, ct);
        var expected = ReadFingerprint(path);
        var matches = expected is not null && string.Equals(expected, await Sha256Async(path, ct), StringComparison.OrdinalIgnoreCase);
        return new BackupCheck(name, intact, expected is not null, matches);
    }

    /// <summary>
    /// Nettoyage à la demande : supprime les copies au-delà des <paramref name="keep"/> plus récentes,
    /// et seulement si elles ont plus de <paramref name="olderThanDays"/> jours. Rien n'est supprimé automatiquement.
    /// </summary>
    public int Cleanup(int keep, int olderThanDays)
    {
        if (keep < 1) throw new DomainException("sauvegardes", "Gardez au moins une sauvegarde.");
        if (!Directory.Exists(settings.BackupDirectory)) return 0;
        var limit = DateTime.UtcNow.AddDays(-Math.Max(0, olderThanDays));
        var files = new DirectoryInfo(settings.BackupDirectory).GetFiles("scolia-*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
        var removed = 0;
        foreach (var file in files.Skip(keep).Where(f => f.LastWriteTimeUtc < limit))
        {
            try
            {
                file.Delete();
                var fingerprint = file.FullName + FingerprintExtension;
                if (File.Exists(fingerprint)) File.Delete(fingerprint);
                removed++;
            }
            catch (IOException ex) { logger.LogWarning(ex, "Ancienne sauvegarde non supprimée : {Name}", file.Name); }
        }
        return removed;
    }

    private const string FingerprintExtension = ".sha256";

    private static string? ReadFingerprint(string path)
    {
        var fingerprint = path + FingerprintExtension;
        if (!File.Exists(fingerprint)) return null;
        var first = File.ReadAllText(fingerprint).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public static async Task<bool> IsIntactAsync(string path, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return (await command.ExecuteScalarAsync(ct)) as string == "ok";
    }

    /// <summary>
    /// PRAGMA integrity_check ne distingue pas une base SCOLIA d'une base SQLite
    /// quelconque. Refuser les restaurations sans les tables fondamentales du produit,
    /// avant toute copie vers la base active.
    /// </summary>
    private static async Task<bool> IsScoliaDatabaseAsync(string path, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('Etablissement', 'Apprenants', 'Inscriptions', 'Recus');
            """;
        var found = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) found.Add(reader.GetString(0));
        return found.Count == 4;
    }

    public async Task<bool> CheckLiveDatabaseAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(settings.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return (await command.ExecuteScalarAsync(ct)) as string == "ok";
    }

    /// <summary>Restaure une sauvegarde dans la base en service. Une sauvegarde de sécurité de l'état actuel est faite avant.</summary>
    public async Task RestoreAsync(string name, CancellationToken ct = default)
    {
        var check = await VerifyAsync(name, ct);
        if (!check.Intact) throw new DomainException("sauvegardes", "Cette sauvegarde est endommagée : restauration refusée.");
        if (check.HasFingerprint && !check.FingerprintMatches) throw new DomainException("sauvegardes", "L'empreinte de cette sauvegarde ne correspond plus : restauration refusée.");
        var path = PathOf(name);
        if (!await IsScoliaDatabaseAsync(path, ct))
            throw new DomainException("sauvegardes", "La sauvegarde n'est pas une base SCOLIA reconnue : restauration refusée.");
        await CreateAsync("avantrestauration", ct);
        await Lock.WaitAsync(ct);
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await using var destination = new SqliteConnection(settings.ConnectionString);
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);
            logger.LogWarning("Base restaurée depuis {Name}", name);
        }
        finally
        {
            Lock.Release();
        }
    }
}

/// <summary>Sauvegarde automatique au démarrage puis à intervalle régulier.</summary>
public sealed class AutomaticBackupWorker(BackupService backups, IOptions<ScoliaStorageOptions> options, ILogger<AutomaticBackupWorker> logger,
    Live.LiveHub live, Persistence.DataRevision revision) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = Math.Max(1, options.Value.AutomaticBackupHours);
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var list = backups.List();
                var latest = list.Count > 0 ? list[0] : null;
                if (latest is null || latest.CreatedUtc < DateTime.UtcNow.AddHours(-hours))
                {
                    await backups.CreateAsync("automatique", stoppingToken);
                    live.Changed(revision.Value, null, "Ordinateur principal", "Sauvegarde automatique",
                        "Sauvegarde automatique enregistrée et vérifiée.", ["Sauvegarde"]);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Échec de la sauvegarde automatique");
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
