using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using static Material.Icons.MaterialIconKind;

namespace Scolia.Bureau.Ribbon;

/// <summary>Catalogue du ruban, mêmes onglets, groupes et libellés que l'interface web. Les commandes sans droit sont retirées.</summary>
public static class RibbonCatalog
{
    private const string Blue = "ToneBlue";
    private const string Green = "ToneGreen";
    private const string Orange = "ToneOrange";
    private const string Purple = "TonePurple";
    private const string Red = "ToneRed";
    private const string Neutral = "ToneNeutral";

    /// <summary>
    /// Raccourci de chaque commande, par libellé. Les combinaisons Ctrl+Alt ne sont pas utilisées :
    /// sur un clavier AZERTY, AltGr envoie Ctrl+Alt, ce qui déclencherait une commande en tapant un caractère.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Shortcuts { get; } = new Dictionary<string, string>
    {
        ["Tableau de bord"] = "Ctrl+D",
        ["Actualiser"] = "F5",
        ["Rechercher"] = "Ctrl+K",
        ["Imprimer"] = "Ctrl+P",
        ["Plein écran"] = "F11",
        ["Apprenants"] = "Ctrl+Shift+A",
        ["Nouvelle inscription"] = "Ctrl+N",
        ["Réinscriptions"] = "Ctrl+Shift+R",
        ["Encaisser"] = "F2",
        ["Journal des reçus"] = "Ctrl+J",
        ["Reçus (Excel)"] = "Alt+E",
        ["Reçus (PDF)"] = "Alt+P",
        ["Recouvrement"] = "Ctrl+R",
        ["Prises en charge"] = "Ctrl+Shift+S",
        ["Débiteurs (Excel)"] = "Alt+D",
        ["Débiteurs (PDF)"] = "Alt+B",
        ["Caisse"] = "Ctrl+Shift+C",
        ["Dépenses"] = "Ctrl+E",
        ["Nouvelle dépense"] = "Ctrl+Shift+N",
        ["Arrêter la caisse"] = "Ctrl+Shift+Q",
        ["Cours et notes"] = "Ctrl+G",
        ["Résultats"] = "Ctrl+T",
        ["Nouveau cours"] = "Alt+C",
        ["Nouvelle évaluation"] = "Alt+V",
        ["Présences"] = "Ctrl+Shift+P",
        ["Discipline"] = "Ctrl+Shift+I",
        ["Nouvel incident"] = "Alt+I",
        ["Personnel et paie"] = "Ctrl+Shift+M",
        ["Nouveau membre"] = "Alt+M",
        ["Paramètres"] = "Ctrl+OemComma",
        ["Utilisateurs"] = "Ctrl+Shift+U",
        ["Nouvelle promotion"] = "Alt+N",
        ["Nouvelle année"] = "Alt+A",
        ["Nouvel utilisateur"] = "Alt+U",
        ["Données et sauvegardes"] = "Ctrl+Shift+B",
        ["Journal d'audit"] = "Ctrl+Shift+J",
        ["Sauvegarder"] = "Alt+S",
        ["Importer SCOLIA V79"] = "Alt+L",
        ["Journal (CSV)"] = "Alt+J",
        ["Mode sombre"] = "Ctrl+Shift+L",
        ["Taille du texte"] = "Alt+T",
        ["Densité compacte"] = "Alt+K",
        ["Barre d'état"] = "Alt+O",
        ["Préférences"] = "Ctrl+Shift+E",
        ["Quadrillage"] = "Alt+G",
        ["Info-bulles"] = "Alt+Q",
        ["Animations"] = "Alt+X",
        ["Réduire le ruban"] = "Ctrl+F1",
        ["Zoom arrière"] = "Ctrl+OemMinus",
        ["Zoom avant"] = "Ctrl+OemPlus",
        ["Zoom 100 %"] = "Ctrl+D0",
        ["Raccourcis clavier"] = "F1",
        ["À propos"] = "Shift+F1",
        ["Vérifier la base"] = "F8",
        ["Vérifier le journal"] = "F7",
        ["Annuler le reçu"] = "Ctrl+Shift+Delete",
        ["Vérifier en ligne"] = "Ctrl+Shift+V",
        ["Modifier"] = "F4",
        ["Relevé de compte"] = "Alt+R",
        ["Liste des apprenants"] = "Ctrl+Shift+F",
    };

