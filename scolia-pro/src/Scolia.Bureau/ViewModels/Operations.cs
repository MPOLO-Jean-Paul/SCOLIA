using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Sauvegarde de la base, avec sa taille et sa date.</summary>
public sealed record BackupRowVm(string Name, string Created, string Size, BackupDto Source);

/// <summary>Données et sauvegardes : sauvegardes, restauration, contrôle d'intégrité et import du classeur V79.</summary>
public sealed class DataPage : PageViewModel
{
    private BackupRowVm? selected;
    private string subtitle = string.Empty;

    public DataPage(ShellViewModel shell) : base(shell)
    {
        Action("Sauvegarder", MaterialIconKind.ContentSaveOutline, () => BackupNowAsync(Shell), primary: true);
        Action("Télécharger", MaterialIconKind.Download, DownloadAsync, row: true);
        Action("Restaurer", MaterialIconKind.BackupRestore, RestoreAsync, row: true);
        Action("Importation du classeur", MaterialIconKind.Upload, () => ImportAsync(Shell));
        Action("Modèle d'import", MaterialIconKind.FileExcelOutline, () => TemplateAsync(Shell));
        Action("Vérifier la base", MaterialIconKind.DatabaseCheckOutline, () => CheckDatabaseAsync(Shell));
        if (Shell.Can(Perm.BackupManage))
        {
            Action("Effacer des paiements", MaterialIconKind.DeleteSweepOutline, () => Shell.Open(() => new EraseDataPage(Shell, payments: true)));
            Action("Effacer des dossiers", MaterialIconKind.AccountRemoveOutline, () => Shell.Open(() => new EraseDataPage(Shell, payments: false)));
        }
        ActionRefresh();
    }

    public override string Title => "Sauvegardes";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.DatabaseExportOutline;

