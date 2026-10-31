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

/// <summary>Indicateur de l'accueil : une icône, un chiffre, une précision ; un clic ouvre la page détaillée.</summary>
public sealed record HomeKpiVm(string Label, string Value, string Hint, MaterialIconKind Icon, IBrush Brush, AsyncCommand Open);

/// <summary>Point de la liste « Aujourd'hui » : ce qui demande une action dans la journée.</summary>
public sealed record HomeTaskVm(string Text, string Detail, MaterialIconKind Icon, IBrush Brush, AsyncCommand Open);

/// <summary>
/// Accueil : un bandeau de bienvenue avec la date et l'état de la caisse, quatre indicateurs, les outils
/// recommandés, puis les derniers reçus et ce qui reste à faire aujourd'hui. Le détail reste dans le Tableau de bord.
/// </summary>
public sealed class HomePage : PageViewModel
{
    private string subtitle = string.Empty;
    private string todayLine = string.Empty;

    public HomePage(ShellViewModel shell) : base(shell)
    {
        ShowAllTools = new RelayCommand(Shell.ShowSidebar);
        BuildTools();
    }

    public override string Title => "Accueil";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.HomeOutline;

    public ObservableCollection<ToolCardVm> Tools { get; } = [];
    public ObservableCollection<RecentReceiptVm> Receipts { get; } = [];
    public ObservableCollection<HomeKpiVm> Kpis { get; } = [];
    public ObservableCollection<HomeTaskVm> Tasks { get; } = [];

    /// <summary>Bonjour le jour, bonsoir à partir de 18 heures.</summary>
    public string Greeting => $"{(DateTime.Now.Hour >= 18 ? "Bonsoir" : "Bonjour")}, {Shell.AccountName}";
    public string DateLine => Format.LongDate(DateOnly.FromDateTime(DateTime.Today));
    public string SchoolLine => Shell.SchoolLine;
    public string AccountContextLine => $"{Shell.Session?.RoleLabel ?? "Compte"} · {Shell.SchoolHeaderTitle}";
    public string HeaderSchoolLine => $"{Shell.SchoolHeaderTitle}{(string.IsNullOrWhiteSpace(Shell.SchoolYearLine) ? string.Empty : $" · {Shell.SchoolYearLine}")}";
    /// <summary>Mobile : l'accueil montre l'état de la connexion et l'activité des autres postes.</summary>
    public SyncCenter Sync => Shell.Sync;
    public RelayCommand Network => Shell.NetworkCommand;
    public Avalonia.Media.Imaging.Bitmap? SchoolLogo => Shell.SchoolLogo;
    public bool HasSchoolLogo => Shell.HasSchoolLogo;
    public string Initials => Shell.UserInitials;
    private string cashLine = string.Empty;
    private IBrush cashBrush = Brushes.Transparent;
    public string CashLine { get => cashLine; private set { if (Set(ref cashLine, value)) Raise(nameof(HasCashLine)); } }
    public bool HasCashLine => cashLine.Length > 0;
    public IBrush CashBrush { get => cashBrush; private set => Set(ref cashBrush, value); }
    public bool HasKpis => Kpis.Count > 0;
    public bool HasTasks => Tasks.Count > 0;
    public RelayCommand ShowAllTools { get; }

    /// <summary>Les deux premiers outils, en boutons dans le bandeau de bienvenue : l'action du jour d'un clic.</summary>
    public IReadOnlyList<ToolCardVm> QuickActions => Tools.Take(2).ToList();
    public string TodayLine { get => todayLine; private set { if (Set(ref todayLine, value)) Raise(nameof(HasTodayLine)); } }
    public bool HasTodayLine => todayLine.Length > 0;

