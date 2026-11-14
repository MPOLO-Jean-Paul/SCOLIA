using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Domain.School;

namespace Scolia.Bureau.ViewModels;

/// <summary>Entrée de la barre latérale : un écran principal, son icône et sa couleur.</summary>
public sealed class NavItemVm : Observable
{
    private bool selected;

    public NavItemVm(string label, MaterialIconKind icon, string tone, Type page, Func<PageViewModel> create, ShellViewModel shell)
    {
        Label = label;
        Icon = icon;
        Brush = IconPalette.Get(tone);
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
        "Mes cours et cotes" => "Cours & cotes",
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

    public NavGroupVm(string title, IReadOnlyList<NavItemVm> items, bool expandedByDefault = false, Action<NavGroupVm>? toggleGroup = null)
    {
        Title = title;
        Items = items;
        Icon = FeatureVisuals.Icon(title);
        Brush = IconPalette.Get(FeatureVisuals.Tone(title));
        // L'accueil est accessible immédiatement ; les autres métiers sont repliés sur mobile.
        isExpanded = title.Length == 0 || expandedByDefault;
        Toggle = new RelayCommand(() =>
        {
            if (toggleGroup is not null) toggleGroup(this);
            else IsExpanded = !IsExpanded;
        });
    }

    public string Title { get; }
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public IReadOnlyList<NavItemVm> Items { get; }
    public bool HasTitle => Title.Length > 0;

    /// <summary>Actions d'affichage et autres réglages rassemblés dans le seul accordéon Paramètres.</summary>
    public IReadOnlyList<RibbonCommandVm> SupplementalCommands { get; private set; } = [];

    public void SetSupplementalCommands(IReadOnlyList<RibbonCommandVm> commands)
    {
        SupplementalCommands = commands;
        Raise(nameof(SupplementalCommands));
    }

    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }
    public RelayCommand Toggle { get; }
}

