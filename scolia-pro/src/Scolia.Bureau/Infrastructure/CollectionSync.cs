using System.Collections.ObjectModel;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Applique une photographie sans vider la liste ni recréer les lignes inchangées.</summary>
public static class CollectionSync
{
    public static void Apply<T>(ObservableCollection<T> target, IEnumerable<T> incoming, Func<T, T, bool>? same = null)
    {
        var next = incoming.ToList();
        var standard = same is null;
        same ??= EqualityComparer<T>.Default.Equals;
        // Cas fréquent : une photographie identique, un ajout en fin ou une suppression en fin.
        // Ne construire les index qu'au premier écart réel. Les tuples acceptent aussi les valeurs nulles.
        Dictionary<ValueTuple<T>, int>? remaining = null;
        Dictionary<ValueTuple<T>, int>? lastIncoming = null;
        void Consume(T item)
        {
            if (remaining is null) return;
            var key = ValueTuple.Create(item);
            if (remaining.TryGetValue(key, out var count))
            {
                if (count <= 1) remaining.Remove(key);
                else remaining[key] = count - 1;
            }
        }

        for (var i = 0; i < next.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(next[i]);
                continue;
            }
            if (same(target[i], next[i]))
            {
                Consume(target[i]);
                continue;
            }
            if (standard && remaining is null)
            {
                remaining = new();
                for (var j = i; j < target.Count; j++)
                {
                    var key = ValueTuple.Create(target[j]);
                    remaining.TryGetValue(key, out var count);
                    remaining[key] = count + 1;
                }
                lastIncoming = new();
                for (var j = i; j < next.Count; j++) lastIncoming[ValueTuple.Create(next[j])] = j;
            }
            var match = -1;
            // Une valeur absente du suffixe courant ne nécessite aucun balayage de la liste.
            if (!standard || remaining!.ContainsKey(ValueTuple.Create(next[i])))
                for (var j = i + 1; j < target.Count; j++)
                    if (same(target[j], next[i])) { match = j; break; }
            if (match >= 0)
            {
                Consume(target[match]);
                target.Move(match, i);
                continue;
            }

            bool neededLater;
            if (standard)
                neededLater = lastIncoming!.TryGetValue(ValueTuple.Create(target[i]), out var last) && last > i;
            else
            {
                // Un comparateur métier n'est pas nécessairement compatible avec un index par égalité.
                neededLater = false;
                for (var j = i + 1; j < next.Count; j++)
                    if (same(target[i], next[j])) { neededLater = true; break; }
            }
            if (!neededLater)
            {
                Consume(target[i]);
                target[i] = next[i];
            }
            else target.Insert(i, next[i]);
        }
        while (target.Count > next.Count) target.RemoveAt(target.Count - 1);
    }
}
