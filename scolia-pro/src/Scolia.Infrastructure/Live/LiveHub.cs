using System.Collections.Concurrent;
using System.Threading.Channels;
using Scolia.Domain.Common;

namespace Scolia.Infrastructure.Live;

/// <summary>Événement du direct : une écriture validée, un poste qui change de page, un verrou pris ou rendu.</summary>
public sealed record LiveEvent(
    string Kind,
    long Revision,
    string? DeviceId,
    string? Device,
    string? User,
    string? Summary,
    IReadOnlyList<string> Topics,
    DateTime AtUtc);

/// <summary>Poste connecté : qui l'utilise, sur quelle page, et ce qu'il est en train de faire.</summary>
public sealed record PresenceEntry(string DeviceId, string Device, string User, string? Page, string? Activity, DateTime SeenUtc);

/// <summary>
/// Action réservée par un poste : encaissement d'un apprenant, modification d'un dossier, arrêté de caisse.
/// Le verrou expire seul si le poste disparaît sans le rendre (coupure, téléphone éteint).
/// </summary>
public sealed record LockEntry(string Key, string Label, string DeviceId, string Device, string User, DateTime ExpiresUtc);

/// <summary>Photographie du direct : les postes présents et les actions réservées.</summary>
public sealed record LiveSnapshot(long Revision, IReadOnlyList<PresenceEntry> Presence, IReadOnlyList<LockEntry> Locks);

/// <summary>
/// Le direct du réseau de l'établissement, en mémoire du serveur : chaque poste abonné reçoit les écritures validées
/// des autres (pour rafraîchir son écran), voit qui travaille et sur quoi, et ne peut pas lancer une action
/// qu'un autre poste a déjà commencée. Tout est éphémère : au redémarrage du serveur, chaque poste se réannonce.
/// </summary>
public sealed class LiveHub(IClock clock)
{
    /// <summary>Un poste qui ne s'est pas annoncé depuis ce délai n'est plus présent.</summary>
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Durée d'un verrou sans nouvelle du poste qui le tient ; chaque annonce le prolonge.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<Guid, Channel<LiveEvent>> subscribers = new();
    private readonly ConcurrentDictionary<string, PresenceEntry> presence = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LockEntry> locks = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private long revision;

    public int Subscribers => subscribers.Count;

    /// <summary>Abonne un poste au direct. Les événements en trop pour un poste lent sont abandonnés, jamais bloquants.</summary>
    public (Guid Id, ChannelReader<LiveEvent> Reader) Subscribe()
    {
        var channel = Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var id = Guid.NewGuid();
        subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }

    /// <summary>Écriture validée sur le serveur : tous les postes abonnés en sont prévenus.</summary>
    public void Changed(long dataRevision, string? deviceId, string? device, string? user, string summary, IReadOnlyList<string> topics)
    {
        Interlocked.Exchange(ref revision, dataRevision);
        Publish(new LiveEvent("donnees", dataRevision, deviceId, device, user, summary, topics, clock.UtcNow));
    }

    /// <summary>
    /// Annonce d'un poste : il est présent, sur telle page. Les verrous qu'il tient sont prolongés.
    /// Un changement de page ou d'activité est diffusé aux autres postes.
    /// </summary>
    public LiveSnapshot Touch(string deviceId, string device, string user, string? page, string? activity, IEnumerable<string> heldKeys)
    {
        var now = clock.UtcNow;
        Sweep(now);
        var entry = new PresenceEntry(deviceId, device, user, page, activity, now);
        var previous = presence.TryGetValue(deviceId, out var known) ? known : null;
        presence[deviceId] = entry;
        lock (gate)
        {
            foreach (var key in heldKeys)
                if (locks.TryGetValue(key, out var held) && held.DeviceId == deviceId)
                    locks[key] = held with { ExpiresUtc = now + LockTimeout };
        }
        if (previous is null || previous.Page != page || previous.Activity != activity || previous.User != user)
            Publish(new LiveEvent("presence", Revision, deviceId, device, user, activity ?? page, [], now));
        return Snapshot();
    }

