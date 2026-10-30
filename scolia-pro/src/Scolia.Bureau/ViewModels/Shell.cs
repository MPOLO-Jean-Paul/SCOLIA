using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Input;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Ribbon;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Fenêtre principale : ruban, écran courant, boîtes de dialogue, notifications et barre d'état.</summary>
public sealed class ShellViewModel : Observable
{
    /// <summary>Délai entre deux vérifications de la révision des données, pour voir les écritures des autres postes.</summary>
    private static readonly TimeSpan RevisionInterval = TimeSpan.FromSeconds(5);

    private readonly ScoliaClient api;
    private readonly DispatcherTimer revisionTimer;
    private readonly List<(string Label, Func<PageViewModel> Open)> recents = [];
    private long knownRevision = -1;
    private SessionDto? session;
    private PageViewModel? current;
    private object? dialog;
    private bool backstage;
    private string status = "Démarrage de SCOLIA…";
    private string? notice;
    private RibbonTabVm? selectedTab;
    private ILocalServer? localServer;
    private bool choosingServer;

    public ShellViewModel(ScoliaClient api, Preferences prefs)
    {
        this.api = api;
        Prefs = prefs;
        Login = new LoginViewModel(api);
        Setup = new SetupViewModel(this);
        Connection = new ConnectionViewModel(this);
        Login.LoggedIn += s => _ = OpenSessionAsync(s);
        Sync = new SyncCenter(this, api);
        // Connexion réussie : le compte est gardé sur l'appareil pour une connexion hors ligne, et en mémoire pour
        // rouvrir seul la session quand l'ordinateur principal revient après une coupure.
        Login.Authenticated += (user, secret, s) =>
        {
            api.Offline = new OfflineStore(api.Server) { User = s.UserName };
            api.Offline.SaveCredential(user, secret, s, api.SessionCookie);
            Sync.Remember(user, secret);
        };
        Sync.RemoteChange += OnRemoteChange;
        Backstage = new BackstageViewModel(this);
        ReloadCommand = new AsyncCommand(() => Run(Reload));
        SearchCommand = new RelayCommand(ShowSearch);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        BackCommand = new RelayCommand(GoBack);
        ForwardCommand = new RelayCommand(GoForward);
        revisionTimer = new DispatcherTimer(RevisionInterval, DispatcherPriority.Background, (_, _) => _ = CheckRevisionAsync());
        revisionTimer.Start();
        prefs.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Preferences.Sidebar)) Raise(nameof(SidebarShown));
            if (args.PropertyName == nameof(Preferences.StatusBar)) Raise(nameof(ShowStatusBar));
            // Les sons et la vitesse de transition ne changent pas les ressources
            // graphiques : les réappliquer ici reconstruirait inutilement toute l'interface.
            if (args.PropertyName is nameof(Preferences.Theme) or nameof(Preferences.Accent)
                or nameof(Preferences.Font) or nameof(Preferences.Scale)
                or nameof(Preferences.Compact) or nameof(Preferences.Grid))
                prefs.Apply();
            prefs.Save();
            // Une préférence ne change pas les permissions ni les pages disponibles :
            // conserver les objets de navigation et leurs états, sans recréer le menu.
            if (IsLoggedIn && args.PropertyName is
                nameof(Preferences.Theme) or nameof(Preferences.Compact) or
                nameof(Preferences.Animations) or nameof(Preferences.Grid) or
                nameof(Preferences.StatusBar) or nameof(Preferences.Sidebar) or
                nameof(Preferences.RibbonCollapsed) or nameof(Preferences.Tips))
                RebuildRibbon(rebuildNavigation: false);
        };
        prefs.Apply();
        FormViewModel.Finished += ok => Sounds.Play(Prefs, ok ? SoundKind.Success : SoundKind.Error);
    }

    public ScoliaClient Api => api;
    public Preferences Prefs { get; }
    public LoginViewModel Login { get; }

    /// <summary>Menu Fichier : informations, mot de passe, préférences, déconnexion.</summary>
    public BackstageViewModel Backstage { get; private set; } = null!;

    /// <summary>Commandes de la barre d'accès rapide.</summary>
    public AsyncCommand ReloadCommand { get; private set; } = null!;
    public RelayCommand SearchCommand { get; private set; } = null!;
    public RelayCommand ToggleSidebarCommand { get; private set; } = null!;
    public SessionDto? Session => session;

    private Avalonia.Media.Imaging.Bitmap? schoolLogo;

    /// <summary>Logo de l'établissement, servi par le serveur : il figure sur l'écran de connexion et en tête du volet.</summary>
    /// <summary>Travail à plusieurs postes et hors ligne : direct, présence, verrous, envois en attente.</summary>
    public SyncCenter Sync { get; }

    /// <summary>La vue dit si l'utilisateur est en train d'écrire : une mise à jour venue d'ailleurs attend alors.</summary>
    public Func<bool>? IsTyping { get; set; }

    private bool remoteWaiting;

    /// <summary>Des données ont changé ailleurs pendant une saisie : un bouton discret propose de les afficher.</summary>
    public bool RemoteWaiting { get => remoteWaiting; private set => Set(ref remoteWaiting, value); }

    public AsyncCommand ApplyRemoteCommand => applyRemote ??= new AsyncCommand(RefreshQuietlyAsync);
    private AsyncCommand? applyRemote;

    /// <summary>
    /// Écriture d'un autre poste : l'écran se met à jour seul, en gardant la ligne choisie et la position.
    /// Pendant une saisie, une fenêtre ouverte ou un encaissement, rien ne bouge : la mise à jour attend.
    /// </summary>
    private void OnRemoteChange(LiveEventDto change)
    {
        knownRevision = Math.Max(knownRevision, change.Revision);
        if (MustWait) RemoteWaiting = true;
        else _ = RefreshQuietlyAsync();
    }

    private bool MustWait => HasDialog || BackstageOpen || HasSheet || PhoneMenuOpen || IsTyping?.Invoke() == true
        || Current?.Busy == true || running > 0
        || Current is CashierPage { HasStudent: true } or BatchPaymentPage;

    /// <summary>Recharge l'écran sans notification ni perte de la ligne choisie, ni de la hauteur où il était lu.</summary>
    private bool refreshingQuietly;
    private bool quietRefreshRequested;

    public async Task RefreshQuietlyAsync()
    {
        if (refreshingQuietly) { quietRefreshRequested = true; return; }
        refreshingQuietly = true;
        try
        {
            do
            {
                quietRefreshRequested = false;
                RemoteWaiting = false;
                if (Current is not { } page) return;
                await page.RefreshQuietlyAsync();
                // Une rafale d'événements du direct se regroupe en une seule lecture suivante.
                if (quietRefreshRequested && MustWait) { RemoteWaiting = true; return; }
            } while (quietRefreshRequested);
            // Le défilement reste sous le contrôle du doigt : aucune ancienne position n'est réimposée.
        }
        finally { refreshingQuietly = false; }
    }

    /// <summary>
    /// Fourni par la vue : relève la position de défilement de la page et rend l'action qui la remet,
    /// une fois l'écran rechargé. Une mise à jour venue d'un autre poste ne ramène pas la page en haut.
    /// </summary>
    public Func<Action>? KeepScrollPosition { get; set; }

    public Avalonia.Media.Imaging.Bitmap? SchoolLogo
    {
        get => schoolLogo;
        private set { if (Set(ref schoolLogo, value)) { Raise(nameof(HasSchoolLogo)); Login.Logo = value; } }
    }

    public bool HasSchoolLogo => schoolLogo is not null;

    /// <summary>Année scolaire en cours, sous le nom de l'établissement dans le volet.</summary>
    public string SchoolYearLine => session?.CurrentYear is { } year ? $"Année {year}" : string.Empty;

    /// <summary>Nom court de l'établissement pour l'en-tête du volet.</summary>
    public string SchoolTitle => session?.SchoolShortName is { Length: > 0 } shortName ? shortName : session?.SchoolName ?? Login.SchoolName;

    private int logoRequest;
    /// <summary>Recharge uniquement le logo de l'établissement actif, jamais un logo en cache d'un ancien serveur.</summary>
    public async Task RefreshLogoAsync()
    {
        var request = ++logoRequest;
        var server = api.Server;
        SchoolLogo = null;
        try
        {
            var bytes = await api.DownloadAsync("api/etablissement/logo");
            if (request != logoRequest || api.Server != server) return;
            using var stream = new MemoryStream(bytes);
            SchoolLogo = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            if (request == logoRequest) SchoolLogo = null;
        }
    }
    public bool IsLoggedIn => session is not null;

    private bool setupRequired;
    private bool entryChecked;

    /// <summary>Un écran neutre apparaît durant la détection, jamais une fausse connexion.</summary>
    public bool ShowStartup => !entryChecked && !ChoosingServer && !Opening && !IsLoggedIn;

    /// <summary>Une réponse vérifiée du serveur décide entre connexion et assistant.</summary>
    public void ApplyInstallationStatus(bool configured)
    {
        IsSetupRequired = !configured;
        if (entryChecked) return;
        entryChecked = true;
        RaiseLogin();
    }

    private void MarkEntryChecked()
    {
        if (entryChecked) return;
        entryChecked = true;
        RaiseLogin();
    }

    /// <summary>Premier lancement : aucune configuration sur ce serveur, l'assistant remplace la connexion.</summary>
    public bool IsSetupRequired { get => setupRequired; set { if (Set(ref setupRequired, value)) { RaiseLogin(); Raise(nameof(ShowSetup)); } } }
    public bool ShowSetup => entryChecked && IsSetupRequired && !Opening;

    /// <summary>La connexion s'affiche seulement quand une configuration existe et qu'aucune session n'est ouverte.</summary>
    public bool ShowLogin => entryChecked && !IsLoggedIn && !IsSetupRequired && !ChoosingServer && !Opening;

    /// <summary>L'application s'affiche une fois l'ouverture terminée.</summary>
    public bool ShowApp => IsLoggedIn && !Opening;

    // ---------- Ouverture : entre la connexion et l'accueil, une roue et les étapes, au moins deux secondes ----------
    /// <summary>Durée minimale de l'écran d'ouverture : le passage à l'accueil reste lisible, sans cassure.</summary>
    public static TimeSpan MinimumOpening { get; set; } = TimeSpan.FromSeconds(2);

    private bool opening;
    private bool openingCompact;
    private string openingStep = string.Empty;
    private double openingProgress;

    public bool Opening
    {
        get => opening;
        private set
        {
            if (!Set(ref opening, value)) return;
            Raise(nameof(ShowLogin)); Raise(nameof(ShowSetup)); Raise(nameof(ShowStartup)); Raise(nameof(ShowApp)); Raise(nameof(CompactWindow));
        }
    }

    /// <summary>La petite fenêtre de connexion reste petite pendant l'ouverture qui suit la connexion.</summary>
    public bool CompactWindow => ShowLogin || (Opening && openingCompact);
    public string OpeningStep { get => openingStep; private set => Set(ref openingStep, value); }
    public double OpeningProgress { get => openingProgress; private set => Set(ref openingProgress, value); }
    private string openingTitle = string.Empty;
    public string OpeningTitle { get => openingTitle; private set => Set(ref openingTitle, value); }

    private async Task OpeningStepAsync(string step, double progress, TimeSpan shown)
    {
        OpeningStep = step;
        OpeningProgress = progress;
        await Task.Delay(shown);
    }

    /// <summary>Choix du serveur de l'établissement, affiché quand le choix n'est pas évident.</summary>
    public ConnectionViewModel Connection { get; }

    public bool ChoosingServer { get => choosingServer; private set { if (Set(ref choosingServer, value)) RaiseLogin(); } }

    /// <summary>Vrai quand cet ordinateur est le serveur de l'établissement, faux quand il est relié à un autre.</summary>
    public bool IsServerComputer => api.Server.IsLoopback;

    public SetupViewModel Setup { get; }

    private void RaiseLogin()
    {
        Raise(nameof(IsLoggedIn));
        Raise(nameof(ShowLogin));
        Raise(nameof(ShowSetup));
        Raise(nameof(ShowStartup));
        Raise(nameof(ShowApp));
        Raise(nameof(CompactWindow));
    }

    /// <summary>L'assistant a créé la configuration et ouvert la première session ; l'ouverture garde la grande fenêtre.</summary>
    public Task CompleteSetupAsync(SessionDto session) => OpenSessionAsync(session, compact: false);
    public string SchoolLine => session is null ? string.Empty : $"{session.SchoolName}{(session.CurrentYear is null ? string.Empty : $" · année {session.CurrentYear}")}";
    public string UserLine => session is null ? string.Empty : $"{session.DisplayName} · {session.RoleLabel}";
    public string UserInitials => session is null ? string.Empty : Initials(session.DisplayName);
    /// <summary>Nom affiché en tête du volet latéral, au-dessus de « Compte et poste ».</summary>
    public string AccountName => session?.DisplayName ?? string.Empty;
    public string ServerLine => IsServerComputer
        ? $"Serveur de l'établissement · port {api.Server.Port}"
        : ConnectionStore.Load() is { IsRemote: true } link ? $"Relié à {link.Machine} · {api.Server.Host}" : $"Serveur {api.Server.Host}:{api.Server.Port}";

    public PageViewModel? Current
    {
        get => current;
        private set
        {
            var previous = current;
            if (!Set(ref current, value)) return;
            previous?.Left();
            value?.Entered();
            Notice = null;
            RemoteWaiting = false;
            Raise(nameof(HasPage));
            Raise(nameof(DesktopPage));
            Raise(nameof(PhonePage));
            PhoneMenuOpen = false;
            Sheet = null;
            // Entrer dans une fonctionnalité referme son accordéon, y compris au clavier.
            CollapseGroups();
            MarkNavigation();
        }
    }
    public bool HasPage => current is not null;

    /// <summary>Boîte de dialogue ouverte : formulaire, recherche. Null quand aucune n'est affichée.</summary>
    public object? Dialog
    {
        get => dialog;
        private set
        {
            if (!Set(ref dialog, value)) return;
            Raise(nameof(HasDialog));
            Raise(nameof(Working));
            // La fenêtre se ferme : les mises à jour venues d'un autre poste pendant la saisie s'affichent.
            if (value is null && remoteWaiting) _ = RefreshQuietlyAsync();
        }
    }
    public bool HasDialog => dialog is not null;

    public bool BackstageOpen { get => backstage; private set => Set(ref backstage, value); }

    public string Status { get => status; private set => Set(ref status, value); }
    public string? Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(notice);

    public ObservableCollection<RibbonTabVm> Tabs { get; } = [];

    public RibbonTabVm? SelectedTab
    {
        get => selectedTab;
        private set
        {
            if (selectedTab is not null) selectedTab.IsSelected = false;
            if (Set(ref selectedTab, value) && value is not null) value.IsSelected = true;
            Raise(nameof(SelectedGroups));
        }
    }

    public IReadOnlyList<RibbonGroupVm> SelectedGroups => SelectedTab?.Groups ?? [];

    /// <summary>Lit un fichier choisi par l'utilisateur. Fourni par la fenêtre, qui détient le sélecteur de fichiers.</summary>
    public Func<IReadOnlyList<string>, Task<PickedFile?>>? PickFileHandler { get; set; }

    /// <summary>Copie un texte dans le presse-papiers du poste ; fourni par la fenêtre.</summary>
    public Func<string, Task>? CopyHandler { get; set; }

    public bool Can(string permission) => session?.Permissions.Contains(permission) == true;

    /// <summary>Plein écran : la fenêtre gère l'affichage réel, le shell garde l'état pour le ruban.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>Fourni par la fenêtre, qui applique l'état d'affichage.</summary>
    public Action<bool>? FullscreenHandler { get; set; }

    /// <summary>Synchronise le ruban avec l'état réel de la fenêtre, y compris au lancement en plein écran.</summary>
    public void SyncFullscreenState(bool full)
    {
        if (IsFullscreen == full) return;
        IsFullscreen = full;
        Raise(nameof(IsFullscreen));
        if (IsLoggedIn) RebuildRibbon();
    }

    public void ToggleFullscreen()
    {
        SyncFullscreenState(!IsFullscreen);
        FullscreenHandler?.Invoke(IsFullscreen);
    }

    public IReadOnlyList<(string Label, Func<PageViewModel> Open)> Recents => recents;

    /// <summary>
    /// Démarrage : trouve le serveur de l'établissement sans adresse à saisir, comme la version 79.
    /// Un poste qui porte la base en est le serveur. Un poste sans base cherche SCOLIA sur le réseau et le rejoint.
    /// Un serveur mémorisé dont l'adresse a changé est retrouvé par l'identifiant de son établissement.
    /// </summary>
    public async Task StartAsync(ILocalServer? server)
    {
        localServer = server;
        var forced = Environment.GetEnvironmentVariable("SCOLIA_SERVEUR");
        if (!string.IsNullOrWhiteSpace(forced))
        {
            api.Retarget(new Uri(forced));
            await ShowEntryAsync();
            return;
        }

        var saved = ConnectionStore.Load();
        if (RemoteConnection.Load() is { Host: false } remote && saved?.Mode == ConnectionSettings.Internet)
        {
            if (remote.Link.ExpiresUtc <= DateTime.UtcNow)
            {
                ShowChooser([], "La liaison Internet a expiré. Demandez au secrétariat de la renouveler, puis collez la nouvelle invitation. Les données et opérations locales restent conservées.");
                return;
            }
            api.UseRemote(remote.Link);
            if (await api.IsRunningAsync()) await ShowEntryAsync();
            else if (!OfferOfflineEntry(remote.School)) ShowChooser([], "L'appareil principal est indisponible sur Internet. Vos données locales restent conservées. Vérifiez sa connexion ou rejoignez-le sur le Wi-Fi.");
            return;
        }
        if (RemoteConnection.Load() is { Host: true } && saved?.Mode == ConnectionSettings.Local)
            RemoteConnection.StartHost(ConnectionStore.LocalAddress);
        if (saved is { IsRemote: true })
        {
            api.Retarget(new Uri(saved.Url!));
            if (await api.IsRunningAsync())
            {
                await ShowEntryAsync();
                return;
            }
            Status = $"Recherche du serveur de {saved.School}…";
            var servers = await LanDiscovery.FindAsync();
            if (servers.FirstOrDefault(s => s.SchoolId == saved.SchoolId) is { } moved)
            {
                await JoinAsync(moved);
                return;
            }
            if (OfferOfflineEntry(saved.School ?? string.Empty)) return;
            ShowChooser(servers, $"Le serveur de {saved.School} (ordinateur {saved.Machine}) ne répond pas. Allumez-le ou ouvrez SCOLIA dessus, puis recherchez à nouveau.");
            return;
        }

        if (Platform.IsMobile)
        {
            // Téléphone serveur de l'établissement : son serveur démarre avec l'application.
            if (saved?.Mode == ConnectionSettings.Local && localServer is not null)
            {
                api.Retarget(ConnectionStore.LocalAddress);
                await ShowEntryAsync();
                return;
            }
            // Premier lancement : rejoindre le SCOLIA de l'établissement sur le Wi-Fi, ou faire de ce téléphone le serveur.
            Status = "Recherche de SCOLIA sur le Wi-Fi de l'établissement…";
            var nearby = await LanDiscovery.FindAsync(TimeSpan.FromSeconds(2.5));
            ShowChooser(nearby, nearby.Count switch
            {
                0 => Platform.CanHostServer
                    ? "Aucun SCOLIA ne répond sur ce Wi-Fi. Configurez l'établissement sur ce téléphone : il en devient le serveur, et les autres téléphones et ordinateurs s'y connectent. Ou saisissez l'adresse du serveur."
                    : "Aucun SCOLIA ne répond sur ce Wi-Fi. Le téléphone doit être sur le même réseau que le serveur de l'établissement. Vous pouvez aussi saisir son adresse.",
                1 => "SCOLIA a été trouvé sur ce Wi-Fi : rejoignez votre établissement.",
                _ => "Plusieurs SCOLIA répondent sur ce Wi-Fi : choisissez celui de votre établissement.",
            });
            return;
        }

        if (saved?.Mode == ConnectionSettings.Local || ConnectionStore.HasLocalData())
        {
            await ShowEntryAsync();
            return;
        }

        // Premier lancement d'un poste sans base : SCOLIA cherche d'abord l'établissement sur le réseau.
        Status = "Recherche de SCOLIA sur le réseau de l'établissement…";
        var found = await LanDiscovery.FindAsync();
        if (found.Count == 1)
        {
            await JoinAsync(found[0]);
            return;
        }
        if (found.Count > 1)
        {
            ShowChooser(found, "Plusieurs SCOLIA répondent sur ce réseau : choisissez celui de votre établissement.");
            return;
        }
        // Aucun serveur sur le réseau : cet ordinateur devient celui de l'établissement.
        ConnectionStore.Save(new ConnectionSettings(ConnectionSettings.Local));
        await ShowEntryAsync();
    }

    /// <summary>
    /// Ordinateur principal injoignable au lancement : si un compte s'est déjà connecté sur cet appareil, la connexion
    /// hors ligne est proposée. Le travail continue sur les données gardées, et part au retour du serveur.
    /// </summary>
    private bool OfferOfflineEntry(string school)
    {
        var store = new OfflineStore(api.Server);
        if (!store.HasCredential) return false;
        api.Offline = store;
        if (school.Length > 0) Login.SchoolName = school;
        Login.Notice = "L'ordinateur principal ne répond pas. Connectez-vous avec le dernier compte utilisé sur cet appareil pour travailler hors ligne : tout sera envoyé à son retour.";
        IsSetupRequired = false;
        ChoosingServer = false;
        MarkEntryChecked();
        Status = "Hors ligne : connexion avec les données de cet appareil.";
        return true;
    }

    private void ShowChooser(IReadOnlyList<DiscoveredServer> servers, string message)
    {
        IsSetupRequired = false;
        Connection.Show(servers, message);
        ChoosingServer = true;
        Status = "Choix du serveur de l'établissement.";
    }

    /// <summary>Affiche le choix du serveur et lance une recherche : depuis l'assistant ou depuis Poste et réseau.</summary>
    public async Task ChooseServerAsync()
    {
        ShowChooser([], string.Empty);
        await Connection.SearchAsync();
    }

    /// <summary>Relie ce poste au serveur choisi et le mémorise.</summary>
    public async Task JoinAsync(DiscoveredServer server)
    {
        var previous = ConnectionStore.Load();
        ConnectionStore.Save(new ConnectionSettings(ConnectionSettings.Remote, server.Address.ToString(), server.SchoolId, server.School, server.Machine,
            previous?.LanPromptDone ?? false));
        if (IsLoggedIn) await LogoutAsync();
        // Un téléphone qui était le serveur et en rejoint un autre arrête le sien : il ne garde qu'un établissement.
        if (Platform.IsMobile && localServer is not null && !server.Address.IsLoopback) localServer.Stop();
        RemoteConnection.StopHost();
        api.Retarget(server.Address);
        ChoosingServer = false;
        await ShowEntryAsync();
        Notify($"Ce poste est relié à SCOLIA · {server.School} (ordinateur {server.Machine}).");
    }

    public async Task JoinInternetAsync(Scolia.Transport.RemoteLink link)
    {
        if (IsLoggedIn) await LogoutAsync();
        // Aucun effacement de l'établissement précédemment porté par cet appareil.
        localServer?.Stop();
        RemoteConnection.StopHost();
        api.UseRemote(link);
        var installation = await api.GetAsync<SetupStatusDto>("api/installation");
        var school = installation.SchoolName ?? "Établissement";
        RemoteConnection.Save(new RemoteConfiguration(link, false, school));
        ConnectionStore.Save(new ConnectionSettings(ConnectionSettings.Internet, link.VirtualServer.ToString(), School: school, Machine: "Internet chiffré"));
        ChoosingServer = false;
        await ShowEntryAsync();
        Notify("Liaison Internet sécurisée prête. Connectez-vous avec votre compte habituel.");
    }

    /// <summary>Cet ordinateur, ou ce téléphone, devient (ou redevient) le serveur de l'établissement.</summary>
    public async Task UseThisComputerAsync()
    {
        var previous = ConnectionStore.Load();
        ConnectionStore.Save(new ConnectionSettings(ConnectionSettings.Local, LanPromptDone: previous?.LanPromptDone ?? false));
        if (IsLoggedIn) await LogoutAsync();
        api.Retarget(ConnectionStore.LocalAddress);
        RemoteConnection.StartHost(ConnectionStore.LocalAddress);
        ChoosingServer = false;
        await ShowEntryAsync();
    }

    /// <summary>
    /// Ouvre ou ferme le réseau de l'établissement et l'applique tout de suite. Windows demande l'autorisation ;
    /// SCOLIA fait le reste : pare-feu, réseau privé, découverte des postes et relance de son serveur.
    /// </summary>
    public async Task SetNetworkAsync(bool open)
    {
        var message = await PosteAdmin.RunAsync(open ? "reseau-on" : "reseau-off");
        await RestartLocalServerAsync();
        Notify(message.Replace(Environment.NewLine, " "));
    }

    /// <summary>Relance le serveur de ce poste pour appliquer un réglage. Le service Windows, lui, est relancé par l'opération élevée.</summary>
    public async Task RestartLocalServerAsync()
    {
        if (localServer is not null && IsServerComputer && !(OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled()))
        {
            Status = "Application du réglage réseau…";
            localServer.Restart();
        }
        for (var i = 0; i < 80 && !await api.IsRunningAsync(); i++) await Task.Delay(250);
        Status = await api.IsRunningAsync() ? $"Serveur prêt : {api.Server}" : "Le serveur SCOLIA ne répond pas.";
    }

    /// <summary>
    /// Sur l'ordinateur principal, une seule fois : propose d'ouvrir SCOLIA aux autres ordinateurs de l'établissement.
    /// </summary>
    private async Task OfferNetworkAsync()
    {
        if (!OperatingSystem.IsWindows() || !IsServerComputer || !Can(Perm.SettingsWrite) || PosteAdmin.LanEnabled()) return;
        var saved = ConnectionStore.Load() ?? new ConnectionSettings(ConnectionSettings.Local);
        if (saved.LanPromptDone) return;
        ConnectionStore.Save(saved with { LanPromptDone = true });
        var accepted = await Confirm("Partager SCOLIA avec les autres ordinateurs",
            "Les autres ordinateurs de l'établissement qui ont SCOLIA s'y connecteront automatiquement, sans aucun réglage. "
            + "Windows va vous demander l'autorisation : SCOLIA ouvre alors son port dans le pare-feu et déclare ce réseau comme privé. "
            + "Vous pourrez changer d'avis dans Fichier > Poste et réseau.",
            "Autoriser");
        if (accepted) await Run(() => SetNetworkAsync(true));
    }

    /// <summary>Serveur choisi : le démarre s'il est sur ce poste, puis affiche la connexion ou l'installation.</summary>
    private async Task ShowEntryAsync()
    {
        Raise(nameof(IsServerComputer));
        Raise(nameof(ServerLine));
        if (!await api.IsRunningAsync() && IsServerComputer && localServer is not null)
        {
            Status = Platform.IsMobile ? "Démarrage du serveur SCOLIA sur ce téléphone…" : "Démarrage du serveur SCOLIA…";
            localServer.Start();
            // Un téléphone met plus de temps qu'un ordinateur à préparer le serveur, surtout la première fois.
            var tries = Platform.IsMobile ? 240 : 80;
            for (var i = 0; i < tries && !await api.IsRunningAsync(); i++) await Task.Delay(250);
        }
        var ready = await api.IsRunningAsync();
        Status = ready ? $"Serveur prêt : {api.Server}" : "Le serveur SCOLIA ne répond pas.";
        if (!ready)
        {
            if (!IsServerComputer && !OfferOfflineEntry(Login.SchoolName))
                ShowChooser([], "Le serveur de l'établissement ne répond pas. Vérifiez que l'ordinateur principal est allumé, puis recherchez à nouveau.");
            return;
        }
        // Configuration reconnue : la connexion s'affiche directement. Sinon, l'assistant de première installation.
        try
        {
            var status = await api.GetAsync<SetupStatusDto>("api/installation");
            Login.SchoolName = status.SchoolName ?? string.Empty;
            // La proposition d’année et le logo sont accessoires : ils ne bloquent plus la première page.
            if (!status.IsConfigured)
            {
                SchoolLogo = null;
                _ = Setup.LoadAsync();
            }
            else _ = RefreshLogoAsync();
            ApplyInstallationStatus(status.IsConfigured);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            Status = "Impossible de vérifier la configuration de SCOLIA.";
            ShowChooser([], "Impossible de vérifier la configuration. Réessayez la connexion au serveur.");
        }
    }

    /// <summary>
    /// Ouvre la session : l'écran d'ouverture montre une roue et les étapes, au moins deux secondes,
    /// pendant que le ruban et l'accueil se préparent ; l'accueil apparaît ensuite d'un seul geste.
    /// </summary>
    public async Task OpenSessionAsync(SessionDto s, bool compact = true)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Durée choisie dans Paramètres, Animations et fluidité ; l'outil de captures peut la fixer à part.
        // Après la création du premier compte, éviter la séquence d’attente de connexion.
        var minimum = IsSetupRequired ? TimeSpan.FromMilliseconds(320)
            : MinimumOpening == TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(Prefs.OpeningSeconds) : MinimumOpening;
        var pace = minimum / 4;
        openingCompact = compact;
        session = s;
        knownRevision = -1;
        // Données gardées sur l'appareil, propres à ce compte ; puis le direct avec les autres postes.
        api.Offline ??= new OfflineStore(api.Server);
        api.Offline.User = s.UserName;
        Sync.Start();
        OpeningTitle = IsSetupRequired ? "Configuration terminée" : $"Bienvenue, {s.DisplayName}";
        OpeningProgress = 0;
        Opening = true;
        IsSetupRequired = false;
        RaiseLogin();
        try
        {
            await OpeningStepAsync("Vérification du compte", 20, pace);
            Raise(nameof(SchoolTitle));
            Raise(nameof(SchoolYearLine));
            await OpeningStepAsync($"Chargement de {s.SchoolName}", 45, pace);
            if (SchoolLogo is null) await RefreshLogoAsync();
            Raise(nameof(SchoolLine));
            Raise(nameof(UserLine));
            Raise(nameof(UserInitials));
            Raise(nameof(AccountName));
            Status = $"Connecté : {s.DisplayName} · {s.SchoolName}";
            Login.Reset();
            await OpeningStepAsync("Préparation des menus", 70, pace);
            RebuildRibbon();
            current = null;
            ClearHistory();
            OpeningStep = "Ouverture de l'accueil";
            OpeningProgress = 90;
            await Open(() => new HomePage(this));
            var rest = minimum - watch.Elapsed;
            if (rest > TimeSpan.Zero) await Task.Delay(rest);
            OpeningProgress = 100;
            await Task.Delay(TimeSpan.FromMilliseconds(minimum > TimeSpan.Zero ? 150 : 0));
        }
        finally { Opening = false; }
        Sounds.Play(Prefs, SoundKind.Opening);
        await OfferNetworkAsync();
    }

    /// <summary>Écrans précédents et suivants, comme les flèches des Réglages de macOS. Chaque écran garde sa saisie.</summary>
    private readonly List<PageViewModel> backStack = [];
    private readonly Stack<PageViewModel> forwardStack = new();

    /// <summary>Au-delà, les écrans les plus anciens sortent de l'historique.</summary>
    private const int MaxHistory = 30;

    public bool CanGoBack => backStack.Count > 0;
    public bool CanGoForward => forwardStack.Count > 0;
    public RelayCommand BackCommand { get; private set; } = null!;
    public RelayCommand ForwardCommand { get; private set; } = null!;

    /// <summary>Affiche un écran, le mémorise dans les récents et l'historique, puis charge ses données.</summary>
    public async Task Open(Func<PageViewModel> create)
    {
        var page = create();
        Remember(page.Title, create);
        BackstageOpen = false;
        if (narrow && narrowSidebarOpen)
        {
            narrowSidebarOpen = false;
            Raise(nameof(SidebarShown));
        }
        if (current is not null)
        {
            backStack.Add(current);
            if (backStack.Count > MaxHistory) backStack.RemoveAt(0);
        }
        forwardStack.Clear();
        Show(page);
        await page.RefreshAsync();
    }

    /// <summary>Revient à l'écran précédent, tel qu'il était laissé.</summary>
    public void GoBack()
    {
        if (backStack.Count == 0 || current is null) return;
        forwardStack.Push(current);
        var page = backStack[^1];
        backStack.RemoveAt(backStack.Count - 1);
        BackstageOpen = false;
        Show(page);
    }

    /// <summary>
    /// Quitte un écran dont l'objet vient d'être supprimé : retour à l'écran précédent, ou à celui proposé,
    /// sans le garder dans « Suivant » ni dans l'historique, puisqu'il ne s'ouvrirait plus.
    /// </summary>
    public async Task LeaveDeletedAsync(PageViewModel deleted, Func<PageViewModel> fallback)
    {
        backStack.RemoveAll(p => p == deleted);
        forwardStack.Clear();
        if (current != deleted) return;
        if (backStack.Count > 0)
        {
            var page = backStack[^1];
            backStack.RemoveAt(backStack.Count - 1);
            BackstageOpen = false;
            Show(page);
            await page.RefreshAsync();
        }
        else
        {
            var page = fallback();
            Show(page);
            await page.RefreshAsync();
        }
    }

    public void GoForward()
    {
        if (forwardStack.Count == 0 || current is null) return;
        backStack.Add(current);
        BackstageOpen = false;
        Show(forwardStack.Pop());
    }

    private bool rowTipShown;

    private void Show(PageViewModel page)
    {
        var previous = Current;
        Current = page;
        // Le menu mobile est stable : changer de page ne reconstruit pas tous ses boutons.
        // Seules les fiches ont des commandes contextuelles à remplacer.
        if (!phone || previous is ReceiptDetailPage or StudentDetailPage || page is ReceiptDetailPage or StudentDetailPage)
            RebuildRibbon(rebuildNavigation: false);
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));
        // Une fois par session, sur la première liste qui en propose : où trouver les actions d'une ligne.
        if (!rowTipShown && Prefs.Tips && page.RowActions.Count > 0)
        {
            rowTipShown = true;
            Notify(phone ? "Astuce : touchez « … » sur une carte pour ses actions, ou appuyez longuement dessus."
                : "Astuce : clic droit sur une ligne pour ses actions, double-clic pour l'ouvrir.");
        }
    }

    private void ClearHistory()
    {
        backStack.Clear();
        forwardStack.Clear();
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));
    }

    /// <summary>Recharge l'écran courant.</summary>
    /// <summary>Recharge l'écran affiché et le confirme d'un message discret.</summary>
    public async Task Reload()
    {
        if (Current is not { } page) return;
        await page.RefreshAsync();
        Notify($"{page.Title} : données à jour.");
    }

    /// <summary>Exécute une action métier : les erreurs deviennent une notification, jamais un plantage.</summary>
    private int running;
    private bool workingDue;
    private int workGeneration;

    /// <summary>
    /// Traitement en cours depuis plus d'un instant : un indicateur s'affiche au centre de la fenêtre, sauf pendant
    /// qu'une boîte de dialogue attend une saisie. Les actions brèves n'affichent rien, pour ne pas faire clignoter l'écran.
    /// </summary>
    public bool Working => workingDue && running > 0 && !HasDialog && Current?.Busy != true;

    /// <summary>Délai avant l'affichage de l'indicateur de traitement.</summary>
    private static readonly TimeSpan WorkingDelay = TimeSpan.FromMilliseconds(350);

    public async Task Run(Func<Task> work)
    {
        if (running++ == 0) _ = ShowWorkingLaterAsync(++workGeneration);
        try { await work(); }
        // Opération gardée hors ligne : ce n'est pas une erreur, la notification vient de la file d'envoi.
        catch (OfflineQueuedException) { }
        catch (ApiException ex) { Notify(ex.Message, SoundKind.Error); }
        catch (HttpRequestException) { Notify("Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste.", SoundKind.Error); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notify($"Erreur inattendue : {ex.Message}", SoundKind.Error); }
        finally
        {
            running--;
            if (running == 0)
            {
                workingDue = false;
                Raise(nameof(Working));
            }
            // Nos propres écritures ne doivent pas déclencher un rechargement : on mémorise la révision après l'action.
            if (IsLoggedIn) _ = RememberRevisionAsync();
        }
    }

    private async Task ShowWorkingLaterAsync(int generation)
    {
        await Task.Delay(WorkingDelay);
        if (running == 0 || generation != workGeneration) return;
        workingDue = true;
        Raise(nameof(Working));
    }

    private async Task RememberRevisionAsync()
    {
        if (await ReadRevisionAsync() is { } latest) knownRevision = Math.Max(knownRevision, latest);
    }

    /// <summary>
    /// Vérifie si une autre station a modifié les données. L'écran courant se recharge seul, sauf quand un
    /// formulaire ou l'encaissement est ouvert : on prévient alors l'utilisateur, pour ne pas effacer sa saisie.
    /// </summary>
    private bool checkingRevision;

    private async Task CheckRevisionAsync()
    {
        if (!IsLoggedIn || checkingRevision) return;
        checkingRevision = true;
        try
        {
            if (await ReadRevisionAsync() is not { } latest) return;
            if (latest <= knownRevision) return;
            if (knownRevision < 0)
            {
                knownRevision = latest;
                return;
            }
            knownRevision = latest;
            if (MustWait) RemoteWaiting = true;
            else await RefreshQuietlyAsync();
        }
        finally { checkingRevision = false; }
    }

    private async Task<long?> ReadRevisionAsync()
    {
        try { return (await api.GetAsync<RevisionDto>("api/revision")).Revision; }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>Affiche un formulaire et attend la décision de l'utilisateur.</summary>
    public async Task<bool> Ask(FormViewModel form)
    {
        Dialog = form;
        try { return await form.Completion; }
        finally { Dialog = null; }
    }

    /// <summary>Demande une confirmation avant une action irréversible ou sensible.</summary>
    public Task<bool> Confirm(string title, string message, string okText = "Confirmer", bool danger = false) =>
        Ask(new FormViewModel(title, string.Empty, _ => Task.FromResult(true)) { Message = message, SubmitText = okText, Danger = danger, PlaysSound = false });

    public void CloseDialog() => Dialog = null;

    /// <summary>Affiche la recherche globale, Ctrl+K.</summary>
    public void ShowSearch() => Dialog = new SearchViewModel(this);

    /// <summary>Fenêtre « À propos » : version, éditeur et développeur.</summary>
    public void ShowAbout() => Dialog = new AboutViewModel(this);

    /// <summary>Aide des raccourcis clavier, rangés par onglet.</summary>
    public void ShowShortcuts() => Dialog = new ShortcutsViewModel(this);

    /// <summary>Réseau et activité : qui travaille ailleurs, et ce qui attend d'être envoyé.</summary>
    public void ShowNetwork() => Dialog = new NetworkViewModel(this);

    public RelayCommand NetworkCommand => networkCommand ??= new RelayCommand(ShowNetwork);
    private RelayCommand? networkCommand;

    public void OpenBackstage() => BackstageOpen = true;

    public void CloseBackstage() => BackstageOpen = false;

    /// <summary>Message discret, effacé après quelques secondes.</summary>
    public void Notify(string text, SoundKind sound = SoundKind.Notification)
    {
        Sounds.Play(Prefs, sound);
        // Le même message relancé se réaffiche et repart pour quatre secondes : un second clic ne paraît pas ignoré.
        var token = ++noticeToken;
        if (Notice == text) Notice = null;
        Notice = text;
        DispatcherTimer.RunOnce(() => { if (noticeToken == token) Notice = null; }, TimeSpan.FromSeconds(4));
    }

    private int noticeToken;

    public void SetStatus(string text) => Status = text;

    public void SelectTab(RibbonTabVm tab)
    {
        if (tab.Id == "fichier")
        {
            OpenBackstage();
            return;
        }
        BackstageOpen = false;
        SelectedTab = tab;
    }

    /// <summary>Télécharge un document servi par l'API, l'enregistre et l'ouvre.</summary>
    public async Task Download(string apiPath, string fileName)
    {
        var data = await api.DownloadAsync(apiPath);
        var path = Files.Save(data, fileName);
        Notify($"Document enregistré : {path}");
        Files.Open(path);
    }

    /// <summary>Reconstruit le ruban selon la session, la page et les préférences.</summary>
    public void RebuildRibbon(bool rebuildNavigation = true)
    {
        var keep = SelectedTab?.Id ?? "accueil";
        Tabs.Clear();
        foreach (var tab in RibbonCatalog.Build(this)) Tabs.Add(tab);
        SelectedTab = Tabs.FirstOrDefault(t => t.Id == keep) ?? Tabs.FirstOrDefault(t => t.Id == "accueil");
        Raise(nameof(SelectedGroups));
        RebuildShortcuts();
        // La barre latérale suit le rôle et le thème (ses couleurs sont lues au moment de la construction).
        if (rebuildNavigation)
        {
            var expandedTitle = Navigation.FirstOrDefault(g => g.HasTitle && g.IsExpanded)?.Title;
            Navigation = IsLoggedIn ? Sidebar.Build(this) : [];
            if (expandedTitle is not null)
            {
                var active = Navigation.FirstOrDefault(g => g.Title == expandedTitle);
                if (active is not null) active.IsExpanded = true;
            }
            NavigationBlocks = Sidebar.Blocks(Navigation);
            Raise(nameof(Navigation));
            Raise(nameof(NavigationBlocks));
            RebuildPhoneTabs();
        }
        else RebuildPhoneTools();
        MarkNavigation();
    }

    /// <summary>Navigation principale, à gauche : un écran par ligne, rangés par métier.</summary>
    public IReadOnlyList<NavGroupVm> Navigation { get; private set; } = [];
    public IReadOnlyList<NavBlockVm> NavigationBlocks { get; private set; } = [];

    /// <summary>Un seul accordéon ouvert, qu'il appartienne au menu ou au ruban mobile.</summary>
    public void CollapseGroups()
    {
        foreach (var group in Navigation)
            if (group.HasTitle) group.IsExpanded = false;
        foreach (var group in PhoneTools)
            group.IsExpanded = false;
    }

    public void ToggleNavGroup(NavGroupVm selected)
    {
        var wasOpen = selected.IsExpanded;
        CollapseGroups();
        if (!wasOpen) selected.IsExpanded = true;
    }

    public void TogglePhoneToolGroup(PhoneToolGroupVm selected)
    {
        var wasOpen = selected.IsExpanded;
        CollapseGroups();
        if (!wasOpen) selected.IsExpanded = true;
    }

    /// <summary>Surligne l'entrée de la barre latérale qui correspond à l'écran affiché.</summary>
    private void MarkNavigation()
    {
        // Une fiche de détail appartient à sa liste : la fiche apprenant éclaire « Apprenants », le reçu « Journal des reçus ».
        var type = Current switch
        {
            StudentDetailPage => typeof(StudentsPage),
            ReceiptDetailPage => typeof(ReceiptsPage),
            _ => Current?.GetType(),
        };
        NavItemVm? match = null;
        foreach (var group in Navigation)
            foreach (var item in group.Items)
            {
                item.IsSelected = item.PageType == type;
                if (item.IsSelected) match = item;
            }
        // L'icône de la page reprend celle de son outil dans le volet latéral, avec la même couleur.
        CurrentIcon = match?.Icon ?? Current?.Glyph ?? Material.Icons.MaterialIconKind.FileDocument;
        CurrentIconBrush = match?.Brush ?? ThemeBrush.Get("ToneNeutral");
        Raise(nameof(CurrentIcon));
        Raise(nameof(CurrentIconBrush));
        Raise(nameof(PhoneMenuSelected));
    }

    /// <summary>Icône de couleur affichée devant le titre de la page, comme dans les Réglages de macOS.</summary>
    public Material.Icons.MaterialIconKind CurrentIcon { get; private set; } = Material.Icons.MaterialIconKind.Home;
    public Avalonia.Media.IBrush CurrentIconBrush { get; private set; } = Avalonia.Media.Brushes.Gray;

    private bool narrow;
    private bool narrowSidebarOpen;

    /// <summary>
    /// Fenêtre étroite : le volet se replie seul pour laisser la place à la page, puis revient quand la fenêtre s'élargit.
    /// Il s'ouvre au besoin avec le bouton du volet et se referme dès qu'on choisit une page.
    /// </summary>
    public bool Narrow
    {
        get => narrow;
        set
        {
            if (narrow == value) return;
            narrow = value;
            narrowSidebarOpen = false;
            Raise();
            Raise(nameof(SidebarShown));
        }
    }

    private bool phone;

    /// <summary>Écran de téléphone : barre d'état masquée, fenêtres à la largeur de l'écran, blocs rangés l'un sous l'autre.</summary>
    public bool Phone
    {
        get => phone;
        set
        {
            if (!Set(ref phone, value)) return;
            Raise(nameof(ShowStatusBar));
            Raise(nameof(PageScroll));
            Raise(nameof(DesktopPage));
            Raise(nameof(PhonePage));
            Current?.PhoneChanged();
        }
    }

    /// <summary>Page montrée dans la fenêtre de l'ordinateur : aucune sur le téléphone, qui a son propre cadre.</summary>
    public PageViewModel? DesktopPage => phone ? null : current;

    /// <summary>Page montrée dans le cadre du téléphone : chaque vue y est construite dans sa forme de téléphone.</summary>
    public PageViewModel? PhonePage => phone ? current : null;

    /// <summary>Onglets du bas de l'écran du téléphone : l'accueil et les outils du quotidien permis par le rôle.</summary>
    public IReadOnlyList<NavItemVm> PhoneTabs { get; private set; } = [];

    private static readonly Type[] PhoneTabOrder =
        [typeof(HomePage), typeof(StudentsPage), typeof(CashierPage), typeof(ReceiptsPage), typeof(MonthlyPage), typeof(DebtorsPage), typeof(CashPage)];

    private void RebuildPhoneTabs()
    {
        var items = Navigation.SelectMany(g => g.Items).ToList();
        PhoneTabs = PhoneTabOrder.Select(t => items.FirstOrDefault(i => i.PageType == t)).OfType<NavItemVm>().Take(phoneWidth >= 600 ? 6 : 4).ToList();
        Raise(nameof(PhoneTabs));
        RebuildPhoneTools();
    }

    private double phoneWidth;

    /// <summary>
    /// Largeur de l'écran mobile, en points. Une tablette en largeur a un volet de navigation à gauche, comme l'iPad ;
    /// un téléphone, ou une tablette en hauteur, garde les onglets du bas, plus nombreux sur une tablette.
    /// </summary>
    public double PhoneWidth
    {
        get => phoneWidth;
        set
        {
            if (Math.Abs(phoneWidth - value) < 0.5) return;
            var tabs = phoneWidth >= 600;
            var rail = PhoneRail;
            phoneWidth = value;
            if (tabs != phoneWidth >= 600) RebuildPhoneTabs();
            if (rail != PhoneRail)
            {
                Raise(nameof(PhoneRail));
                Raise(nameof(PhoneBottomTabs));
            }
        }
    }

    /// <summary>Tablette en largeur : la navigation passe dans un volet à gauche, toujours visible.</summary>
    public bool PhoneRail => phoneWidth >= 900;
    public bool PhoneBottomTabs => !PhoneRail;

    /// <summary>
    /// Commandes du téléphone qui ne sont pas des pages : créations, exports, sauvegardes et contrôles, rangées par métier.
    /// Ce qui ne sert qu'à l'ordinateur en est exclu : fenêtre, zoom, ruban, info-bulles et quadrillage.
    /// </summary>
    public IReadOnlyList<PhoneToolGroupVm> PhoneTools { get; private set; } = [];
    public IReadOnlyList<PhoneToolBlockVm> PhoneToolBlocks { get; private set; } = [];

    private static readonly HashSet<string> DesktopOnly = new(StringComparer.Ordinal)
    {
        "Rechercher", "Actualiser", "Imprimer", "Plein écran", "Barre d'état", "Quadrillage", "Info-bulles", "Barre latérale",
        "Réduire le ruban", "Zoom arrière", "Zoom avant", "Zoom 100 %", "Densité compacte", "Animations",
    };

    private void RebuildPhoneTools()
    {
        var pages = Navigation.SelectMany(g => g.Items).Select(i => i.Label).ToHashSet(StringComparer.Ordinal);
        var expandedTitle = PhoneTools.FirstOrDefault(g => g.IsExpanded)?.Title;
        var groups = new List<PhoneToolGroupVm>();
        foreach (var tab in Tabs.Where(t => t.Id is not ("fichier" or "accueil")))
        {
            var commands = tab.Groups.SelectMany(g => g.Commands)
                .Where(c => !DesktopOnly.Contains(c.Label) && !pages.Contains(c.Label))
                .Select(c => c.Label == "Raccourcis clavier" ? new RibbonCommandVm("Aide et gestes", Material.Icons.MaterialIconKind.GestureTap, string.Empty, c.Command, false, "ToneBlue") : c)
                .ToList();
            if (commands.Count > 0)
            {
                var group = new PhoneToolGroupVm(tab.Title, commands, TogglePhoneToolGroup)
                {
                    IsExpanded = expandedTitle == tab.Title &&
                        !Navigation.Any(g => g.HasTitle && g.IsExpanded)
                };
                groups.Add(group);
            }
        }
        PhoneTools = groups;
        PhoneToolBlocks = PhoneToolBlockVm.Organize(groups);
        Raise(nameof(PhoneTools));
        Raise(nameof(PhoneToolBlocks));
    }

    private bool phoneMenuOpen;

    /// <summary>Menu du téléphone : tous les outils, rangés par métier, en plein écran.</summary>
    public bool PhoneMenuOpen
    {
        get => phoneMenuOpen;
        set
        {
            if (!Set(ref phoneMenuOpen, value)) return;
            if (!value) CollapseGroups();
            Raise(nameof(PhoneMenuSelected));
        }
    }

    /// <summary>L'onglet « Menu » est éclairé quand le menu est ouvert, ou quand la page n'a pas d'onglet à elle.</summary>
    public bool PhoneMenuSelected => phoneMenuOpen || (current is not null && !PhoneTabs.Any(t => t.IsSelected));

    public void TogglePhoneMenu()
    {
        Sheet = null;
        PhoneMenuOpen = !PhoneMenuOpen;
    }

    private SheetVm? sheet;

    /// <summary>Feuille d'actions ouverte en bas de l'écran du téléphone, ou null.</summary>
    public SheetVm? Sheet { get => sheet; private set { if (Set(ref sheet, value)) Raise(nameof(HasSheet)); } }
    public bool HasSheet => sheet is not null;

    public void CloseSheet() => Sheet = null;

    /// <summary>Ouvre une feuille d'actions ; chaque action referme la feuille avant de s'exécuter.</summary>
    public void ShowSheet(string title, string subtitle, IEnumerable<(string Label, Material.Icons.MaterialIconKind Icon, System.Windows.Input.ICommand Command, bool Danger, bool Emphasis)> items)
    {
        var list = items.Select(i => new SheetItemVm(i.Label, i.Icon, new RelayCommand(() =>
        {
            Sheet = null;
            if (i.Command.CanExecute(null)) i.Command.Execute(null);
        }), i.Danger, i.Emphasis)).ToList();
        if (list.Count == 0) return;
        Sheet = new SheetVm(title, subtitle, list);
    }

    /// <summary>
    /// Feuille de choix : une ligne par valeur, la valeur en cours cochée. Toucher une ligne l'applique tout de suite
    /// et referme la feuille.
    /// </summary>
    public void ShowChoiceSheet(string title, string subtitle, IEnumerable<SheetChoice> choices)
    {
        var list = choices.Select(c => new SheetItemVm(c.Label, c.Icon, new RelayCommand(() =>
        {
            Sheet = null;
            c.Choose();
        }), isChecked: c.IsChecked, detail: c.Detail, brush: c.Brush)).ToList();
        if (list.Count > 0) Sheet = new SheetVm(title, subtitle, list);
    }

    /// <summary>Thème du téléphone : système, clair ou sombre.</summary>
    public void ShowThemeSheet()
    {
        PhoneMenuOpen = false;
        Material.Icons.MaterialIconKind IconOf(string key) => key switch
        {
            "light" => Material.Icons.MaterialIconKind.WeatherSunny,
            "dark" => Material.Icons.MaterialIconKind.WeatherNight,
            _ => Material.Icons.MaterialIconKind.ThemeLightDark,
        };
        string? DetailOf(string key) => key switch
        {
            "system" => "Suit le réglage clair ou sombre de l'appareil.",
            "light" => "Fond blanc, idéal en journée.",
            _ => "Fond sombre, reposant le soir et plus économe.",
        };
        ShowChoiceSheet("Thème", "Choisissez l'apparence de SCOLIA sur cet appareil.",
            Preferences.Themes.Select(t => new SheetChoice(t.Label, IconOf(t.Value), Prefs.Theme == t.Value, () => Prefs.Theme = t.Value, DetailOf(t.Value))));
    }

    /// <summary>Couleur d'accent : boutons, sélection et liens.</summary>
    public void ShowAccentSheet()
    {
        PhoneMenuOpen = false;
        ShowChoiceSheet("Couleur d'accent", "Couleur des boutons, de la sélection et des liens.",
            Preferences.AccentSwatches.Select(a => new SheetChoice(a.Label, Material.Icons.MaterialIconKind.Circle, Prefs.Accent == a.Key,
                () => Prefs.Accent = a.Key, Brush: new Avalonia.Media.SolidColorBrush(a.Color))));
    }

    /// <summary>Police et taille du texte, avec un aperçu qui change en direct.</summary>
    public void ShowTextSettings()
    {
        PhoneMenuOpen = false;
        Sheet = null;
        Dialog = new TextSettingsViewModel(this);
    }

    /// <summary>Feuille des actions de la page affichée, depuis le bouton « … » de la barre du téléphone.</summary>
    public void ShowPageSheet()
    {
        if (current is not { } page) return;
        // Les actions d'un élément sont sur le « … » de sa carte : la feuille de la page ne garde que les siennes.
        ShowSheet(page.Heading, page.Subtitle, page.HeaderActions.Where(a => !a.Row).Select(a => (a.Label, a.Icon, a.Command, a.Danger, a.IsPrimary)));
    }

    /// <summary>Défilement de la page entière : sur téléphone seulement.</summary>
    public Avalonia.Controls.Primitives.ScrollBarVisibility PageScroll =>
        phone ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;

    /// <summary>La barre d'état suit la préférence, sauf sur un téléphone où la place manque.</summary>
    public bool ShowStatusBar => Prefs.StatusBar && !phone;

    /// <summary>Volet affiché : le choix enregistré en fenêtre large, une ouverture passagère en fenêtre étroite.</summary>
    public bool SidebarShown => narrow ? narrowSidebarOpen : Prefs.Sidebar;

    public void ToggleSidebar()
    {
        if (narrow)
        {
            narrowSidebarOpen = !narrowSidebarOpen;
            Raise(nameof(SidebarShown));
        }
        else Prefs.Sidebar = !Prefs.Sidebar;
    }

    /// <summary>Affiche le volet, par exemple depuis « Afficher tous les outils » de l'accueil.</summary>
    public void ShowSidebar()
    {
        // Sur le mobile, tous les outils sont dans le menu plein écran.
        if (phone) PhoneMenuOpen = true;
        else if (!SidebarShown) ToggleSidebar();
    }

    /// <summary>Commandes ayant un raccourci, dans l'ordre affiché par la boîte des raccourcis.</summary>
    public IReadOnlyList<RibbonCommandVm> ShortcutCommands { get; private set; } = [];

    private readonly Dictionary<KeyGesture, RibbonCommandVm> shortcuts = [];

    /// <summary>Un raccourci suit les commandes visibles : l'onglet contextuel, plus récent, passe avant le ruban général.</summary>
    private void RebuildShortcuts()
    {
        shortcuts.Clear();
        foreach (var command in Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands))
            if (command.Gesture is { } gesture) shortcuts[gesture] = command;
        ShortcutCommands = shortcuts.Values.OrderBy(c => c.ShortcutText, StringComparer.Ordinal).ToList();
    }

    /// <summary>Lance la commande liée à ce raccourci, si elle est disponible. Une boîte de dialogue ouverte bloque les raccourcis.</summary>
    public bool RunShortcut(Key key, KeyModifiers modifiers)
    {
        if (!IsLoggedIn || HasDialog || BackstageOpen) return false;
        if (!shortcuts.TryGetValue(new KeyGesture(key, modifiers), out var command) || !command.Command.CanExecute(null)) return false;
        command.Command.Execute(null);
        return true;
    }

    public async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); }
        catch (HttpRequestException) { }
        Sync.Stop();
        Sync.Forget();
        session = null;
        Tabs.Clear();
        ClearHistory();
        Navigation = [];
        NavigationBlocks = [];
        Raise(nameof(Navigation));
        Raise(nameof(NavigationBlocks));
        RebuildPhoneTabs();
        PhoneMenuOpen = false;
        Sheet = null;
        SelectedTab = null;
        Current = null;
        BackstageOpen = false;
        RaiseLogin();
        Raise(nameof(SchoolLine));
        Raise(nameof(UserLine));
        Raise(nameof(UserInitials));
        Status = "Session fermée.";
    }

    private void Remember(string label, Func<PageViewModel> create)
    {
        recents.RemoveAll(r => r.Label == label);
        recents.Insert(0, (label, create));
        if (recents.Count > 6) recents.RemoveRange(6, recents.Count - 6);
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
        };
    }
}
