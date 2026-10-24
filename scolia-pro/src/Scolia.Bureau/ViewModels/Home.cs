using System.Collections.ObjectModel;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Outil proposé sur l'accueil : son icône de couleur, son nom, ce qu'il fait et le bouton qui l'ouvre.</summary>
public sealed record ToolCardVm(string Title, string Description, MaterialIconKind Icon, IBrush Brush, AsyncCommand Open);

/// <summary>Reçu récent de l'accueil : une ligne de la liste « Récents », qui ouvre le reçu.</summary>
public sealed record RecentReceiptVm(string Number, string Student, string Date, string Method, string Amount, string Status, AsyncCommand Open);

/// <summary>
/// Accueil, sur le modèle d'Acrobat : les outils recommandés en tête, puis les derniers reçus.
/// Les indicateurs détaillés restent dans Données, Tableau de bord.
/// </summary>
public sealed class HomePage : PageViewModel
{
    private string subtitle = string.Empty;
    private string todayLine = string.Empty;

    public HomePage(ShellViewModel shell) : base(shell)
    {
        ShowAllTools = new RelayCommand(() => Shell.Prefs.Sidebar = true);
        BuildTools();
    }

    public override string Title => "Accueil";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.HomeOutline;

    public ObservableCollection<ToolCardVm> Tools { get; } = [];
    public ObservableCollection<RecentReceiptVm> Receipts { get; } = [];
    public RelayCommand ShowAllTools { get; }
    public string TodayLine { get => todayLine; private set { if (Set(ref todayLine, value)) Raise(nameof(HasTodayLine)); } }
    public bool HasTodayLine => todayLine.Length > 0;

    public override async Task LoadAsync()
    {
        subtitle = Shell.SchoolLine;
        Raise(nameof(Subtitle));
        Receipts.Clear();
        if (!Shell.Can(Perm.ReportsRead)) return;

        var d = await Shell.Api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord");
        foreach (var r in d.LatestReceipts)
            Receipts.Add(new RecentReceiptVm(r.Number, Student(r.Allocations), Format.Date(r.Date), Labels.Method(r.Method), Money(r.Amount), r.Status,
                new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new ReceiptDetailPage(Shell, r.Id))))));

        try
        {
            var t = await Shell.Api.GetAsync<TodayDto>("api/rapports/aujourdhui");
            TodayLine = $"Aujourd'hui : {Format.Count(t.ReceiptsToday, "reçu", "reçus")} pour {Money(t.CollectedToday)}"
                + (t.CashClosed ? " · caisse arrêtée" : t.ReceiptsToday > 0 ? " · caisse à arrêter" : string.Empty)
                + (t.Appointments.Count > 0 ? $" · {Format.Count(t.Appointments.Count, "parent convoqué", "parents convoqués")}" : string.Empty)
                + (t.Birthdays.Count > 0 ? $" · {Format.Count(t.Birthdays.Count, "anniversaire", "anniversaires")}" : string.Empty);
        }
        catch (ApiException) { TodayLine = string.Empty; }
    }

    /// <summary>Le libellé du serveur est « matricule · nom » : seul le nom est affiché, le matricule en tête de ligne serait du bruit.</summary>
    private static string Student(string? allocations) =>
        allocations is null ? string.Empty : allocations.Split(" · ", 2) is [_, var name] ? name : allocations;

    /// <summary>Les outils les plus utilisés, selon les droits du compte : quatre en tête, comme sur l'accueil d'Acrobat.</summary>
    private void BuildTools()
    {
        void Add(string? perm, string title, string description, MaterialIconKind icon, string tone, Func<Task> run)
        {
            if (perm is null || Shell.Can(perm))
                Tools.Add(new ToolCardVm(title, description, icon, ThemeBrush.Get(tone), new AsyncCommand(() => Shell.Run(run))));
        }

        Add(Perm.PaymentsCreate, "Encaisser", "Choisir les mois payés et imprimer le reçu numéroté.", MaterialIconKind.HandCoin, "ToneGreen",
            () => Shell.Open(() => new CashierPage(Shell)));
        Add(Perm.StudentsWrite, "Inscrire un apprenant", "Créer le dossier. Le matricule est attribué seul.", MaterialIconKind.AccountPlus, "ToneBlue",
            () => StudentForms.CreateAsync(Shell));
        Add(Perm.ReportsRead, "Recouvrement", "Voir les retards et relancer les parents.", MaterialIconKind.ChartBar, "ToneOrange",
            () => Shell.Open(() => new DebtorsPage(Shell)));
        Add(null, "Rapports et exports", "Tous les documents en PDF ou en Excel.", MaterialIconKind.FileChart, "ToneRed",
            () => Shell.Open(() => new ReportsPage(Shell)));
        Add(Perm.PaymentsRead, "Journal des reçus", "Retrouver, imprimer ou annuler un reçu.", MaterialIconKind.ReceiptText, "TonePurple",
            () => Shell.Open(() => new ReceiptsPage(Shell)));
        Add(Perm.AcademicsRead, "Présences", "Faire l'appel du jour par promotion.", MaterialIconKind.ClipboardCheck, "ToneTeal",
            () => Shell.Open(() => new AttendancePage(Shell)));
        Add(null, "Tableau de bord", "Recettes, retards et indicateurs de l'année.", MaterialIconKind.ViewDashboard, "ToneIndigo",
            () => Shell.Open(() => new DashboardPage(Shell)));
        Add(Perm.StudentsRead, "Apprenants", "Rechercher un dossier et ouvrir sa fiche.", MaterialIconKind.AccountGroup, "ToneBlue",
            () => Shell.Open(() => new StudentsPage(Shell)));
        while (Tools.Count > 4) Tools.RemoveAt(Tools.Count - 1);
    }
}
