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
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public Type PageType { get; }
    public AsyncCommand Open { get; }
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

/// <summary>Rubrique de la barre latérale, avec son titre discret.</summary>
public sealed record NavGroupVm(string Title, IReadOnlyList<NavItemVm> Items)
{
    public bool HasTitle => Title.Length > 0;
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
                Item("Tableau de bord", MaterialIconKind.ViewDashboardOutline, "ToneBlue", null, typeof(DashboardPage), () => new DashboardPage(shell))),
            Group("SECRÉTARIAT",
                Item("Apprenants", MaterialIconKind.AccountGroupOutline, "ToneBlue", Perm.StudentsRead, typeof(StudentsPage), () => new StudentsPage(shell)),
                Item("Réinscriptions", MaterialIconKind.SchoolOutline, "ToneIndigo", Perm.StudentsWrite, typeof(ReEnrollmentPage), () => new ReEnrollmentPage(shell))),
            Group("FINANCES",
                Item("Encaisser", MaterialIconKind.HandCoin, "ToneGreen", Perm.PaymentsCreate, typeof(CashierPage), () => new CashierPage(shell)),
                Item("Encaissement par lot", MaterialIconKind.FormatListChecks, "ToneGreen", Perm.PaymentsCreate, typeof(BatchPaymentPage), () => new BatchPaymentPage(shell)),
                Item("Journal des reçus", MaterialIconKind.ReceiptTextOutline, "ToneTeal", Perm.PaymentsRead, typeof(ReceiptsPage), () => new ReceiptsPage(shell)),
                Item("Recouvrement", MaterialIconKind.ChartBar, "ToneOrange", Perm.ReportsRead, typeof(DebtorsPage), () => new DebtorsPage(shell)),
                Item("Caisse", MaterialIconKind.CashMultiple, "ToneGreen", Perm.PaymentsRead, typeof(CashPage), () => new CashPage(shell)),
                Item("Dépenses", MaterialIconKind.WalletOutline, "ToneRed", Perm.PaymentsRead, typeof(ExpensesPage), () => new ExpensesPage(shell)),
                Item("Prises en charge", MaterialIconKind.ShieldCheckOutline, "ToneTeal", Perm.PaymentsRead, typeof(SponsorshipsPage), () => new SponsorshipsPage(shell))),
            Group("SCOLARITÉ",
                Item("Cours et notes", MaterialIconKind.BookOpenVariantOutline, "TonePurple", Perm.AcademicsRead, typeof(GradesPage), () => new GradesPage(shell)),
                Item("Résultats", MaterialIconKind.CertificateOutline, "TonePurple", Perm.AcademicsRead, typeof(ResultsPage), () => new ResultsPage(shell)),
                Item("Présences", MaterialIconKind.ClipboardCheckOutline, "ToneBlue", Perm.AcademicsRead, typeof(AttendancePage), () => new AttendancePage(shell)),
                Item("Discipline", MaterialIconKind.AlertOctagonOutline, "ToneRed", Perm.AcademicsRead, typeof(DisciplinePage), () => new DisciplinePage(shell)),
                Item("Enseignants et cours", MaterialIconKind.AccountSchoolOutline, "ToneIndigo", Perm.AcademicsRead, typeof(TeachingPage), () => new TeachingPage(shell)),
                Item("Cartes d'accès", MaterialIconKind.CardAccountDetailsOutline, "ToneIndigo", Perm.AcademicsRead, typeof(CardsPage), () => new CardsPage(shell))),
            Group("ADMINISTRATION",
                Item("Personnel et paie", MaterialIconKind.BriefcaseOutline, "ToneOrange", Perm.StaffManage, typeof(StaffPage), () => new StaffPage(shell)),
                Item("Pointage", MaterialIconKind.ClockCheckOutline, "ToneOrange", Perm.StaffManage, typeof(StaffAttendancePage), () => new StaffAttendancePage(shell)),
                Item("Paramètres", MaterialIconKind.CogOutline, "ToneNeutral", Perm.SettingsWrite, typeof(SettingsPage), () => new SettingsPage(shell)),
                Item("Utilisateurs", MaterialIconKind.AccountCogOutline, "ToneNeutral", Perm.UsersManage, typeof(UsersPage), () => new UsersPage(shell)),
                Item("Données et sauvegardes", MaterialIconKind.DatabaseExportOutline, "ToneGreen", Perm.BackupManage, typeof(DataPage), () => new DataPage(shell)),
                Item("Journal d'audit", MaterialIconKind.FileDocumentOutline, "ToneNeutral", Perm.AuditRead, typeof(AuditPage), () => new AuditPage(shell))),
        };
        return groups.Where(g => g.Items.Count > 0).ToList();
    }
}
