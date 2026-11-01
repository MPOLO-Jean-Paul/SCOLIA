using System.Net;
using System.Net.Http.Json;

namespace Scolia.Tests;

/// <summary>Rapport journalier : PDF du jour, réglages d'envoi vérifiés, envoi manuel honnête sans compte WhatsApp.</summary>
public sealed class DailyReportApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Le_rapport_du_jour_est_un_pdf()
    {
        var admin = await app.InstalledAdmin();
        var response = await admin.GetAsync("/api/rapports/journalier");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public async Task Les_reglages_sont_verifies_et_le_jeton_ne_revient_jamais()
    {
        var admin = await app.InstalledAdmin();
        var noNumber = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", new { enabled = true, time = "18:00", recipients = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, noNumber.StatusCode);
        var badTime = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", new { enabled = true, time = "25:90", recipients = new[] { "0812345678" } });
        Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);

        var saved = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", new
        {
            enabled = true, time = "19:30", recipients = new[] { "0812345678", "+243991234567" },
            whatsAppPhoneNumberId = "1234567890", whatsAppToken = "jeton-secret-de-test",
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var text = await (await admin.GetAsync("/api/rapports/journalier/reglages")).Content.ReadAsStringAsync();
        Assert.Contains("19:30", text);
        Assert.Contains("\"hasToken\":true", text);
        Assert.DoesNotContain("jeton-secret-de-test", text);
        // Sur le disque aussi, le jeton est chiffré.
        var file = Directory.GetFiles(app.DataDirectory, "rapport-journalier.json", SearchOption.AllDirectories).Single();
        Assert.DoesNotContain("jeton-secret-de-test", File.ReadAllText(file));
    }

    [Fact]
    public async Task Sans_compte_whatsapp_l_envoi_prepare_le_pdf_et_le_dit()
    {
        var admin = await app.InstalledAdmin();
        await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", new { enabled = false, time = "18:00", recipients = new[] { "0812345678" } });
        var sent = await admin.PostAsync("/api/rapports/journalier/envoyer", null);
        Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
        var body = await sent.Content.ReadAsStringAsync();
        Assert.Contains("non configuré", body);
        Assert.Contains("rapport-journalier-", body);
    }
}

/// <summary>Heure d'envoi : le jour du rapport, avec le rattrapage de la veille si l'ordinateur était éteint.</summary>
public sealed class DailyReportScheduleTests
{
    private static readonly TimeOnly At = new(18, 0);

    [Theory]
    [InlineData("2026-10-31", "2026-11-01 17:59", null)]          // pas encore l'heure, la veille est déjà partie
    [InlineData("2026-10-31", "2026-11-01 18:00", "2026-11-01")]  // l'heure est passée : le rapport du jour part
    [InlineData("2026-11-01", "2026-11-01 21:00", null)]          // déjà parti aujourd'hui
    [InlineData("2026-10-30", "2026-11-01 08:00", "2026-10-31")]  // éteint hier à 18 h : rattrapage de la veille
    public void Le_bon_jour_part_au_bon_moment(string last, string now, string? expected)
    {
        var due = Scolia.Infrastructure.Reports.DailyReportSettings.Due(last, DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture), At);
        Assert.Equal(expected, due?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }
}
