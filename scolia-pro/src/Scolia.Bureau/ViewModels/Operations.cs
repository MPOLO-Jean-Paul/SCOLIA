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
        Action("Télécharger", MaterialIconKind.Download, DownloadAsync);
        Action("Restaurer", MaterialIconKind.BackupRestore, RestoreAsync);
        Action("Importer SCOLIA V79", MaterialIconKind.Upload, () => ImportAsync(Shell));
        Action("Vérifier la base", MaterialIconKind.DatabaseCheckOutline, () => CheckDatabaseAsync(Shell));
        ActionRefresh();
    }

    public override string Title => "Données et sauvegardes";
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

    /// <summary>Lit un classeur SCOLIA V79, montre le contenu détecté, puis l'importe après confirmation.</summary>
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
        var warnings = preview.Warnings.Count == 0 ? string.Empty : "\n\nAvertissements :\n" + string.Join('\n', preview.Warnings);
        var confirmed = await shell.Confirm("Importer le classeur V79", $"Contenu détecté dans {picked.Name} : {detected}.{warnings}\n\nUne sauvegarde est faite avant l'import.", "Importer");
        if (!confirmed) return;

        var applied = await shell.Api.UploadAsync<ImportReportDto>(HttpMethod.Post, "api/import/scolia?appliquer=true",
            [new UploadedFile("fichiers", picked.Name, picked.Data)]);
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
        Action("Exporter en CSV", MaterialIconKind.FileDownloadOutline, ExportAsync);
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

    public BackstageViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Items =
        [
            new BackstageItemVm("infos", "Informations", MaterialIconKind.InformationOutline, "ToneBlue"),
            new BackstageItemVm("mot-de-passe", "Mot de passe", MaterialIconKind.KeyOutline, "ToneOrange"),
            new BackstageItemVm("poste", "Poste et réseau", MaterialIconKind.LanConnect, "ToneGreen"),
            new BackstageItemVm("preferences", "Préférences", MaterialIconKind.TuneVariant, "TonePurple"),
            new BackstageItemVm("aide", "Aide et raccourcis", MaterialIconKind.KeyboardOutline, "ToneNeutral"),
        ];
        section = Items[0];
        Close = new RelayCommand(shell.CloseBackstage);
        Logout = new AsyncCommand(() => shell.LogoutAsync());
        PasswordForm = BuildPasswordForm();
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
                Raise(nameof(IsInfos));
                Raise(nameof(IsPassword));
                Raise(nameof(IsPoste));
            }
        }
    }

    public bool IsInfos => section?.Value == "infos";
    public bool IsPassword => section?.Value == "mot-de-passe";
    public bool IsPoste => section?.Value == "poste";
    public FormViewModel PasswordForm { get => passwordForm!; private set => Set(ref passwordForm, value); }

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
    public static Task<bool> ShowAsync(ShellViewModel shell)
    {
        var p = shell.Prefs;
        var form = new FormViewModel("Préférences", "Ces réglages s'appliquent à ce poste et sont conservés.", f =>
        {
            p.Theme = f.ChoiceValue("theme") ?? p.Theme;
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
    /// <summary>La liste est construite à partir des commandes du ruban : elle suit toujours les raccourcis réels.</summary>
    public static Task<bool> ShowAsync(ShellViewModel shell)
    {
        var lines = shell.ShortcutCommands.Select(c => $"{c.ShortcutText} : {c.Label}").Append("Échap : fermer la boîte de dialogue ouverte");
        return shell.Ask(new FormViewModel("Raccourcis clavier", "Les raccourcis fonctionnent dans toutes les pages.")
        {
            Message = string.Join(Environment.NewLine, lines),
            CancelText = "Fermer",
        });
    }
}

/// <summary>À propos de SCOLIA Pro.</summary>
public static class AboutDialog
{
    private const string Text = """
        SCOLIA Pro : gestion scolaire, encaissements, scolarité et paie.
        Le client natif et le serveur fonctionnent sur ce poste ou sur le réseau local de l'établissement.
        Les données restent chez l'établissement : SCOLIA n'envoie aucune donnée à un tiers.
        """;

    public static Task<bool> ShowAsync(ShellViewModel shell) =>
        shell.Ask(new FormViewModel("SCOLIA Pro", $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0"}") { Message = Text, CancelText = "Fermer" });
}
