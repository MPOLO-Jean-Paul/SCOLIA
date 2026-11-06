using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

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
    public AsyncCommand Discard { get; } = new(() => sync.DiscardAsync(item.Id, item.Label));
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
    private static readonly TimeSpan OnlineHealthInterval = TimeSpan.FromSeconds(3);

    private readonly ShellViewModel shell;
    private readonly ScoliaClient api;
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim offlineWarmupGate = new(1, 1);
    private DateTime lastLanDiscoveryUtc = DateTime.MinValue;
    private CancellationTokenSource? running;
    private string? userName;
    private string? password;
    private bool syncing;
    private bool preparing;
    private string preparationText = "Préparez ce téléphone lorsque le serveur est accessible pour copier les dossiers et les comptes autorisés.";
    private bool flushing;
    private bool pendingNoticeShown;
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
            // Ne dépend pas du maintien de cette fenêtre au premier plan : Android
            // prépare une reprise réseau durable pour les opérations de la file.
            // Une erreur système de planification ne doit jamais perdre l'écriture locale.
            try { Platform.RequestBackgroundSync?.Invoke(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException)
            {
                System.Diagnostics.Trace.WriteLine($"SCOLIA : reprise différée non programmée ({ex.GetType().Name}).");
            }
            // Un seul accusé de réception local par épisode hors ligne.
            // Les autres opérations restent consultables par le petit point du titre.
            if (!pendingNoticeShown)
            {
                pendingNoticeShown = true;
                shell.Notify("Modifications enregistrées localement · envoi automatique à la reconnexion.");
            }
        });
        SyncNow = new AsyncCommand(() => SyncAsync(manual: true));
        PrepareOffline = new AsyncCommand(PrepareOfflineAsync);
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
    public AsyncCommand PrepareOffline { get; }
    public bool Preparing => preparing;
    public string PreparationText
    {
        get => preparationText;
        private set => Set(ref preparationText, value);
    }

    /// <summary>
    /// Sauvegarde volontairement une copie complète des dossiers et des comptes
    /// accessibles à cette session. Elle demeure chiffrée et cloisonnée par compte.
    /// </summary>
    public async Task PrepareOfflineAsync()
    {
        if (preparing || !shell.IsLoggedIn) return;
        if (!api.IsOnline)
        {
            shell.Notify("Reconnectez le téléphone au serveur pour télécharger les données nécessaires.");
            return;
        }
        if (!shell.Can(Perm.StudentsRead))
        {
            shell.Notify("Ce compte n'a pas l'autorisation de lire le registre des apprenants.");
            return;
        }
        preparing = true;
        Raise(nameof(Preparing));
        PreparationText = "Préparation des données locales…";
        try
        {
            var token = running?.Token ?? CancellationToken.None;
            var result = await OfflinePreparation.RunAsync(api,
                includeFinancialAccounts: shell.Can(Perm.PaymentsRead),
                progress: state => Dispatcher.UIThread.Post(() =>
                    PreparationText = $"{state.Stage} · {state.Completed}/{state.Expected}"),
                ct: token);
            PreparationText = result.Complete
                ? $"{result.Indexed} apprenants et {result.Accounts} comptes préparés sur cet appareil."
                : $"Copie partielle : {result.Details} dossiers et {result.Accounts} comptes, {result.Errors} erreurs. Réessayez en ligne.";
            shell.Notify(result.Complete
                ? "Les données autorisées sont préparées pour une utilisation hors ligne."
                : "Préparation partielle. Vérifiez les droits, l'espace libre et la connexion.");
        }
        catch (OperationCanceledException) { PreparationText = "Préparation interrompue."; }
        catch (Exception ex) when (ex is OfflineException or ApiException or IOException or InvalidOperationException)
        {
            PreparationText = "Préparation interrompue : vérifiez la connexion et les permissions.";
            System.Diagnostics.Trace.WriteLine($"Préparation hors ligne : {ex.Message}");
        }
        finally
        {
            preparing = false;
            Raise(nameof(Preparing));
        }
    }

    /// <summary>État lisible de la connexion, pour la barre du haut et l'accueil.</summary>
    public string StatusText => Offline ? (HasPending ? $"Hors ligne · {Pending} en attente" : "Hors ligne")
        : syncing ? "Synchronisation…"
        : HasConflicts ? $"{Conflicts} opération(s) refusée(s)"
        : "En ligne";

    public string StatusDetail => Offline
        ? "Serveur inaccessible : les données déjà téléchargées restent consultables. Seules les opérations autorisées sont enregistrées en attente ; les validations définitives reprennent après reconnexion."
        : HasConflicts ? "Le serveur a refusé des opérations faites hors ligne. Ouvrez la liste pour les revoir."
        : TailscaleEndpoint.IsTailnetAddress(api.Server)
            ? $"Réseau privé Tailscale · {PeersText}. La base reste sur l'appareil principal ; les droits SCOLIA restent appliqués."
        : api.IsLongDistance ? $"Liaison Internet chiffrée · {PeersText}. Les opérations sont confirmées par l’appareil principal."
        : $"Relié à l'ordinateur principal · {PeersText}.";

    public IBrush StatusBrush => ThemeBrush.Get(Offline ? "ToneOrange" : syncing ? "ToneBlue" : HasConflicts ? "ToneRed" : "ToneGreen");

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
        _ = ReconnectLoopAsync(token);
        _ = HealthLoopAsync(token);
        // Préparer les pages locales après le rendu de l'accueil, sans retarder l'ouverture.
        _ = WarmOfflinePagesAsync(token);
        RaiseCounts();
    }

    /// <summary>
    /// Précharge en arrière-plan les ressources autorisées dont les utilisateurs ont
    /// besoin sans connexion. La navigation reste prioritaire ; le réseau et les
    /// permissions sont toujours contrôlés par l'API réelle.
    /// </summary>
    private async Task WarmOfflinePagesAsync(CancellationToken ct)
    {
        try
        {
            // Ne surtout pas ralentir l'accueil ni la page de connexion.
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            if (!api.IsOnline || !shell.IsLoggedIn || api.Offline is not { } store ||
                string.IsNullOrWhiteSpace(store.User)) return;
            if (!await offlineWarmupGate.WaitAsync(0, ct)) return;
            try
            {
                var user = store.User;
                bool StillAllowed() => !ct.IsCancellationRequested && api.IsOnline &&
                    shell.IsLoggedIn && ReferenceEquals(api.Offline, store) &&
                    string.Equals(store.User, user, StringComparison.Ordinal);

                if (StillAllowed() && shell.Can(Perm.PilotageRead))
                {
                    // Le Promoteur n'a pas droit aux listes nominatives : seulement
                    // son tableau de bord consolidé, y compris hors ligne.
                    try { await api.GetAsync<PromoterDashboardDto>("api/pilotage/synthese", ct); }
                    catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
                }

                if (StillAllowed() && shell.Can(Perm.ReportsRead))
                {
                    try { await api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord", ct); }
                    catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
                    if (StillAllowed())
                    {
                        try { await api.GetAsync<TodayDto>("api/rapports/aujourdhui", ct); }
                        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
                    }
                }

                if (!StillAllowed() || !shell.Can(Perm.StudentsRead)) return;
                try { await api.GetAsync<List<PromotionDto>>("api/etablissement/promotions", ct); }
                catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
                for (var page = 1; page <= 20 && StillAllowed(); page++)
                {
                    // Mêmes clés de cache que la liste Apprenants ; les 20 premières
                    // pages couvrent jusqu'à 1 000 dossiers, sans inventer les suivants.
                    var path = "api/apprenants" + ScoliaClient.Query(("page", page), ("taille", 50));
                    PagedResult<StudentListItem>? result;
                    try { result = await api.GetAsync<PagedResult<StudentListItem>>(path, ct); }
                    catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { break; }
                    if (result is null || result.Items.Count == 0 || page * 50 >= result.Total) break;
                    // Priorité à la fluidité des écrans et à la batterie sur Android.
                    await Task.Delay(TimeSpan.FromMilliseconds(175), ct);
                }
            }
            finally { offlineWarmupGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Cache indisponible : l'application principale doit continuer à fonctionner.
        }
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

    /// <summary>Ne jamais supprimer un conflit hors ligne sans confirmation explicite.</summary>
    public async Task DiscardAsync(string id, string label)
    {
        if (flushing || Syncing)
        {
            shell.Notify("Synchronisation en cours : vérifiez le résultat avant d'abandonner cette opération.");
            return;
        }

        var store = api.Offline;
        if (store is null || !store.Conflicts().Any(x => x.Id == id))
        {
            LoadQueue();
            shell.Notify("Cette opération n'est plus en attente de décision.");
            return;
        }

        // Le centre réseau est lui-même un dialogue : le restaurer après la confirmation,
        // qu'elle soit acceptée ou annulée, pour ne pas perdre le contexte de la file.
        var networkDialog = shell.Dialog is NetworkViewModel ? shell.Dialog : null;
        bool confirmed;
        try
        {
            confirmed = await shell.Confirm(
                "Abandonner cette opération hors ligne ?",
                $"Opération : {label}.\n\nCette modification refusée sera retirée de cet appareil et ne sera plus réessayée. Cela ne supprime ni n'annule aucune opération déjà validée sur le serveur.",
                "Abandonner définitivement", danger: true);
        }
        finally
        {
            if (networkDialog is not null && shell.Dialog is null) shell.ShowNetwork();
        }

        if (!confirmed) return;
        if (flushing || Syncing || !ReferenceEquals(store, api.Offline))
        {
            LoadQueue();
            shell.Notify("L'état de la synchronisation a changé. Vérifiez de nouveau l'opération.");
            return;
        }

        if (!store.Conflicts().Any(x => x.Id == id))
        {
            LoadQueue();
            shell.Notify("Cette opération n'est plus dans les conflits.");
            return;
        }

        store.Discard(id);
        LoadQueue();
        shell.Notify("Opération refusée retirée localement. Aucune transaction du serveur n'a été annulée.");
    }

    // ---------------- Connexion, reconnexion et envoi ----------------

    private void OnConnectivity(bool online)
    {
        RaiseStatus();
        if (online)
        {
            pendingNoticeShown = false;
            _ = SyncAsync(manual: false);
        }
        else
        {
            // Sans serveur, la présence des autres postes n'est plus connue : elle reviendra avec lui.
            Peers.Clear();
            locks = [];
            Ticker = string.Empty;
            RaisePeers();
            // L'icône orange du titre suffit ; ne jamais interrompre la saisie.
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
        // Une reprise réseau doit recharger les données locales même si aucune
        // opération n'attend dans le journal.
        var wasOffline = Offline;
        try
        {
            if (!await api.IsRunningAsync())
            {
                if (manual) shell.Notify("L'ordinateur principal ne répond toujours pas. Vérifiez qu'il est allumé et que la liaison Wi-Fi ou Internet est disponible.");
                return;
            }
            if (await api.CurrentSessionAsync() is null)
            {
                if (userName is null || password is null)
                {
                    // Appareil autorisé par QR : renouvelle sa propre session sans le mot de passe du compte du serveur.
                    if (await api.RestoreDeviceSessionAsync() is null)
                    {
                        shell.Notify("Le serveur est revenu mais l'autorisation de cet appareil doit être renouvelée.");
                        return;
                    }
                }
                else
                {
                    var restored = await api.LoginAsync(userName, password);
                    api.Offline?.SaveCredential(userName, password, restored, api.SessionCookie);
                }
            }
            var (sent, refused) = await api.FlushAsync();
            // Le point du réseau passe simplement au vert. Ne pas afficher une notification
            // à chaque reconnexion réussie : seules les opérations refusées demandent une action.
            if (refused > 0) shell.Notify($"{refused} opération(s) refusée(s) par le serveur : ouvrez « Envois » pour les revoir.", SoundKind.Error);
            if (wasOffline || sent > 0 || refused > 0 || manual) await shell.RefreshQuietlyAsync();
            if (wasOffline && running is { } session && !session.IsCancellationRequested)
                _ = WarmOfflinePagesAsync(session.Token);
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
    /// Contrôle la disponibilité du serveur même lorsque personne ne navigue et
    /// que le flux temps réel a été interrompu. Les deux interfaces Avalonia (PC
    /// et Android) utilisent le même état ; aucune notification répétée.
    /// La boucle de reprise existante prend le relais dès le premier échec.
    /// </summary>
    private async Task HealthLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(OnlineHealthInterval, ct); }
            catch (OperationCanceledException) { return; }

            if (!shell.IsLoggedIn || Offline) continue;
            try { await api.IsRunningAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // IsRunningAsync annonce déjà une coupure HTTP ; une erreur
                // inattendue de transport ne doit pas tuer la surveillance.
                System.Diagnostics.Trace.WriteLine($"SCOLIA : sonde de liaison interrompue ({ex.GetType().Name}).");
            }
        }
    }

    /// <summary>Recherche le serveur toutes les cinq secondes pendant une coupure.</summary>
    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(ProbeInterval, ct); }
            catch (OperationCanceledException) { return; }
            if (!Offline) continue;
            // Sur un nouveau Wi-Fi, l'ancien serveur peut avoir une autre IP.
            // Ne suivre que le serveur prouvant le secret d'un jumelage antérieur.
            if (DateTime.UtcNow - lastLanDiscoveryUtc >= TimeSpan.FromSeconds(25))
            {
                lastLanDiscoveryUtc = DateTime.UtcNow;
                try
                {
                    if (await shell.TryReconnectPairedLanAsync(ct) ||
                        await shell.TryReconnectRelayAsync(ct))
                    {
                        await SyncAsync(manual: true);
                        _ = WarmOfflinePagesAsync(ct);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
            await SyncAsync(manual: false);
        }
    }

    /// <summary>
    /// Annonce l'activité du poste toutes les quinze secondes, sans retarder
    /// la reprise d'une liaison perdue pendant cette attente.
    /// </summary>
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return; }
            if (!Offline) await AnnounceAsync();
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
        Remote = new RemoteConnectionViewModel(shell);
        Sync.LoadQueue();
        Close = new RelayCommand(shell.CloseDialog);
    }

    public SyncCenter Sync { get; }
    public RemoteConnectionViewModel Remote { get; }
    public RelayCommand Close { get; }
}
