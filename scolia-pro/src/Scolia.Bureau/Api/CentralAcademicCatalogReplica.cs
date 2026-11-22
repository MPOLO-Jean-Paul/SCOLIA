using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Api;

/// <summary>Ressources non nominatives D1. Les paiements, notes et dossiers
/// restent regis exclusivement par le serveur scolaire existant.</summary>
public enum CentralAcademicCatalogKind { Years, Promotions }

public sealed record CentralAcademicYearRow(string Id, string Label,
    string StartDate, string EndDate, string Status, long Revision);

public sealed record CentralAcademicPromotionRow(string Id, string AcademicYearId,
    string? SectionId, string Code, string Name, bool IsActive, long Revision);

public sealed record CentralAcademicCatalogState(
    IReadOnlyList<CentralAcademicYearRow> Years,
    IReadOnlyList<CentralAcademicPromotionRow> Promotions,
    long Revision, bool Offline, bool HasMorePages = false);

/// <summary>
/// Replica locale de lecture des annees et promotions D1, utilisable par
/// Windows et Android. Le cache SQLite chiffre est porte par OfflineStore.
/// La lecture n'emporte AUCUNE mutation metier ni acquittement de paiement.
/// </summary>
public sealed class CentralAcademicCatalogReplica : IDisposable
{
    private sealed class Snapshot
    {
        public long Revision { get; set; }
        public List<CentralAcademicYearRow> Years { get; set; } = [];
        public List<CentralAcademicPromotionRow> Promotions { get; set; } = [];
    }