    public ObservableCollection<BackupRowVm> Items { get; } = [];
    public BackupRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var backups = await Shell.Api.GetAsync<List<BackupDto>>("api/sauvegardes");
        Items.Clear();
        foreach (var b in backups.OrderByDescending(b => b.CreatedUtc))
            Items.Add(new BackupRowVm(b.Name, Format.DateTime(b.CreatedUtc), Size(b.SizeBytes), b));
        subtitle = $"{Format.Count(backups.Count, "sauvegarde", "sauvegardes")} · conservées 30 jours, au moins 10 fichiers";
        Raise(nameof(Subtitle));
    }

    private static string Size(long bytes) => bytes >= 1_048_576
        ? $"{bytes / 1_048_576.0:0.0} Mo"
        : $"{bytes / 1024.0:0} Ko";

    private async Task DownloadAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez une sauvegarde à télécharger.");
            return;
        }
        var name = Selected.Name;
        await Shell.Download($"api/sauvegardes/{Uri.EscapeDataString(name)}", name);
    }

    private async Task RestoreAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez la sauvegarde à restaurer.");
            return;
        }
        var name = Selected.Name;
        var confirmed = await Shell.Confirm("Restaurer la sauvegarde",
            $"La base sera remplacée par « {name} ». Les données saisies après cette sauvegarde seront perdues. Une copie de la base actuelle est conservée avant.",
            "Restaurer", danger: true);
        if (!confirmed) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/sauvegardes/{Uri.EscapeDataString(name)}/restauration");
        Shell.Notify("Base restaurée. Reconnectez-vous.");
        await Shell.LogoutAsync();
    }

    /// <summary>Crée tout de suite une sauvegarde vérifiée.</summary>
    public static async Task BackupNowAsync(ShellViewModel shell)
    {
        await shell.Api.SendAsync(HttpMethod.Post, "api/sauvegardes");
        shell.Notify("Sauvegarde créée et vérifiée.");
        if (shell.Current is DataPage page) await page.RefreshAsync();
    }

    /// <summary>Contrôle d'intégrité de la base de données en cours d'utilisation.</summary>
    public static async Task CheckDatabaseAsync(ShellViewModel shell)
    {
        var result = await shell.Api.GetAsync<IntegrityDto>("api/sauvegardes/integrite");
        await shell.Confirm("Vérification de la base",
            result.Intacte
                ? "La base est intacte : aucune erreur d'intégrité n'a été trouvée."
                : "La base signale des erreurs d'intégrité. Sauvegardez-la et prévenez le responsable informatique.",
            "Fermer");
    }

    /// <summary>Importe les apprenants d'un classeur ; paiements et coordonnées sont facultatifs.</summary>
    /// <summary>Classeur vide au format de l'import, pour reprendre des données tenues dans un autre fichier Excel.</summary>
    public static Task TemplateAsync(ShellViewModel shell) => shell.Download("api/import/modele", "modele-import-scolia.xlsx");

    public static async Task ImportAsync(ShellViewModel shell)
    {
        if (shell.PickFileHandler is null) return;
        var picked = await shell.PickFileHandler([".xlsm", ".xlsx"]);
        if (picked is null) return;

        var preview = await shell.Api.UploadAsync<ImportReportDto>(HttpMethod.Post, "api/import/scolia?appliquer=false",
            [new UploadedFile("fichiers", picked.Name, picked.Data)]);
        if (preview.Errors.Count > 0)
        {
            await shell.Confirm("Import impossible", string.Join('\n', preview.Errors), "Fermer");
            return;
        }

        var detected = $"Promotions : {preview.Promotions} · apprenants : {preview.Students} · reçus : {preview.Receipts} · imputations : {preview.Allocations}";
        // Les détails nombreux restent limités à l'aperçu. Aucune liste de contacts
        // absents ne doit cacher le bouton Importer sur un petit écran.
        var warnings = preview.Warnings.Count == 0 ? string.Empty
            : "\n\nÀ vérifier :\n" + string.Join('\n', preview.Warnings.Take(6))
              + (preview.Warnings.Count > 6 ? $"\n… et {preview.Warnings.Count - 6} autre(s) remarque(s)." : string.Empty);
        var message = $"Contenu détecté dans {picked.Name} : {detected}.\n\n"
            + "Les dates, téléphones, mensualités, notes, reçus et paiements peuvent être complétés après l'importation."
            + warnings + "\n\nUne sauvegarde est faite avant l'import.";
        var confirmed = await shell.Confirm("Importation du classeur", message, "Importer");
        if (!confirmed) return;

        var applied = await shell.Api.UploadAsync<ImportReportDto>(HttpMethod.Post, "api/import/scolia?appliquer=true",
            [new UploadedFile("fichiers", picked.Name, picked.Data)]);
        if (!applied.Applied || applied.Errors.Count > 0)
        {
            await shell.Confirm("Importation non appliquée",
                "Aucune nouvelle donnée n'a été enregistrée. Corrigez les erreurs suivantes :\n"
                + string.Join('\n', applied.Errors.Take(8)), "Fermer");
            return;
        }
        shell.Notify($"Import terminé : {applied.Students} apprenant(s), {applied.Receipts} reçu(s).");
        if (shell.Current is DataPage page) await page.RefreshAsync();
    }
}

/// <summary>Ligne du journal d'audit.</summary>
public sealed record AuditRowVm(string At, string User, string Action, string Entity, string Summary, string Ip);

/// <summary>Journal d'audit : qui a fait quoi, filtrable, avec contrôle de la chaîne et export CSV.</summary>
public sealed class AuditPage : PageViewModel
{
    private string search = string.Empty;
    private string entity = string.Empty;
    private DateTime? from;
    private DateTime? to;
    private string subtitle = string.Empty;
    private DispatcherTimer? timer;

    public AuditPage(ShellViewModel shell) : base(shell)
    {
        Action("Vérifier le journal", MaterialIconKind.ShieldCheckOutline, () => VerifyAsync(Shell));
        Action("Exporter en CSV", MaterialIconKind.FileExportOutline, ExportAsync);
        ActionRefresh();
    }

    public override string Title => "Journal d'audit";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.FileDocumentOutline;

