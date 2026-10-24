using System.Collections.ObjectModel;
using System.Windows.Input;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Commande du ruban : une icône, un libellé, une action.</summary>
public sealed record RibbonCommandVm(string Label, MaterialIconKind Icon, ICommand Command);

/// <summary>Groupe de commandes du ruban.</summary>
public sealed record RibbonGroupVm(string Title, IReadOnlyList<RibbonCommandVm> Commands);

/// <summary>Onglet du ruban.</summary>
public sealed record RibbonTabVm(string Title, IReadOnlyList<RibbonGroupVm> Groups);

/// <summary>Fenêtre principale : ruban, écran courant, barre d'état. Ne contient que des commandes réellement disponibles.</summary>
public sealed class ShellViewModel : Observable
{
    private readonly ScoliaClient api;
    private object? current;
    private SessionDto? session;
    private RibbonTabVm? selectedTab;
    private string status = "Démarrage de SCOLIA…";
    private DashboardViewModel? dashboard;
    private StudentsViewModel? students;
    private ReceiptsViewModel? receipts;

    public ShellViewModel(ScoliaClient api)
    {
        this.api = api;
        Login = new LoginViewModel(api);
        Login.LoggedIn += s => _ = OpenSessionAsync(s);
        current = Login;
    }

    public LoginViewModel Login { get; }
    public object? Current { get => current; private set => Set(ref current, value); }
    public bool IsLoggedIn => session is not null;
    public string Status { get => status; private set => Set(ref status, value); }
    public string SchoolLine => session is null ? string.Empty : $"{session.SchoolName}{(session.CurrentYear is null ? string.Empty : $" · année {session.CurrentYear}")}";
    public string UserLine => session is null ? string.Empty : $"{session.DisplayName} · {session.RoleLabel}";
    public ObservableCollection<RibbonTabVm> Tabs { get; } = [];

    public RibbonTabVm? SelectedTab
    {
        get => selectedTab;
        set { if (Set(ref selectedTab, value)) Raise(nameof(SelectedGroups)); }
    }

    public IReadOnlyList<RibbonGroupVm> SelectedGroups => SelectedTab?.Groups ?? [];

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

    public DashboardViewModel? Dashboard => dashboard;
    public StudentsViewModel? Students => students;
    public ReceiptsViewModel? Receipts => receipts;

    /// <summary>Ouvre la session : crée les écrans, construit le ruban et affiche le tableau de bord.</summary>
    public async Task OpenSessionAsync(SessionDto s)
    {
        session = s;
        var currency = s.CurrencySymbol;
        dashboard = new DashboardViewModel(api, currency);
        students = new StudentsViewModel(api, currency);
        receipts = new ReceiptsViewModel(api, currency);
        BuildRibbon();
        Raise(nameof(IsLoggedIn));
        Raise(nameof(SchoolLine));
        Raise(nameof(UserLine));
        Status = $"Connecté : {s.DisplayName} · serveur {api.Server}";
        SelectedTab = Tabs[0];
        await ShowAsync(dashboard);
    }

    private void BuildRibbon()
    {
        Tabs.Clear();
        Tabs.Add(new RibbonTabVm("Accueil",
        [
            new RibbonGroupVm("Tableau de bord",
            [
                new RibbonCommandVm("Tableau de bord", MaterialIconKind.ViewDashboardOutline, new AsyncCommand(() => ShowAsync(dashboard!))),
                new RibbonCommandVm("Actualiser", MaterialIconKind.Refresh, new AsyncCommand(ReloadAsync)),
            ]),
        ]));
        Tabs.Add(new RibbonTabVm("Secrétariat",
        [
            new RibbonGroupVm("Apprenants",
            [
                new RibbonCommandVm("Apprenants", MaterialIconKind.AccountGroupOutline, new AsyncCommand(() => ShowAsync(students!))),
            ]),
        ]));
        Tabs.Add(new RibbonTabVm("Finances",
        [
            new RibbonGroupVm("Encaissement",
            [
                new RibbonCommandVm("Journal des reçus", MaterialIconKind.ReceiptTextOutline, new AsyncCommand(() => ShowAsync(receipts!))),
            ]),
        ]));
        Tabs.Add(new RibbonTabVm("Compte",
        [
            new RibbonGroupVm("Session",
            [
                new RibbonCommandVm("Se déconnecter", MaterialIconKind.Logout, new AsyncCommand(LogoutAsync)),
            ]),
        ]));
    }

    /// <summary>Affiche un écran et charge ses données.</summary>
    public async Task ShowAsync(object page)
    {
        Current = page;
        switch (page)
        {
            case DashboardViewModel d: await d.LoadAsync(); break;
            case StudentsViewModel s: await s.LoadAsync(); break;
            case ReceiptsViewModel r: await r.LoadAsync(); break;
        }
    }

    private async Task ReloadAsync()
    {
        if (Current is not null) await ShowAsync(Current);
    }

    private async Task LogoutAsync()
    {
        try { await api.LogoutAsync(); }
        catch (HttpRequestException) { }
        session = null;
        Tabs.Clear();
        SelectedTab = null;
        Raise(nameof(IsLoggedIn));
        Raise(nameof(SchoolLine));
        Raise(nameof(UserLine));
        Status = "Session fermée.";
        Current = Login;
    }
}
