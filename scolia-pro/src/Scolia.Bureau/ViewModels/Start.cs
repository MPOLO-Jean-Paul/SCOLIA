using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
namespace Scolia.Bureau.ViewModels;
/// <summary>Point d'entrée léger, sans requête financière avant le choix d'Accueil.</summary>
public sealed class StartPage : PageViewModel
{
    public StartPage(ShellViewModel shell) : base(shell)
    {
        var actions = new List<ToolCardVm>();
        void Add(string title, string description, MaterialIconKind icon, string tone, Func<Task> run)
            => actions.Add(new ToolCardVm(title, description, icon, ThemeBrush.Get(tone), new AsyncCommand(() => Shell.Run(run))));
        if (Shell.Can(Perm.StudentsWrite))
            Add("Inscrire", "Créer un nouveau dossier.", MaterialIconKind.AccountPlus, "ToneBlue", () => StudentForms.CreateAsync(Shell));
        if (Shell.Can(Perm.PaymentsCreate))
            Add("Encaisser", "Enregistrer un versement et son reçu.", MaterialIconKind.HandCoin, "ToneGreen", () => Shell.Open(() => new CashierPage(Shell)));
        NewActions = actions;
        OpenHome = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new HomePage(Shell))));
    }
    public override string Title => "Démarrer";
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboard;
    public string School => Shell.SchoolHeaderTitle;
    public string Year => Shell.SchoolYearLine;
    public string Greeting => $"Bienvenue, {Shell.AccountName}";
    public IReadOnlyList<ToolCardVm> NewActions { get; }
    public bool HasNewActions => NewActions.Count > 0;
    public AsyncCommand OpenHome { get; }

    // Les cartes ouvrent les écrans existants, selon les droits du compte. Les commandes
    // de création Inscrire et Encaisser restent exclusivement dans le volet Windows.
    private IReadOnlyList<NavItemVm> AllowedTools => Shell.Navigation.SelectMany(g => g.Items)
        .Where(i => i.PageType != typeof(StartPage) && i.PageType != typeof(HomePage) &&
                    i.PageType != typeof(CashierPage)).ToList();

    public IReadOnlyList<NavItemVm> Recommended
    {
        get
        {
            Type[] order = [typeof(StudentsPage), typeof(ReceiptsPage), typeof(MonthlyPage),
                typeof(ReportsPage), typeof(DashboardPage), typeof(CardsPage), typeof(AttendancePage), typeof(DebtorsPage)];
            var allowed = AllowedTools;
            return order.Select(t => allowed.FirstOrDefault(i => i.PageType == t)).OfType<NavItemVm>().ToList();
        }
    }
    public bool HasRecommended => Recommended.Count > 0;

    // Historique réel de la session, limité aux écrans de navigation encore autorisés.
    // Aucune fiche individuelle ni donnée de démonstration dans la page de lancement.
    public IReadOnlyList<NavItemVm> RecentTools
    {
        get
        {
            var allowed = AllowedTools;
            return Shell.Recents.Select(r => allowed.FirstOrDefault(i =>
                string.Equals(i.Label, r.Label, StringComparison.Ordinal)))
                .OfType<NavItemVm>().DistinctBy(i => i.PageType).ToList();
        }
    }
    public bool HasRecentTools => RecentTools.Count > 0;
    public override void Entered() => RefreshLaunchLists();
    public override Task LoadAsync()
    {
        RefreshLaunchLists();
        return Task.CompletedTask;
    }
    private void RefreshLaunchLists()
    {
        Raise(nameof(Recommended));
        Raise(nameof(HasRecommended));
        Raise(nameof(RecentTools));
        Raise(nameof(HasRecentTools));
        Raise(nameof(Greeting));
        Raise(nameof(School));
        Raise(nameof(Year));
    }
}