    public ObservableCollection<AuditRowVm> Items { get; } = [];

    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            timer?.Stop();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (_, _) => { timer?.Stop(); await Shell.Run(RefreshAsync); };
            timer.Start();
        }
    }

    /// <summary>Type d'objet concerné : Paiement, Apprenant, Utilisateur...</summary>
    public string Entity { get => entity; set { if (Set(ref entity, value)) _ = Shell.Run(RefreshAsync); } }
    public DateTime? From { get => from; set { if (Set(ref from, value)) _ = Shell.Run(RefreshAsync); } }
    public DateTime? To { get => to; set { if (Set(ref to, value)) _ = Shell.Run(RefreshAsync); } }

    public override async Task LoadAsync()
    {
        var page = await Shell.Api.GetAsync<PagedResult<AuditDto>>("api/audit" + Filters(1, 100));
        Items.Clear();
        foreach (var a in page.Items)
            Items.Add(new AuditRowVm(Format.DateTime(a.AtUtc), a.UserName, a.Action, a.EntityType, a.Summary, a.IpAddress ?? "—"));
        subtitle = $"{Format.Count(page.Total, "événement", "événements")}" + (page.Total > 100 ? " · 100 plus récents affichés" : string.Empty);
        Raise(nameof(Subtitle));
    }

    private string Filters(int page, int size) => ScoliaClient.Query(
        ("q", Search.Trim()), ("type", Entity.Trim()), ("du", From is null ? null : (DateOnly?)ToDay(From)), ("au", To is null ? null : (DateOnly?)ToDay(To)),
        ("page", page), ("taille", size));

    private async Task ExportAsync() => await Shell.Download($"api/audit/export{Filters(1, 100)}&format=csv", "journal-audit.csv");

    /// <summary>Contrôle la chaîne d'empreintes : toute modification d'une ligne la rend visible.</summary>
    public static async Task VerifyAsync(ShellViewModel shell)
    {
        var result = await shell.Api.GetAsync<AuditVerificationDto>("api/audit/verification");
        await shell.Confirm("Vérification du journal",
            result.IsIntact
                ? "La chaîne d'empreintes est intacte : aucune ligne du journal n'a été modifiée."
                : "La chaîne d'empreintes est rompue : une ligne du journal a été modifiée. Prévenez le responsable.",
            "Fermer");
    }
}

/// <summary>Recherche globale : apprenants, reçus, pages. Ouvre le résultat choisi.</summary>
public sealed class SearchViewModel : Observable
{
    private readonly ShellViewModel shell;
    private DispatcherTimer? timer;
    private string query = string.Empty;
    private SearchHitRow? selected;
    private string message = "Tapez au moins deux caractères : nom, matricule, numéro de reçu ou nom d'une page.";