    public static List<RibbonTabVm> Build(ShellViewModel shell)
    {
        var tabs = new List<RibbonTabVm>
        {
            new("accueil", "Accueil",
            [
                Group("Tableau de bord",
                    Large(shell, "Tableau de bord", ViewDashboardOutline, "Indicateurs du jour, recettes et retards de paiement.", () => shell.Open(() => new DashboardPage(shell)), Blue),
                    Large(shell, "Actualiser", Refresh, "Recharge les données affichées depuis le serveur.", () => shell.Reload(), Blue)),
                Group("Rechercher",
                    Large(shell, "Rechercher", Magnify, "Recherche globale : apprenants, reçus, pages et commandes.", () => { shell.ShowSearch(); return Task.CompletedTask; }, Blue)),
                Group("Page",
                    Large(shell, "Imprimer", Printer, "Imprime le document de la page affichée.", () => shell.Current?.PrintAsync() ?? Task.CompletedTask, Blue),
                    Small(shell, "Plein écran", Fullscreen, "Affiche SCOLIA sans bordures de fenêtre.", () => { shell.ToggleFullscreen(); return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.IsFullscreen)),
                ..RecentsGroups(shell),
            ]),
            new("secretariat", "Secrétariat",
            [
                Group("Apprenants",
                    Large(shell, "Apprenants", AccountGroupOutline, "Liste, recherche et fiches des apprenants.", () => shell.Open(() => new StudentsPage(shell)), Blue, perm: Perm.StudentsRead),
                    Large(shell, "Nouvelle inscription", AccountPlusOutline, "Inscrit un apprenant. Le matricule est attribué automatiquement.", () => StudentForms.CreateAsync(shell), Green, perm: Perm.StudentsWrite),
                    Small(shell, "Réinscriptions", SchoolOutline, "Reconduit les apprenants vers la promotion suivante.", () => shell.Open(() => new ReEnrollmentPage(shell)), Neutral, perm: Perm.StudentsWrite)),
            ]),
            new("finances", "Finances",
            [
                Group("Encaissement",
                    Large(shell, "Encaisser", HandCoin, "Encaisse un versement : il est réparti sur les échéances et numéroté.", () => shell.Open(() => new CashierPage(shell)), Green, perm: Perm.PaymentsCreate),
                    Large(shell, "Encaissement par lot", FormatListChecks, "Plusieurs apprenants et plusieurs mois en une opération : un reçu numéroté par apprenant.", () => shell.Open(() => new BatchPaymentPage(shell)), Green, perm: Perm.PaymentsCreate),
                    Large(shell, "Journal des reçus", ReceiptTextOutline, "Tous les reçus, leur statut et les annulations motivées.", () => shell.Open(() => new ReceiptsPage(shell)), Neutral, perm: Perm.PaymentsRead),
                    Small(shell, "Reçus (Excel)", FileExcelOutline, "Exporte tous les reçus dans un classeur Excel.", () => shell.Download("api/paiements/export?format=xlsx", "recus.xlsx"), Neutral, perm: Perm.PaymentsRead),
                    Small(shell, "Reçus (PDF)", FilePdfBox, "Exporte la liste des reçus au format PDF.", () => shell.Download("api/paiements/export?format=pdf", "recus.pdf"), Neutral, perm: Perm.PaymentsRead)),
                Group("Suivi",
                    Large(shell, "Recouvrement", ChartBar, "Retards classés par ancienneté, avec rappels WhatsApp et SMS.", () => shell.Open(() => new DebtorsPage(shell)), Orange, perm: Perm.ReportsRead),
                    Small(shell, "Prises en charge", ShieldCheckOutline, "Bourses, sponsors et exonérations de l'année.", () => shell.Open(() => new SponsorshipsPage(shell)), Neutral, perm: Perm.PaymentsRead),
                    Small(shell, "Débiteurs (Excel)", FileExcelOutline, "Exporte la liste des retards de paiement dans Excel.", () => shell.Download("api/rapports/debiteurs/export?format=xlsx", "debiteurs.xlsx"), Neutral, perm: Perm.ReportsRead),
                    Small(shell, "Débiteurs (PDF)", FilePdfBox, "Exporte la liste des retards de paiement en PDF.", () => shell.Download("api/rapports/debiteurs/export?format=pdf", "debiteurs.pdf"), Neutral, perm: Perm.ReportsRead)),
                Group("Caisse",
                    Large(shell, "Caisse", CashMultiple, "Mouvements de la période et situation de la caisse.", () => shell.Open(() => new CashPage(shell)), Green, perm: Perm.PaymentsRead),
                    Large(shell, "Dépenses", WalletOutline, "Sorties de caisse et de banque, numérotées et justifiées.", () => shell.Open(() => new ExpensesPage(shell)), Red, perm: Perm.PaymentsRead),
                    Small(shell, "Nouvelle dépense", Plus, "Ouvre le formulaire d'une nouvelle dépense.", () => ExpensesPage.NewExpenseAsync(shell), Neutral, perm: Perm.ExpensesWrite),
                    Small(shell, "Arrêter la caisse", LockOutline, "Compte les espèces et verrouille la période.", () => CashPage.ClosingAsync(shell), Neutral, perm: Perm.CashClose)),
            ]),
            new("scolarite", "Scolarité",
            [
                Group("Cours",
                    Large(shell, "Cours et notes", BookOpenVariantOutline, "Cours, évaluations et saisie des notes.", () => shell.Open(() => new GradesPage(shell)), Purple, perm: Perm.AcademicsRead),
                    Large(shell, "Résultats", CertificateOutline, "Moyennes, rangs, décisions et bulletins PDF.", () => shell.Open(() => new ResultsPage(shell)), Purple, perm: Perm.AcademicsRead),
                    Small(shell, "Nouveau cours", FileDocumentPlusOutline, "Crée un cours dans la promotion choisie.", () => GradesPage.NewCourseAsync(shell), Neutral, perm: Perm.AcademicsWrite),
                    Small(shell, "Nouvelle évaluation", ClipboardPlusOutline, "Crée une évaluation pour un cours.", () => GradesPage.NewAssessmentAsync(shell), Neutral, perm: Perm.AcademicsWrite)),
                Group("Vie de classe",
                    Large(shell, "Présences", ClipboardCheckOutline, "Appel quotidien par promotion et taux d'assiduité.", () => shell.Open(() => new AttendancePage(shell)), Blue, perm: Perm.AcademicsRead),
                    Large(shell, "Discipline", AlertOctagonOutline, "Incidents, sanctions et suivi du comportement.", () => shell.Open(() => new DisciplinePage(shell)), Red, perm: Perm.AcademicsRead),
                    Small(shell, "Nouvel incident", Plus, "Signale un incident de discipline.", () => DisciplinePage.NewDecisionAsync(shell), Neutral, perm: Perm.AcademicsWrite)),
            ]),
            new("administration", "Administration",
            [
                Group("Personnel",
                    Large(shell, "Personnel et paie", BriefcaseOutline, "Dossiers du personnel, bulletins mensuels et paiement des salaires.", () => shell.Open(() => new StaffPage(shell)), Neutral, perm: Perm.StaffManage),
                    Small(shell, "Nouveau membre", AccountPlusOutline, "Ajoute un membre du personnel.", () => StaffPage.NewMemberAsync(shell), Neutral, perm: Perm.StaffManage)),
                Group("Établissement",
                    Large(shell, "Paramètres", CogOutline, "Identité, années scolaires, sections et promotions.", () => shell.Open(() => new SettingsPage(shell)), Neutral, perm: Perm.SettingsWrite),
                    Large(shell, "Utilisateurs", AccountCogOutline, "Comptes nominatifs, rôles et mots de passe provisoires.", () => shell.Open(() => new UsersPage(shell)), Neutral, perm: Perm.UsersManage),
                    Small(shell, "Nouvelle promotion", Plus, "Crée une promotion avec ses frais.", () => SettingsPage.NewPromotionAsync(shell), Neutral, perm: Perm.SettingsWrite),
                    Small(shell, "Nouvelle année", CalendarPlus, "Prépare une nouvelle année scolaire.", () => SettingsPage.NewYearAsync(shell), Neutral, perm: Perm.SettingsWrite),
                    Small(shell, "Nouvel utilisateur", AccountPlusOutline, "Crée un compte nominatif.", () => UsersPage.NewUserAsync(shell), Neutral, perm: Perm.UsersManage)),
                Group("Données",
                    Large(shell, "Données et sauvegardes", DatabaseExportOutline, "Sauvegardes, restauration et import du classeur V79.", () => shell.Open(() => new DataPage(shell)), Green, perm: Perm.BackupManage),
                    Large(shell, "Journal d'audit", FileDocumentOutline, "Qui a fait quoi, et vérification de la chaîne d'empreintes.", () => shell.Open(() => new AuditPage(shell)), Neutral, perm: Perm.AuditRead),
                    Small(shell, "Sauvegarder", ContentSaveOutline, "Crée tout de suite une sauvegarde vérifiée.", () => DataPage.BackupNowAsync(shell), Neutral, perm: Perm.BackupManage),
                    Small(shell, "Importer SCOLIA V79", Upload, "Reprend les données d'un classeur Excel de l'ancienne version.", () => DataPage.ImportAsync(shell), Neutral, perm: Perm.BackupManage),
                    Small(shell, "Journal (CSV)", FileDownloadOutline, "Exporte le journal d'audit au format CSV.", () => shell.Download("api/audit/export?format=csv", "journal-audit.csv"), Neutral, perm: Perm.AuditRead)),
            ]),
            new("affichage", "Affichage",
            [
                Group("Apparence",
                    Large(shell, "Mode sombre", WeatherNight, "Bascule entre le thème clair et le thème sombre.", () => { shell.Prefs.IsDark = !shell.Prefs.IsDark; return Task.CompletedTask; }, Blue, toggle: true, isOn: () => shell.Prefs.IsDark),
                    Small(shell, "Taille du texte", FormatSize, "Fait défiler les tailles : petite, normale, grande, très grande.", () => { CycleTextSize(shell); return Task.CompletedTask; }, Neutral),
                    Small(shell, "Densité compacte", FormatLineSpacing, "Lignes et boutons plus serrés, pour afficher davantage de données.", () => { shell.Prefs.Compact = !shell.Prefs.Compact; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.Compact),
                    Small(shell, "Barre d'état", ViewDashboardOutline, "Affiche la barre du bas : statut, serveur et zoom.", () => { shell.Prefs.StatusBar = !shell.Prefs.StatusBar; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.StatusBar),
                    Small(shell, "Préférences", Tune, "Ouvre toutes les préférences d'affichage.", () => PreferencesDialog.ShowAsync(shell), Neutral)),
                Group("Affichage",
                    Small(shell, "Quadrillage", Grid, "Affiche les lignes entre les colonnes des tableaux.", () => { shell.Prefs.Grid = !shell.Prefs.Grid; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.Grid),
                    Small(shell, "Info-bulles", InformationOutline, "Affiche la description des commandes au survol.", () => { shell.Prefs.Tips = !shell.Prefs.Tips; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.Tips),
                    Small(shell, "Animations", AutoFix, "Désactive les transitions de l'interface.", () => { shell.Prefs.Animations = !shell.Prefs.Animations; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.Animations),
                    Small(shell, "Réduire le ruban", ChevronUp, "Masque les commandes et ne garde que les onglets.", () => { shell.Prefs.RibbonCollapsed = !shell.Prefs.RibbonCollapsed; return Task.CompletedTask; }, Neutral, toggle: true, isOn: () => shell.Prefs.RibbonCollapsed)),
                Group("Zoom",
                    Small(shell, "Zoom arrière", MagnifyMinus, "Réduit la taille de l'interface.", () => { shell.Prefs.Scale -= 0.1; return Task.CompletedTask; }, Neutral),
                    Small(shell, "Zoom avant", MagnifyPlus, "Agrandit la taille de l'interface.", () => { shell.Prefs.Scale += 0.1; return Task.CompletedTask; }, Neutral),
                    Small(shell, "Zoom 100 %", Restore, "Rétablit la taille normale.", () => { shell.Prefs.Scale = 1.0; return Task.CompletedTask; }, Neutral)),
            ]),
            new("aide", "Aide",
            [
                Group("Aide",
                    Large(shell, "Raccourcis clavier", Keyboard, "Liste des raccourcis clavier de SCOLIA.", () => ShortcutsDialog.ShowAsync(shell), Blue),
                    Large(shell, "À propos", Information, "Version de SCOLIA Pro et conditions d'utilisation.", () => AboutDialog.ShowAsync(shell), Neutral)),
                Group("Diagnostic",
                    Small(shell, "Vérifier la base", DatabaseCheckOutline, "Contrôle d'intégrité de la base de données.", () => DataPage.CheckDatabaseAsync(shell), Neutral, perm: Perm.BackupManage),
                    Small(shell, "Vérifier le journal", ShieldCheckOutline, "Contrôle de la chaîne d'empreintes du journal d'audit.", () => AuditPage.VerifyAsync(shell), Neutral, perm: Perm.AuditRead)),
            ]),
        };

        tabs.Insert(0, new RibbonTabVm("fichier", "Fichier", []));
        if (shell.Current is ReceiptDetailPage receipt) tabs.Add(ReceiptContext(shell, receipt));
        if (shell.Current is StudentDetailPage student) tabs.Add(StudentContext(shell, student));

        return tabs.Where(t => t.Id == "fichier" || t.Groups.Count > 0).ToList();
    }

    private static RibbonGroupVm Group(string title, params RibbonCommandVm?[] commands) =>
        new(title, commands.Where(c => c is not null).Cast<RibbonCommandVm>().ToList());

    private static RibbonGroupVm RecentsGroup(ShellViewModel shell)
    {
        var items = shell.Recents
            .Select(r => new RibbonCommandVm(r.Label, Clock, $"Ouvrir {r.Label}.", new AsyncCommand(() => shell.Open(r.Open)), false, Neutral))
            .ToList();
        return new RibbonGroupVm("Récents", items);
    }

    private static IEnumerable<RibbonGroupVm> RecentsGroups(ShellViewModel shell) => shell.Recents.Count == 0 ? [] : [RecentsGroup(shell)];

    private static RibbonCommandVm? Large(ShellViewModel shell, string label, MaterialIconKind icon, string tip, Func<Task> action, string tone,
        string? perm = null, bool toggle = false, Func<bool>? isOn = null, string? shortcut = null) =>
        Make(shell, label, icon, tip, action, tone, true, perm, toggle, isOn, shortcut);

    private static RibbonCommandVm? Small(ShellViewModel shell, string label, MaterialIconKind icon, string tip, Func<Task> action, string tone,
        string? perm = null, bool toggle = false, Func<bool>? isOn = null, string? shortcut = null) =>
        Make(shell, label, icon, tip, action, tone, false, perm, toggle, isOn, shortcut);

    private static RibbonCommandVm? Make(ShellViewModel shell, string label, MaterialIconKind icon, string tip, Func<Task> action, string tone,
        bool large, string? perm, bool toggle, Func<bool>? isOn, string? shortcut)
    {
        if (perm is not null && !shell.Can(perm)) return null;
        var command = new AsyncCommand(() => shell.Run(action));
        var vm = new RibbonCommandVm(label, icon, shell.Prefs.Tips ? tip : string.Empty, command, large, tone, toggle, shortcut ?? Shortcuts.GetValueOrDefault(label));
        if (isOn is not null) vm.IsOn = isOn();
        return vm;
    }

    private static RibbonTabVm ReceiptContext(ShellViewModel shell, ReceiptDetailPage receipt)
    {
        var groups = new List<RibbonGroupVm>
        {
            Group("Reçu",
                Large(shell, "Imprimer", Printer, "Ouvre le reçu au format PDF A5, avec son QR de vérification.", () => receipt.PrintAsync(), Blue),
                Large(shell, "Annuler le reçu", Cancel, "Annule le reçu avec un motif. Un reçu n'est jamais supprimé.", () => receipt.CancelAsync(), Red, perm: Perm.PaymentsCancel),
                Small(shell, "Vérifier en ligne", ShieldCheckOutline, "Ouvre la page publique de vérification du reçu.", () => { receipt.OpenVerification(); return Task.CompletedTask; }, Neutral),
                Small(shell, "Journal des reçus", ReceiptTextOutline, "Retour à la liste des reçus.", () => shell.Open(() => new ReceiptsPage(shell)), Neutral)),
        };
        return new RibbonTabVm("outils-recu", "Reçu", groups, "Outils du reçu", Orange);
    }

    private static RibbonTabVm StudentContext(ShellViewModel shell, StudentDetailPage student)
    {
        var groups = new List<RibbonGroupVm>
        {
            Group("Encaissement",
                Large(shell, "Encaisser", HandCoin, "Encaisse un versement pour cet apprenant.", () => shell.Open(() => new CashierPage(shell, student.StudentId)), Green, perm: Perm.PaymentsCreate)),
            Group("Dossier",
                Large(shell, "Modifier", Pencil, "Modifie l'identité de l'apprenant.", () => student.EditAsync(), Blue, perm: Perm.StudentsWrite),
                Small(shell, "Relevé de compte", FileDocumentOutline, "Relevé PDF des frais et des paiements.", () => student.StatementAsync(), Neutral, perm: Perm.PaymentsRead),
                Small(shell, "Liste des apprenants", AccountGroupOutline, "Retour à la liste.", () => shell.Open(() => new StudentsPage(shell)), Neutral)),
        };
        return new RibbonTabVm("outils-apprenant", "Apprenant", groups, "Outils de l'apprenant", Purple);
    }

    private static void CycleTextSize(ShellViewModel shell)
    {
        var sizes = Preferences.TextSizes.Select(s => double.Parse(s.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var next = sizes.FindIndex(s => Math.Abs(s - shell.Prefs.Scale) < 0.01);
        shell.Prefs.Scale = sizes[(next + 1) % sizes.Count];
    }
}

/// <summary>Permissions attendues par les commandes, identiques à celles du serveur.</summary>
public static class Perm
{
    public const string StudentsRead = "apprenants.lire";
    public const string StudentsWrite = "apprenants.ecrire";
    public const string PaymentsRead = "paiements.lire";
    public const string PaymentsCreate = "paiements.encaisser";
    public const string PaymentsCancel = "paiements.annuler";
    public const string ExpensesWrite = "depenses.ecrire";
    public const string CashClose = "caisse.arreter";
    public const string ReportsRead = "rapports.lire";
    public const string SettingsWrite = "parametres.ecrire";
    public const string UsersManage = "utilisateurs.gerer";
    public const string AuditRead = "audit.lire";
    public const string BackupManage = "sauvegardes.gerer";
    public const string AcademicsWrite = "scolarite.ecrire";
    public const string DisciplineCancel = "discipline.annuler";
    public const string AcademicsRead = "scolarite.lire";
    public const string StaffManage = "personnel.gerer";
}
