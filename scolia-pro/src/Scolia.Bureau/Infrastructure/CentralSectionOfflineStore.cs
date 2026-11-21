using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Cache et outbox chiffrés du catalogue central, indépendants de la base
/// scolaire principale. L'utilisateur continue à lire/encoder ses sections
/// hors réseau ; jamais de suppression d'opération sur timeout ou HTTP 202.
/// Ce catalogue pilote n'est PAS encore la table métier locale définitive.
/// </summary>
public sealed class CentralSectionOfflineStore
{
    private const string StateKey = "scolia:central:section-catalog:v1";
    private readonly OfflineDatabase database;

    private sealed class Stored
    {
        public long Revision { get; set; }
        public List<CentralSectionEvent> Sections { get; set; } = [];
        public List<CentralSectionQueuedChange> Pending { get; set; } = [];
    }

    public CentralSectionOfflineStore(
        Uri trustedOrigin, string schoolId, string userId, string? rootFolder = null)
    {
        var origin = CentralPairingClient.RequireTrustedHttps(trustedOrigin);
        if (!SafeId(schoolId) || !SafeId(userId))
            throw new ArgumentException("Identité centrale invalide.");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            origin.AbsoluteUri + "\n" + schoolId + "\n" + userId)));
        var root = Path.Combine(rootFolder ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SCOLIA", "catalogue-central"), digest);
        Directory.CreateDirectory(root);
        database = new OfflineDatabase(root);
    }

    public CentralSectionLocalSnapshot Snapshot()
    {
        var current = Read(database.Read(StateKey));
        return new CentralSectionLocalSnapshot(
            current.Revision, current.Sections.ToArray(), current.Pending.ToArray());
    }

    /// <summary>Enregistrer l'opération EN LOCAL avant tout appel réseau.</summary>
    public void Enqueue(CentralSectionChange change)
    {
        CentralSectionClient.ValidateChange(change);
        database.Update([StateKey], map =>
        {
            var current = Read(map[StateKey]);
            if (current.Pending.Count >= 500)
                throw new InvalidOperationException("Le catalogue a atteint sa limite de travaux en attente.");
            if (current.Pending.Any(item => item.Change.OperationId == change.OperationId))
                throw new InvalidOperationException("Identifiant d'opération déjà présent dans l'outbox.");
            current.Pending.Add(new CentralSectionQueuedChange(change, false));
            map[StateKey] = JsonSerializer.Serialize(current);
        });
    }

    /// <summary>
    /// Ne retirer l'opération qu'après HTTP 200/201 et un accusé signé par la
    /// session centrale correspondant exactement au même operation_id.
    /// Le catalogue visible sera actualisé lors du prochain pull.
    /// </summary>
    public void Confirm(CentralSectionReceipt receipt)
    {
        database.Update([StateKey], map =>
        {
            var current = Read(map[StateKey]);
            current.Pending.RemoveAll(item => item.Change.OperationId == receipt.OperationId);
            map[StateKey] = JsonSerializer.Serialize(current);
        });
    }

    /// <summary>Conflit explicite : conserver l'opération pour arbitrage.</summary>
    public void MarkConflict(Guid operationId)
    {
        database.Update([StateKey], map =>
        {
            var current = Read(map[StateKey]);
            for (var i = 0; i < current.Pending.Count; i++)
                if (current.Pending[i].Change.OperationId == operationId)
                    current.Pending[i] = current.Pending[i] with { Conflict = true };
            map[StateKey] = JsonSerializer.Serialize(current);
        });
    }

    /// <summary>
    /// Appliquer tous les événements et leur curseur dans UNE transaction
    /// SQLite. Si l'opération échoue, la progression n'avance pas.
    /// </summary>
    public void ApplyPage(long expectedAfter, CentralSectionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        database.Update([StateKey], map =>
        {
            var current = Read(map[StateKey]);
            if (current.Revision != expectedAfter)
                throw new InvalidOperationException("Le cache a changé pendant la synchronisation.");
            var byId = current.Sections.ToDictionary(x => x.SectionId);
            var last = expectedAfter;
            foreach (var item in page.Events)
            {
                if (item.Revision <= last || item.SectionId == Guid.Empty ||
                    !CentralSectionClient.ValidCode(item.Code) ||
                    !CentralSectionClient.ValidName(item.Name))
                    throw new InvalidDataException("Événement de catalogue invalide.");
                last = item.Revision;
                byId[item.SectionId] = item;
            }
            if (page.NextRevision != last)
                throw new InvalidDataException("Curseur incohérent.");

            current.Sections = byId.Values.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
            current.Revision = page.NextRevision;
            map[StateKey] = JsonSerializer.Serialize(current);
        });
    }

    private static bool SafeId(string? value) => value is { Length: >= 1 and <= 128 } &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or ':' or '-');

    private static Stored Read(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new Stored();
        var state = JsonSerializer.Deserialize<Stored>(text) ??
            throw new InvalidDataException("Cache local de sections invalide.");
        if (state.Revision < 0 || state.Sections is null || state.Pending is null)
            throw new InvalidDataException("Cache local de sections endommagé.");
        return state;
    }
}

public sealed record CentralSectionQueuedChange(
    CentralSectionChange Change, bool Conflict);

public sealed record CentralSectionLocalSnapshot(
    long Revision, IReadOnlyList<CentralSectionEvent> Sections,
    IReadOnlyList<CentralSectionQueuedChange> Pending);
