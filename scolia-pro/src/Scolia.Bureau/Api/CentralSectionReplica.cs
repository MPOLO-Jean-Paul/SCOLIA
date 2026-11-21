using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Api;

/// <summary>
/// Réplique chiffrée SQLite des sections D1 pour un compte et une école.
/// Les écritures locales restent consultables sans réseau et gardent toujours
/// leur clé d'idempotence. Aucune donnée de paiement/étudiant n'est transférée.
/// </summary>
public sealed class CentralSectionReplica : IDisposable
{
    private readonly HttpClient http;
    private readonly OfflineStore store;
    private readonly string user;
    private readonly string schoolId;
    private readonly string cacheKey;
    private readonly SemaphoreSlim gate = new(1, 1);

    private sealed class Snapshot
    {
        public long Revision { get; set; }
        public List<CentralSectionRow> Sections { get; set; } = [];
        public List<CentralSectionPendingChange> Pending { get; set; } = [];
    }

    public CentralSectionReplica(OfflineStore store, string user, string schoolId,
        HttpMessageHandler? handler = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(user) || !ValidId(schoolId))
            throw new ArgumentException("Compte et établissement obligatoires.");
        this.user = user;
        this.schoolId = schoolId;
        cacheKey = "central:sections:v1:" + schoolId;
        http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseCookies = false, AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        }, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(20) };
    }

    private static bool ValidId(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':');

    private void CheckUser()
    {
        // L'ancien compte ne peut pas écrire son cache après un changement de
        // profil. L'identité est vérifiée de nouveau après chaque attente HTTP.
        if (!string.Equals(store.User, user, StringComparison.Ordinal))
            throw new InvalidOperationException("Le compte local SCOLIA a changé.");
    }

    private Snapshot Read()
    {
        CheckUser();
        if (store.ReadPage(cacheKey)?.Json is not { Length: > 0 } text) return new Snapshot();
        var doc = JsonSerializer.Deserialize<Snapshot>(text)
            ?? throw new InvalidDataException("Réplique des sections invalide.");
        if (doc.Revision < 0 || doc.Sections is null || doc.Pending is null)
            throw new InvalidDataException("Réplique des sections incohérente.");
        return doc;
    }

    private void Save(Snapshot doc)
    {
        CheckUser();
        store.SavePageFor(user, cacheKey, JsonSerializer.Serialize(doc));
    }

    public CentralSectionLocalState Local()
    {
        var snapshot = Read();
        var visible = snapshot.Sections.ToDictionary(x => x.Id, x => x,
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.Pending)
            visible[item.SectionId] = new CentralSectionRow(item.SectionId,
                item.Code, item.Name, item.IsActive, item.ExpectedRevision, IsPending: true);
        return new CentralSectionLocalState(
            visible.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray(),
            snapshot.Pending.Count, snapshot.Revision, Offline: true,
            HasConflict: snapshot.Pending.Any(x => x.HasConflict));
    }

    /// <summary>
    /// Écriture 100 % locale dans SQLite, même sans connexion Internet.
    /// Une seule opération en attente par section : éviter un dernier-écrivain
    /// arbitraire en cas de changements concurrents de classe.
    /// </summary>
    public async Task<string> QueueUpsertAsync(
        Guid sectionId, string code, string name, bool active,
        CancellationToken ct = default)
    {
        if (sectionId == Guid.Empty || code.Length is < 2 or > 16 ||
            code.Any(c => !(char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c is '_' or '-')) ||
            string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
            name != name.Trim() || name.Any(char.IsControl))
            throw new ArgumentException("Code ou libellé de section invalide.");

        await gate.WaitAsync(ct);
        try
        {
            var snapshot = Read();
            var id = sectionId.ToString("D");
            if (snapshot.Pending.Any(x =>
                string.Equals(x.SectionId, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Une modification de cette section attend encore la confirmation centrale.");
            var previous = snapshot.Sections.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            var op = Guid.NewGuid().ToString("D");
            snapshot.Pending.Add(new CentralSectionPendingChange(op, id, code, name,
                active, previous?.Revision ?? 0));
            Save(snapshot);
            return op;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Transmettre la file persistante, puis appliquer les événements D1 dans
    /// le cache local. Jamais d'ACK avant un véritable moteur métier ; seules
    /// les réponses "applied" retirent une opération de la file.
    /// </summary>
    public async Task<CentralSectionLocalState> SyncAsync(
        Uri trustedCentralOrigin, string centralSessionJwt, CancellationToken ct = default)
    {
        var origin = CentralPairingClient.RequireTrustedHttps(trustedCentralOrigin);
        if (string.IsNullOrWhiteSpace(centralSessionJwt) ||
            centralSessionJwt.Length > 4096 ||
            centralSessionJwt.Count(ch => ch == '.') != 2 ||
            centralSessionJwt.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.')))
            throw new ArgumentException("Session D1 temporaire invalide.", nameof(centralSessionJwt));

        await gate.WaitAsync(ct);
        try
        {
            var snapshot = Read();
            try
            {
                foreach (var operation in snapshot.Pending.Where(x => !x.HasConflict).ToArray())
                {
                    ct.ThrowIfCancellationRequested();
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        new Uri(origin, "v1/catalog/sections"))
                    {
                        Content = JsonContent.Create(new
                        {
                            operation_id = operation.OperationId,
                            section_id = operation.SectionId,
                            code = operation.Code, name = operation.Name,
                            is_active = operation.IsActive,
                            expected_revision = operation.ExpectedRevision,
                        }),
                    };
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", centralSessionJwt);
                    using var result = await http.SendAsync(request, ct);
                    if (result.StatusCode == HttpStatusCode.Conflict)
                    {
                        // Conserver la saisie et son identifiant pour arbitrage :
                        // ne pas rejouer un conflit à chaque reconnexion.
                        var conflictIndex = snapshot.Pending.FindIndex(x =>
                            x.OperationId == operation.OperationId);
                        if (conflictIndex >= 0)
                            snapshot.Pending[conflictIndex] = operation with { HasConflict = true };
                        Save(snapshot);
                        return Status(snapshot, Offline: false, HasConflict: true);
                    }
                    if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                        throw new CentralPairingException((int)result.StatusCode,
                            "La session centrale a été retirée ou a expiré.");
                    if (result.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
                        return Status(snapshot, Offline: true, HasConflict: false);
                    var payload = await ReadJsonAsync(result, ct);
                    if (payload.GetProperty("status").GetString() != "applied" ||
                        payload.GetProperty("operation_id").GetString() != operation.OperationId ||
                        payload.GetProperty("section_id").GetString() != operation.SectionId ||
                        !payload.GetProperty("revision").TryGetInt64(out var version) ||
                        version <= 0)
                        throw new InvalidDataException("Accusé D1 incomplet ou non définitif.");

                    // Confirmation D1 reçue : conserver immédiatement l'état
                    // validé dans le cache local AVANT de retirer l'outbox.
                    // Si le réseau tombe avant la lecture du journal, l'école
                    // et ses sections restent consultables hors connexion.
                    var index = snapshot.Sections.FindIndex(x =>
                        string.Equals(x.Id, operation.SectionId,
                            StringComparison.OrdinalIgnoreCase));
                    var accepted = new CentralSectionRow(operation.SectionId,
                        operation.Code, operation.Name, operation.IsActive,
                        version, IsPending: false);
                    if (index >= 0) snapshot.Sections[index] = accepted;
                    else snapshot.Sections.Add(accepted);
                    snapshot.Pending.RemoveAll(x => x.OperationId == operation.OperationId);
                    Save(snapshot); // État local + retrait outbox : un seul commit.
                }

                var hasMorePages = false;
                for (var batch = 0; batch < 8; batch++)
                {
                    ct.ThrowIfCancellationRequested();
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        new Uri(origin, "v1/catalog/sections?after=" +
                            snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            "&limit=50"));
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", centralSessionJwt);
                    using var result = await http.SendAsync(request, ct);
                    if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                        throw new CentralPairingException((int)result.StatusCode,
                            "La lecture centrale de cette école n'est plus autorisée.");
                    if (result.StatusCode != HttpStatusCode.OK)
                        return Status(snapshot, Offline: true, HasConflict: false);
                    var payload = await ReadJsonAsync(result, ct);
                    var after = snapshot.Revision;
                    var changes = payload.GetProperty("events");
                    if (changes.ValueKind != JsonValueKind.Array ||
                        changes.GetArrayLength() > 50)
                        throw new InvalidDataException("Lot D1 non valide.");

                    foreach (var item in changes.EnumerateArray())
                    {
                        if (!item.GetProperty("revision").TryGetInt64(out var revision) ||
                            revision <= after ||
                            item.GetProperty("section_id").GetString() is not { } id ||
                            !Guid.TryParse(id, out _) ||
                            item.GetProperty("code").GetString() is not { } code ||
                            code.Length is < 2 or > 16 ||
                            item.GetProperty("name").GetString() is not { } name ||
                            string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
                            !item.GetProperty("is_active").TryGetInt32(out var active) ||
                            active is not (0 or 1))
                            throw new InvalidDataException("Événement de section non valide.");
                        var old = snapshot.Sections.FindIndex(x =>
                            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                        var next = new CentralSectionRow(id, code, name,
                            active == 1, revision, IsPending: false);
                        // Le journal peut repartir d'un ancien curseur après
                        // confirmation d'une écriture (POST applied). Ne jamais
                        // remplacer l'état local confirmé à la révision 70 par
                        // un événement antérieur à la révision 70. Avancer quand
                        // même le curseur de réception pour poursuivre le pull.
                        if (old >= 0)
                        {
                            if (snapshot.Sections[old].Revision <= revision)
                                snapshot.Sections[old] = next;
                        }
                        else snapshot.Sections.Add(next);
                        after = revision;
                    }

                    if (!payload.GetProperty("next").TryGetInt64(out var nextRevision) ||
                        nextRevision != after)
                        throw new InvalidDataException("Curseur de section incohérent.");
                    var more = payload.GetProperty("has_more");
                    if (more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Indicateur de continuation invalide.");

                    hasMorePages = more.GetBoolean();
                    if (hasMorePages && changes.GetArrayLength() == 0)
                        throw new InvalidDataException("Pagination D1 sans progression.");
                    snapshot.Revision = after;
                    Save(snapshot); // Données et curseur : UNE SEULE écriture SQLite.
                    if (!hasMorePages) break;
                }
                // Huit lots au maximum par passage pour ne pas monopoliser
                // le téléphone. Le curseur est durable ; l'appelant peut
                // planifier une autre passe sans repartir de zéro.
                return Status(snapshot, Offline: false,
                    HasConflict: snapshot.Pending.Any(x => x.HasConflict),
                    HasMorePages: hasMorePages);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (ct.IsCancellationRequested) throw;
                // Coupure réseau : toutes les opérations sont déjà conservées.
                return Status(snapshot, Offline: true, HasConflict: false);
            }
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Termine automatiquement plusieurs pages de rattrapage, sans boucle
    /// infinie ni remise à zéro du curseur. Une passe lit au plus huit lots
    /// de cinquante événements ; un appel est borné à quatre passes par
    /// défaut. Si la limite est atteinte, HasMorePages reste vrai pour une
    /// reprise ultérieure.
    /// </summary>
    public async Task<CentralSectionLocalState> CatchUpAsync(
        Uri trustedCentralOrigin, string centralSessionJwt,
        int maxPasses = 4, CancellationToken ct = default)
    {
        if (maxPasses is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxPasses));
        CentralSectionLocalState state;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();
            state = await SyncAsync(trustedCentralOrigin, centralSessionJwt, ct);
            if (state.Offline || !state.HasMorePages)
                return state;
        }
        // Relecture locale seulement, sans répéter une requête réseau.
        // Signaler les pages restantes pour la prochaine synchronisation.
        var local = Local();
        return local with { Offline = false, HasMorePages = true };
    }

    private CentralSectionLocalState Status(Snapshot snapshot, bool Offline,
        bool HasConflict, bool HasMorePages = false)
    {
        // Une réponse HTTP 503, un timeout ou un 409 peut arriver APRÈS
        // qu'un autre compte s'est connecté. Ne jamais rendre à ce compte
        // le cliché de l'utilisateur précédent, même sur le chemin d'erreur.
        CheckUser();
        var view = snapshot.Sections.ToDictionary(x => x.Id, x => x,
            StringComparer.OrdinalIgnoreCase);
        foreach (var pending in snapshot.Pending)
            view[pending.SectionId] = new CentralSectionRow(pending.SectionId,
                pending.Code, pending.Name, pending.IsActive,
                pending.ExpectedRevision, IsPending: true);
        return new CentralSectionLocalState(
            view.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToArray(),
            snapshot.Pending.Count, snapshot.Revision, Offline, HasConflict, HasMorePages);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (Encoding.UTF8.GetByteCount(text) > 90_000)
            throw new InvalidDataException("Réponse D1 trop volumineuse.");
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Réponse D1 invalide.");
        return doc.RootElement.Clone();
    }

    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }
}

public sealed record CentralSectionRow(string Id, string Code, string Name,
    bool IsActive, long Revision, bool IsPending = false);

public sealed record CentralSectionPendingChange(string OperationId, string SectionId,
    string Code, string Name, bool IsActive, long ExpectedRevision,
    bool HasConflict = false);

public sealed record CentralSectionLocalState(IReadOnlyList<CentralSectionRow> Sections,
    int PendingCount, long Revision, bool Offline, bool HasConflict = false,
    bool HasMorePages = false);
