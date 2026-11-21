using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Scolia.Bureau.Api;

/// <summary>
/// Premier lot métier central : catalogue des sections, hors données personnelles.
/// Ce transport ne modifie pas les dossiers locaux existants et ne considère
/// jamais HTTP 202 comme une opération définitivement appliquée.
/// </summary>
public sealed class CentralSectionClient : IDisposable
{
    private readonly HttpClient http;

    public CentralSectionClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public async Task<CentralSectionPage> ReadAsync(
        Uri origin, string sessionJwt, long after = 0, int limit = 50,
        CancellationToken ct = default)
    {
        var trusted = CentralPairingClient.RequireTrustedHttps(origin);
        ValidateSession(sessionJwt);
        if (after < 0 || limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(after), "Curseur de catalogue invalide.");

        using var request = Request(HttpMethod.Get,
            new Uri(trusted, $"v1/catalog/sections?after={after}&limit={limit}"), sessionJwt);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new CentralPairingException((int)response.StatusCode,
                "Le catalogue central est momentanément inaccessible ou non autorisé.");

        var root = await ReadResponseAsync(response, 48_000, ct);
        if (!root.TryGetProperty("events", out var elements) ||
            elements.ValueKind != JsonValueKind.Array ||
            elements.GetArrayLength() > limit ||
            !root.TryGetProperty("next", out var nextElement) ||
            !nextElement.TryGetInt64(out var next) ||
            !root.TryGetProperty("has_more", out var moreElement) ||
            moreElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Réponse du catalogue invalide.");

        var events = new List<CentralSectionEvent>();
        var last = after;
        foreach (var item in elements.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("revision", out var revisionElement) ||
                !revisionElement.TryGetInt64(out var revision) ||
                revision <= last ||
                !item.TryGetProperty("section_id", out var sectionElement) ||
                !Guid.TryParseExact(sectionElement.GetString(), "D", out var sectionId) ||
                !item.TryGetProperty("code", out var codeElement) ||
                codeElement.GetString() is not { } code || !ValidCode(code) ||
                !item.TryGetProperty("name", out var nameElement) ||
                nameElement.GetString() is not { } name || !ValidName(name) ||
                !item.TryGetProperty("is_active", out var activeElement) ||
                !activeElement.TryGetInt32(out var active) || active is not (0 or 1))
                throw new InvalidDataException("Événement de section invalide.");
            events.Add(new CentralSectionEvent(revision, sectionId, code, name, active == 1));
            last = revision;
        }
        if (next != last || (elements.GetArrayLength() == 0 && moreElement.GetBoolean()))
            throw new InvalidDataException("Curseur central incohérent.");
        return new CentralSectionPage(events, next, moreElement.GetBoolean());
    }

    /// <summary>
    /// N'envoyer ici qu'une opération déjà durablement enregistrée dans
    /// l'outbox locale, avec son identifiant permanent. Le serveur peut
    /// retourner un conflit de révision : ne pas réécrire silencieusement.
    /// </summary>
    public async Task<CentralSectionReceipt> UpsertAsync(
        Uri origin, string sessionJwt, CentralSectionChange change,
        CancellationToken ct = default)
    {
        var trusted = CentralPairingClient.RequireTrustedHttps(origin);
        ValidateSession(sessionJwt);
        ValidateChange(change);
        using var request = Request(HttpMethod.Post,
            new Uri(trusted, "v1/catalog/sections"), sessionJwt);
        request.Content = JsonContent.Create(new
        {
            section_id = change.SectionId.ToString("D"),
            operation_id = change.OperationId.ToString("D"),
            code = change.Code,
            name = change.Name,
            is_active = change.IsActive,
            expected_revision = change.ExpectedRevision,
        });

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
            throw new CentralPairingException((int)response.StatusCode,
                "La modification n'est pas confirmée par le serveur central.");
        var root = await ReadResponseAsync(response, 2048, ct);
        if (!root.TryGetProperty("status", out var status) ||
            status.GetString() != "applied" ||
            !root.TryGetProperty("operation_id", out var op) ||
            !Guid.TryParseExact(op.GetString(), "D", out var returnedOp) ||
            returnedOp != change.OperationId ||
            !root.TryGetProperty("section_id", out var section) ||
            !Guid.TryParseExact(section.GetString(), "D", out var returnedSection) ||
            returnedSection != change.SectionId ||
            !root.TryGetProperty("revision", out var revision) ||
            !revision.TryGetInt64(out var version) || version < 1 ||
            !root.TryGetProperty("replayed", out var replayed) ||
            replayed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Accusé central incompatible avec l'opération.");
        return new CentralSectionReceipt(change.OperationId, version, replayed.GetBoolean());
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string jwt)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return request;
    }

    private static async Task<JsonElement> ReadResponseAsync(
        HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (Encoding.UTF8.GetByteCount(raw) > maxBytes)
            throw new InvalidDataException("Réponse du serveur central trop volumineuse.");
        using var document = JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Réponse du serveur central non reconnue.");
        return document.RootElement.Clone();
    }

    private static void ValidateSession(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt) || jwt.Length > 4096 ||
            jwt.Count(c => c == '.') != 2 ||
            jwt.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ArgumentException("Session centrale invalide.", nameof(jwt));
    }

    internal static bool ValidCode(string? code) =>
        code is { Length: >= 2 and <= 16 } &&
        code.All(c => char.IsAsciiUpper(c) || char.IsAsciiDigit(c) || c is '-' or '_');

    internal static bool ValidName(string? name) =>
        name is { Length: >= 1 and <= 120 } &&
        name.Trim() == name &&
        name.All(c => !char.IsControl(c));

    internal static void ValidateChange(CentralSectionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.SectionId == Guid.Empty || change.OperationId == Guid.Empty ||
            !ValidCode(change.Code) || !ValidName(change.Name) ||
            change.ExpectedRevision < 0)
            throw new ArgumentException("Modification de section invalide.", nameof(change));
    }

    public void Dispose() => http.Dispose();
}

public sealed record CentralSectionChange(
    Guid OperationId, Guid SectionId, string Code, string Name,
    bool IsActive, long ExpectedRevision);

public sealed record CentralSectionEvent(
    long Revision, Guid SectionId, string Code, string Name, bool IsActive);

public sealed record CentralSectionPage(
    IReadOnlyList<CentralSectionEvent> Events, long NextRevision, bool HasMore);

public sealed record CentralSectionReceipt(Guid OperationId, long Revision, bool Replayed);
