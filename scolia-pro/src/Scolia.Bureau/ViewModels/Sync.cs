using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Poste vu par le direct : qui l'utilise, sur quelle page, ce qu'il fait.</summary>
public sealed record PresenceDto(string DeviceId, string Device, string User, string? Page, string? Activity, DateTime SeenUtc);

/// <summary>Action réservée par un poste, jusqu'à la fin de l'opération.</summary>
public sealed record LockDto(string Key, string Label, string DeviceId, string Device, string User, DateTime ExpiresUtc);

/// <summary>État du direct : les postes présents et les actions réservées.</summary>
public sealed record LiveSnapshotDto(long Revision, List<PresenceDto> Presence, List<LockDto> Locks);

/// <summary>Événement du direct : une écriture validée, un changement de présence, un verrou.</summary>
public sealed record LiveEventDto(string Kind, long Revision, string? DeviceId, string? Device, string? User, string? Summary, List<string> Topics, DateTime AtUtc);

/// <summary>Autre poste connecté, tel qu'il s'affiche : initiales, nom, ce qu'il fait.</summary>
public sealed class PeerVm(PresenceDto presence)
{
    public string Initials { get; } = string.Concat(presence.User.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public string Device { get; } = presence.Device;
    public string User { get; } = presence.User;
    public string Doing { get; } = presence.Activity ?? (presence.Page is { Length: > 0 } page ? $"Sur « {page} »" : "Connecté");
    public string Line => $"{User} · {Device}";
}

/// <summary>Opération en attente d'envoi ou refusée, dans la liste des envois.</summary>
public sealed class OutboxRowVm(OutboxItem item, bool conflict, SyncCenter sync)
{
    public string Label { get; } = item.Label;
    public string When { get; } = item.CreatedAt.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    public string? Error { get; } = item.Error;
    public bool IsConflict { get; } = conflict;
    public RelayCommand Retry { get; } = new(() => sync.Retry(item.Id));
    public RelayCommand Discard { get; } = new(() => sync.Discard(item.Id));
}

/// <summary>
/// Travail à plusieurs postes et hors ligne.
/// En ligne : un flux en direct apporte les écritures des autres postes (l'écran se met à jour seul, sans fermer
/// la fenêtre ouverte), la présence de chacun et les actions réservées ; ce poste s'annonce toutes les 15 secondes.
/// Hors ligne : l'appareil continue sur ses données gardées, garde ses opérations, cherche le serveur toutes les
/// 5 secondes, puis à son retour rouvre la session et envoie les opérations dans l'ordre, sans doublon.
/// </summary>
public sealed class SyncCenter : Observable, IDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);

    private readonly ShellViewModel shell;
    private readonly ScoliaClient api;
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    private CancellationTokenSource? running;
    private string? userName;
    private string? password;
    private bool syncing;
    private bool flushing;
    private string? activity;
    private string ticker = string.Empty;
    private bool pulse;
    private DispatcherTimer? tickerTimer;
    private List<LockDto> locks = [];

    public SyncCenter(ShellViewModel shell, ScoliaClient api)
    {
        this.shell = shell;
        this.api = api;
        api.ConnectivityChanged += online => Dispatcher.UIThread.Post(() => OnConnectivity(online));
        api.Queued += item => Dispatcher.UIThread.Post(() =>
        {
            RaiseCounts();
            shell.Notify($"{item.Label} : gardé sur cet appareil. Il sera envoyé à l'ordinateur principal dès son retour.");
        });
        SyncNow = new AsyncCommand(() => SyncAsync(manual: true));
    }

    /// <summary>Un autre poste a enregistré une modification : l'écran concerné se met à jour.</summary>
    public event Action<LiveEventDto>? RemoteChange;

    /// <summary>Les actions réservées ont changé : une action bloquée peut redevenir possible.</summary>
    public event Action? LocksChanged;

    public bool Online => api.IsOnline;
    public bool Offline => !api.IsOnline;
    public bool Syncing { get => syncing; private set { if (Set(ref syncing, value)) RaiseStatus(); } }
    public int Pending => api.Offline?.Outbox().Count ?? 0;
    public int Conflicts => api.Offline?.Conflicts().Count ?? 0;
    public bool HasPending => Pending > 0;
    public bool HasConflicts => Conflicts > 0;
    public bool HasQueue => HasPending || HasConflicts;

    public ObservableCollection<PeerVm> Peers { get; } = [];
    public bool HasPeers => Peers.Count > 0;
    public string PeersText => Peers.Count switch { 0 => "Seul sur le réseau", 1 => "1 autre poste", _ => $"{Peers.Count} autres postes" };
    public string PeersCount => Peers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public ObservableCollection<OutboxRowVm> Queue { get; } = [];

    /// <summary>Dernière activité d'un autre poste, affichée quelques secondes : « PC Secrétariat · Reçu enregistré ».</summary>
    public string Ticker { get => ticker; private set { if (Set(ref ticker, value)) Raise(nameof(HasTicker)); } }
    public bool HasTicker => ticker.Length > 0;

    /// <summary>Vrai un court instant à chaque activité d'un autre poste : le point vert pulse.</summary>
    public bool Pulse { get => pulse; private set => Set(ref pulse, value); }

    public AsyncCommand SyncNow { get; }

    /// <summary>État lisible de la connexion, pour la barre du haut et l'accueil.</summary>
    public string StatusText => syncing ? "Synchronisation…"
        : Offline ? (HasPending ? $"Hors ligne · {Pending} en attente" : "Hors ligne")
        : HasConflicts ? $"{Conflicts} opération(s) refusée(s)"
        : "En ligne";

    public string StatusDetail => Offline
        ? "L'ordinateur principal ne répond pas. Vous travaillez sur les données gardées sur cet appareil ; vos opérations seront envoyées à son retour."
        : HasConflicts ? "Le serveur a refusé des opérations faites hors ligne. Ouvrez la liste pour les revoir."
        : $"Relié à l'ordinateur principal · {PeersText}.";

    public IBrush StatusBrush => ThemeBrush.Get(syncing ? "ToneBlue" : Offline ? "ToneOrange" : HasConflicts ? "ToneRed" : "ToneGreen");

    /// <summary>Action en cours sur ce poste, annoncée aux autres : « Encaissement de MUKENDI ».</summary>
    public string? Activity
    {
        get => activity;
        set
        {
            if (activity == value) return;
            activity = value;
            _ = AnnounceAsync();
        }
    }

    /// <summary>Garde le compte connecté en mémoire, pour rouvrir la session au retour du serveur.</summary>
    public void Remember(string user, string secret)
    {
        userName = user;
        password = secret;
    }

    public void Start()
    {
        Stop();
        running = new CancellationTokenSource();
        var token = running.Token;
        _ = StreamLoopAsync(token);
        _ = HeartbeatLoopAsync(token);
        RaiseCounts();
    }

    public void Stop()
    {
        if (running is null) return;
        running.Cancel();
        running.Dispose();
        running = null;
        _ = LeaveAsync();
        held.Clear();
        Peers.Clear();
        locks = [];
        RaisePeers();
    }

    public void Dispose() => Stop();

    /// <summary>Oublie le compte : à la déconnexion, rien ne reste en mémoire.</summary>
    public void Forget()
    {
        userName = null;
        password = null;
    }

    // ---------------- Verrous d'action ----------------

    /// <summary>
    /// Réserve une action pour ce poste. Refusée si un autre poste l'a commencée : le message dit lequel.
    /// Hors ligne, l'action reste possible ; l'opération est vérifiée par le serveur à l'envoi.
    /// </summary>
    public async Task<(bool Granted, string? Holder)> AcquireAsync(string key, string label)
    {
        if (Offline) return (true, null);
        try
        {
            await api.SendAsync<LockDto>(HttpMethod.Post, "api/direct/verrous", new { key, label });
            held.Add(key);
            return (true, null);
        }
        catch (ApiException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return (false, ex.Message);
        }
        catch (ApiException) { return (true, null); }
    }

    public async Task ReleaseAsync(string key)
    {
        if (!held.Remove(key) || Offline) return;
        try { await api.SendAsync(HttpMethod.Delete, $"api/direct/verrous?cle={Uri.EscapeDataString(key)}"); }
        catch (ApiException) { }
        catch (OfflineQueuedException) { }
    }

    /// <summary>
    /// Fait une action sous verrou : refusée avec le nom de l'autre poste s'il l'a commencée, annoncée aux autres
    /// pendant qu'elle dure, puis rendue à la fin, même en cas d'erreur.
    /// </summary>
    public async Task<bool> WithLockAsync(string key, string label, Func<Task<bool>> action)
    {
        var (granted, holder) = await AcquireAsync(key, label);
        if (!granted)
        {
            shell.Notify(holder ?? $"{label} : déjà en cours sur un autre poste.", SoundKind.Error);
            return false;
        }
        var before = activity;
        Activity = label;
        try
        {
            using var underLock = ScoliaClient.UnderLock(key);
            return await action();
        }
        finally
        {
            Activity = before;
            await ReleaseAsync(key);
        }
    }

    /// <summary>Poste qui tient cette action, s'il en est un autre que celui-ci.</summary>
    public LockDto? HolderOf(string key) => locks.FirstOrDefault(l => l.Key == key && l.DeviceId != DeviceIdentity.Id);

    // ---------------- Liste des envois ----------------

    public void LoadQueue()
    {
        Queue.Clear();
        if (api.Offline is not { } store) return;
        foreach (var item in store.Conflicts()) Queue.Add(new OutboxRowVm(item, true, this));
        foreach (var item in store.Outbox()) Queue.Add(new OutboxRowVm(item, false, this));
        RaiseCounts();
    }

    public void Retry(string id)
    {
        api.Offline?.Retry(id);
        LoadQueue();
        _ = SyncAsync(manual: true);
    }

    public void Discard(string id)
    {
        api.Offline?.Discard(id);
        LoadQueue();
    }

    // ---------------- Connexion, reconnexion et envoi ----------------

    private void OnConnectivity(bool online)
    {
        RaiseStatus();
        if (online) _ = SyncAsync(manual: false);
        else
        {
            // Sans serveur, la présence des autres postes n'est plus connue : elle reviendra avec lui.
            Peers.Clear();
            locks = [];
            Ticker = string.Empty;
            RaisePeers();
            shell.Notify("Ordinateur principal injoignable : vous continuez sur les données de cet appareil.", SoundKind.Error);
        }
    }

    /// <summary>
    /// Au retour du serveur : la session est rouverte si elle a expiré, puis les opérations gardées partent dans
    /// l'ordre. L'écran est ensuite rechargé avec les données de l'ordinateur principal.
    /// </summary>
    public async Task SyncAsync(bool manual)
    {
        if (flushing || !shell.IsLoggedIn) return;
        flushing = true;
        Syncing = true;
        try
        {
            if (!await api.IsRunningAsync())
            {
                if (manual) shell.Notify("L'ordinateur principal ne répond toujours pas. Vérifiez qu'il est allumé et sur le même Wi-Fi.");
                return;
            }
            if (await api.CurrentSessionAsync() is null)
            {
                if (userName is null || password is null)
                {
                    shell.Notify("Le serveur est revenu : reconnectez-vous pour envoyer les opérations en attente.");
                    return;
                }
                await api.LoginAsync(userName, password);
            }
            var (sent, refused) = await api.FlushAsync();
            if (sent > 0) shell.Notify($"{sent} opération(s) envoyée(s) à l'ordinateur principal.");
            if (refused > 0) shell.Notify($"{refused} opération(s) refusée(s) par le serveur : ouvrez « Envois » pour les revoir.", SoundKind.Error);
            if (sent > 0 || refused > 0 || manual) await shell.RefreshQuietlyAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
        finally
        {
            flushing = false;
            Syncing = false;
            RaiseCounts();
        }
    }

    /// <summary>
    /// Annonce ce poste toutes les 15 secondes : sa page, son activité et les verrous qu'il tient encore.
    /// Hors ligne, la même boucle cherche le serveur toutes les 5 secondes.
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(Offline ? ProbeInterval : HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return; }
            if (Offline) await SyncAsync(manual: false);
            else await AnnounceAsync();
        }
    }

    private async Task AnnounceAsync()
    {
        if (!shell.IsLoggedIn || Offline) return;
        try
        {
            var snapshot = await api.SendAsync<LiveSnapshotDto>(HttpMethod.Post, "api/direct/presence",
                new { page = shell.Current?.Title, activity, locks = held.ToList() });
            Apply(snapshot);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or OfflineQueuedException) { }
    }

    private async Task LeaveAsync()
    {
        try { await api.SendAsync(HttpMethod.Delete, "api/direct/presence"); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or OfflineQueuedException) { }
    }

    /// <summary>Flux en direct : reste ouvert, se rouvre seul après une coupure, avec une attente croissante.</summary>
    private async Task StreamLoopAsync(CancellationToken ct)
    {
        var wait = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            if (Offline)
            {
                try { await Task.Delay(ProbeInterval, ct); } catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                using var client = api.CreateStreamClient();
                using var response = await client.GetAsync("api/direct", HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Direct indisponible ({(int)response.StatusCode}).");
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                wait = TimeSpan.FromSeconds(2);
                string? kind = null;
                while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal)) kind = line[7..];
                    else if (line.StartsWith("data: ", StringComparison.Ordinal) && kind is not null) Dispatch(kind, line[6..]);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException) { }
            try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { return; }
            wait = TimeSpan.FromSeconds(Math.Min(30, wait.TotalSeconds * 2));
        }
    }

    private void Dispatch(string kind, string json)
    {
        switch (kind)
        {
            case "etat":
                Apply(JsonSerializer.Deserialize<LiveSnapshotDto>(json, Json.Options));
                break;
            case "donnees" when JsonSerializer.Deserialize<LiveEventDto>(json, Json.Options) is { } change:
                if (change.DeviceId == DeviceIdentity.Id) break;
                Show($"{change.User} · {change.Device} : {change.Summary}");
                RemoteChange?.Invoke(change);
                break;
            case "presence" or "verrou":
                // Une présence ou un verrou a changé ailleurs : la photographie complète remet tout d'aplomb.
                _ = RefreshStateAsync();
                break;
        }
    }

    private async Task RefreshStateAsync()
    {
        try { Apply(await api.GetAsync<LiveSnapshotDto>("api/direct/etat")); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
    }

    private void Apply(LiveSnapshotDto? snapshot)
    {
        if (snapshot is null) return;
        Peers.Clear();
        foreach (var p in snapshot.Presence.Where(p => p.DeviceId != DeviceIdentity.Id)) Peers.Add(new PeerVm(p));
        locks = snapshot.Locks;
        RaisePeers();
        LocksChanged?.Invoke();
    }

    /// <summary>Activité d'un autre poste : affichée quatre secondes, avec un battement du point vert.</summary>
    private void Show(string text)
    {
        Ticker = text;
        Pulse = false;
        Pulse = true;
        tickerTimer?.Stop();
        tickerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        tickerTimer.Tick += (_, _) =>
        {
            tickerTimer?.Stop();
            Ticker = string.Empty;
            Pulse = false;
        };
        tickerTimer.Start();
    }

    private void RaisePeers()
    {
        Raise(nameof(HasPeers));
        Raise(nameof(PeersText));
        Raise(nameof(PeersCount));
        RaiseStatus();
    }

    public void RaiseCounts()
    {
        Raise(nameof(Pending));
        Raise(nameof(Conflicts));
        Raise(nameof(HasPending));
        Raise(nameof(HasConflicts));
        Raise(nameof(HasQueue));
        RaiseStatus();
    }

    private void RaiseStatus()
    {
        Raise(nameof(Online));
        Raise(nameof(Offline));
        Raise(nameof(StatusText));
        Raise(nameof(StatusDetail));
        Raise(nameof(StatusBrush));
    }
}

/// <summary>Fenêtre « Réseau et activité » : la connexion, les autres postes, les envois en attente et les conflits.</summary>
public sealed class NetworkViewModel
{
    public NetworkViewModel(ShellViewModel shell)
    {
        Sync = shell.Sync;
        Sync.LoadQueue();
        Close = new RelayCommand(shell.CloseDialog);
    }

    public SyncCenter Sync { get; }
    public RelayCommand Close { get; }
}
