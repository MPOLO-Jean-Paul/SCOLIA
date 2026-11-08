using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Api.Cloud;
using Scolia.Api.Security;
using Scolia.Domain.Cloud;
using Scolia.Domain.Common;

namespace Scolia.Tests;

/// <summary>
/// Continuité en ligne de bout en bout, avec une fausse console JP-Services en mémoire :
/// activation, copie chiffrée par l'école puis récupérée, opérations déposées pendant une coupure
/// appliquées une seule fois au nom de leur auteur, et opérations frauduleuses refusées.
/// </summary>
public sealed class CloudContinuityTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly ScoliaApp app = new();

    public CloudContinuityTests() => app.Settings["Scolia:Licence:ServiceUrl"] = "https://console.example.org/";

    public void Dispose() => app.Dispose();

    /// <summary>Console JP-Services simulée : mêmes routes et mêmes contrôles d'empreinte que le Worker.</summary>
    private sealed class FakeConsole : HttpMessageHandler
    {
        public string SchoolToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string DepositToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string? KeyFingerprint { get; private set; }
        public bool Enabled { get; private set; }
        /// <summary>Case « Activer la sauvegarde en ligne pour cette école » de la console.</summary>
        public bool CloudRequested { get; set; }
        public List<(Guid Id, byte[] Bytes, string Sha, string Key, long At)> Backups { get; } = [];
        public List<(string OpId, string Payload)> Mailbox { get; } = [];
        public Dictionary<string, string> Outcomes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var auth = request.Headers.Authorization?.Parameter;
            static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
                new(status) { Content = JsonContent.Create(body) };
            if (path == "/api/v1/register")
                return Json(new { school_id = Guid.NewGuid().ToString(), school_token = SchoolToken }, HttpStatusCode.Created);
            if (auth != SchoolToken) return Json(new { error = "jeton" }, HttpStatusCode.Unauthorized);
            if (path == "/api/v1/heartbeat")
                return Json(new
                {
                    license_status = "free", access_mode = "full", @readonly = false, billing_enabled = false, days_remaining = (int?)null,
                    expires_at = (long?)null, issued_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), reason = "facturation_desactivee",
                    offline_grace_seconds = 0, next_check_seconds = 900, cloud_requested = CloudRequested,
                });
            if (path == "/api/v1/server/cloud/enable")
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                Enabled = body.GetProperty("enabled").GetBoolean();
                if (!Enabled) return Json(new { enabled = false });
                KeyFingerprint = body.GetProperty("key_fingerprint").GetString();
                return Json(new { enabled = true, deposit_token = DepositToken }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Put && path == "/api/v1/server/cloud/backups")
            {
                var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (sha != query["sha256"]) return Json(new { error = "altéré" }, HttpStatusCode.UnprocessableEntity);
                var id = Guid.NewGuid();
                Backups.Add((id, bytes, sha, query["key"]!, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return Json(new { id, size_bytes = bytes.Length }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/server/cloud/backups")
                return Json(new { backups = Backups.Select(b => new { id = b.Id, size_bytes = b.Bytes.Length, sha256 = b.Sha, key_fingerprint = b.Key, created_at = b.At }) });
            if (request.Method == HttpMethod.Get && path.StartsWith("/api/v1/server/cloud/backups/", StringComparison.Ordinal))
            {
                var backup = Backups.Single(b => b.Id == Guid.Parse(path[(path.LastIndexOf('/') + 1)..]));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(backup.Bytes) };
                response.Headers.Add("X-Scolia-Sha256", backup.Sha);
                response.Headers.Add("X-Scolia-Key", backup.Key);
                return response;
            }
            if (path == "/api/v1/server/cloud/mailbox")
                return Json(new { operations = Mailbox.Select((m, i) => new { op_id = m.OpId, device_hash = "x", sequence = i, payload = m.Payload, created_at = i }) });
            if (path == "/api/v1/server/cloud/mailbox/ack")
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                foreach (var item in body.GetProperty("outcomes").EnumerateArray())
                {
                    var opId = item.GetProperty("op_id").GetString()!;
                    Outcomes[opId] = item.GetProperty("outcome").GetString()!;
                    Mailbox.RemoveAll(m => m.OpId == opId);
                }
                return Json(new { acknowledged = Outcomes.Count });
            }
            return Json(new { error = "inconnu" }, HttpStatusCode.NotFound);
        }
    }

    private async Task<(HttpClient Admin, FakeConsole Console)> ConnectedAsync()
    {
        var admin = await app.InstalledAdmin();
        var console = new FakeConsole();
        var license = app.Services.GetRequiredService<SchoolLicenseService>();
        license.OwnerHandler = console;
        app.Services.GetRequiredService<CloudDispatcher>().HandlerFactory = () => app.Server.CreateHandler();
        await license.EnrollAsync("ITM Cœur de Dieu", "invitation-jp-services-2026-test", true, CancellationToken.None);
        return (admin, console);
    }

    private static object Staff(string lastName) => new
    {
        lastName, middleName = "", firstName = "Paul", gender = "M", function = "Enseignant", phone = "0812345678", email = (string?)null,
        hireDate = new DateOnly(2020, 1, 1), exitDate = (DateOnly?)null, baseSalary = 300000m, isActive = true,
    };

    [Fact]
    public async Task Online_backup_is_encrypted_by_the_school_and_recoverable_with_the_recovery_code()
    {
        var (admin, console) = await ConnectedAsync();
        var (refused, _) = await admin.Post("/api/nuage/activation", new { enabled = true, confirm = false }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        var status = await admin.Post("/api/nuage/activation", new { enabled = true, confirm = true }).Ok();
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.False(status.GetProperty("recoveryConfirmed").GetBoolean());

        var code = (await admin.GetAsync("/api/nuage/code-secours").Ok()).GetProperty("code").GetString()!;
        var key = CloudSeal.ParseRecoveryCode(code);
        Assert.Equal(console.KeyFingerprint, CloudSeal.Fingerprint(key));
        await admin.Post("/api/nuage/code-secours/confirmation", new { }).Ok();

        await admin.Post("/api/personnel", Staff("MUKENDI")).Ok();
        status = await admin.Post("/api/nuage/sauvegarder", new { }).Ok();
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("lastBackupAt").ValueKind);
        var sent = Assert.Single(console.Backups);

        // Ce que reçoit la console est illisible sans la clé de l'école ; avec le code de secours, c'est la base.
        Assert.DoesNotContain("SQLite format 3", Encoding.ASCII.GetString(sent.Bytes), StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => CloudSeal.Open(sent.Bytes, CloudSeal.NewKey(), CloudSeal.BackupContext));
        var database = CloudSeal.Decompress(CloudSeal.Open(sent.Bytes, key, CloudSeal.BackupContext), 1L << 30);
        Assert.StartsWith("SQLite format 3", Encoding.ASCII.GetString(database, 0, 15), StringComparison.Ordinal);

        // Récupération : copie vérifiée rangée parmi les sauvegardes locales, jamais restaurée d'office.
        var listed = await admin.GetAsync("/api/nuage/sauvegardes").Ok();
        Assert.True(listed[0].GetProperty("sameKey").GetBoolean());
        var (wrongCode, _) = await admin.Post($"/api/nuage/sauvegardes/{sent.Id}/recuperer", new { recoveryCode = "ABCD-EFGH" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, wrongCode);
        var recovered = await admin.Post($"/api/nuage/sauvegardes/{sent.Id}/recuperer", new { recoveryCode = code }).Ok();
        var name = recovered.GetProperty("name").GetString()!;
        Assert.Contains("enligne", name, StringComparison.Ordinal);
        Assert.Contains((await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray(), b => b.GetProperty("name").GetString() == name);

        // Arrêt : plus rien n'est envoyé.
        await admin.Post("/api/nuage/activation", new { enabled = false, confirm = true }).Ok();
        Assert.False(console.Enabled);
        var (stopped, _) = await admin.Post("/api/nuage/sauvegarder", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, stopped);
    }

    [Fact]
    public async Task Operations_deposited_during_an_outage_are_applied_once_as_their_author()
    {
        var (admin, console) = await ConnectedAsync();
        await admin.Post("/api/nuage/activation", new { enabled = true, confirm = true }).Ok();
        var access = await admin.GetAsync("/api/nuage/depot").Ok();
        Assert.True(access.GetProperty("enabled").GetBoolean());
        Assert.Equal(console.DepositToken, access.GetProperty("depositToken").GetString());
        var depositKey = Convert.FromBase64String(access.GetProperty("depositKey").GetString()!);
        var delegation = access.GetProperty("delegation").GetString()!;

        string Deposit(string path, string body, string proof)
        {
            var key = Guid.NewGuid().ToString("N");
            var operation = new CloudOperation(key, "POST", path, body, "Pointage", proof, DateTime.UtcNow, "telephone-de-test");
            var sealedBytes = CloudSeal.Seal(JsonSerializer.SerializeToUtf8Bytes(operation, Web),
                depositKey, CloudSeal.OperationContext);
            console.Mailbox.Add((key, Base64Url.EncodeToString(sealedBytes)));
            return key;
        }
        var body = JsonSerializer.Serialize(Staff("KALENGA"), Http.Json);
        var good = Deposit("api/personnel", body, delegation);
        var forged = Deposit("api/personnel", JsonSerializer.Serialize(Staff("FRAUDE"), Http.Json), "preuve-inventee");
        var forbidden = Deposit("api/utilisateurs", "{\"userName\":\"pirate\",\"displayName\":\"Pirate\",\"role\":\"Secretariat\",\"password\":\"Pirate2026!\"}", delegation);
        console.Mailbox.Add((Guid.NewGuid().ToString("N"), "illisible-" + new string('x', 40)));

        var drained = await admin.Post("/api/nuage/releve", new { }).Ok();
        Assert.Equal(1, drained.GetProperty("applied").GetInt32());
        Assert.Empty(console.Mailbox);
        Assert.Equal("applied", console.Outcomes[good]);
        Assert.Equal("refused", console.Outcomes[forged]);
        Assert.Equal("refused", console.Outcomes[forbidden]);
        Assert.Equal(3, drained.GetProperty("status").GetProperty("refused").GetInt32());

        var staff = (await admin.GetAsync("/api/personnel").Ok()).EnumerateArray().ToList();
        Assert.Single(staff, s => s.GetProperty("lastName").GetString() == "KALENGA");
        Assert.DoesNotContain(staff, s => s.GetProperty("lastName").GetString() == "FRAUDE");
        Assert.DoesNotContain((await admin.GetAsync("/api/utilisateurs").Ok()).EnumerateArray(), u => u.GetProperty("userName").GetString() == "pirate");

        // Le téléphone revient et renvoie lui-même l'opération : même clé, même corps → aucun doublon.
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/personnel") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        replay.Headers.Add("Idempotency-Key", good);
        using var response = await admin.SendAsync(replay);
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.Contains("Idempotent-Replayed"));
        Assert.Single((await admin.GetAsync("/api/personnel").Ok()).EnumerateArray(), s => s.GetProperty("lastName").GetString() == "KALENGA");

        // Les opérations rejouées en ligne exigent le secret du serveur : un en-tête deviné est refusé.
        using var guess = new HttpRequestMessage(HttpMethod.Post, "/api/personnel") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        guess.Headers.Add(CloudDispatcher.SecretHeader, "devine");
        guess.Headers.Add(CloudDispatcher.UserHeader, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().SendAsync(guess)).StatusCode);
    }

    [Fact]
    public async Task Activation_requested_from_the_console_runs_without_anyone_but_never_overrides_the_school()
    {
        var (admin, console) = await ConnectedAsync();
        var license = app.Services.GetRequiredService<SchoolLicenseService>();
        var cloud = app.Services.GetRequiredService<CloudContinuityService>();

        // Rien n'est demandé : rien n'est activé.
        await cloud.RunOnceAsync(CancellationToken.None);
        Assert.False(cloud.Status.Enabled);

        // JP-Services coche l'école dans sa console : au contrôle suivant, le serveur active seul,
        // génère la clé de l'école et envoie aussitôt une première copie.
        console.CloudRequested = true;
        await license.RefreshAsync();
        Assert.True(license.CloudRequested);
        await cloud.RunOnceAsync(CancellationToken.None);
        Assert.True(cloud.Status.Enabled);
        Assert.True(cloud.Status.ActivatedByOwner);
        Assert.False(cloud.Status.RecoveryConfirmed);
        Assert.Single(console.Backups);
        Assert.Equal(console.KeyFingerprint, cloud.Status.KeyFingerprint);
        // Pas de nouvelle copie tant que rien ne change dans l'heure.
        await cloud.RunOnceAsync(CancellationToken.None);
        Assert.Single(console.Backups);

        // Le responsable de l'école arrête : la console ne peut pas la relancer contre son avis.
        await admin.Post("/api/nuage/activation", new { enabled = false, confirm = true }).Ok();
        await cloud.RunOnceAsync(CancellationToken.None);
        Assert.False(cloud.Status.Enabled);

        // Il la réactive lui-même ; si JP-Services retire ensuite sa proposition, le choix de l'école demeure.
        await admin.Post("/api/nuage/activation", new { enabled = true, confirm = true }).Ok();
        console.CloudRequested = false;
        await license.RefreshAsync();
        await cloud.RunOnceAsync(CancellationToken.None);
        Assert.True(cloud.Status.Enabled);
        Assert.False(cloud.Status.ActivatedByOwner);
    }

    [Fact]
    public void Recovery_code_round_trips_and_detects_typing_errors()
    {
        var key = CloudSeal.NewKey();
        var code = CloudSeal.RecoveryCode(key);
        Assert.Equal(14, code.Split('-').Length);
        Assert.Equal(key, CloudSeal.ParseRecoveryCode(code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)));
        var typo = (code[0] == 'A' ? 'B' : 'A') + code[1..];
        Assert.Throws<DomainException>(() => CloudSeal.ParseRecoveryCode(typo));
        var sealedBytes = CloudSeal.Seal("bonjour"u8, key, CloudSeal.OperationContext);
        Assert.Throws<DomainException>(() => CloudSeal.Open(sealedBytes, key, CloudSeal.BackupContext));
        sealedBytes[^1] ^= 1;
        Assert.Throws<DomainException>(() => CloudSeal.Open(sealedBytes, key, CloudSeal.OperationContext));
        Assert.True(CloudOperationRules.Queueable("POST", "api/paiements"));
        Assert.False(CloudOperationRules.Queueable("POST", "api/paiements/annulation"));
        Assert.False(CloudOperationRules.Queueable("POST", "api/utilisateurs"));
        Assert.False(CloudOperationRules.Queueable("POST", "api/personnel/../utilisateurs"));
        Assert.False(CloudOperationRules.Queueable("DELETE", "api/apprenants/1"));
    }
}
