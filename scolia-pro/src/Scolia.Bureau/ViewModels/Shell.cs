using System.Collections.ObjectModel;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Ribbon;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Fenêtre principale : ruban, écran courant, boîtes de dialogue, notifications et barre d'état.</summary>
public sealed class ShellViewModel : Observable
{
    private readonly ScoliaClient api;
    private readonly List<(string Label, Func<PageViewModel> Open)> recents = [];
    private SessionDto? session;
    private PageViewModel? current;
    private object? dialog;
    private bool backstage;
    private string status = "Démarrage de SCOLIA…";
    private string? notice;
    private RibbonTabVm? selectedTab;

    public ShellViewModel(ScoliaClient api, Preferences prefs)
    {
        this.api = api;
        Prefs = prefs;
        Login = new LoginViewModel(api);
        Login.LoggedIn += s => _ = OpenSessionAsync(s);
        Backstage = new BackstageViewModel(this);
        ReloadCommand = new AsyncCommand(() => Run(Reload));
        SearchCommand = new RelayCommand(ShowSearch);
        prefs.PropertyChanged += (_, _) =>
        {
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
    public SessionDto? Session => session;
    public bool IsLoggedIn => session is not null;
    public string SchoolLine => session is null ? string.Empty : $"{session.SchoolName}{(session.CurrentYear is null ? string.Empty : $" · année {session.CurrentYear}")}";
    public string UserLine => session is null ? string.Empty : $"{session.DisplayName} · {session.RoleLabel}";
    public string UserInitials => session is null ? string.Empty : Initials(session.DisplayName);
    public string ServerLine => $"Serveur {api.Server.Host}:{api.Server.Port}";

    public PageViewModel? Current { get => current; private set { if (Set(ref current, value)) Raise(nameof(HasPage)); } }
    public bool HasPage => current is not null;

    /// <summary>Boîte de dialogue ouverte : formulaire, recherche. Null quand aucune n'est affichée.</summary>
    public object? Dialog { get => dialog; private set { if (Set(ref dialog, value)) Raise(nameof(HasDialog)); } }
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

    /// <summary>Vérifie que le serveur répond. S'il est arrêté, lance SCOLIA.exe du même dossier.</summary>
    public async Task StartAsync(ServerProcess server)
    {
        if (!await api.IsRunningAsync())
        {
            Status = "Démarrage du serveur SCOLIA…";
            server.Start();
            for (var i = 0; i < 80 && !await api.IsRunningAsync(); i++) await Task.Delay(250);
        }
        Status = await api.IsRunningAsync() ? $"Serveur prêt : {api.Server}" : "Le serveur SCOLIA ne répond pas.";
    }

    /// <summary>Ouvre la session : construit le ruban et affiche le tableau de bord.</summary>
    public async Task OpenSessionAsync(SessionDto s)
    {
        session = s;
        Raise(nameof(IsLoggedIn));
        Raise(nameof(SchoolLine));
        Raise(nameof(UserLine));
        Raise(nameof(UserInitials));
        Status = $"Connecté : {s.DisplayName} · {s.SchoolName}";
        Login.Reset();
        RebuildRibbon();
        await Open(() => new DashboardPage(this));
    }

    /// <summary>Affiche un écran, le mémorise dans les récents et charge ses données.</summary>
    public async Task Open(Func<PageViewModel> create)
    {
        var page = create();
        Remember(page.Title, create);
        BackstageOpen = false;
        Current = page;
        RebuildRibbon();
        await page.RefreshAsync();
    }

    /// <summary>Recharge l'écran courant.</summary>
    public Task Reload() => Current?.RefreshAsync() ?? Task.CompletedTask;

    /// <summary>Exécute une action métier : les erreurs deviennent une notification, jamais un plantage.</summary>
    public async Task Run(Func<Task> work)
    {
        try { await work(); }
        catch (ApiException ex) { Notify(ex.Message); }
        catch (HttpRequestException) { Notify("Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Notify($"Erreur inattendue : {ex.Message}"); }
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
    }

    public async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); }
        catch (HttpRequestException) { }
        session = null;
        Tabs.Clear();
        SelectedTab = null;
        Current = null;
        BackstageOpen = false;
        Raise(nameof(IsLoggedIn));
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
