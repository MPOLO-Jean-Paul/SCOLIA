using System.Net;

namespace Scolia.Tests;

/// <summary>Données (version 79) : empreinte des sauvegardes, restauration refusée sur empreinte modifiée, nettoyage à la demande, sauvegarde avant clôture.</summary>
public sealed class DataSafetyTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private string BackupPath(string name) => Path.Combine(app.DataDirectory, "sauvegardes", name);

    [Fact]
    public async Task Each_backup_gets_a_fingerprint_that_is_checked_before_restore()
    {
        var admin = await app.InstalledAdmin();
        var backup = await admin.Post("/api/sauvegardes", new { }).Ok();
        var name = backup.GetProperty("name").GetString()!;
        Assert.False(string.IsNullOrEmpty(backup.GetProperty("sha256").GetString()));

        var check = await admin.GetAsync($"/api/sauvegardes/{name}/verification").Ok();
        Assert.True(check.GetProperty("intact").GetBoolean());
        Assert.True(check.GetProperty("fingerprintMatches").GetBoolean());

        // Une copie modifiée après coup n'est plus restaurable : son empreinte ne concorde plus.
        await File.AppendAllTextAsync(BackupPath(name), "modification");
        var altered = await admin.GetAsync($"/api/sauvegardes/{name}/verification").Ok();
        Assert.False(altered.GetProperty("fingerprintMatches").GetBoolean());
        var (status, body) = await admin.Post($"/api/sauvegardes/{name}/restauration", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("empreinte", body.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cleanup_is_manual_and_keeps_the_most_recent_backups()
    {
        var admin = await app.InstalledAdmin();
        // Une sauvegarde porte l'horodatage à la seconde : on espace les créations pour obtenir trois fichiers distincts.
        for (var i = 0; i < 3; i++)
        {
            await admin.Post("/api/sauvegardes", new { }).Ok();
            await Task.Delay(1100);
        }
        var before = (await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray().Count();
        Assert.Equal(3, before);

        var cleaned = await admin.Post("/api/sauvegardes/nettoyage", new { garder = 1, plusAnciennesQue = 0 }).Ok();
        Assert.Equal(2, cleaned.GetProperty("supprimees").GetInt32());
        Assert.Single((await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray());
    }

    [Fact]
    public async Task Closing_a_school_year_first_saves_the_whole_database()
    {
        var admin = await app.InstalledAdmin();
        var year = (await admin.GetAsync("/api/etablissement/annees").Ok()).EnumerateArray().Single(y => y.GetProperty("isCurrent").GetBoolean());
        await admin.Post($"/api/etablissement/annees/{year.GetProperty("id").GetString()}/cloture", new { }).Ok();

        var names = (await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
        Assert.Contains(names, n => n.Contains("avantcloture", StringComparison.Ordinal));
    }
}
