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
            Directory.CreateDirectory(settings.BackupDirectory);
            var safeReason = new string(reason.Where(char.IsAsciiLetterOrDigit).ToArray());
            var name = $"scolia-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{(safeReason.Length == 0 ? "manuelle" : safeReason)}.db";
            var target = Path.Combine(settings.BackupDirectory, name);
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
public sealed class AutomaticBackupWorker(BackupService backups, IOptions<ScoliaStorageOptions> options, ILogger<AutomaticBackupWorker> logger) : BackgroundService
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
                if (latest is null || latest.CreatedUtc < DateTime.UtcNow.AddHours(-hours)) await backups.CreateAsync("automatique", stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Échec de la sauvegarde automatique");
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
