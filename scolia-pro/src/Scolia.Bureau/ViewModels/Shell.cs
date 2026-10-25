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
    private ServerProcess? localServer;
    private bool choosingServer;

    public ShellViewModel(ScoliaClient api, Preferences prefs)
    {
        this.api = api;
        Prefs = prefs;
        Login = new LoginViewModel(api);
        Setup = new SetupViewModel(this);
        Connection = new ConnectionViewModel(this);
        Login.LoggedIn += s => _ = OpenSessionAsync(s);
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
            prefs.Apply();
            prefs.Save();
            if (IsLoggedIn) RebuildRibbon();
        };
        prefs.Apply();
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

    /// <summary>Recharge le logo, au démarrage et après un changement dans les paramètres.</summary>
    public async Task RefreshLogoAsync()
    {
        try
        {
            var bytes = await api.DownloadAsync("api/etablissement/logo");
            using var stream = new MemoryStream(bytes);
            SchoolLogo = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            SchoolLogo = null;
        }
    }
    public bool IsLoggedIn => session is not null;

    private bool setupRequired;

    /// <summary>Premier lancement : aucune configuration sur ce serveur, l'assistant remplace la connexion.</summary>
    public bool IsSetupRequired { get => setupRequired; set { if (Set(ref setupRequired, value)) RaiseLogin(); } }

    /// <summary>La connexion s'affiche seulement quand une configuration existe et qu'aucune session n'est ouverte.</summary>
    public bool ShowLogin => !IsLoggedIn && !IsSetupRequired && !ChoosingServer;

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
    }

    /// <summary>L'assistant a créé la configuration et ouvert la première session.</summary>
    public Task CompleteSetupAsync(SessionDto session)
    {
        IsSetupRequired = false;
        return OpenSessionAsync(session);
    }
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
            if (!Set(ref current, value)) return;
            Raise(nameof(HasPage));
            MarkNavigation();
        }
    }
    public bool HasPage => current is not null;

    /// <summary>Boîte de dialogue ouverte : formulaire, recherche. Null quand aucune n'est affichée.</summary>
    public object? Dialog { get => dialog; private set { if (Set(ref dialog, value)) { Raise(nameof(HasDialog)); Raise(nameof(Working)); } } }
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

    public bool Can(string permission) => session?.Permissions.Contains(permission) == true;

    /// <summary>Plein écran : la fenêtre gère l'affichage réel, le shell garde l'état pour le ruban.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>Fourni par la fenêtre, qui applique l'état d'affichage.</summary>
    public Action<bool>? FullscreenHandler { get; set; }

    public void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
        Raise(nameof(IsFullscreen));
        FullscreenHandler?.Invoke(IsFullscreen);
        RebuildRibbon();
    }

    public IReadOnlyList<(string Label, Func<PageViewModel> Open)> Recents => recents;

    /// <summary>
    /// Démarrage : trouve le serveur de l'établissement sans adresse à saisir, comme la version 79.
    /// Un poste qui porte la base en est le serveur. Un poste sans base cherche SCOLIA sur le réseau et le rejoint.
    /// Un serveur mémorisé dont l'adresse a changé est retrouvé par l'identifiant de son établissement.
    /// </summary>
    public async Task StartAsync(ServerProcess server)
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
            ShowChooser(servers, $"Le serveur de {saved.School} (ordinateur {saved.Machine}) ne répond pas. Allumez-le ou ouvrez SCOLIA dessus, puis recherchez à nouveau.");
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
        api.Retarget(server.Address);
        ChoosingServer = false;
        await ShowEntryAsync();
        Notify($"Ce poste est relié à SCOLIA · {server.School} (ordinateur {server.Machine}).");
    }

    /// <summary>Cet ordinateur devient (ou redevient) le serveur de l'établissement.</summary>
    public async Task UseThisComputerAsync()
    {
        var previous = ConnectionStore.Load();
        ConnectionStore.Save(new ConnectionSettings(ConnectionSettings.Local, LanPromptDone: previous?.LanPromptDone ?? false));
        if (IsLoggedIn) await LogoutAsync();
        api.Retarget(ConnectionStore.LocalAddress);
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
            Status = "Démarrage du serveur SCOLIA…";
            localServer.Start();
            for (var i = 0; i < 80 && !await api.IsRunningAsync(); i++) await Task.Delay(250);
        }
        var ready = await api.IsRunningAsync();
        Status = ready ? $"Serveur prêt : {api.Server}" : "Le serveur SCOLIA ne répond pas.";
        if (!ready)
        {
            if (!IsServerComputer)
                ShowChooser([], "Le serveur de l'établissement ne répond pas. Vérifiez que l'ordinateur principal est allumé, puis recherchez à nouveau.");
            return;
        }
        // Configuration reconnue : la connexion s'affiche directement. Sinon, l'assistant de première installation.
        try
        {
            var status = await api.GetAsync<SetupStatusDto>("api/installation");
            Login.SchoolName = status.SchoolName ?? string.Empty;
            await RefreshLogoAsync();
            IsSetupRequired = !status.IsConfigured;
            if (IsSetupRequired) await Setup.LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            Status = "Impossible de vérifier la configuration de SCOLIA.";
        }
    }

    /// <summary>Ouvre la session : construit le ruban et affiche le tableau de bord.</summary>
    public async Task OpenSessionAsync(SessionDto s)
    {
        session = s;
        knownRevision = -1;
        Raise(nameof(SchoolTitle));
        Raise(nameof(SchoolYearLine));
        if (SchoolLogo is null) await RefreshLogoAsync();
        RaiseLogin();
        Raise(nameof(SchoolLine));
        Raise(nameof(UserLine));
        Raise(nameof(UserInitials));
        Raise(nameof(AccountName));
        Status = $"Connecté : {s.DisplayName} · {s.SchoolName}";
        Login.Reset();
        RebuildRibbon();
        current = null;
        ClearHistory();
        await Open(() => new HomePage(this));
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
        Current = page;
        RebuildRibbon();
        Raise(nameof(CanGoBack));
        Raise(nameof(CanGoForward));
        // Une fois par session, sur la première liste qui en propose : où trouver les actions d'une ligne.
        if (!rowTipShown && Prefs.Tips && page.RowActions.Count > 0)
        {
            rowTipShown = true;
            Notify("Astuce : clic droit sur une ligne pour ses actions, double-clic pour l'ouvrir.");
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
    public Task Reload() => Current?.RefreshAsync() ?? Task.CompletedTask;

    /// <summary>Exécute une action métier : les erreurs deviennent une notification, jamais un plantage.</summary>
    private int running;
    private bool workingDue;

    /// <summary>
    /// Traitement en cours depuis plus d'un instant : un indicateur s'affiche au centre de la fenêtre, sauf pendant
    /// qu'une boîte de dialogue attend une saisie. Les actions brèves n'affichent rien, pour ne pas faire clignoter l'écran.
    /// </summary>
    public bool Working => workingDue && running > 0 && !HasDialog;

    /// <summary>Délai avant l'affichage de l'indicateur de traitement.</summary>
    private static readonly TimeSpan WorkingDelay = TimeSpan.FromMilliseconds(350);

    public async Task Run(Func<Task> work)
    {
        running++;
        _ = ShowWorkingLaterAsync();
        try { await work(); }
        catch (ApiException ex) { Notify(ex.Message); }
        catch (HttpRequestException) { Notify("Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notify($"Erreur inattendue : {ex.Message}"); }
        finally
        {
            running--;
            if (running == 0)
            {
                workingDue = false;
                Raise(nameof(Working));
            }
            // Nos propres écritures ne doivent pas déclencher un rechargement : on mémorise la révision après l'action.
            if (IsLoggedIn && await ReadRevisionAsync() is { } latest) knownRevision = latest;
        }
    }

    private async Task ShowWorkingLaterAsync()
    {
        await Task.Delay(WorkingDelay);
        if (running == 0) return;
        workingDue = true;
        Raise(nameof(Working));
    }

    /// <summary>
    /// Vérifie si une autre station a modifié les données. L'écran courant se recharge seul, sauf quand un
    /// formulaire ou l'encaissement est ouvert : on prévient alors l'utilisateur, pour ne pas effacer sa saisie.
    /// </summary>
    private async Task CheckRevisionAsync()
    {
        if (!IsLoggedIn) return;
        if (await ReadRevisionAsync() is not { } latest) return;
        if (knownRevision < 0 || latest == knownRevision)
        {
            knownRevision = latest;
            return;
        }
        knownRevision = latest;
        if (HasDialog || BackstageOpen || Current is CashierPage) Notify("Des données ont changé sur un autre poste. Appuyez sur F5 pour actualiser.");
        else await Run(Reload);
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
        Ask(new FormViewModel(title, string.Empty, _ => Task.FromResult(true)) { Message = message, SubmitText = okText, Danger = danger });

    public void CloseDialog() => Dialog = null;

    /// <summary>Affiche la recherche globale, Ctrl+K.</summary>
    public void ShowSearch() => Dialog = new SearchViewModel(this);

    public void OpenBackstage() => BackstageOpen = true;

    public void CloseBackstage() => BackstageOpen = false;

    /// <summary>Message discret, effacé après quelques secondes.</summary>
    public void Notify(string text)
    {
        Notice = text;
        DispatcherTimer.RunOnce(() => { if (Notice == text) Notice = null; }, TimeSpan.FromSeconds(4));
    }

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
    public void RebuildRibbon()
    {
        var keep = SelectedTab?.Id ?? "accueil";
        Tabs.Clear();
        foreach (var tab in RibbonCatalog.Build(this)) Tabs.Add(tab);
        SelectedTab = Tabs.FirstOrDefault(t => t.Id == keep) ?? Tabs.FirstOrDefault(t => t.Id == "accueil");
        Raise(nameof(SelectedGroups));
        RebuildShortcuts();
        // La barre latérale suit le rôle et le thème (ses couleurs sont lues au moment de la construction).
        Navigation = IsLoggedIn ? Sidebar.Build(this) : [];
        Raise(nameof(Navigation));
        MarkNavigation();
    }

    /// <summary>Navigation principale, à gauche : un écran par ligne, rangés par métier.</summary>
    public IReadOnlyList<NavGroupVm> Navigation { get; private set; } = [];

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
        if (!SidebarShown) ToggleSidebar();
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
        session = null;
        Tabs.Clear();
        ClearHistory();
        Navigation = [];
        Raise(nameof(Navigation));
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