/// <summary>Bloc métier : plusieurs rubriques de même nature dans une carte visuelle.</summary>
public sealed record NavBlockVm(string Title, IReadOnlyList<NavGroupVm> Groups);

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

        NavGroupVm Group(string title, params NavItemVm?[] items) =>
            new(title, items.OfType<NavItemVm>().ToList(), toggleGroup: shell.ToggleNavGroup);

        // Aucune entrée n'apparaît sur les anciens ITM/IM. Le module est
        // visible seulement lorsqu'un profil scolaire est officiellement activé.
        var type = shell.Session?.InstitutionType;
        var cyclesAvailable = InstitutionProfiles.IsEnabled(type) &&
            type is InstitutionProfiles.Maternelle or InstitutionProfiles.Primaire
                or InstitutionProfiles.Secondaire or InstitutionProfiles.ComplexeScolaire;

        var groups = new[]
        {
            Group(string.Empty,
                // L'enseignant a son propre accueil : ses cours, ses annonces et les règles de l'encodage.
                shell.TeacherOnly
                    ? Item("Mon espace", MaterialIconKind.AccountSchool, "TonePurple", null, typeof(TeacherHomePage), () => new TeacherHomePage(shell))
                    : Item("Accueil", MaterialIconKind.Home, "ToneBlue", null,
                        shell.Can(Perm.PilotageRead) && !shell.Can(Perm.ReportsRead) ? typeof(PromoterDashboardPage) : typeof(HomePage),
                        () => shell.Can(Perm.PilotageRead) && !shell.Can(Perm.ReportsRead)
                            ? new PromoterDashboardPage(shell) : new HomePage(shell))),
            // Espace enseignant : seulement ce qui relève de son métier (encodage anonyme de ses cours).
            shell.TeacherOnly
                ? Group("Espace enseignant",
                    Item("Mes cours et cotes", MaterialIconKind.Pen, "TonePurple", null, typeof(MarksPage), () => new MarksPage(shell)))
                : Group("Espace enseignant"),
            // Pilotage : regarder et exporter les chiffres.
            Group("Pilotage",
                Item("Tableau de bord", MaterialIconKind.ViewDashboard, "ToneBlue", Perm.ReportsRead, typeof(DashboardPage), () => new DashboardPage(shell)),
                Item("Rapports et exports", MaterialIconKind.FileChart, "ToneIndigo", Perm.ReportsRead, typeof(ReportsPage), () => new ReportsPage(shell))),
            // Secrétariat : les dossiers des apprenants et ce qui en sort (cartes d'accès).
            Group("Secrétariat",
                Item(shell.StudentTerms.PluralTitle, MaterialIconKind.AccountGroup, "ToneBlue", Perm.StudentsRead, typeof(StudentsPage), () => new StudentsPage(shell)),
                Item("Réinscriptions", MaterialIconKind.School, "ToneIndigo", Perm.StudentsWrite, typeof(ReEnrollmentPage), () => new ReEnrollmentPage(shell)),
                Item("Cartes d'accès", MaterialIconKind.CardAccountDetails, "ToneBlue", Perm.AcademicsRead, typeof(CardsPage), () => new CardsPage(shell))),
            Group("Finances",
                Item("Encaisser", MaterialIconKind.HandCoin, "ToneGreen", Perm.PaymentsCreate, typeof(CashierPage), () => new CashierPage(shell)),
                Item("Encaissement par lot", MaterialIconKind.FormatListChecks, "ToneTeal", Perm.PaymentsCreate, typeof(BatchPaymentPage), () => new BatchPaymentPage(shell)),
                Item("Journal des reçus", MaterialIconKind.ReceiptText, "ToneOrange", Perm.PaymentsRead, typeof(ReceiptsPage), () => new ReceiptsPage(shell)),
                Item("Suivi mensuel", MaterialIconKind.CalendarCheck, "ToneIndigo", Perm.ReportsRead, typeof(MonthlyPage), () => new MonthlyPage(shell)),
                Item("Recouvrement", MaterialIconKind.ChartBar, "ToneOrange", Perm.ReportsRead, typeof(DebtorsPage), () => new DebtorsPage(shell)),
                Item("Caisse", MaterialIconKind.CashMultiple, "ToneGreen", Perm.PaymentsRead, typeof(CashPage), () => new CashPage(shell)),
                Item("Dépenses", MaterialIconKind.Wallet, "ToneRed", Perm.PaymentsRead, typeof(ExpensesPage), () => new ExpensesPage(shell)),
                Item("Prises en charge", MaterialIconKind.HandHeart, "TonePink", Perm.PaymentsRead, typeof(SponsorshipsPage), () => new SponsorshipsPage(shell))),
            // Scolarité : la vie de classe (notes du cycle ordinaire, appel, discipline).
            Group("Scolarité",
                Item("Cours et notes", MaterialIconKind.BookOpenVariant, "TonePurple", Perm.AcademicsRead, typeof(GradesPage), () => new GradesPage(shell)),
                Item("Résultats", MaterialIconKind.Certificate, "ToneIndigo", Perm.AcademicsRead, typeof(ResultsPage), () => new ResultsPage(shell)),
                Item("Présences", MaterialIconKind.ClipboardCheck, "ToneTeal", Perm.AcademicsRead, typeof(AttendancePage), () => new AttendancePage(shell)),
                Item("Discipline", MaterialIconKind.AlertOctagon, "ToneRed", Perm.AcademicsRead, typeof(DisciplinePage), () => new DisciplinePage(shell))),
            // Les référentiels de cycles, matières et semestres relèvent de la pédagogie,
            // pas de l'apparence ou de la sécurité du logiciel. Même accès sur Windows/Android.
            Group("Organisation pédagogique",
                cyclesAvailable ? Item("Cycles scolaires", MaterialIconKind.SchoolOutline, "ToneTeal", Perm.SettingsWrite,
                    typeof(SchoolCyclesPage), () => new SchoolCyclesPage(shell)) : null,
                type == InstitutionProfiles.SuperieurLmd
                    ? Item("Organisation LMD", MaterialIconKind.BookEducationOutline, "ToneTeal", Perm.SettingsWrite,
                        typeof(LmdPlanningPage), () => new LmdPlanningPage(shell)) : null),
            // Gestion des délibérations (fusion d'ITMCD), dans l'ordre du travail :
            // 1. les enseignants, leurs comptes et l'attribution des cours ;
            Group("Enseignants et cours",
                Item("Attribution des cours", MaterialIconKind.AccountSchool, "ToneOrange", Perm.AcademicsRead, typeof(TeachingPage), () => new TeachingPage(shell)),
                Item("Comptes enseignants", MaterialIconKind.AccountKey, "ToneBlue", Perm.UsersManage, typeof(TeacherAccountsPage), () => new TeacherAccountsPage(shell))),
            // 2. la préparation (paramètres, codes anonymes), l'encodage et son suivi, puis le jury.
            Group("Délibération",
                Item("Paramètres de délibération", MaterialIconKind.Tune, "ToneOrange", Perm.DeliberationManage, typeof(DeliberationSettingsPage), () => new DeliberationSettingsPage(shell)),
                Item("Codes anonymes", MaterialIconKind.Incognito, "ToneIndigo", Perm.DeliberationManage, typeof(AnonymousCodesPage), () => new AnonymousCodesPage(shell)),
                Item("Encodage des cotes", MaterialIconKind.Pen, "TonePurple", Perm.AcademicsRead, typeof(MarksPage), () => new MarksPage(shell)),
                Item("Suivi de l'encodage", MaterialIconKind.ProgressClock, "ToneTeal", Perm.AcademicsRead, typeof(EncodingProgressPage), () => new EncodingProgressPage(shell)),
                Item("Délibération", MaterialIconKind.Gavel, "ToneRed", Perm.AcademicsRead, typeof(DeliberationPage), () => new DeliberationPage(shell))),
            // Personnel : les membres de l'établissement, leur paie et leur pointage.
            Group("Personnel",
                Item("Personnel et paie", MaterialIconKind.Briefcase, "ToneOrange", Perm.StaffManage, typeof(StaffPage), () => new StaffPage(shell)),
                Item("Pointage", MaterialIconKind.ClockCheck, "ToneTeal", Perm.StaffManage, typeof(StaffAttendancePage), () => new StaffAttendancePage(shell))),
            // Connexions : destination autonome facile à découvrir, hors du Compte.
            Group("Réseau et postes",
                Item("Réseau de l'établissement", MaterialIconKind.LanConnect, "ToneTeal", null,
                    typeof(NetworkHubPage), () => new NetworkHubPage(shell))),
            Group("Paramètres",
                Item("Configuration", MaterialIconKind.Cog, "TonePurple", null, typeof(SettingsPage), () => new SettingsPage(shell)),
                shell.TeacherOnly ? Item("Encodage des cotes", MaterialIconKind.Pen, "TonePurple",
                    null, typeof(MarksPage), () => new MarksPage(shell)) : null,
                Item("Utilisateurs", MaterialIconKind.AccountCog, "ToneBlue", Perm.UsersManage, typeof(UsersPage), () => new UsersPage(shell))),
            // Données : protéger, contrôler et, si besoin, effacer.
            Group("Données",
                Item("Sauvegardes", MaterialIconKind.DatabaseExport, "ToneGreen", Perm.BackupManage, typeof(DataPage), () => new DataPage(shell)),
                Item("Sauvegarde en ligne", MaterialIconKind.CloudLock, "ToneTeal", Perm.BackupManage, typeof(CloudBackupPage), () => new CloudBackupPage(shell)),
                Item("Journal d'audit", MaterialIconKind.ShieldCheck, "ToneIndigo", Perm.AuditRead, typeof(AuditPage), () => new AuditPage(shell)),
                Item("Effacement des données", MaterialIconKind.DeleteSweep, "ToneRed", Perm.BackupManage, typeof(EraseDataPage), () => new EraseDataPage(shell, payments: true)))
        };
        return groups.Where(g => g.Items.Count > 0).ToList();
    }

    /// <summary>Classe les rubriques autorisées en blocs, sans répéter ni cacher de fonctions.</summary>
    public static IReadOnlyList<NavBlockVm> Blocks(IReadOnlyList<NavGroupVm> groups)
    {
        var blocks = new List<NavBlockVm>();
        var used = new HashSet<NavGroupVm>();
        void Add(string title, params string[] names)
        {
            var entries = groups.Where(g => names.Contains(g.Title, StringComparer.Ordinal)).ToArray();
            if (entries.Length == 0) return;
            foreach (var entry in entries) used.Add(entry);
            blocks.Add(new NavBlockVm(title, entries));
        }
        Add("VUE D’ENSEMBLE", "", "Pilotage");
        Add("ESPACE ENSEIGNANT", "Espace enseignant");
        Add("GESTION SCOLAIRE", "Secrétariat", "Scolarité", "Organisation pédagogique");
        Add("GESTION DES DÉLIBÉRATIONS", "Enseignants et cours", "Délibération");
        Add("GESTION FINANCIÈRE", "Finances");
        Add("ORGANISATION", "Personnel");
        Add("CONNEXIONS", "Réseau et postes");
        Add("RÉGLAGES ET DONNÉES", "Paramètres", "Données");
        var other = groups.Where(g => !used.Contains(g)).ToArray();
        if (other.Length != 0) blocks.Add(new NavBlockVm("AUTRES OUTILS", other));
        return blocks;
    }
}
