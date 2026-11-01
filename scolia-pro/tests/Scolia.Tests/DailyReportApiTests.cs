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

/// <summary>Envoi par e-mail : un vrai dialogue SMTP avec un petit serveur de messagerie lancé pour le test.</summary>
public sealed class DailyReportEmailTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Le_rapport_part_aussi_par_email_avec_le_pdf_en_piece_jointe()
    {
        using var smtp = new FakeSmtpServer();
        var admin = await app.InstalledAdmin();
        var saved = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages", new
        {
            enabled = true, time = "18:00", recipients = Array.Empty<string>(),
            emailRecipients = new[] { "direction@ecole.cd" },
            smtpHost = "127.0.0.1", smtpPort = smtp.Port, smtpSsl = false, smtpFrom = "scolia@ecole.cd",
            smtpUser = "scolia@ecole.cd", smtpPassword = "mot-de-passe-messagerie",
        });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var settings = await (await admin.GetAsync("/api/rapports/journalier/reglages")).Content.ReadAsStringAsync();
        Assert.Contains("\"emailReady\":true", settings);
        Assert.DoesNotContain("mot-de-passe-messagerie", settings);
        var file = Directory.GetFiles(app.DataDirectory, "rapport-journalier.json", SearchOption.AllDirectories).Single();
        Assert.DoesNotContain("mot-de-passe-messagerie", File.ReadAllText(file));

        var sent = await admin.PostAsync("/api/rapports/journalier/envoyer", null);
        var body = await sent.Content.ReadAsStringAsync();
        Assert.True(sent.IsSuccessStatusCode, body);
        Assert.Contains("envoyé par e-mail à 1 adresse(s)", body);
        var mail = smtp.Messages.Single();
        Assert.Contains("RCPT TO:<direction@ecole.cd>", mail.Envelope);
        Assert.Contains("application/pdf", mail.Data);
        Assert.Contains("rapport-journalier-", mail.Data);
    }

    [Fact]
    public async Task Une_adresse_invalide_est_refusee()
    {
        var admin = await app.InstalledAdmin();
        var refused = await admin.PutAsJsonAsync("/api/rapports/journalier/reglages",
            new { enabled = true, time = "18:00", emailRecipients = new[] { "direction-ecole" } });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}

/// <summary>Serveur SMTP minimal : il accepte les messages et les garde pour le test, sans rien envoyer ailleurs.</summary>
public sealed class FakeSmtpServer : IDisposable
{
    public sealed record Received(string Envelope, string Data);
    private readonly System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    public System.Collections.Concurrent.ConcurrentQueue<Received> Messages { get; } = new();
    public int Port => ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

    public FakeSmtpServer()
    {
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception) { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(System.Net.Sockets.TcpClient client)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 test ESMTP");
        var envelope = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            var verb = line.Split(' ', 2)[0].ToUpperInvariant();
            if (verb is "EHLO") { await writer.WriteLineAsync("250-test"); await writer.WriteLineAsync("250-AUTH LOGIN PLAIN"); await writer.WriteLineAsync("250 OK"); }
            else if (verb is "HELO") await writer.WriteLineAsync("250 OK");
            else if (verb is "AUTH") await writer.WriteLineAsync("235 OK");
            else if (verb is "MAIL" or "RCPT") { envelope.AppendLine(line); await writer.WriteLineAsync("250 OK"); }
            else if (verb is "DATA")
            {
                await writer.WriteLineAsync("354 Envoyez");
                var data = new System.Text.StringBuilder();
                while (await reader.ReadLineAsync() is { } d && d != ".") data.AppendLine(d);
                Messages.Enqueue(new Received(envelope.ToString(), data.ToString()));
                await writer.WriteLineAsync("250 OK");
            }
            else if (verb is "QUIT") { await writer.WriteLineAsync("221 Au revoir"); return; }
            else await writer.WriteLineAsync("250 OK");
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
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