    public SearchViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Open = new AsyncCommand(OpenSelectedAsync);
        Close = new RelayCommand(shell.CloseDialog);
    }

    public string Query { get => query; set { if (Set(ref query, value)) Schedule(); } }
    public string Message { get => message; private set => Set(ref message, value); }
    public ObservableCollection<SearchHitRow> Results { get; } = [];
    public SearchHitRow? Selected { get => selected; set => Set(ref selected, value); }
    public AsyncCommand Open { get; }
    public RelayCommand Close { get; }

    private void Schedule()
    {
        timer?.Stop();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { timer?.Stop(); await shell.Run(SearchAsync); };
        timer.Start();
    }

    public async Task SearchAsync()
    {
        Results.Clear();
        Selected = null;
        if (Query.Trim().Length < 2)
        {
            Message = "Tapez au moins deux caractères : nom, matricule, numéro de reçu ou nom d'une page.";
            return;
        }
        var hits = await shell.Api.GetAsync<List<SearchHitDto>>("api/recherche" + ScoliaClient.Query(("q", Query.Trim())));
        foreach (var h in hits) Results.Add(new SearchHitRow(h));
        Selected = Results.FirstOrDefault();
        Message = hits.Count == 0 ? "Aucun résultat." : Format.Count(hits.Count, "résultat", "résultats");
    }

    private async Task OpenSelectedAsync()
    {
        if (Selected is null) return;
        var target = PageFor(Selected.Hit.Url);
        shell.CloseDialog();
        if (target is null)
        {
            shell.Notify("Cette commande s'ouvre depuis le ruban.");
            return;
        }
        await shell.Open(target);
    }

    /// <summary>Traduit l'adresse d'un résultat en écran natif.</summary>
    private Func<PageViewModel>? PageFor(string url)
    {
        var parts = url.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return () => new DashboardPage(shell);
        return parts[0] switch
        {
            "apprenants" when parts.Length > 1 && Guid.TryParse(parts[1], out var id) => () => new StudentDetailPage(shell, id),
            "apprenants" => () => new StudentsPage(shell),
            "paiements" when parts.Length > 2 && Guid.TryParse(parts[2], out var rid) => () => new ReceiptDetailPage(shell, rid),
            "paiements" => () => new ReceiptsPage(shell),
            "encaisser" => () => new CashierPage(shell),
            "recouvrement" => () => new DebtorsPage(shell),
            "prises-en-charge" => () => new SponsorshipsPage(shell),
            "caisse" => () => new CashPage(shell),
            "depenses" => () => new ExpensesPage(shell),
            "reinscriptions" => () => new ReEnrollmentPage(shell),
            "notes" => () => new GradesPage(shell),
            "presences" => () => new AttendancePage(shell),
            "discipline" => () => new DisciplinePage(shell),
            "personnel" => () => new StaffPage(shell),
            "parametres" => () => new SettingsPage(shell),
            "utilisateurs" => () => new UsersPage(shell),
            "donnees" => () => new DataPage(shell),
            "audit" => () => new AuditPage(shell),
            _ => null,
        };
    }
}

/// <summary>Résultat de recherche dans la liste, avec son libellé de type.</summary>
public sealed class SearchHitRow
{
    public SearchHitRow(SearchHitDto hit) => Hit = hit;

    public SearchHitDto Hit { get; }
    public string Type => Hit.Type;
    public string Title => Hit.Title;
    public string Subtitle => Hit.Subtitle;

    /// <summary>Icône du résultat, la même que celle de son outil dans le volet latéral.</summary>
    public MaterialIconKind Icon => Hit.Type == "Reçu" ? MaterialIconKind.ReceiptText : MaterialIconKind.AccountGroup;
    public IBrush Brush => ThemeBrush.Get(Hit.Type == "Reçu" ? "ToneOrange" : "ToneBlue");
}

/// <summary>Élément de la colonne de gauche du menu Fichier.</summary>
/// <summary>Rubrique du menu Fichier. La tuile d'icône prend la couleur de sa tonalité, comme dans les paramètres Android.</summary>
public sealed record BackstageItemVm(string Value, string Label, MaterialIconKind Icon, string Tone)
{
    public IBrush Brush => ThemeBrush.Get(Tone);
}

/// <summary>Menu Fichier : informations, mot de passe, préférences, aide et déconnexion.</summary>
public sealed class BackstageViewModel : Observable
{
    private readonly ShellViewModel shell;
    private BackstageItemVm? section;
    private FormViewModel? passwordForm;
    private PosteViewModel? poste;
    private string securityPassword = string.Empty;
    private string securityNotice = string.Empty;