    public override async Task LoadAsync()
    {
        subtitle = HeaderSchoolLine;
        Raise(nameof(Subtitle));
        Raise(nameof(Greeting));
        Raise(nameof(SchoolLine));
        Raise(nameof(AccountContextLine));
        Raise(nameof(HeaderSchoolLine));
        if (!Shell.Can(Perm.ReportsRead)) { RaiseLists(); return; }

        // Toutes les réponses sont prêtes avant de toucher aux cartes affichées.
        var d = await Shell.Api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord");
        TodayDto? t = null;
        try { t = await Shell.Api.GetAsync<TodayDto>("api/rapports/aujourdhui"); }
        catch (ApiException) { }
        var nextReceipts = new List<RecentReceiptVm>();
        var nextKpis = new List<HomeKpiVm>();
        var nextTasks = new List<HomeTaskVm>();
        foreach (var r in d.LatestReceipts.Take(6))
            nextReceipts.Add(new RecentReceiptVm(r.Number, Student(r.Allocations), Format.Date(r.Date), Labels.Method(r.Method), Money(r.Amount), r.Status,
                new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new ReceiptDetailPage(Shell, r.Id))))));

        AsyncCommand Go(Func<PageViewModel> page) => new(() => Shell.Run(() => Shell.Open(page)));
        nextKpis.Add(new HomeKpiVm("Encaissé aujourd'hui", Money(t?.CollectedToday ?? d.CollectedToday),
            t is null ? "Journée en cours" : Format.Count(t.ReceiptsToday, "reçu émis", "reçus émis"), MaterialIconKind.HandCoin, ThemeBrush.Get("ToneGreen"),
            Go(() => new ReceiptsPage(Shell))));
        nextKpis.Add(new HomeKpiVm("Encaissé ce mois", Money(d.CollectedThisMonth), $"{Money(d.CollectedTotal)} depuis la rentrée", MaterialIconKind.CalendarMonth, ThemeBrush.Get("ToneBlue"),
            Go(() => new DashboardPage(Shell))));
        nextKpis.Add(new HomeKpiVm("Apprenants actifs", d.ActiveStudents.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
            d.NewThisMonth > 0 ? $"{Format.Count(d.NewThisMonth, "inscrit", "inscrits")} ce mois" : "Aucune inscription ce mois", MaterialIconKind.AccountGroup, ThemeBrush.Get("ToneIndigo"),
            Go(() => new StudentsPage(Shell))));
        nextKpis.Add(new HomeKpiVm("Recouvrement", $"{d.RecoveryRate:0} %", d.StudentsLate > 0 ? $"{Format.Count(d.StudentsLate, "apprenant en retard", "apprenants en retard")}" : "Tout le monde est à jour",
            MaterialIconKind.ChartBar, ThemeBrush.Get(d.StudentsLate > 0 ? "ToneOrange" : "ToneGreen"), Go(() => new DebtorsPage(Shell))));

        if (t is not null)
        {
            CashLine = t.CashClosed ? "Caisse arrêtée" : t.ReceiptsToday > 0 ? "Caisse à arrêter" : "Caisse ouverte";
            CashBrush = ThemeBrush.Get(t.CashClosed ? "ToneGreen" : t.ReceiptsToday > 0 ? "ToneOrange" : "ToneBlue");
            if (!t.CashClosed && t.ReceiptsToday > 0 && Shell.Can(Perm.CashClose))
                nextTasks.Add(new HomeTaskVm("Arrêter la caisse", $"{Format.Count(t.ReceiptsToday, "reçu", "reçus")} pour {Money(t.CollectedToday)}", MaterialIconKind.CashRegister,
                    ThemeBrush.Get("ToneOrange"), Go(() => new CashPage(Shell))));
            if (t.AttendanceMarked == 0 && Shell.Can(Perm.AcademicsWrite))
                nextTasks.Add(new HomeTaskVm("Faire l'appel", "Aucun apprenant pointé aujourd'hui", MaterialIconKind.ClipboardCheck, ThemeBrush.Get("ToneTeal"),
                    Go(() => new AttendancePage(Shell))));
            foreach (var a in t.Appointments.Take(3))
                nextTasks.Add(new HomeTaskVm($"Parent convoqué : {a.Name}", $"{a.Promotion} · {a.Detail}", MaterialIconKind.AccountClock, ThemeBrush.Get("ToneRed"),
                    Go(() => new StudentDetailPage(Shell, a.StudentId))));
            foreach (var b in t.Birthdays.Take(3))
                nextTasks.Add(new HomeTaskVm($"Anniversaire : {b.Name}", b.Promotion, MaterialIconKind.CakeVariant, ThemeBrush.Get("TonePink"),
                    Go(() => new StudentDetailPage(Shell, b.StudentId))));
            TodayLine = Format.Count(t.ReceiptsToday, "reçu aujourd'hui", "reçus aujourd'hui");
        }
        else TodayLine = string.Empty;
        CollectionSync.Apply(Receipts, nextReceipts, (a, b) => a with { Open = b.Open } == b);
        CollectionSync.Apply(Kpis, nextKpis, (a, b) => a with { Open = b.Open } == b);
        CollectionSync.Apply(Tasks, nextTasks);
        RaiseLists();
    }

    private void RaiseLists()
    {
        Raise(nameof(HasKpis));
        Raise(nameof(HasTasks));
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
