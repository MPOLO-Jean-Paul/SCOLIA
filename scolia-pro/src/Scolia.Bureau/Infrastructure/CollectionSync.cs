using System.Collections.ObjectModel;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Applique une photographie sans vider la liste ni recréer les lignes inchangées.</summary>
public static class CollectionSync
{
    public static void Apply<T>(ObservableCollection<T> target, IEnumerable<T> incoming, Func<T, T, bool>? same = null)
    {
        var next = incoming.ToList();
        same ??= EqualityComparer<T>.Default.Equals;
        for (var i = 0; i < next.Count; i++)
        {
            if (i < target.Count && same(target[i], next[i])) continue;
            var match = -1;
            for (var j = i + 1; j < target.Count; j++)
                if (same(target[j], next[i])) { match = j; break; }
            if (match >= 0) target.Move(match, i);
            else if (i < target.Count && !next.Skip(i + 1).Any(item => same(target[i], item))) target[i] = next[i];
            else target.Insert(i, next[i]);
        }
        while (target.Count > next.Count) target.RemoveAt(target.Count - 1);
    }
}