    public BackstageViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Items =
        [
            new BackstageItemVm("infos", "Informations", MaterialIconKind.Information, "ToneBlue"),
            new BackstageItemVm("mot-de-passe", "Sécurité", MaterialIconKind.ShieldLockOutline, "ToneOrange"),
            new BackstageItemVm("poste", "Poste et réseau", MaterialIconKind.LanConnect, "ToneGreen"),
            new BackstageItemVm("preferences", "Préférences", MaterialIconKind.TuneVariant, "TonePurple"),
            Preferences.MobileLayout
                ? new BackstageItemVm("aide", "Aide et gestes", MaterialIconKind.GestureTap, "ToneNeutral")
                : new BackstageItemVm("aide", "Aide et raccourcis", MaterialIconKind.Keyboard, "ToneNeutral"),
        ];
        section = Items[0];
        Close = new RelayCommand(shell.CloseBackstage);
        Logout = new AsyncCommand(() => shell.LogoutAsync());
        PasswordForm = BuildPasswordForm();
        ManageBiometrics = new AsyncCommand(ManageBiometricsAsync);
    }

    public IReadOnlyList<BackstageItemVm> Items { get; }

    /// <summary>Rubrique affichée. Préférences et aide ouvrent leur boîte, sans quitter les informations.</summary>
    public BackstageItemVm? Section
    {
        get => section;
        set
        {
            if (value is null) return;
            switch (value.Value)
            {
                case "preferences":
                    _ = PreferencesDialog.ShowAsync(shell);
                    return;
                case "aide":
                    _ = ShortcutsDialog.ShowAsync(shell);
                    return;
            }
            if (Set(ref section, value))
            {
                if (value.Value == "mot-de-passe")
                {
                    PasswordForm = BuildPasswordForm();
                    SecurityPassword = string.Empty;
                    SecurityNotice = string.Empty;
                    RefreshSecurity();
                }
                Raise(nameof(IsInfos));
                Raise(nameof(IsPassword));
                Raise(nameof(IsPoste));
                Raise(nameof(ShowList));
                Raise(nameof(ShowDetail));
                Raise(nameof(DetailTitle));
            }
        }
    }

    public bool IsInfos => section?.Value == "infos";
    /// <summary>Mobile : la liste des rubriques s'affiche tant qu'aucune rubrique à page propre n'est ouverte.</summary>
    public bool ShowList => !IsPassword && !IsPoste;
    public bool ShowDetail => !ShowList;
    public string DetailTitle => IsPassword ? "Sécurité du compte" : IsPoste ? "Poste et réseau" : string.Empty;
    public string Initials => string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
    /// <summary>Mobile : revient de la rubrique ouverte à la liste des rubriques.</summary>
    public RelayCommand BackToList => backToList ??= new RelayCommand(() => Section = Items[0]);
    private RelayCommand? backToList;
    /// <summary>Mobile : ouvre une rubrique depuis sa ligne.</summary>
    public RelayCommand<BackstageItemVm> OpenItem => openItem ??= new RelayCommand<BackstageItemVm>(item => Section = item);
    private RelayCommand<BackstageItemVm>? openItem;
    public bool IsPassword => section?.Value == "mot-de-passe";
    public bool IsPoste => section?.Value == "poste";
    public FormViewModel PasswordForm { get => passwordForm!; private set => Set(ref passwordForm, value); }

    public bool ShowBiometricSecurity => Platform.IsMobile;
    private string? BiometricAccount => BiometricAccess.ReadAccount(shell.Api.Server);
    public bool BiometricsEnabled => ShowBiometricSecurity && (Platform.HasBiometricProof?.Invoke() ?? false)
        && BiometricAccount is { } account && shell.Session is { } session
        && BiometricAccess.AccountMatches(account, session.UserName);
    public bool OtherBiometricAccount => BiometricAccount is { } account && shell.Session is { } session
        && !BiometricAccess.AccountMatches(account, session.UserName);
    public string BiometricSecurityStatus => OtherBiometricAccount
        ? "Une autre session est associée à la biométrie de ce téléphone. Reconnectez-vous avec le compte concerné pour la modifier."
        : BiometricsEnabled
            ? "Empreinte activée pour ce compte sur ce téléphone. Chaque utilisation demande une validation Android."
            : Platform.IsBiometricAvailable?.Invoke() == true
                ? "Empreinte disponible. Son activation exige le mot de passe du compte et votre accord."
                : "Empreinte indisponible sur cet appareil ou non configurée dans Android.";
    public string BiometricActionLabel => BiometricsEnabled ? "Désactiver l'empreinte" : "Activer l'empreinte";
    public bool BiometricSecurityAllowed => ShowBiometricSecurity && !OtherBiometricAccount
        && (BiometricsEnabled || Platform.IsBiometricAvailable?.Invoke() == true);
    public string SecurityPassword { get => securityPassword; set => Set(ref securityPassword, value); }
    public string SecurityNotice { get => securityNotice; private set => Set(ref securityNotice, value); }
    public bool HasSecurityNotice => SecurityNotice.Length > 0;
    public AsyncCommand ManageBiometrics { get; }
    public RelayCommand CloseSecurity => closeSecurity ??= new RelayCommand(() => Section = Items[0]);
    private RelayCommand? closeSecurity;

    public void RefreshSecurity()
    {
        Raise(nameof(BiometricsEnabled));
        Raise(nameof(OtherBiometricAccount));
        Raise(nameof(BiometricSecurityStatus));
        Raise(nameof(BiometricActionLabel));
        Raise(nameof(BiometricSecurityAllowed));
    }

    /// <summary>Chaque changement sensible exige une preuve fraîche, jamais la seule session ouverte.</summary>
    private async Task ManageBiometricsAsync()
    {
        if (shell.Session is not { } session || !ShowBiometricSecurity) return;
        SecurityNotice = string.Empty;
        if (OtherBiometricAccount)
        {
            SecurityNotice = "Ce téléphone est déjà associé à un autre compte : déconnectez-vous et utilisez ce compte.";
            return;
        }

        var enabled = BiometricsEnabled;
        var verified = false;
        if (enabled && Platform.VerifyBiometricAsync is { } verify)
            verified = await verify();

        // Activer requiert systématiquement le mot de passe SCOLIA ; désactiver
        // admet soit une preuve Android fraîche, soit le mot de passe du même compte.
        if (!enabled || !verified)
        {
            if (string.IsNullOrWhiteSpace(SecurityPassword))
            {
                SecurityNotice = enabled
                    ? "Confirmez votre empreinte ou saisissez le mot de passe actuel pour désactiver."
                    : "Saisissez votre mot de passe actuel pour activer l'empreinte.";
                return;
            }
            try
            {
                var authenticated = await shell.Api.LoginAsync(session.UserName, SecurityPassword);
                verified = BiometricAccess.AccountMatches(authenticated.UserName, session.UserName);
            }
            catch (Api.ApiException)
            {
                SecurityNotice = "Mot de passe incorrect. Aucun réglage biométrique n'a été modifié.";
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                SecurityNotice = "Le serveur ne peut pas vérifier votre mot de passe. Réessayez lorsque la connexion est rétablie.";
                return;
            }
        }
        SecurityPassword = string.Empty;
        if (!verified) { SecurityNotice = "Vérification refusée. Aucun réglage n'a été modifié."; return; }

        if (enabled)
        {
            Platform.DisableBiometricAccess?.Invoke();
            BiometricAccess.Disable();
            SecurityNotice = "Connexion par empreinte désactivée après vérification.";
        }
        else if (Platform.EnrollBiometricAsync is { } enroll && await enroll())
        {
            BiometricAccess.Enable(shell.Api.Server, session.UserName);
            SecurityNotice = "Empreinte activée pour votre compte sur ce téléphone.";
        }
        else SecurityNotice = "Activation annulée ou indisponible : le mot de passe reste nécessaire.";

        Raise(nameof(HasSecurityNotice));
        RefreshSecurity();
        shell.Login.RefreshBiometrics();
    }

    /// <summary>Rubrique poste et réseau, créée à la première ouverture.</summary>
    public PosteViewModel PosteSection => poste ??= new PosteViewModel(shell);
    public string UserName => shell.Session?.DisplayName ?? string.Empty;
    public string Role => shell.Session?.RoleLabel ?? string.Empty;
    public string School => shell.Session?.SchoolName ?? string.Empty;
    public string Year => shell.Session?.CurrentYear ?? "non définie";
    public string Server => shell.ServerLine;
    public string Version => $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0"}";
    public RelayCommand Close { get; }
    public AsyncCommand Logout { get; }

    private FormViewModel BuildPasswordForm()
    {
        var form = new FormViewModel("Changer le mot de passe", "Le nouveau mot de passe remplace le provisoire.", async f =>
        {
            if (f.Text("nouveau") != f.Text("confirmation"))
            {
                f.Find("confirmation").Error = "Les deux mots de passe ne correspondent pas.";
                return false;
            }
            await shell.Api.SendAsync(HttpMethod.Post, "api/session/mot-de-passe", new { currentPassword = f.Text("actuel"), newPassword = f.Text("nouveau") });
            shell.Notify("Mot de passe modifié.");
            PasswordForm = BuildPasswordForm();
            shell.CloseBackstage();
            return true;
        })
        { SubmitText = "Enregistrer" };
        form.Add("actuel", "Mot de passe actuel", FieldKind.Secret, required: true);
        form.Add("nouveau", "Nouveau mot de passe", FieldKind.Secret, required: true);
        form.Add("confirmation", "Confirmer", FieldKind.Secret, required: true);
        return form;
    }
}

