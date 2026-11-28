using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scolia.Domain.Common;
using Scolia.Infrastructure;
using Scolia.Infrastructure.Backup;

namespace Scolia.Tests;

public sealed class BackupScheduleTests
{
    [Fact]
    public void La_planification_survit_a_un_nouveau_chargement_et_un_reglage_invalide_ne_l_ecrase_pas()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scolia-planification-" + Guid.NewGuid());
        var options = Options.Create(new ScoliaStorageOptions { DataDirectory = directory });
        try
        {
            var store = new BackupScheduleStore(options, NullLogger<BackupScheduleStore>.Instance);
            var chosen = new BackupSchedule(false, 6, 3, 90, 10);
            store.Save(chosen);
            Assert.Throws<DomainException>(() => store.Save(chosen with { LocalIntervalHours = 0 }));
            Assert.Equal(chosen, new BackupScheduleStore(options, NullLogger<BackupScheduleStore>.Instance).Read());
            File.WriteAllText(Path.Combine(directory, "backup-schedule.json"), "fichier interrompu");
            Assert.Equal(24, new BackupScheduleStore(options, NullLogger<BackupScheduleStore>.Instance).Read().LocalIntervalHours);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, 5.99, false)]
    [InlineData(true, 6d, true)]
    [InlineData(true, 7d, true)]
    [InlineData(false, 7d, false)]
    public void La_frequence_et_la_desactivation_sont_respectees(bool enabled, double? elapsed, bool expected)
    {
        var now = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, BackupScheduleStore.IsDue(new BackupSchedule(enabled, 6), elapsed.HasValue ? now.AddHours(-elapsed.Value) : null, now));
    }

    [Fact]
    public async Task Les_parametres_sont_enregistres_valides_et_reserves_aux_responsables_autorises()
    {
        using var app = new ScoliaApp();
        var admin = await app.InstalledAdmin();
        var initial = await admin.GetAsync("/api/sauvegardes/parametres").Ok();
        Assert.Equal(24, initial.GetProperty("localIntervalHours").GetInt32());
        await admin.Put("/api/sauvegardes/parametres", new { enabled = false, localIntervalHours = 6, cloudIntervalHours = 3, cleanupAfterDays = 90, keepLatest = 10 }).Ok();
        var saved = await admin.GetAsync("/api/sauvegardes/parametres").Ok();
        Assert.False(saved.GetProperty("enabled").GetBoolean());
        Assert.Equal(6, saved.GetProperty("localIntervalHours").GetInt32());
        Assert.Equal(3, saved.GetProperty("cloudIntervalHours").GetInt32());
        Assert.Equal(10, saved.GetProperty("keepLatest").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/sauvegardes/parametres", new { enabled = true, localIntervalHours = 0, cloudIntervalHours = 25, cleanupAfterDays = 90, keepLatest = 10 })).StatusCode);
        Assert.Equal(6, (await admin.GetAsync("/api/sauvegardes/parametres").Ok()).GetProperty("localIntervalHours").GetInt32());
        await admin.Post("/api/utilisateurs", new { userName = "compta-plan", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        var accounting = await app.FirstLogin("compta-plan", "Provisoire1", "Compta2026!");
        Assert.Equal(HttpStatusCode.OK, (await accounting.GetAsync("/api/sauvegardes/parametres")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accounting.Put("/api/sauvegardes/parametres", new { enabled = true, localIntervalHours = 1 })).StatusCode);
    }
}
