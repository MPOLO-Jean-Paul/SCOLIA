using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scolia.Application.Services;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Backup;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Infrastructure.Reports;

/// <summary>
/// Réglages du rapport journalier, gardés dans le dossier des données de l'ordinateur principal. Le jeton WhatsApp
/// n'en sort jamais : l'interface ne voit que « configuré » ou « non configuré ».
/// </summary>
public sealed class DailyReportSettings
{
    public bool Enabled { get; set; }
    /// <summary>Heure d'envoi, au format 18:00, heure de l'ordinateur principal.</summary>
    public string Time { get; set; } = "18:00";
    /// <summary>Numéros qui reçoivent le rapport : 0812345678 ou +243812345678.</summary>
    public List<string> Recipients { get; set; } = [];
    public string? WhatsAppPhoneNumberId { get; set; }
    public string? WhatsAppToken { get; set; }
    /// <summary>Modèle de message approuvé par Meta, avec un document en en-tête : nécessaire pour écrire en premier.</summary>
    public string? WhatsAppTemplate { get; set; }
    public string WhatsAppLanguage { get; set; } = "fr";
    /// <summary>Adresses e-mail qui reçoivent aussi le rapport, en pièce jointe.</summary>
    public List<string> EmailRecipients { get; set; } = [];
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    /// <summary>Connexion chiffrée (STARTTLS), exigée par Gmail, Outlook et la plupart des messageries.</summary>
    public bool SmtpSsl { get; set; } = true;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    /// <summary>Adresse qui envoie ; par défaut, l'identifiant de connexion.</summary>
    public string? SmtpFrom { get; set; }
    public string? LastDate { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? LastRunUtc { get; set; }

    public bool WhatsAppReady => !string.IsNullOrWhiteSpace(WhatsAppPhoneNumberId) && !string.IsNullOrWhiteSpace(WhatsAppToken);
    public bool EmailReady => !string.IsNullOrWhiteSpace(SmtpHost) && SmtpPort is > 0 and < 65536
        && !string.IsNullOrWhiteSpace(SmtpFrom ?? SmtpUser);

    /// <summary>
    /// Journée dont le rapport doit partir maintenant : aujourd'hui une fois l'heure passée, sinon hier s'il n'est pas
    /// parti (ordinateur éteint à l'heure prévue). Rien si le dernier rapport envoyé est déjà celui-là.
    /// </summary>
    public static DateOnly? Due(string? lastDate, DateTime now, TimeOnly at)
    {
        var today = DateOnly.FromDateTime(now);
        var target = TimeOnly.FromDateTime(now) >= at ? today : today.AddDays(-1);
        var last = DateOnly.TryParseExact(lastDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null;
        return last is null || last < target ? target : null;
    }

    public static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

/// <summary>
/// Lecture et écriture des réglages, une à la fois. Le jeton WhatsApp et le mot de passe de la messagerie sont chiffrés
/// sur le disque par la protection de clés du serveur : copié ailleurs, le fichier ne les révèle pas.
/// </summary>
public sealed class DailyReportStore(IOptions<ScoliaStorageOptions> options, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider? protection = null)
{
    private const string Sealed = "scellé:";
    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector? protector =
        protection is null ? null : Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.CreateProtector(protection, "Scolia.RapportJournalier.WhatsApp");

    private string? Seal(string? token) => string.IsNullOrEmpty(token) || protector is null ? token : Sealed + protector.Protect(token);

    private string? Open(string? token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(Sealed, StringComparison.Ordinal) || protector is null) return token;
        // Clés du serveur perdues : le jeton est considéré comme absent, il suffit de le saisir à nouveau.
        try { return protector.Unprotect(token[Sealed.Length..]); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly ScoliaStorageOptions storage = options.Value;

    private string FilePath => Path.Combine(storage.DataDirectory, "rapport-journalier.json");
    public string ReportsDirectory => Path.Combine(storage.DataDirectory, "rapports");

    public DailyReportSettings Load()
    {
        lock (Gate)
        {
            try
            {
                var settings = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<DailyReportSettings>(File.ReadAllText(FilePath), Json) ?? new DailyReportSettings()
                    : new DailyReportSettings();
                settings.WhatsAppToken = Open(settings.WhatsAppToken);
                settings.SmtpPassword = Open(settings.SmtpPassword);
                return settings;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new DailyReportSettings(); }
        }
    }

    public void Save(DailyReportSettings settings)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(storage.DataDirectory);
            var temporary = FilePath + ".tmp";
            var (token, password) = (settings.WhatsAppToken, settings.SmtpPassword);
            settings.WhatsAppToken = Seal(token);
            settings.SmtpPassword = Seal(password);
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json)); }
            finally { (settings.WhatsAppToken, settings.SmtpPassword) = (token, password); }
            File.Move(temporary, FilePath, overwrite: true);
        }
    }

    public void Update(Action<DailyReportSettings> change)
    {
        lock (Gate)
        {
            var settings = Load();
            change(settings);
            Save(settings);
        }
    }
}