/// <summary>Préférences d'affichage : thème, taille du texte, densité, barre d'état, info-bulles, quadrillage, animations.</summary>
public static class PreferencesDialog
{
    /// <summary>Les préférences vivent dans Paramètres, rubriques de ce poste : la commande y mène directement.</summary>
    public static async Task<bool> ShowAsync(ShellViewModel shell)
    {
        shell.CloseBackstage();
        await shell.Open(() => new SettingsPage(shell, "apparence"));
        return true;
    }

    /// <summary>Ancienne boîte de préférences, gardée pour l'outil de captures.</summary>
    public static Task<bool> ShowDialogAsync(ShellViewModel shell)
    {
        var p = shell.Prefs;
        var form = new FormViewModel("Préférences", "Ces réglages s'appliquent à ce poste et sont conservés.", f =>
        {
            p.Theme = f.ChoiceValue("theme") ?? p.Theme;
            p.Accent = f.ChoiceValue("accent") ?? p.Accent;
            p.Scale = f.ChoiceValue("taille") is { } size ? double.Parse(size, CultureInfo.InvariantCulture) : p.Scale;
            p.Compact = f.Flag("compact");
            p.StatusBar = f.Flag("barre");
            p.Tips = f.Flag("bulles");
            p.Grid = f.Flag("quadrillage");
            p.Animations = f.Flag("animations");
            return Task.FromResult(true);
        })
        { SubmitText = "Appliquer" };
        form.AddChoice("theme", "Thème", Preferences.Themes, p.Theme);
        form.AddChoice("accent", "Couleur d'accent", Preferences.Accents, p.Accent);
        form.AddChoice("taille", "Taille du texte", Preferences.TextSizes, p.Scale.ToString("0.##", CultureInfo.InvariantCulture));
        form.AddFlag("compact", "Densité compacte", p.Compact);
        form.AddFlag("barre", "Barre d'état", p.StatusBar);
        form.AddFlag("bulles", "Info-bulles", p.Tips);
        form.AddFlag("quadrillage", "Quadrillage des tableaux", p.Grid);
        form.AddFlag("animations", "Animations", p.Animations);
        return shell.Ask(form);
    }
}

