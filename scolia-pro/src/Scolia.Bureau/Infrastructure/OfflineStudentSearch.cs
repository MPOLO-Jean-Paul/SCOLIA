using System.Runtime.CompilerServices;
using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Recherche sans serveur depuis le registre chiffré local. L'index trié est
/// construit UNE fois par révision du compte et réutilisé à chaque caractère.
/// L'instance est faiblement référencée pour être libérée avec la session.
/// </summary>
public static class OfflineStudentSearch
{
    public sealed record Result(PagedResult<StudentListItem> Page, int Loaded, int Registered, bool Complete);
    private sealed record Indexed(StudentListItem Student, string SearchKey);
    private sealed record Snapshot(IReadOnlyList<Indexed> Rows, int Loaded, int Registered, bool Complete);
    private sealed class CachedIndex
    {
        public object Gate { get; } = new();
        public long Revision = -1;
        public string User = string.Empty;
        public Snapshot? Value;
    }

    private static readonly ConditionalWeakTable<OfflineStore, CachedIndex> Indexes = new();

    /// <summary>Filtre en mémoire sans refaire des centaines de requêtes SQLite.</summary>
    public static Result? Search(OfflineStore store, string? query, string? promotionCode,
        string? status, int requestedPage, int pageSize = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var snapshot = LoadIndex(store);
        if (snapshot is null) return null;

        var term = (query ?? string.Empty).Trim();
        var matches = snapshot.Rows.Where(x =>
            (string.IsNullOrWhiteSpace(promotionCode) ||
             string.Equals(x.Student.PromotionCode, promotionCode, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(status) ||
             string.Equals(x.Student.Status, status, StringComparison.OrdinalIgnoreCase)) &&
            (term.Length == 0 || x.SearchKey.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var safePage = Math.Clamp(requestedPage, 1,
            Math.Max(1, (int)Math.Ceiling(matches.Count / (double)pageSize)));
        var items = matches.Skip((safePage - 1) * pageSize).Take(pageSize)
            .Select(x => x.Student).ToList();
        return new Result(
            new PagedResult<StudentListItem>(items, matches.Count, safePage, pageSize, null),
            snapshot.Loaded, snapshot.Registered, snapshot.Complete);
    }

    private static Snapshot? LoadIndex(OfflineStore store)
    {
        var cache = Indexes.GetValue(store, _ => new CachedIndex());
        lock (cache.Gate)
        {
            var revision = store.SearchRevision;
            var user = store.User;
            if (cache.Revision == revision && cache.User == user) return cache.Value;
            var index = BuildIndex(store, user);
            // Si une synchronisation est arrivée pendant la lecture, la
            // prochaine recherche rebâtit l'index à partir du nouvel état.
            cache.Revision = store.SearchRevision == revision ? revision : -1;
            cache.User = user;
            cache.Value = index;
            return user == store.User ? index : null;
        }
    }

    private static Snapshot? BuildIndex(OfflineStore store, string user)
    {
        if (user.Length == 0) return null;
        var pending = store.Outbox();
        var provisional = OfflineReplica.PendingRegistrationRows(store, pending);
        var paths = Enumerable.Range(1, OfflinePreparation.MaximumPages)
            .Select(OfflinePreparation.PagePath).ToList();
        var pages = store.ReadPages(paths);
        var items = new Dictionary<Guid, StudentListItem>();
        var registered = 0;
        var snapshotFound = false;

        foreach (var path in paths)
        {
            if (!pages.TryGetValue(path, out var json)) break;
            PagedResult<StudentListItem>? page;
            try { page = JsonSerializer.Deserialize<PagedResult<StudentListItem>>(json, Json.Options); }
            catch (JsonException) { return null; }
            if (page is null) return null;
            snapshotFound = true;
            registered = Math.Max(registered, page.Total);
            foreach (var row in page.Items)
                items[row.Id] = OfflineReplica.ApplyStudentEdits(row, pending);
            if (items.Count >= registered) break;
        }
        if (!snapshotFound && provisional.Count == 0) return null;
        var loadedFromServer = items.Count;
        foreach (var row in provisional) items[row.Id] = row;
        var ordered = items.Values
            .OrderBy(s => s.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.MiddleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.FirstName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new Indexed(s, string.Join(" ", s.Matricule, s.LastName,
                s.MiddleName, s.FirstName, s.GuardianPhone ?? string.Empty)))
            .ToList();
        return new Snapshot(ordered, ordered.Count, registered + provisional.Count,
            snapshotFound && loadedFromServer >= registered);
    }
}