    private readonly OfflineStore store;
    private readonly string user;
    private readonly string cacheKey;
    private readonly Uri origin;
    private readonly CentralAcademicCatalogKind kind;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);

    public CentralAcademicCatalogReplica(OfflineStore store, string user,
        string schoolId, Uri trustedOrigin, CentralAcademicCatalogKind kind,
        HttpMessageHandler? handler = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(user) || !ValidSchool(schoolId) ||
            !Enum.IsDefined(kind))
            throw new ArgumentException("Compte, ecole ou catalogue invalide.");
        this.user = user;
        this.kind = kind;
        origin = CentralPairingClient.RequireTrustedHttps(trustedOrigin);
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(origin.AbsoluteUri)));
        cacheKey = "central:catalog:v1:" + kind + ":" + schoolId + ":" + fingerprint;
        http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseCookies = false, AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        }, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static bool ValidSchool(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':');

    private void CheckUser()
    {
        if (!string.Equals(store.User, user, StringComparison.Ordinal))
            throw new InvalidOperationException("Le compte local SCOLIA a change.");
    }

    private static Snapshot Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new Snapshot();
        var value = JsonSerializer.Deserialize<Snapshot>(text)
            ?? throw new InvalidDataException("Cache scolaire invalide.");
        if (value.Revision < 0 || value.Years is null || value.Promotions is null)
            throw new InvalidDataException("Cache scolaire incoherent.");
        return value;
    }

    private Snapshot Read()
    {
        CheckUser();
        return Decode(store.ReadPage(cacheKey)?.Json);
    }

    private Snapshot Update(Func<Snapshot, Snapshot> mutation)
    {
        CheckUser();
        Snapshot? saved = null;
        try
        {
            store.UpdatePageFor(user, cacheKey, previous =>
            {
                CheckUser();
                saved = mutation(Decode(previous));
                CheckUser();
                return JsonSerializer.Serialize(saved);
            });
        }
        catch (IOException ex)
        {
            throw new InvalidDataException(
                "Impossible d'enregistrer le catalogue dans SQLite.", ex);
        }
        CheckUser();
        return saved ?? throw new InvalidDataException("Ecriture locale absente.");
    }

    public CentralAcademicCatalogState Local()
    {
        var value = Read();
        return new CentralAcademicCatalogState(
            value.Years.OrderBy(y => y.Label, StringComparer.Ordinal).ToArray(),
            value.Promotions.OrderBy(p => p.Code, StringComparer.Ordinal).ToArray(),
            value.Revision, Offline: true);
    }

    // Les champs d'un serveur distant doivent etre valides avant usage.
    private static JsonElement Require(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty(name, out var value))
            throw new InvalidDataException("Propriete obligatoire absente dans D1.");
        return value;
    }

    private static string? ReadString(JsonElement item, string name)
    {
        var value = Require(item, name);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Type de champ D1 invalide.");
        return value.GetString();
    }

    private static string ValidGuid(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { } id ||
            !Guid.TryParseExact(id, "D", out var guid) ||
            guid == Guid.Empty || id != guid.ToString("D"))
            throw new InvalidDataException("Identifiant de catalogue D1 invalide.");
        return id;
    }

    private static bool ValidDate(string? date, out DateOnly parsed) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out parsed);

    private static CentralAcademicYearRow ReadYear(JsonElement item, long revision)
    {
        var id = ValidGuid(Require(item, "academic_year_id"));
        var label = ReadString(item, "label");
        var start = ReadString(item, "start_date");
        var end = ReadString(item, "end_date");
        var status = ReadString(item, "status");
        if (label is null || label.Length != 9 || label[4] != '-' ||
            !int.TryParse(label.AsSpan(0, 4), NumberStyles.None,
                CultureInfo.InvariantCulture, out var first) ||
            !int.TryParse(label.AsSpan(5), NumberStyles.None,
                CultureInfo.InvariantCulture, out var second) ||
            first is < 2000 or > 2199 || second != first + 1 ||
            !ValidDate(start, out var startDay) ||
            !ValidDate(end, out var endDay) || endDay < startDay ||
            startDay.Year != first ||
            (endDay.Year != first && endDay.Year != second) ||
            status is not ("preparation" or "open" or "closed"))
            throw new InvalidDataException("Annee scolaire D1 invalide.");
        return new CentralAcademicYearRow(id, label, start!, end!, status, revision);
    }

    private static CentralAcademicPromotionRow ReadPromotion(JsonElement item,
        long revision)
    {
        var id = ValidGuid(Require(item, "promotion_id"));
        var year = ValidGuid(Require(item, "academic_year_id"));
        var sectionElement = Require(item, "section_id");
        var section = sectionElement.ValueKind == JsonValueKind.Null ?
            null : ValidGuid(sectionElement);
        var code = ReadString(item, "code");
        var name = ReadString(item, "name");
        var activeElement = Require(item, "is_active");
        if (code is null || code.Length is < 2 or > 16 ||
            code.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) ||
                             c is '_' or '-')) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
            name != name.Trim() || name.Any(char.IsControl) ||
            activeElement.ValueKind != JsonValueKind.Number ||
            !activeElement.TryGetInt32(out var active) || active is not (0 or 1))
            throw new InvalidDataException("Promotion D1 invalide.");
        return new CentralAcademicPromotionRow(id, year, section, code, name,
            active == 1, revision);
    }

    /// <summary>
    /// Au plus huit pages de 50 evenements. Le curseur et les enregistrements
    /// valides avancent dans la MEME transaction SQLite, puis une autre
    /// invocation peut reprendre sans internet ni perte de donnees.
    /// </summary>
    public async Task<CentralAcademicCatalogState> SyncAsync(
        Uri trustedOrigin, string sessionJwt, CancellationToken ct = default)
    {
        var requestedOrigin = CentralPairingClient.RequireTrustedHttps(trustedOrigin);
        if (!string.Equals(origin.AbsoluteUri, requestedOrigin.AbsoluteUri,
            StringComparison.Ordinal))
            throw new ArgumentException("Origine Cloudflare differente.", nameof(trustedOrigin));
        if (string.IsNullOrWhiteSpace(sessionJwt) || sessionJwt.Length > 4096 ||
            sessionJwt.Count(c => c == '.') != 2 ||
            sessionJwt.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new ArgumentException("Session D1 invalide.", nameof(sessionJwt));

        await gate.WaitAsync(ct);
        try
        {
            var snapshot = Read();
            var more = false;
            try
            {
                for (var batch = 0; batch < 8; batch++)
                {
                    ct.ThrowIfCancellationRequested();
                    var resource = kind == CentralAcademicCatalogKind.Years ?
                        "years" : "promotions";
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        new Uri(origin, "v1/catalog/" + resource + "?after=" +
                            snapshot.Revision.ToString(CultureInfo.InvariantCulture) +
                            "&limit=50"));
                    request.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer", sessionJwt);
                    using var response = await http.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (response.StatusCode is HttpStatusCode.Forbidden or
                        HttpStatusCode.Unauthorized)
                        throw new CentralPairingException((int)response.StatusCode,
                            "Acces au catalogue central refuse.");
                    if (response.StatusCode != HttpStatusCode.OK)
                        return Local() with { Offline = true };

                    var root = await CentralBoundedJson.ReadAsync(response, 90_000, ct);
                    var items = Require(root, "events");
                    if (items.ValueKind != JsonValueKind.Array ||
                        items.GetArrayLength() > 50)
                        throw new InvalidDataException("Page de catalogue D1 invalide.");

                    var nextYears = new List<CentralAcademicYearRow>();
                    var nextPromotions = new List<CentralAcademicPromotionRow>();
                    var last = snapshot.Revision;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object ||
                            Require(item, "revision").ValueKind != JsonValueKind.Number ||
                            !Require(item, "revision").TryGetInt64(out var rev) ||
                            rev <= last)
                            throw new InvalidDataException("Revision D1 invalide.");
                        if (kind == CentralAcademicCatalogKind.Years)
                            nextYears.Add(ReadYear(item, rev));
                        else
                            nextPromotions.Add(ReadPromotion(item, rev));
                        last = rev;
                    }
                    if (Require(root, "next").ValueKind != JsonValueKind.Number ||
                        !Require(root, "next").TryGetInt64(out var cursor) ||
                        cursor != last)
                        throw new InvalidDataException("Curseur D1 incoherent.");
                    var flag = Require(root, "has_more");
                    if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Pagination D1 invalide.");
                    more = flag.GetBoolean();
                    if (more && items.GetArrayLength() == 0)
                        throw new InvalidDataException("Pagination D1 sans progression.");

                    snapshot = Update(current =>
                    {
                        if (kind == CentralAcademicCatalogKind.Years)
                        {
                            foreach (var row in nextYears)
                            {
                                var index = current.Years.FindIndex(x => x.Id == row.Id);
                                if (index < 0) current.Years.Add(row);
                                else if (current.Years[index].Revision <= row.Revision)
                                    current.Years[index] = row;
                            }
                        }
                        else
                        {
                            foreach (var row in nextPromotions)
                            {
                                var index = current.Promotions.FindIndex(x => x.Id == row.Id);
                                if (index < 0) current.Promotions.Add(row);
                                else if (current.Promotions[index].Revision <= row.Revision)
                                    current.Promotions[index] = row;
                            }
                        }
                        current.Revision = Math.Max(current.Revision, last);
                        return current;
                    });
                    if (!more) break;
                }
                return Local() with { Offline = false, HasMorePages = more };
            }
            catch (Exception ex) when (ex is HttpRequestException or
                TaskCanceledException or IOException)
            {
                if (ct.IsCancellationRequested) throw;
                return Local() with { Offline = true };
            }
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }
}