/// <summary>Raccourcis clavier de SCOLIA.</summary>
public static class ShortcutsDialog
{
    public static Task<bool> ShowAsync(ShellViewModel shell)
    {
        shell.ShowShortcuts();
        return Task.FromResult(true);
    }
}

/// <summary>Raccourci de la fenêtre d'aide : les touches, la commande et ce qu'elle fait.</summary>
public sealed record ShortcutRowVm(string Keys, string Label, string? Tip);

/// <summary>Rubrique de raccourcis : un onglet du ruban et ses commandes.</summary>
public sealed record ShortcutGroupVm(string Title, IReadOnlyList<ShortcutRowVm> Rows);

/// <summary>
/// Aide des raccourcis clavier : rangés par onglet, avec les touches en pastilles. La liste est construite à partir
/// des commandes du ruban : elle suit toujours les raccourcis réels.
/// </summary>
public sealed class ShortcutsViewModel
{
    public ShortcutsViewModel(ShellViewModel shell)
    {
        Close = new RelayCommand(shell.CloseDialog);
        var seen = new HashSet<string>(["Ctrl+K", "Échap", "Entrée", "Maj+F10"], StringComparer.Ordinal);
        var groups = new List<ShortcutGroupVm>
        {
            new("Partout", [
                new("Ctrl+K", "Rechercher", "Un apprenant, un reçu ou une page."),
                new("Échap", "Fermer", "La fenêtre ou le menu ouvert."),
                new("Entrée", "Ouvrir la ligne choisie", "Dans tous les tableaux."),
                new("Maj+F10", "Menu de la ligne", "Comme le clic droit."),
            ]),
        };
        foreach (var tab in shell.Tabs.Where(t => !t.IsFile))
        {
            var rows = tab.Groups.SelectMany(g => g.Commands)
                .Where(c => c.ShortcutText is not null && seen.Add(c.ShortcutText))
                .Select(c => new ShortcutRowVm(c.ShortcutText!, c.Label, c.Tip?.Split(Environment.NewLine)[0]))
                .ToList();
            if (rows.Count > 0) groups.Add(new ShortcutGroupVm(tab.Title, rows));
        }
        Groups = groups;
    }