/// <summary>
/// Envoi par l'API WhatsApp Business de Meta (Cloud API) : le PDF est déposé, puis envoyé à chaque numéro. Avec un
/// modèle approuvé, SCOLIA peut écrire en premier ; sans modèle, WhatsApp n'accepte le document que si le destinataire
/// a écrit au numéro de l'établissement dans les dernières 24 heures.
/// </summary>
public sealed class WhatsAppCloudSender(IHttpClientFactory clients)
{
    public const string ApiVersion = "v21.0";

    public async Task<string> SendAsync(DailyReportSettings settings, string countryCode, byte[] pdf, string fileName, string caption, CancellationToken ct)
    {
        var http = clients.CreateClient("whatsapp");
        http.BaseAddress = new Uri($"https://graph.facebook.com/{ApiVersion}/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.WhatsAppToken!.Trim());
        var phoneId = settings.WhatsAppPhoneNumberId!.Trim();

        using var form = new MultipartFormDataContent
        {
            { new StringContent("whatsapp"), "messaging_product" },
            { new StringContent("application/pdf"), "type" },
        };
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        using var upload = await http.PostAsync($"{phoneId}/media", form, ct);
        var uploaded = await upload.Content.ReadAsStringAsync(ct);
        if (!upload.IsSuccessStatusCode) return $"dépôt du PDF refusé par WhatsApp ({Reason(uploaded, upload.StatusCode)})";
        var mediaId = JsonDocument.Parse(uploaded).RootElement.GetProperty("id").GetString();

        var sent = 0;
        var errors = new List<string>();
        foreach (var number in settings.Recipients)
        {
            var to = IdentityRules.ToInternational(number, countryCode);
            if (string.IsNullOrEmpty(to)) continue;
            object message = string.IsNullOrWhiteSpace(settings.WhatsAppTemplate)
                ? new { messaging_product = "whatsapp", to, type = "document", document = new { id = mediaId, filename = fileName, caption } }
                : new
                {
                    messaging_product = "whatsapp", to, type = "template",
                    template = new
                    {
                        name = settings.WhatsAppTemplate!.Trim(),
                        language = new { code = string.IsNullOrWhiteSpace(settings.WhatsAppLanguage) ? "fr" : settings.WhatsAppLanguage.Trim() },
                        components = new object[]
                        {
                            new { type = "header", parameters = new object[] { new { type = "document", document = new { id = mediaId, filename = fileName } } } },
                        },
                    },
                };
            using var response = await http.PostAsJsonAsync($"{phoneId}/messages", message, ct);
            if (response.IsSuccessStatusCode) sent++;
            else errors.Add($"{number} : {Reason(await response.Content.ReadAsStringAsync(ct), response.StatusCode)}");
        }
        return errors.Count == 0 ? $"envoyé par WhatsApp à {sent} numéro(s)"
            : $"envoyé à {sent} numéro(s), {errors.Count} refus : {string.Join(" ; ", errors)}";
    }

    private static string Reason(string body, System.Net.HttpStatusCode status)
    {
        try
        {
            if (JsonDocument.Parse(body).RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
                return message.GetString() ?? status.ToString();
        }
        catch (JsonException) { }
        return $"code {(int)status}";
    }
}

/// <summary>
/// Envoi par e-mail : un message avec le PDF en pièce jointe, par le serveur de messagerie de l'établissement (SMTP),
/// par exemple smtp.gmail.com, port 587, connexion chiffrée, avec un mot de passe d'application.
/// </summary>
public sealed class EmailReportSender
{
    public async Task<string> SendAsync(DailyReportSettings settings, byte[] pdf, string fileName, string subject, string body, CancellationToken ct)
    {
        var from = (settings.SmtpFrom ?? settings.SmtpUser)!.Trim();
        using var message = new System.Net.Mail.MailMessage { From = new System.Net.Mail.MailAddress(from, "SCOLIA Pro"), Subject = subject, Body = body };
        foreach (var address in settings.EmailRecipients) message.To.Add(address);
        using var attachment = new System.Net.Mail.Attachment(new MemoryStream(pdf), fileName, "application/pdf");
        message.Attachments.Add(attachment);
#pragma warning disable SYSLIB0014 // SmtpClient reste le client SMTP fourni par .NET ; il suffit pour un message par jour.
        using var client = new System.Net.Mail.SmtpClient(settings.SmtpHost!.Trim(), settings.SmtpPort)
        {
            EnableSsl = settings.SmtpSsl,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
            Timeout = 60_000,
        };
#pragma warning restore SYSLIB0014
        if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
            client.Credentials = new System.Net.NetworkCredential(settings.SmtpUser.Trim(), settings.SmtpPassword ?? string.Empty);
        await client.SendMailAsync(message, ct);
        return $"envoyé par e-mail à {settings.EmailRecipients.Count} adresse(s)";
    }
}

/// <summary>Résultat d'un passage : le PDF fait, ce qui a été envoyé, et le message à montrer.</summary>
public sealed record DailyReportRun(DateOnly Date, string FileName, string Status, DailyReportSummary Summary);

/// <summary>Prépare le rapport d'une journée, l'enregistre, l'envoie si possible, et prévient les autres postes.</summary>
public sealed class DailyReportRunner(IServiceScopeFactory scopes, DailyReportStore store, WhatsAppCloudSender sender,
    EmailReportSender mailer, Live.LiveHub live, DataRevision revision, ILogger<DailyReportRunner> logger)
{
    public async Task<DailyReportRun> RunAsync(DateOnly day, bool send, string trigger, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<DailyReportService>();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var (pdf, summary) = await reports.BuildAsync(day, ct);
        var name = DailyReportService.FileName(day);
        Directory.CreateDirectory(store.ReportsDirectory);
        await File.WriteAllBytesAsync(Path.Combine(store.ReportsDirectory, name), pdf, ct);

        var settings = store.Load();
        var countryCode = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).Select(s => s.PhoneCountryCode).FirstOrDefaultAsync(ct) ?? "243";
        var caption = $"Rapport journalier du {day:dd/MM/yyyy} · {summary.SchoolName}\n{summary.Text}";
        var status = send ? await SendAsync(settings, countryCode, pdf, name, caption, day, ct) : "PDF préparé";
        store.Update(s =>
        {
            s.LastRunUtc = DateTime.UtcNow;
            s.LastStatus = $"{day:dd/MM/yyyy} ({trigger}) : {status}";
            if (send && trigger == "automatique") s.LastDate = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        });
        // Les téléphones et les autres postes l'apprennent : bannière « Rapport du jour prêt ».
        live.Changed(revision.Value, null, "Ordinateur principal", "Rapport journalier",
            $"Rapport du {day:dd/MM/yyyy} prêt : {summary.Text}", ["RapportJournalier", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]);
        return new DailyReportRun(day, name, status, summary);
    }

    /// <summary>
    /// Les deux canaux, chacun s'il est configuré : WhatsApp vers les numéros, e-mail vers les adresses. L'échec de
    /// l'un n'empêche pas l'autre ; le message dit ce qui est parti et ce qui manque.
    /// </summary>
    private async Task<string> SendAsync(DailyReportSettings settings, string countryCode, byte[] pdf, string name, string caption,
        DateOnly day, CancellationToken ct)
    {
        var parts = new List<string>();
        if (settings.Recipients.Count > 0)
        {
            if (!settings.WhatsAppReady) parts.Add("WhatsApp non configuré, partagez le PDF depuis SCOLIA");
            else
            {
                try { parts.Add(await sender.SendAsync(settings, countryCode, pdf, name, caption, ct)); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    logger.LogWarning(ex, "Envoi WhatsApp du rapport journalier impossible");
                    parts.Add("WhatsApp injoignable, l'envoi n'a pas pu se faire");
                }
            }
        }
        if (settings.EmailRecipients.Count > 0)
        {
            if (!settings.EmailReady) parts.Add("messagerie non configurée");
            else
            {
                try { parts.Add(await mailer.SendAsync(settings, pdf, name, $"Rapport journalier du {day:dd/MM/yyyy}", caption, ct)); }
                catch (Exception ex) when (ex is System.Net.Mail.SmtpException or IOException or InvalidOperationException
                                           or FormatException or TaskCanceledException or System.Security.Authentication.AuthenticationException)
                {
                    logger.LogWarning(ex, "Envoi par e-mail du rapport journalier impossible");
                    parts.Add($"e-mail refusé ({ex.Message.TrimEnd('.')})");
                }
            }
        }
        return parts.Count == 0 ? "PDF préparé ; aucun numéro ni aucune adresse configurés pour l'envoi" : "PDF préparé ; " + string.Join(" ; ", parts);
    }
}


/// <summary>
/// Chaque minute : si l'heure choisie est passée et que le rapport du jour n'est pas parti, il part. Si l'ordinateur
/// principal était éteint à l'heure prévue la veille, le rapport de la veille part dès son démarrage (rattrapage).
/// </summary>
public sealed class DailyReportWorker(DailyReportRunner runner, DailyReportStore store, ILogger<DailyReportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = store.Load();
                var now = DateTime.Now;
                if (settings.Enabled && DailyReportSettings.TryTime(settings.Time, out var at)
                    && DailyReportSettings.Due(settings.LastDate, now, at) is { } day)
                    await runner.RunAsync(day, send: true, "automatique", stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Rapport journalier automatique impossible");
                store.Update(s => { s.LastStatus = $"Échec du rapport automatique : {ex.Message}"; s.LastDate = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); });
            }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
