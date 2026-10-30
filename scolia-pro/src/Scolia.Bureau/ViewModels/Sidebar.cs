using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Entrée de la barre latérale : un écran principal, son icône et sa couleur.</summary>
public sealed class NavItemVm : Observable
{
    private bool selected;

    public NavItemVm(string label, MaterialIconKind icon, string tone, Type page, Func<PageViewModel> create, ShellViewModel shell)
    {
        Label = label;
        Icon = icon;
        Brush = ThemeBrush.Get(tone);
        PageType = page;
        Open = new AsyncCommand(() => shell.Run(() => shell.Open(create)));
    }

    public string Label { get; }
    /// <summary>Libellé court des onglets du téléphone, où la place manque : « Reçus » pour « Journal des reçus ».</summary>
    public string TabLabel => Label switch
    {
        "Journal des reçus" => "Reçus",
        "Suivi mensuel" => "Suivi",
        "Recouvrement" => "Retards",
        "Encaissement par lot" => "Par lot",
        _ => Label,
    };
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public Type PageType { get; }
    public AsyncCommand Open { get; }
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

/// <summary>Rubrique de la barre latérale, avec son titre discret.</summary>
public sealed class NavGroupVm : Observable
{
    private bool isExpanded;

    public NavGroupVm(string title, IReadOnlyList<NavItemVm> items)
    {
        Title = title;
        Items = items;
        Icon = FeatureVisuals.Icon(title);
        Brush = ThemeBrush.Get(FeatureVisuals.Tone(title));
        // L'accueil est accessible immédiatement ; les autres métiers sont repliés sur mobile.
        isExpanded = title.Length == 0;
        Toggle = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Title { get; }
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public IReadOnlyList<NavItemVm> Items { get; }
    public bool HasTitle => Title.Length > 0;
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }
    public RelayCommand Toggle { get; }
}

/// <summary>
/// Navigation principale, façon Finder ou Mail : chaque écran est à un clic, rangé par métier.
/// Seuls les écrans permis par le rôle de l'utilisateur sont proposés.
/// </summary>
public static class Sidebar
{
    public static IReadOnlyList<NavGroupVm> Build(ShellViewModel shell)
    {
        NavItemVm? Item(string label, MaterialIconKind icon, string tone, string? perm, Type page, Func<PageViewModel> create) =>
            perm is null || shell.Can(perm) ? new NavItemVm(label, icon, tone, page, create, shell) : null;

        NavGroupVm Group(string title, params NavItemVm?[] items) => new(title, items.OfType<NavItemVm>().ToList());

        var groups = new[]
        {
            Group(string.Empty,
                Item("Accueil", MaterialIconKind.Home, "ToneBlue", null, typeof(HomePage), () => new HomePage(shell))),
            Group("Données",
                Item("Tableau de bord", MaterialIconKind.ViewDashboard, "ToneBlue", null, typeof(DashboardPage), () => new DashboardPage(shell)),
                Item("Rapports et exports", MaterialIconKind.FileChart, "ToneIndigo", null, typeof(ReportsPage), () => new ReportsPage(shell)),
                Item("Sauvegardes", MaterialIconKind.DatabaseExport, "ToneGreen", Perm.BackupManage, typeof(DataPage), () => new DataPage(shell)),
                Item("Journal d'audit", MaterialIconKind.ShieldCheck, "ToneIndigo", Perm.AuditRead, typeof(AuditPage), () => new AuditPage(shell)),
                Item("Effacement", MaterialIconKind.DeleteSweep, "ToneRed", Perm.BackupManage, typeof(EraseDataPage), () => new EraseDataPage(shell, payments: true))),
            Group("Secrétariat",
                Item("Apprenants", MaterialIconKind.AccountGroup, "ToneBlue", Perm.StudentsRead, typeof(StudentsPage), () => new StudentsPage(shell)),
                Item("Réinscriptions", MaterialIconKind.School, "ToneIndigo", Perm.StudentsWrite, typeof(ReEnrollmentPage), () => new ReEnrollmentPage(shell))),
            Group("Finances",
                Item("Encaisser", MaterialIconKind.HandCoin, "ToneGreen", Perm.PaymentsCreate, typeof(CashierPage), () => new CashierPage(shell)),
                Item("Encaissement par lot", MaterialIconKind.FormatListChecks, "ToneTeal", Perm.PaymentsCreate, typeof(BatchPaymentPage), () => new BatchPaymentPage(shell)),
                Item("Journal des reçus", MaterialIconKind.ReceiptText, "ToneOrange", Perm.PaymentsRead, typeof(ReceiptsPage), () => new ReceiptsPage(shell)),
                Item("Suivi mensuel", MaterialIconKind.CalendarCheck, "ToneIndigo", Perm.ReportsRead, typeof(MonthlyPage), () => new MonthlyPage(shell)),
                Item("Recouvrement", MaterialIconKind.ChartBar, "ToneOrange", Perm.ReportsRead, typeof(DebtorsPage), () => new DebtorsPage(shell)),
                Item("Caisse", MaterialIconKind.CashMultiple, "ToneGreen", Perm.PaymentsRead, typeof(CashPage), () => new CashPage(shell)),
                Item("Dépenses", MaterialIconKind.Wallet, "ToneRed", Perm.PaymentsRead, typeof(ExpensesPage), () => new ExpensesPage(shell)),
                Item("Prises en charge", MaterialIconKind.HandHeart, "TonePink", Perm.PaymentsRead, typeof(SponsorshipsPage), () => new SponsorshipsPage(shell))),
            Group("Scolarité",
                Item("Cours et notes", MaterialIconKind.BookOpenVariant, "TonePurple", Perm.AcademicsRead, typeof(GradesPage), () => new GradesPage(shell)),
                Item("Résultats", MaterialIconKind.Certificate, "ToneIndigo", Perm.AcademicsRead, typeof(ResultsPage), () => new ResultsPage(shell)),
                Item("Présences", MaterialIconKind.ClipboardCheck, "ToneTeal", Perm.AcademicsRead, typeof(AttendancePage), () => new AttendancePage(shell)),
                Item("Discipline", MaterialIconKind.AlertOctagon, "ToneRed", Perm.AcademicsRead, typeof(DisciplinePage), () => new DisciplinePage(shell)),
                Item("Enseignants et cours", MaterialIconKind.AccountSchool, "ToneOrange", Perm.AcademicsRead, typeof(TeachingPage), () => new TeachingPage(shell)),
                Item("Cartes d'accès", MaterialIconKind.CardAccountDetails, "ToneBlue", Perm.AcademicsRead, typeof(CardsPage), () => new CardsPage(shell))),
            Group("Administration",
                Item("Personnel et paie", MaterialIconKind.Briefcase, "ToneOrange", Perm.StaffManage, typeof(StaffPage), () => new StaffPage(shell)),
                Item("Pointage", MaterialIconKind.ClockCheck, "ToneTeal", Perm.StaffManage, typeof(StaffAttendancePage), () => new StaffAttendancePage(shell))),
            Group("Paramètres",
                Item("Paramètres", MaterialIconKind.Cog, "TonePurple", null, typeof(SettingsPage), () => new SettingsPage(shell)),
                Item("Utilisateurs", MaterialIconKind.AccountCog, "ToneBlue", Perm.UsersManage, typeof(UsersPage), () => new UsersPage(shell))),
        };
        return groups.Where(g => g.Items.Count > 0).ToList();
    }
}