    public IReadOnlyList<ShortcutGroupVm> Groups { get; }
    public RelayCommand Close { get; }
}

/// <summary>À propos de SCOLIA Pro : la fenêtre de présentation, avec l'éditeur et le développeur.</summary>
public static class AboutDialog
{
    public static Task<bool> ShowAsync(ShellViewModel shell)
    {
        shell.ShowAbout();
        return Task.FromResult(true);
    }
}

/// <summary>
/// Fenêtre « À propos » : le logo, la version, ce que fait SCOLIA, puis l'éditeur Jp-Services et les coordonnées
/// du développeur, comme dans la version 79, avec de quoi l'appeler, lui écrire ou copier ses coordonnées.
/// </summary>
public sealed class AboutViewModel : Observable
{
    private readonly ShellViewModel shell;

    public AboutViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Close = new RelayCommand(shell.CloseDialog);
        WriteEmail = new RelayCommand(() => Files.Open($"mailto:{AboutInfo.Email}?subject=SCOLIA%20Pro%20{AboutInfo.Version}"));
        OpenWhatsApp = new RelayCommand(() => Files.Open($"https://wa.me/{AboutInfo.PhoneDigits}"));
        CopyContacts = new AsyncCommand(CopyAsync);
    }

    public string Title => "SCOLIA Pro";
    public string Tagline => AboutInfo.Tagline;
    public string VersionLine => $"Version {AboutInfo.Version}";
    public string Description => AboutInfo.Description;
    public IReadOnlyList<string> Modules => AboutInfo.Modules;
    public string Publisher => AboutInfo.Publisher;
    public string Developer => AboutInfo.Developer;
    public string Role => AboutInfo.Role;
    public string Phone => AboutInfo.Phone;
    public string Email => AboutInfo.Email;
    public string Copyright => AboutInfo.Copyright;
    public string Accounts => "Deux comptes : Secrétariat, qui administre SCOLIA, et Comptabilité.";

    public RelayCommand Close { get; }
    public RelayCommand WriteEmail { get; }
    public RelayCommand OpenWhatsApp { get; }
    public AsyncCommand CopyContacts { get; }

    private async Task CopyAsync()
    {
        var text = $"SCOLIA Pro {AboutInfo.Version} · {AboutInfo.Publisher}{Environment.NewLine}{AboutInfo.Developer}{Environment.NewLine}{AboutInfo.Phone}{Environment.NewLine}{AboutInfo.Email}";
        if (shell.CopyHandler is { } copy) await copy(text);
        shell.Notify("Coordonnées du développeur copiées.");
    }
}
