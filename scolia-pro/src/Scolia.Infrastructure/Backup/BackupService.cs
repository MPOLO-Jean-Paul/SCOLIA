using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

public sealed record BackupInfo(string Name, DateTime CreatedUtc, long SizeBytes);

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
            }
            if (!await IsIntactAsync(target, ct)) throw new IOException("La sauvegarde créée n'a pas passé le contrôle d'intégrité.");
            Prune();
            var info = new FileInfo(target);
            logger.LogInformation("Sauvegarde créée : {Name} ({Size} octets)", name, info.Length);
            return new BackupInfo(name, info.CreationTimeUtc, info.Length);
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
            .Select(f => new BackupInfo(f.Name, f.LastWriteTimeUtc, f.Length)).ToList();
    }

    public string PathOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.StartsWith("scolia-", StringComparison.Ordinal) || !name.EndsWith(".db", StringComparison.Ordinal))
            throw new FileNotFoundException("Sauvegarde introuvable.");
        var path = Path.Combine(settings.BackupDirectory, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Sauvegarde introuvable.");
        return path;
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
        var path = PathOf(name);
        if (!await IsIntactAsync(path, ct)) throw new InvalidDataException("Cette sauvegarde est endommagée : restauration refusée.");
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

    private void Prune()
    {
        var limit = DateTime.UtcNow.AddDays(-Math.Max(1, settings.BackupRetentionDays));
        var files = new DirectoryInfo(settings.BackupDirectory).GetFiles("scolia-*.db").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var file in files.Skip(10).Where(f => f.LastWriteTimeUtc < limit))
        {
            try { file.Delete(); } catch (IOException ex) { logger.LogWarning(ex, "Ancienne sauvegarde non supprimée : {Name}", file.Name); }
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
