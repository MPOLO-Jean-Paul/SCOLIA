using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Scolia.Bureau.Api;

/// <summary>
/// Transport de lecture du journal D1, commun à Windows/Android.
/// Le journal cloud ne contient pas encore de données scolaires : NE PAS
/// le confondre avec une synchronisation métier ou avancer un curseur local.
/// </summary>
public sealed class CentralJournalClient : IDisposable
{
    private readonly HttpClient http;

    public CentralJournalClient(HttpMessageHandler? handler = null)
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

    /// <summary>
    /// Ne lit que les métadonnées du journal d'un établissement autorisé
    /// par un JWT de SESSION émis côté serveur. Le JWT de jumelage n'est
    /// pas accepté en lieu et place de ce jeton de session.
    /// </summary>
    public async Task<CentralJournalPage> ReadMetadataAsync(
        Uri trustedOrigin, string centralSessionJwt, long afterRevision = 0,
        int pageSize = 50, CancellationToken ct = default)
    {
        var origin = CentralPairingClient.RequireTrustedHttps(trustedOrigin);
        if (afterRevision < 0 || pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(afterRevision), "Curseur ou taille de page invalide.");
        if (string.IsNullOrWhiteSpace(centralSessionJwt) ||
            centralSessionJwt.Length > 4096 ||
            centralSessionJwt.Count(x => x == '.') != 2 ||
            centralSessionJwt.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.')))
            throw new ArgumentException("Une session centrale valide est requise.", nameof(centralSessionJwt));

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(origin, $"v1/sync/events?after={afterRevision}&limit={pageSize}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", centralSessionJwt);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden)
            throw new CentralPairingException((int)response.StatusCode,
                "Le compte ou l'appareil n'est pas autorisé à lire le journal central.");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new CentralPairingException((int)response.StatusCode,
                "Le journal central est momentanément inaccessible.");

        var root = await CentralBoundedJson.ReadAsync(response, 80_000, ct);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("includes_payload", out var payloadFlag) ||
            payloadFlag.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("events", out var elements) ||
            elements.ValueKind != JsonValueKind.Array ||
            elements.GetArrayLength() > pageSize ||
            !root.TryGetProperty("next", out var next) ||
            !next.TryGetInt64(out var nextRevision) || nextRevision < afterRevision ||
            !root.TryGetProperty("has_more", out var more) ||
            more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Format ou autorisation du journal central invalide.");

        var changes = new List<CentralJournalEntry>();
        long previous = afterRevision;
        foreach (var change in elements.EnumerateArray())
        {
            if (!change.TryGetProperty("revision", out var position) ||
                !position.TryGetInt64(out var revision) || revision <= previous ||
                !change.TryGetProperty("entity_kind", out var kind) ||
                kind.GetString() is not { Length: > 0 and <= 120 } entityKind ||
                !change.TryGetProperty("entity_id", out var entity) ||
                entity.GetString() is not { Length: > 0 and <= 128 } entityId ||
                !change.TryGetProperty("change_kind", out var type) ||
                type.GetString() is not ("upsert" or "tombstone") ||
                !change.TryGetProperty("payload_sha256", out var hash) ||
                hash.GetString() is not { Length: 64 } checksum ||
                checksum.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Événement central invalide ou non ordonné.");
            changes.Add(new CentralJournalEntry(revision, entityKind, entityId,
                type.GetString()!, checksum));
            previous = revision;
        }
        if (nextRevision != previous)
            throw new InvalidDataException("Curseur central incohérent.");

        return new CentralJournalPage(changes, nextRevision, more.GetBoolean());
    }

    public void Dispose() => http.Dispose();
}

public sealed record CentralJournalEntry(long Revision, string EntityKind, string EntityId,
    string ChangeKind, string PayloadSha256);

/// <summary>Lecture informative uniquement : aucun acquittement automatique.</summary>
public sealed record CentralJournalPage(IReadOnlyList<CentralJournalEntry> Events,
    long NextRevision, bool HasMore);
