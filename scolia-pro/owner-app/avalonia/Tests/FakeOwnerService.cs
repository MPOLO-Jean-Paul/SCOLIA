using System.Net;
using System.Text;

namespace JPServices.Owner.Tests;

/// <summary>Service propriétaire simulé : enregistre chaque requête et répond par des données réalistes.</summary>
internal sealed class FakeOwnerService : HttpMessageHandler
{
    public const string SchoolId = "3f2c8d0e-1b6a-4c55-9a3e-7d2f1c9b8a60";
    public const string RelayId = "9a1b2c3d-4e5f-4a6b-8c7d-0e1f2a3b4c5d";

    public List<(HttpMethod Method, string Path, Dictionary<string, string> Headers, string? Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage?>>? BeforeResponseAsync { get; set; }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static long Days(int n) => Now + n * 86_400_000L;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, headers, body));
        if (BeforeResponseAsync is { } delay && await delay(request, cancellationToken) is { } delayed) return delayed;
        if (Override?.Invoke(request) is { } custom) return custom;
        var path = request.RequestUri.AbsolutePath;
        var json = (request.Method.Method, path) switch
        {
            ("GET", "/api/v1/owner/summary") => $$$"""{"schools":3,"server_devices":4,"active_schools_30d":2,"suspended":1,"cloud_enabled":2,"cloud_backup_late":1,"storage_configured":true,"cloud_backup_bytes":734003200,"cloud_pending_operations":5}""",
            ("GET", "/api/v1/owner/schools") => $$$"""
                {"schools":[
                  {"id":"{{{SchoolId}}}","name":"Complexe scolaire La Référence","last_seen":{{{Now - 600_000}}},"device_count":2,"cloud_enabled":1,"cloud_requested":1,"last_backup_at":{{{Now - 3_600_000}}},"cloud_backup_late":false,
                   "license":{"license_status":"active","readonly":false,"days_remaining":212,"expires_at":{{{Days(212)}}},"billing_enabled":true,"manual_override":"none"}},
                  {"id":"7b1e0c4a-2d3f-4e5a-9b6c-1d2e3f4a5b6c","name":"Institut Mwinda","last_seen":{{{Now - 3 * 86_400_000L}}},"device_count":1,"cloud_enabled":1,"cloud_requested":1,"last_backup_at":{{{Now - 40 * 3_600_000L}}},"cloud_backup_late":true,
                   "license":{"license_status":"expired","readonly":true,"days_remaining":0,"expires_at":{{{Days(-4)}}},"billing_enabled":true,"manual_override":"none"}},
                  {"id":"c2d3e4f5-a6b7-4c8d-9e0f-1a2b3c4d5e6f","name":"École Les Étoiles","last_seen":{{{Now - 7_200_000}}},"device_count":1,"cloud_enabled":0,"cloud_requested":0,
                   "license":{"license_status":"trial","readonly":false,"days_remaining":12,"expires_at":{{{Days(12)}}},"billing_enabled":false,"manual_override":"none"}}]}
                """,
            ("GET", "/api/v1/owner/schools/" + SchoolId) => $$$"""
                {"school":{"id":"{{{SchoolId}}}","name":"Complexe scolaire La Référence","created_at":{{{Days(-150)}}},"paid_until":{{{Days(212)}}},"cloud_requested":1,"override_reason":null},
                 "license":{"license_status":"active","readonly":false,"days_remaining":212,"expires_at":{{{Days(212)}}},"trial_ends_at":{{{Days(-120)}}},"billing_enabled":true,"manual_override":"none"},
                 "devices":[{"device_name":"PC-SECRETARIAT","first_seen":{{{Days(-150)}}},"last_seen":{{{Now - 600_000}}},"last_ip":"41.243.10.8"}],
                 "activity":[{"activity_day":"2026-11-17","login_count":14,"heartbeat_count":96}],"cloud":{}}
                """,
            ("GET", "/api/v1/owner/servers") => $$$"""{"servers":[{"school_id":"{{{SchoolId}}}","school_name":"Complexe scolaire La Référence","device_name":"PC-SECRETARIAT","last_seen":{{{Now - 600_000}}},"status":"recent","public_ip":"41.243.10.8"},{"school_id":"7b1e0c4a-2d3f-4e5a-9b6c-1d2e3f4a5b6c","school_name":"Institut Mwinda","device_name":"SERVEUR-MWINDA","last_seen":{{{Now - 3 * 86_400_000L}}},"status":"not_recent","public_ip":null}]}""",
            ("GET", "/api/v1/owner/relays") => $$$"""{"relays":[{"id":"{{{RelayId}}}","label":"Relais Cloudflare principal","url":"wss://relais.example.workers.dev/","state":"active","revision":4,"updated_at":{{{Days(-2)}}}}]}""",
            ("GET", "/api/v1/owner/audit") => $$$"""{"history":[{"action":"license_policy_changed","school_name":"Institut Mwinda","created_at":{{{Now - 7_200_000}}}},{"action":"invitation_created","school_name":null,"created_at":{{{Days(-1)}}}}]}""",
            ("GET", "/api/v1/owner/settings/storage") => """{"mode":"credentials","ready":true,"account_id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","bucket":"scolia-demo-backups","access_key_id":"DEMO…2026","notice":"Stockage vérifié."}""",
            ("GET", "/api/v1/owner/relays/cloudflare") => """{"status":"available","configured":true,"explanation":"Catalogue du compte Cloudflare.","relays":[{"uid":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","label":"TURN JP-Services","provider":"Cloudflare Realtime TURN"}]}""",
            ("GET", "/api/v1/owner/relays/netlify") => """{"name":"Secours JP-Services","base_url":"https://scolia-demo.netlify.app","state":"disabled","configured":true,"can_activate":false,"notice":"Pilote en attente de validation."}""",
            ("POST", "/api/v1/owner/invitations") => $$$"""{"invite_code":"SCO-7Q4M-XK2P-91TA","school_name":"École Les Étoiles","expires_at":{{{Days(14)}}},"notice":"Transmettre ce code uniquement au responsable du serveur SCOLIA."}""",
            ("PATCH", _) => """{"notice":"Les données, les connexions et les lectures restent accessibles ; les écritures dépendent du statut."}""",
            _ => null
        };
        return json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"Ressource propriétaire inconnue."}""", Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}

/// <summary>Protection simulée (réversible) : vérifie que le coffre n'écrit jamais en clair.</summary>
internal sealed class FakeProtector(bool available = true) : ISecretProtector
{
    public bool Available { get; } = available;
    public byte[] Protect(byte[] clear) => clear.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
    public byte[] Unprotect(byte[] sealedData) => sealedData.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
}