    /// <summary>Le poste se retire du direct : il quitte l'application ou se déconnecte. Ses verrous sont rendus.</summary>
    public void Leave(string deviceId)
    {
        var now = clock.UtcNow;
        var left = presence.TryRemove(deviceId, out var entry);
        var released = ReleaseAll(deviceId);
        if (left || released) Publish(new LiveEvent("presence", Revision, deviceId, entry?.Device, entry?.User, null, [], now));
    }

    /// <summary>
    /// Réserve une action. Accordée si personne ne la tient, ou si c'est déjà ce poste ;
    /// refusée avec le nom du poste qui la tient sinon.
    /// </summary>
    public (bool Granted, LockEntry Holder) TryLock(string key, string label, string deviceId, string device, string user)
    {
        var now = clock.UtcNow;
        LockEntry result;
        bool granted;
        lock (gate)
        {
            if (locks.TryGetValue(key, out var held) && held.ExpiresUtc > now && held.DeviceId != deviceId)
            {
                result = held;
                granted = false;
            }
            else
            {
                result = new LockEntry(key, label, deviceId, device, user, now + LockTimeout);
                locks[key] = result;
                granted = true;
            }
        }
        if (granted) Publish(new LiveEvent("verrou", Revision, deviceId, device, user, label, [key], now));
        return (granted, result);
    }

    /// <summary>Rend une action réservée par ce poste ; sans effet si un autre poste la tient.</summary>
    public void Release(string key, string deviceId)
    {
        LockEntry? removed = null;
        lock (gate)
        {
            if (locks.TryGetValue(key, out var held) && held.DeviceId == deviceId && locks.TryRemove(key, out var gone)) removed = gone;
        }
        if (removed is not null) Publish(new LiveEvent("verrou", Revision, deviceId, removed.Device, removed.User, null, [key], clock.UtcNow));
    }

    /// <summary>Poste qui tient cette action, s'il en est un autre que celui qui demande.</summary>
    public LockEntry? HeldByOther(string key, string? deviceId)
    {
        var now = clock.UtcNow;
        return locks.TryGetValue(key, out var held) && held.ExpiresUtc > now && held.DeviceId != deviceId ? held : null;
    }

    public LiveSnapshot Snapshot()
    {
        var now = clock.UtcNow;
        Sweep(now);
        return new LiveSnapshot(Revision,
            [.. presence.Values.OrderBy(p => p.Device, StringComparer.CurrentCulture)],
            [.. locks.Values.Where(l => l.ExpiresUtc > now).OrderBy(l => l.Key, StringComparer.Ordinal)]);
    }

    public long Revision => Interlocked.Read(ref revision);

    private bool ReleaseAll(string deviceId)
    {
        var any = false;
        lock (gate)
        {
            foreach (var held in locks.Values.Where(l => l.DeviceId == deviceId).ToList())
                any |= locks.TryRemove(held.Key, out _);
        }
        return any;
    }

    /// <summary>Retire les postes silencieux et les verrous expirés, puis prévient les autres postes.</summary>
    private void Sweep(DateTime now)
    {
        var gone = presence.Values.Where(p => now - p.SeenUtc > PresenceTimeout).ToList();
        foreach (var entry in gone)
            if (presence.TryRemove(entry.DeviceId, out _))
            {
                ReleaseAll(entry.DeviceId);
                Publish(new LiveEvent("presence", Revision, entry.DeviceId, entry.Device, entry.User, null, [], now));
            }
        lock (gate)
        {
            foreach (var expired in locks.Values.Where(l => l.ExpiresUtc <= now).ToList()) locks.TryRemove(expired.Key, out _);
        }
    }

    private void Publish(LiveEvent live)
    {
        foreach (var channel in subscribers.Values) channel.Writer.TryWrite(live);
    }
}
