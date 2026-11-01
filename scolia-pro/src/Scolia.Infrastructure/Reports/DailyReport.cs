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
    public string? LastDate { get; set; }
    public string? LastStatus { get; set; }
    public DateTime? LastRunUtc { get; set; }

    public bool WhatsAppReady => !string.IsNullOrWhiteSpace(WhatsAppPhoneNumberId) && !string.IsNullOrWhiteSpace(WhatsAppToken);

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
/// Lecture et écriture des réglages, une à la fois. Le jeton WhatsApp est chiffré sur le disque par la protection
/// de clés du serveur : copié ailleurs, le fichier ne le révèle pas.
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
            var token = settings.WhatsAppToken;
            settings.WhatsAppToken = Seal(token);
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json)); }
            finally { settings.WhatsAppToken = token; }
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

/// <summary>Résultat d'un passage : le PDF fait, ce qui a été envoyé, et le message à montrer.</summary>
public sealed record DailyReportRun(DateOnly Date, string FileName, string Status, DailyReportSummary Summary);

/// <summary>Prépare le rapport d'une journée, l'enregistre, l'envoie si possible, et prévient les autres postes.</summary>
public sealed class DailyReportRunner(IServiceScopeFactory scopes, DailyReportStore store, WhatsAppCloudSender sender,
    Live.LiveHub live, DataRevision revision, ILogger<DailyReportRunner> logger)
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
        string status;
        if (!send) status = "PDF préparé";
        else if (settings.Recipients.Count == 0) status = "PDF préparé ; aucun numéro configuré pour l'envoi";
        else if (!settings.WhatsAppReady) status = "PDF préparé ; envoi WhatsApp automatique non configuré, partagez-le depuis SCOLIA";
        else
        {
            try
            {
                status = await sender.SendAsync(settings, countryCode, pdf, name,
                    $"Rapport journalier du {day:dd/MM/yyyy} · {summary.SchoolName}\n{summary.Text}", ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                logger.LogWarning(ex, "Envoi WhatsApp du rapport journalier impossible");
                status = "PDF préparé ; WhatsApp injoignable, l'envoi n'a pas pu se faire";
            }
        }
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
