using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scolia.Domain.Common;

namespace Scolia.Infrastructure.Backup;

public sealed record BackupSchedule(bool Enabled = true, int LocalIntervalHours = 24,
    int CloudIntervalHours = 1, int CleanupAfterDays = 30, int KeepLatest = 5);

/// <summary>Réglages du serveur, persistés sans redémarrage ; aucun chemin ni secret transmis par un poste client.</summary>
public sealed class BackupScheduleStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly string path;
    private BackupSchedule current;

    public BackupScheduleStore(IOptions<ScoliaStorageOptions> options, ILogger<BackupScheduleStore> logger)
    {
        var storage = options.Value;
        path = Path.Combine(storage.DataDirectory, "backup-schedule.json");
        current = new BackupSchedule(LocalIntervalHours: storage.AutomaticBackupHours is >= 1 and <= 168 ? storage.AutomaticBackupHours : 24,
            CleanupAfterDays: storage.BackupRetentionDays is >= 1 and <= 3650 ? storage.BackupRetentionDays : 30);
        if (!File.Exists(path)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<BackupSchedule>(File.ReadAllText(path), Json);
            if (loaded is not null) { Validate(loaded); current = loaded; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or DomainException)
        { logger.LogWarning("Planification des sauvegardes illisible : les réglages par défaut sont utilisés."); }
    }

    public BackupSchedule Read() { lock (gate) return current; }

    public BackupSchedule Save(BackupSchedule value)
    {
        Validate(value);
        lock (gate)
        {
            var temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(temporary, path, overwrite: true);
                current = value;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return current;
        }
    }

    public static void Validate(BackupSchedule value)
    {
        if (value.LocalIntervalHours is < 1 or > 168)
            throw new DomainException("localIntervalHours", "Choisissez une fréquence locale entre 1 et 168 heures.");
        if (value.CloudIntervalHours is < 1 or > 24)
            throw new DomainException("cloudIntervalHours", "Choisissez une fréquence en ligne entre 1 et 24 heures.");
        if (value.CleanupAfterDays is < 1 or > 3650)
            throw new DomainException("cleanupAfterDays", "L'âge minimal de nettoyage doit être compris entre 1 et 3650 jours.");
        if (value.KeepLatest is < 1 or > 1000)
            throw new DomainException("keepLatest", "Conservez entre 1 et 1000 copies récentes.");
    }

    public static bool IsDue(BackupSchedule schedule, DateTime? latestUtc, DateTime utcNow) =>
        schedule.Enabled && (latestUtc is null || utcNow - latestUtc.Value >= TimeSpan.FromHours(schedule.LocalIntervalHours));
}
