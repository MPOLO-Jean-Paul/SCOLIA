using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Windows.Input;
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
        Action("Sauvegarder", MaterialIconKind.ContentSave, () => BackupNowAsync(Shell), primary: true);
        Action("Télécharger", MaterialIconKind.Download, DownloadAsync, row: true);
        Action("Exporter chiffré", MaterialIconKind.LockOutline, ExportEncryptedAsync, row: true);
        Action("Importer chiffré", MaterialIconKind.Upload, ImportEncryptedAsync);
        Action("Restaurer", MaterialIconKind.BackupRestore, RestoreAsync, row: true);
        Action("Importation du classeur", MaterialIconKind.Upload, () => ImportAsync(Shell));
        Action("Modèle d'import", MaterialIconKind.FileExcel, () => TemplateAsync(Shell));
        Action("Vérifier la base", MaterialIconKind.DatabaseCheck, () => CheckDatabaseAsync(Shell));
        if (Shell.Can(Perm.BackupManage))
        {
            Action("Effacer des paiements", MaterialIconKind.DeleteSweep, () => Shell.Open(() => new EraseDataPage(Shell, payments: true)));
            Action("Effacer des dossiers", MaterialIconKind.AccountRemove, () => Shell.Open(() => new EraseDataPage(Shell, payments: false)));
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
        subtitle = $"{Format.Count(backups.Count, "sauvegarde", "sauvegardes")} · gardées 30 jours";
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

    private async Task ExportEncryptedAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Choisissez d'abord une sauvegarde à protéger.");
            return;
        }
        var name = Selected.Name;
        var form = new FormViewModel("Exporter une sauvegarde chiffrée",
            "Une phrase secrète de 12 caractères minimum protégera l'archive portable. Elle ne sera pas enregistrée dans SCOLIA.",
            async f =>
            {
                var passphrase = f.Text("passphrase");
                if (passphrase.Length < 12)
                {
                    f.Find("passphrase").Error = "Indiquez au moins 12 caractères.";
                    return false;
                }
                var (data, _) = await Shell.Api.PostDocumentAsync(
                    $"api/sauvegardes/{Uri.EscapeDataString(name)}/export-chiffre", new { passphrase });
                var location = Files.Save(data, name + ".scolia-enc");
                Shell.Notify($"Archive chiffrée enregistrée : {location}");
                return true;
            }) { SubmitText = "Exporter chiffré" };
        form.Add("passphrase", "Phrase secrète de l'archive", FieldKind.Secret, required: true,
            hint: "Conservez cette phrase secrète séparément du fichier ; sans elle, l'archive ne pourra pas être restaurée.");
        await Shell.Ask(form);
    }

    private async Task ImportEncryptedAsync()
    {
        if (Shell.PickFileHandler is null) return;
        var picked = await Shell.PickFileHandler([".scolia-enc"]);
        if (picked is null) return;
        var form = new FormViewModel("Importer une sauvegarde chiffrée",
            "L'archive sera déchiffrée et vérifiée dans une nouvelle copie. La base actuelle restera inchangée.",
            async f =>
            {
                var passphrase = f.Text("passphrase");
                if (passphrase.Length < 12)
                {
                    f.Find("passphrase").Error = "Indiquez au moins 12 caractères.";
                    return false;
                }
                var result = await Shell.Api.UploadWithFieldsAsync<BackupDto>(HttpMethod.Post,
                    "api/sauvegardes/import-chiffre",
                    [new UploadedFile("archive", picked.Name, picked.Data)],
                    new Dictionary<string, string> { ["passphrase"] = passphrase });
                Shell.Notify($"Archive validée et importée : {result.Name}. Vous pourrez la restaurer après vérification.");
                await RefreshAsync();
                return true;
            }) { SubmitText = "Importer et vérifier" };
        form.Add("passphrase", "Phrase secrète de l'archive", FieldKind.Secret, required: true);
        await Shell.Ask(form);
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

        var detected = $"Promotions : {preview.Promotions} · {shell.StudentTerms.Plural} : {preview.Students} · reçus : {preview.Receipts} · imputations : {preview.Allocations}";
        // Les détails nombreux restent limités à l'aperçu. Aucune liste de contacts
        // absents ne doit cacher le bouton Importer sur un petit écran.
        // Le bilan dit combien d'apprenants ont été lus et combien seront créés : aucun ne disparaît sans explication.
        var bilan = preview.Warnings.FirstOrDefault(w => w.StartsWith("Bilan :", StringComparison.Ordinal));
        var remarks = preview.Warnings.Where(w => !ReferenceEquals(w, bilan)).ToList();
        var warnings = remarks.Count == 0 ? string.Empty
            : "\n\nÀ vérifier :\n" + string.Join('\n', remarks.Take(6))
              + (remarks.Count > 6 ? $"\n… et {remarks.Count - 6} autre(s) remarque(s)." : string.Empty);
        var message = $"Contenu détecté dans {picked.Name} : {detected}.\n\n"
            + (bilan is null ? string.Empty : bilan + "\n\n")
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
        shell.Notify($"Import terminé : {applied.Students} {(applied.Students == 1 ? shell.StudentTerms.Singular : shell.StudentTerms.Plural)}, {applied.Receipts} reçu(s).");
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
        Action("Vérifier le journal", MaterialIconKind.ShieldCheck, () => VerifyAsync(Shell));
        Action("Exporter en CSV", MaterialIconKind.FileExport, ExportAsync);
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
            Items.Add(new AuditRowVm(Format.DateTime(a.AtUtc), a.UserName, Labels.AuditAction(a.Action), a.EntityType, a.Summary, a.IpAddress ?? "—"));
        subtitle = $"{Format.Count(page.Total, "événement", "événements")}" + (page.Total > 100 ? " · 100 affichés" : string.Empty);
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
    private readonly bool settingsOnly;
    private DispatcherTimer? timer;
    private string query = string.Empty;
    private SearchHitRow? selected;
    private string message;

    public SearchViewModel(ShellViewModel shell, bool settingsOnly = false)
    {
        this.shell = shell;
        this.settingsOnly = settingsOnly;
        message = settingsOnly
            ? "Cherchez un réglage de l'application, sans consulter les données scolaires."
            : $"Recherche générale : {shell.StudentTerms.Plural}, reçus, fonctionnalités et paramètres autorisés.";
        Open = new AsyncCommand(OpenSelectedAsync);
        Close = new RelayCommand(shell.CloseDialog);
        ClearQuery = new RelayCommand(() => Query = string.Empty);
        ClearRecent = new RelayCommand(() =>
        {
            foreach (var text in shell.Prefs.RecentSearches.ToList()) shell.Prefs.ForgetSearch(text);
            RefreshRecent();
        });
        RefreshRecent();
        // Accès rapides : les outils autorisés de ce compte, en tuiles colorées comme « Découvrir » du Play Store.
        Shortcuts = settingsOnly ? [] : shell.Navigation.SelectMany(g => g.Items)
            .Where(i => i.PageType != typeof(HomePage)).Take(8)
            .Select(i => new SearchShortcutRow(i.TabLabel, i.Icon, i.Brush, new RelayCommand(() =>
            {
                shell.CloseDialog();
                if (i.Open.CanExecute(null)) i.Open.Execute(null);
            }))).ToList();
    }

    /// <summary>Champ vide : l'écran montre les recherches récentes et les accès rapides, comme le Play Store.</summary>
    public bool IsIdle => query.Trim().Length < 2;
    public bool HasResults => !IsIdle;
    public bool HasQuery => query.Length > 0;
    public RelayCommand ClearQuery { get; }
    public RelayCommand ClearRecent { get; }

    // ---------- Voix : le micro du champ, comme dans Google ----------
    public bool VoiceAvailable => VoiceInput.Available;
    private readonly VoiceCaptureSession voiceSession = new();
    private bool listening;
    public bool Listening { get => listening; private set { if (Set(ref listening, value)) { Raise(nameof(Placeholder)); Raise(nameof(VoiceTip)); } } }
    public string VoiceTip => Listening ? "Arrêter l'écoute vocale" : "Rechercher à la voix";
    public ToggleVoiceCommand Listen => listen ??= new ToggleVoiceCommand(() =>
        voiceSession.ToggleAsync(text => Query = text, active => Listening = active));
    private ToggleVoiceCommand? listen;
    /// <summary>Fermer la recherche interrompt l'écoute et ignore toute ancienne transcription.</summary>
    public void StopListening() => voiceSession.Cancel(active => Listening = active);
    public ObservableCollection<RecentSearchRow> Recent { get; } = [];
    public bool HasRecent => Recent.Count > 0;
    public IReadOnlyList<SearchShortcutRow> Shortcuts { get; }
    public bool HasShortcuts => Shortcuts.Count > 0;

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (var text in shell.Prefs.RecentSearches)
            Recent.Add(new RecentSearchRow(text,
                new RelayCommand(() => Query = text),
                new RelayCommand(() => { shell.Prefs.ForgetSearch(text); RefreshRecent(); })));
        Raise(nameof(HasRecent));
    }

    /// <summary>Un résultat ouvert : la saisie rejoint les recherches récentes.</summary>
    private void Remember()
    {
        if (!settingsOnly) shell.Prefs.RememberSearch(query);
    }

    public bool SettingsOnly => settingsOnly;
    public string Placeholder => listening ? "Parlez maintenant…" : settingsOnly ? "Rechercher un réglage : police, mot de passe, mois…" :
        "Rechercher dans SCOLIA";
    public string SearchTitle => settingsOnly ? "Rechercher un réglage" : "Rechercher dans SCOLIA";
    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value ?? string.Empty)) return;
            Raise(nameof(IsIdle));
            Raise(nameof(HasResults));
            Raise(nameof(HasQuery));
            IsEmptyResult = false;
            Schedule();
        }
    }
    public string Message { get => message; private set => Set(ref message, value); }
    public ObservableCollection<SearchHitRow> Results { get; } = [];
    public SearchHitRow? Selected { get => selected; set => Set(ref selected, value); }
    public AsyncCommand Open { get; }
    public RelayCommand Close { get; }

    private void Schedule()
    {
        timer?.Stop();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += async (_, _) => { timer?.Stop(); await shell.Run(SearchAsync); };
        timer.Start();
    }

    /// <summary>Résultats rangés par sorte, comme ceux de la recherche du menu : un bloc par sorte, un titre au-dessus.</summary>
    public IReadOnlyList<SearchGroupVm> Groups { get; private set; } = [];
    private bool isEmptyResult;
    /// <summary>Recherche faite, rien trouvé : la carte « Aucun résultat » s'affiche, comme dans le menu.</summary>
    public bool IsEmptyResult { get => isEmptyResult; private set => Set(ref isEmptyResult, value); }

    private string GroupTitle(string type) => type switch
    {
        "Apprenant" => shell.StudentTerms.PluralTitle.ToUpperInvariant(),
        "Reçu" => "REÇUS",
        "Fonctionnalité" or "Paramètre" => "FONCTIONNALITÉS ET PARAMÈTRES",
        _ => type.ToUpperInvariant(),
    };

    public async Task SearchAsync()
    {
        // Une recherche lancée tout de suite (Entrée, voix) remplace celle qui attendait la fin de la frappe.
        timer?.Stop();
        Results.Clear();
        Selected = null;
        Groups = [];
        Raise(nameof(Groups));
        IsEmptyResult = false;
        if (Query.Trim().Length < 2)
        {
            Message = settingsOnly
                ? "Saisissez au moins deux caractères pour retrouver un réglage."
                : $"Saisissez au moins deux caractères pour trouver un {shell.StudentTerms.Singular}, un reçu ou un réglage.";
            return;
        }
        // Réglages : catalogue local filtré selon les permissions et jamais envoyé au serveur.
        // Sur un compte Promoteur, aucune requête sur les dossiers des apprenants n'est autorisée.
        var terms = new PhoneSettingsSearchVm(shell) { Query = Query.Trim() };
        // Classement compris sans formation : une fonction dont le TITRE correspond vient en tête ;
        // une fonction trouvée seulement par un mot-clé caché passe après les apprenants et les reçus.
        // « ACC » trouve donc d'abord les matricules ACC…, pas « Thème et couleurs » (mot-clé « accent »).
        var strong = new List<SearchHitRow>();
        var weak = new List<SearchHitRow>();
        foreach (var setting in terms.Results)
        {
            // Le résultat porte sa COMMANDE NATIVE et non une adresse recomposée
            // d'après son libellé. Ainsi « promotions », « sécurité », « reçus »
            // et toutes les autres fonctions ouvrent réellement leur destination.
            var isSetting = setting.Title is "Configuration" or "Thème et couleurs"
                or "Texte et polices" or "Fenêtres et affichage" or "Animations et fluidité"
                or "Sons" or "Sécurité du compte" or "Poste et réseau" or "À propos et mises à jour"
                or "Établissement" or "Années scolaires" or "Sections"
                or "Promotions et frais" or "Nouvelle promotion" or "Nouvelle année"
                or "Utilisateurs";
            // Un filtre de catégorie seul ne suffit pas : « Administration » contient
            // aussi bien les réglages que le pointage des enseignants.
            if (settingsOnly && !isSetting) continue;
            var kind = isSetting ? "Paramètre" : "Fonctionnalité";
            // Un seul geste (toucher, clic ou Entrée) lance la commande réelle du résultat.
            (PhoneSettingsSearchVm.Matches(Query.Trim(), setting.Title) ? strong : weak).Add(new SearchHitRow(
                new SearchHitDto(kind, setting.Title, setting.Section,
                    "/native/" + Uri.EscapeDataString(setting.Title)), _ =>
                {
                    if (!setting.Open.CanExecute(null))
                    {
                        Message = "Cette fonctionnalité n'est plus disponible pour ce compte.";
                        return Task.CompletedTask;
                    }
                    Remember();
                    shell.CloseDialog();
                    setting.Open.Execute(null);
                    return Task.CompletedTask;
                }));
        }

        // Une saisie qui ressemble à un matricule (« ACC », « INF104 ») cherche d'abord des dossiers.
        var raw = Query.Trim();
        var looksLikeCode = raw.Any(char.IsDigit) || (raw.Length >= 2 && raw.All(c => char.IsLetter(c) && char.IsUpper(c)));
        if (!looksLikeCode) foreach (var hit in strong) Results.Add(hit);
        var dataUnavailable = false;
        if (!settingsOnly && shell.Can(Perm.StudentsRead))
        {
            var current = Query.Trim();
            try
            {
                var hits = await shell.Api.GetAsync<List<SearchHitDto>>(
                    "api/recherche" + ScoliaClient.Query(("q", current)));
                // Un aller-retour ancien ne doit pas écraser une nouvelle saisie.
                if (!string.Equals(current, Query.Trim(), StringComparison.Ordinal)) return;
                foreach (var h in hits)
                {
                    if (h.Type == "Reçu" && !shell.Can(Perm.PaymentsRead)) continue;
                    Results.Add(new SearchHitRow(h, OpenHitAsync));
                }
            }
            catch (HttpRequestException) { dataUnavailable = true; }
            catch (ApiException) { dataUnavailable = true; }
        }
        if (looksLikeCode) foreach (var hit in strong) Results.Add(hit);
        foreach (var hit in weak) Results.Add(hit);
        Selected = Results.Count > 0 ? Results[0] : null;
        Groups = Results.GroupBy(r => GroupTitle(r.Type)).Select(g => new SearchGroupVm(g.Key, g.ToList())).ToList();
        Raise(nameof(Groups));
        IsEmptyResult = Results.Count == 0;
        Message = dataUnavailable
            ? "Serveur indisponible : utilisez les paramètres et fonctionnalités locales."
            : Results.Count == 0 ? "Aucun résultat dans les catégories autorisées."
            : Format.Count(Results.Count, "résultat", "résultats");
    }

    // Le clavier utilise exactement la même commande que le clic/toucher sur le résultat.
    private Task OpenSelectedAsync()
    {
        if (Selected?.Open is { } command && command.CanExecute(null)) command.Execute(null);
        return Task.CompletedTask;
    }

    /// <summary>Chaque résultat porte sa destination : un seul clic ou une touche Entrée l'ouvre,
    /// même si la sélection visuelle n'a pas encore changé.</summary>
    private async Task OpenHitAsync(SearchHitRow hit)
    {
        Remember();
        var url = hit.Hit.Url;
        var target = PageFor(url);
        if (url is "/compte/poste" or "/compte/mot-de-passe")
        {
            shell.CloseDialog();
            if (shell.Phone || Platform.IsMobile)
            {
                shell.OpenBackstage();
                var section = url == "/compte/poste" ? "poste" : "mot-de-passe";
                shell.Backstage.Section = shell.Backstage.Items.FirstOrDefault(i => i.Value == section)
                    ?? new BackstageItemVm(section, section == "poste" ? "Poste et réseau" : "Sécurité du compte",
                        section == "poste" ? MaterialIconKind.LanConnect : MaterialIconKind.ShieldLockOutline,
                        section == "poste" ? "ToneGreen" : "ToneOrange");
            }
            else if (url == "/compte/poste")
                await shell.Open(() => new NetworkHubPage(shell));
            else
                await shell.Open(() => new SettingsPage(shell, "securite"));
            return;
        }
        if (url.StartsWith("/navigation/", StringComparison.Ordinal))
        {
            shell.CloseDialog();
            var label = Uri.UnescapeDataString(url["/navigation/".Length..]);
            var feature = shell.Navigation.SelectMany(group => group.Items)
                .FirstOrDefault(item => item.Label == label);
            if (feature is not null) feature.Open.Execute(null);
            else shell.Notify("Cette fonctionnalité n'est plus disponible pour ce compte.");
            return;
        }
        if (target is null)
        {
            Message = "Fonctionnalité introuvable ou accès non autorisé.";
            return;
        }
        shell.CloseDialog();
        await shell.Open(target);
    }

    /// <summary>Traduit l'adresse d'un résultat en écran natif.</summary>
    private Func<PageViewModel>? PageFor(string url)
    {
        var parts = url.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return parts[0] switch
        {
            "apprenants" when shell.Can(Perm.StudentsRead) && parts.Length > 1 && Guid.TryParse(parts[1], out var id) => () => new StudentDetailPage(shell, id),
            "apprenants" when shell.Can(Perm.StudentsRead) => () => new StudentsPage(shell),
            "paiements" when shell.Can(Perm.PaymentsRead) && parts.Length > 2 && Guid.TryParse(parts[2], out var rid) => () => new ReceiptDetailPage(shell, rid),
            "paiements" when shell.Can(Perm.PaymentsRead) => () => new ReceiptsPage(shell),
            "encaisser" when shell.Can(Perm.PaymentsCreate) => () => new CashierPage(shell),
            "recouvrement" when shell.Can(Perm.ReportsRead) => () => new DebtorsPage(shell),
            "prises-en-charge" when shell.Can(Perm.PaymentsRead) => () => new SponsorshipsPage(shell),
            "caisse" when shell.Can(Perm.PaymentsRead) => () => new CashPage(shell),
            "depenses" when shell.Can(Perm.PaymentsRead) => () => new ExpensesPage(shell),
            "reinscriptions" when shell.Can(Perm.StudentsWrite) => () => new ReEnrollmentPage(shell),
            "notes" when shell.Can(Perm.AcademicsRead) => () => new GradesPage(shell),
            "presences" when shell.Can(Perm.AcademicsRead) => () => new AttendancePage(shell),
            "discipline" when shell.Can(Perm.AcademicsRead) => () => new DisciplinePage(shell),
            "personnel" when shell.Can(Perm.StaffManage) => () => new StaffPage(shell),
            "parametres" when parts.Length > 1 && parts[1] == "utilisateurs" && shell.Can(Perm.UsersManage)
                => () => new UsersPage(shell),
            "parametres" when parts.Length > 1 && (new[] { "apparence", "texte", "fenetres", "animations", "sons" }.Contains(parts[1]) || (shell.Can(Perm.SettingsWrite) && new[] { "etablissement", "annees", "sections", "promotions", "frais" }.Contains(parts[1]))) => () => new SettingsPage(shell, parts[1]),
            "parametres" => () => new SettingsPage(shell),
            "utilisateurs" when shell.Can(Perm.UsersManage) => () => new UsersPage(shell),
            "donnees" when shell.Can(Perm.BackupManage) => () => new DataPage(shell),
            "audit" when shell.Can(Perm.AuditRead) => () => new AuditPage(shell),
            _ => null,
        };
    }
}

/// <summary>Résultat de recherche dans la liste, avec son libellé de type.</summary>
/// <summary>Un délai de mise en veille, en ligne à cocher.</summary>
public sealed record LockOptionVm(string Label, bool IsSelected, RelayCommand Choose);

/// <summary>Bloc de résultats d'une même sorte : apprenants, reçus, fonctionnalités et paramètres.</summary>
public sealed record SearchGroupVm(string Title, IReadOnlyList<SearchHitRow> Items);

/// <summary>Recherche récente : la toucher la relance, la croix l'oublie.</summary>
public sealed record RecentSearchRow(string Text, RelayCommand Use, RelayCommand Remove);

/// <summary>Tuile d'accès rapide de la recherche : un outil autorisé, avec sa couleur.</summary>
public sealed record SearchShortcutRow(string Label, MaterialIconKind Icon, Avalonia.Media.IBrush Brush, RelayCommand Open);

public sealed class SearchHitRow
{
    public SearchHitRow(SearchHitDto hit, Func<SearchHitRow, Task>? open = null)
    {
        Hit = hit;
        Open = new AsyncCommand(() => open is null ? Task.CompletedTask : open(this));
        OpenAction = open is null ? null : Open;
    }

    public SearchHitDto Hit { get; }
    public AsyncCommand Open { get; }
    /// <summary>Action réelle du résultat (page, réglage ou commande), absente pour un simple libellé.</summary>
    public System.Windows.Input.ICommand? OpenAction { get; }
    public string Type => Hit.Type;
    public string Title => Hit.Title;
    public string Subtitle => Hit.Subtitle;

    /// <summary>Icône du résultat, la même que celle de son outil dans le volet latéral.</summary>
    public MaterialIconKind Icon => Hit.Type == "Reçu" ? MaterialIconKind.ReceiptText
        : Hit.Type == "Paramètre" ? MaterialIconKind.CogOutline
        : Hit.Type == "Fonctionnalité" ? MaterialIconKind.ViewGrid
        : MaterialIconKind.AccountGroup;
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
    private bool biometricBusy;
    private bool securityPasswordVisible;

    public BackstageViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        // Compte Windows : uniquement identité, information et aide.
        // Réseau, sécurité et apparence ont des emplacements dédiés et faciles
        // à retrouver dans la navigation principale ou Paramètres.
        var entries = new List<BackstageItemVm>
        {
            new("infos", "Informations", MaterialIconKind.Information, "ToneBlue"),
        };
        if (Preferences.MobileLayout)
        {
            entries.Add(new("mot-de-passe", "Sécurité du compte", MaterialIconKind.ShieldLockOutline, "ToneOrange"));
            entries.Add(new("poste", "Poste et réseau", MaterialIconKind.LanConnect, "ToneGreen"));
            entries.Add(new("preferences", "Apparence et confort", MaterialIconKind.TuneVariant, "TonePurple"));
        }
        entries.Add(Preferences.MobileLayout
            ? new("aide", "Aide et gestes", MaterialIconKind.GestureTap, "ToneNeutral")
            : new("aide", "Aide et raccourcis", MaterialIconKind.Keyboard, "ToneNeutral"));
        Items = entries;
        section = Items[0];
        Close = new RelayCommand(shell.CloseBackstage);
        Logout = new AsyncCommand(() => shell.LogoutAsync());
        PasswordForm = BuildPasswordForm();
        ManageBiometrics = new AsyncCommand(ManageBiometricsAsync);
        ToggleSecurityPassword = new RelayCommand(() => SecurityPasswordVisible = !SecurityPasswordVisible);
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
                    currentCode = newCode = confirmCode = string.Empty;
                    Raise(nameof(CurrentCode)); Raise(nameof(NewCode)); Raise(nameof(ConfirmCode));
                    CodeVisible = false;
                    RaiseCode();
                    SecurityPassword = string.Empty;
                    SecurityPasswordVisible = false;
                    SecurityNotice = string.Empty;
                    CodeEditorOpen = codeProvisional;
                    BiometricPanelOpen = false;
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

    public bool CanCheckWindowsUpdates => OperatingSystem.IsWindows();

    /// <summary>Contrôle manuel complémentaire au contrôle automatique Windows à l'ouverture.</summary>
    public AsyncCommand CheckWindowsUpdates => checkWindowsUpdates ??= new AsyncCommand(async () =>
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            await DesktopAppUpdates.CheckAsync(window, manual: true);
    });
    private AsyncCommand? checkWindowsUpdates;

    /// <summary>Visible sur Android dans Mon compte : même contrôle forcé que Poste et réseau.</summary>
    public bool CanCheckPhoneUpdates => Platform.IsMobile && Platform.CheckAppUpdatesAsync is not null;
    public string PhoneUpdateStatus => Platform.AppUpdateStatus?.Invoke() ?? "Aucune recherche effectuée.";
    public AsyncCommand CheckPhoneUpdates => checkPhoneUpdates ??= new AsyncCommand(async () =>
    {
        if (Platform.CheckAppUpdatesAsync is not { } check) return;
        await check();
        Raise(nameof(PhoneUpdateStatus));
    });
    private AsyncCommand? checkPhoneUpdates;

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
    /// <summary>Android : empreinte ; iPhone/iPad : Face ID ou Touch ID, selon l'appareil.</summary>
    public string BiometricTitle => Platform.BiometricDisplayName;
    public string BiometricVerificationHelp => Platform.EnrollBiometricAsync is null
        ? "La connexion biométrique n'est pas encore activable sur cette version de l'appareil. Votre mot de passe reste disponible."
        : $"Pour activer : mot de passe puis validation {BiometricTitle.ToLowerInvariant()}. Pour désactiver : validation biométrique ou mot de passe.";
    private string? BiometricAccount => BiometricAccess.ReadAccount(shell.Api.Server);
    public bool BiometricsEnabled => ShowBiometricSecurity && (Platform.HasBiometricProof?.Invoke() ?? false)
        && BiometricAccount is { } account && shell.Session is { } session
        && BiometricAccess.AccountMatches(account, session.UserName);
    public bool OtherBiometricAccount => BiometricAccount is { } account && shell.Session is { } session
        && !BiometricAccess.AccountMatches(account, session.UserName);
    /// <summary>Ne jamais proposer une activation sans enrôlement et preuve cryptographique natives.</summary>
    public bool BiometricEnrollmentReady => Platform.EnrollBiometricAsync is not null
        && Platform.HasBiometricProof is not null && Platform.IsBiometricAvailable?.Invoke() == true;
    public string BiometricSecurityStatus => OtherBiometricAccount
        ? "Une autre session est associée à la biométrie de cet appareil. Connectez-vous avec ce compte pour la modifier."
        : BiometricsEnabled
            ? "Activée pour votre compte. Une vérification biométrique locale sera demandée lors de chaque accès."
            : !BiometricEnrollmentReady
                ? "Non disponible pour l'activation sur cet appareil : une preuve biométrique sécurisée SCOLIA est nécessaire. Votre mot de passe reste utilisable."
                : "Prête à être activée : confirmez votre mot de passe, puis validez l'invite sécurisée de l'appareil.";
    public string BiometricActionLabel => BiometricsEnabled ? "Désactiver la biométrie" : "Activer la biométrie";
    public bool BiometricSecurityAllowed => ShowBiometricSecurity && !OtherBiometricAccount
        && !BiometricBusy && (BiometricsEnabled || BiometricEnrollmentReady);
    public bool BiometricBusy
    {
        get => biometricBusy;
        private set { if (Set(ref biometricBusy, value)) Raise(nameof(BiometricSecurityAllowed)); }
    }
    public string SecurityPassword { get => securityPassword; set => Set(ref securityPassword, value); }
    public bool SecurityPasswordVisible
    {
        get => securityPasswordVisible;
        private set
        {
            if (!Set(ref securityPasswordVisible, value)) return;
            Raise(nameof(SecurityPasswordMask));
            Raise(nameof(SecurityEyeIcon));
        }
    }
    public char SecurityPasswordMask => SecurityPasswordVisible ? '\0' : '•';
    public MaterialIconKind SecurityEyeIcon => SecurityPasswordVisible
        ? MaterialIconKind.EyeOffOutline : MaterialIconKind.EyeOutline;
    public RelayCommand ToggleSecurityPassword { get; }
    public string SecurityNotice { get => securityNotice; private set { if (Set(ref securityNotice, value)) Raise(nameof(HasSecurityNotice)); } }
    public bool HasSecurityNotice => SecurityNotice.Length > 0;
    public AsyncCommand ManageBiometrics { get; }
    public RelayCommand CloseSecurity => closeSecurity ??= new RelayCommand(() => Section = Items[0]);

    // ---------- Code d'accès : code PIN ou mot de passe, au choix de la personne ----------
    private bool usePin;
    private string currentCode = string.Empty;
    private string newCode = string.Empty;
    private string confirmCode = string.Empty;
    private bool codeVisible;
    private bool savingCode;
    private string codeError = string.Empty;

    /// <summary>Code PIN (chiffres seulement) choisi ; sinon, mot de passe.</summary>
    public bool UsePin
    {
        get => usePin;
        set
        {
            if (!Set(ref usePin, value)) return;
            newCode = string.Empty;
            confirmCode = string.Empty;
            Raise(nameof(NewCode));
            Raise(nameof(ConfirmCode));
            RaiseCode();
        }
    }
    public bool UsePassword => !usePin;
    public RelayCommand ChoosePin => choosePin ??= new RelayCommand(() => UsePin = true);
    private RelayCommand? choosePin;
    public RelayCommand ChoosePassword => choosePassword ??= new RelayCommand(() => UsePin = false);
    private RelayCommand? choosePassword;

    public string CurrentCode { get => currentCode; set { if (Set(ref currentCode, value ?? string.Empty)) RaiseCode(); } }
    public string NewCode { get => newCode; set { if (Set(ref newCode, value ?? string.Empty)) RaiseCode(); } }
    public string ConfirmCode { get => confirmCode; set { if (Set(ref confirmCode, value ?? string.Empty)) RaiseCode(); } }

    public bool CodeVisible { get => codeVisible; private set { if (Set(ref codeVisible, value)) { Raise(nameof(CodeMask)); Raise(nameof(CodeEyeIcon)); } } }
    public char CodeMask => codeVisible ? '\0' : '•';
    public MaterialIconKind CodeEyeIcon => codeVisible ? MaterialIconKind.EyeOffOutline : MaterialIconKind.EyeOutline;
    public RelayCommand ToggleCodeVisible => toggleCodeVisible ??= new RelayCommand(() => CodeVisible = !CodeVisible);
    private RelayCommand? toggleCodeVisible;

    public string NewCodeLabel => usePin ? "Nouveau code PIN" : "Nouveau mot de passe";
    public string ConfirmCodeLabel => usePin ? "Confirmer le code PIN" : "Confirmer le mot de passe";
    public string MethodHint => usePin
        ? "Chiffres seulement, comme le code de déverrouillage d'un téléphone. Rapide à saisir sur le pavé numérique."
        : "Lettres, chiffres et symboles, au choix. Une courte phrase facile à retenir convient très bien.";
    /// <summary>Règles de la méthode choisie, cochées pendant la saisie.</summary>
    public IReadOnlyList<AccessCodeRule> CodeRules => AccessCode.Rules(newCode, usePin);
    public bool CodesMatch => confirmCode.Length > 0 && confirmCode == newCode;
    public bool ShowMatch => confirmCode.Length > 0;
    public string MatchText => CodesMatch ? "Les deux saisies correspondent." : "Les deux saisies sont différentes.";
    public bool CanSaveCode => !savingCode && currentCode.Length > 0 && AccessCode.IsPin(newCode) == usePin
        && AccessCode.Problem(newCode) is null && CodesMatch && newCode != currentCode;
    private string codeNotice = string.Empty;
    /// <summary>Message du code d'accès : code provisoire à remplacer, ou nouveau code enregistré.</summary>
    public string CodeNotice { get => codeNotice; private set { if (Set(ref codeNotice, value)) Raise(nameof(HasCodeNotice)); } }
    public bool HasCodeNotice => codeNotice.Length > 0;
    public string CodeError { get => codeError; private set { if (Set(ref codeError, value)) Raise(nameof(HasCodeError)); } }
    public bool HasCodeError => codeError.Length > 0;
    public AsyncCommand SaveCode => saveCode ??= new AsyncCommand(SaveCodeAsync);
    private AsyncCommand? saveCode;

    /// <summary>Premier accès avec un code provisoire : le message invite à choisir son propre code.</summary>
    public void AskForOwnCode()
    {
        codeProvisional = true;
        CodeEditorOpen = true;
        CodeNotice = "Votre code actuel est provisoire. Choisissez maintenant votre propre code PIN ou mot de passe : personne d'autre ne le connaîtra.";
        RefreshSecurity();
    }

    // ---------- Mise en veille : délai réglable sur cet appareil ----------
    public IReadOnlyList<ChoiceItem> LockChoices { get; } = Preferences.LockDelays
        .Select(m => new ChoiceItem(m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            m == 0 ? "Jamais" : m == 1 ? "Après 1 minute" : $"Après {m} minutes")).ToList();

    /// <summary>Délai choisi : sans activité pendant ce temps, SCOLIA se met en veille et redemande le code.</summary>
    public ChoiceItem? LockChoice
    {
        get => LockChoices.FirstOrDefault(c => c.Value == shell.Prefs.LockMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        set
        {
            if (value is null || !int.TryParse(value.Value, out var minutes) || minutes == shell.Prefs.LockMinutes) return;
            shell.Prefs.LockMinutes = minutes;
            Raise(nameof(LockChoice));
            Raise(nameof(LockSummary));
            Raise(nameof(LockDelayLabel));
            shell.NoteActivity();
        }
    }
    /// <summary>Les délais en lignes à cocher, comme le type de code : une seule cochée, un toucher suffit.</summary>
    public IReadOnlyList<LockOptionVm> LockOptions => LockChoices.Select(c => new LockOptionVm(c.Label, c == LockChoice,
        new RelayCommand(() => { LockChoice = c; Raise(nameof(LockOptions)); LockPanelOpen = false; }))).ToList();

    private bool lockPanelOpen;
    /// <summary>La ligne « Verrouillage automatique » s'ouvre sur place et montre les délais.</summary>
    public bool LockPanelOpen
    {
        get => lockPanelOpen;
        set { if (Set(ref lockPanelOpen, value)) Raise(nameof(LockChevron)); }
    }
    public MaterialIconKind LockChevron => lockPanelOpen ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;
    public RelayCommand ToggleLockPanel => toggleLockPanel ??= new RelayCommand(() => LockPanelOpen = !LockPanelOpen);
    private RelayCommand? toggleLockPanel;
    public string LockDelayLabel => LockChoice?.Label ?? "Après 5 minutes";

    public string LockSummary => shell.Prefs.LockMinutes == 0
        ? "Désactivée : la session reste ouverte jusqu'à la déconnexion"
        : $"Verrouillage après {(shell.Prefs.LockMinutes == 1 ? "1 minute" : $"{shell.Prefs.LockMinutes} minutes")} sans activité, aussi en arrière-plan";

    /// <summary>Verrouille tout de suite, par exemple avant de laisser l'appareil sur le bureau.</summary>
    public RelayCommand LockNow => lockNow ??= new RelayCommand(() =>
    {
        shell.CloseBackstage();
        shell.LockNow();
    });
    private RelayCommand? lockNow;

    // ---------- Présentation façon Android 16 : un état en tête, puis des lignes qui s'ouvrent sur place ----------
    private bool codeProvisional;
    private bool codeEditorOpen;
    private bool biometricPanelOpen;

    /// <summary>La ligne « Code d'accès » est ouverte : le formulaire du nouveau code s'affiche dessous.</summary>
    public bool CodeEditorOpen
    {
        get => codeEditorOpen;
        set { if (Set(ref codeEditorOpen, value)) { Raise(nameof(CodeChevron)); if (value) BiometricPanelOpen = false; } }
    }
    /// <summary>La ligne biométrique est ouverte : explication, code de confirmation et bouton.</summary>
    public bool BiometricPanelOpen
    {
        get => biometricPanelOpen;
        set { if (Set(ref biometricPanelOpen, value)) { Raise(nameof(BiometricChevron)); if (value) CodeEditorOpen = false; } }
    }
    public MaterialIconKind CodeChevron => codeEditorOpen ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;
    public MaterialIconKind BiometricChevron => biometricPanelOpen ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;
    public RelayCommand ToggleCodeEditor => toggleCodeEditor ??= new RelayCommand(() => CodeEditorOpen = !CodeEditorOpen);
    private RelayCommand? toggleCodeEditor;
    public RelayCommand ToggleBiometricPanel => toggleBiometricPanel ??= new RelayCommand(() => BiometricPanelOpen = !BiometricPanelOpen);
    private RelayCommand? toggleBiometricPanel;

    /// <summary>Tout est en ordre : code personnel et, sur le téléphone, déverrouillage biométrique.</summary>
    public bool SecurityHealthy => !codeProvisional && (!ShowBiometricSecurity || BiometricsEnabled || !BiometricEnrollmentReady);
    public bool SecurityNeedsAction => !SecurityHealthy;
    public string SecurityHeadline => codeProvisional ? "Choisissez votre propre code"
        : SecurityHealthy ? "Votre compte est protégé" : "Protection recommandée";
    public string SecuritySubline => codeProvisional
        ? "Le code donné par le secrétariat est provisoire."
        : SecurityHealthy
            ? ShowBiometricSecurity && BiometricsEnabled ? $"Code personnel et {BiometricTitle.ToLowerInvariant()} actifs." : "Votre code personnel protège l'accès."
            : $"Activez {Platform.BiometricInSentence} pour ouvrir SCOLIA d'un geste.";
    public MaterialIconKind SecurityIcon => SecurityHealthy ? MaterialIconKind.ShieldCheck : MaterialIconKind.ShieldAlertOutline;
    public string CodeSummary => codeProvisional ? "Provisoire · à remplacer maintenant" : "Code PIN ou mot de passe · Modifier";
    public string BiometricStateLabel => BiometricsEnabled ? "Activée" : BiometricEnrollmentReady ? "Désactivée" : "Indisponible";
    public string BiometricSummary => BiometricsEnabled ? "Ouvrir SCOLIA sans saisir votre code" : OtherBiometricAccount
        ? "Associée à un autre compte sur ce téléphone" : BiometricEnrollmentReady
            ? "Touchez pour l'activer" : "Enregistrez une empreinte dans les réglages du téléphone";

    private void RaiseCode()
    {
        CodeError = string.Empty;
        foreach (var name in new[] { nameof(UsePassword), nameof(NewCodeLabel), nameof(ConfirmCodeLabel), nameof(MethodHint),
                     nameof(CodeRules), nameof(CodesMatch), nameof(ShowMatch), nameof(MatchText), nameof(CanSaveCode) })
            Raise(name);
    }

    private async Task SaveCodeAsync()
    {
        if (!CanSaveCode) return;
        savingCode = true;
        Raise(nameof(CanSaveCode));
        try
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/session/mot-de-passe", new { currentPassword = currentCode, newPassword = newCode });
            // Le cache chiffré hors ligne reconnaît aussitôt le nouveau code, jamais l'ancien.
            if (shell.Api.Offline is { } cache && shell.Session is { } session)
                cache.SaveCredential(session.UserName, newCode, session, shell.Api.SessionCookie);
            var kind = usePin ? "Code PIN" : "Mot de passe";
            var saved = newCode;
            currentCode = newCode = confirmCode = string.Empty;
            Raise(nameof(CurrentCode)); Raise(nameof(NewCode)); Raise(nameof(ConfirmCode));
            RaiseCode();
            var wasProvisional = codeProvisional;
            codeProvisional = false;
            CodeEditorOpen = false;
            CodeNotice = $"{kind} enregistré. Il sert dès maintenant à vous connecter.";
            RefreshSecurity();
            shell.Notify($"{kind} modifié.");
            if (shell.Session is { } current)
            {
                shell.Sync.Remember(current.UserName, saved);
                await ResealBiometricsAsync(current.UserName, saved);
            }
            // L'écran ouvert avant le choix du code (refusé avec le code provisoire) se recharge aussitôt.
            if (wasProvisional && shell.Current is { } page) await page.RefreshAsync();
        }
        catch (ApiException ex)
        {
            CodeError = ex.Message;
        }
        finally
        {
            savingCode = false;
            Raise(nameof(CanSaveCode));
        }
    }
    private RelayCommand? closeSecurity;

    /// <summary>
    /// Nouveau code : l'empreinte doit garder le nouveau, sinon la connexion suivante serait refusée. Le téléphone
    /// demande l'empreinte une fois ; si elle est refusée, la biométrie est coupée plutôt que laissée fausse.
    /// </summary>
    private async Task ResealBiometricsAsync(string userName, string secret)
    {
        if (!BiometricsEnabled || Platform.EnrollBiometricAsync is not { } enroll) return;
        if (await enroll(BiometricAccess.Seal(userName, secret)))
        {
            BiometricAccess.Enable(shell.Api.Server, userName);
            CodeNotice += $" {BiometricTitle} reconnaît aussi le nouveau code.";
        }
        else
        {
            Platform.DisableBiometricAccess?.Invoke();
            BiometricAccess.Disable();
            CodeNotice += $" {BiometricTitle} a été désactivée : réactivez-la ci-dessous pour ne plus saisir le code.";
        }
        RefreshSecurity();
        shell.Login.RefreshBiometrics();
    }

    public void RefreshSecurity()
    {
        foreach (var name in new[] { nameof(SecurityHealthy), nameof(SecurityNeedsAction), nameof(SecurityHeadline), nameof(SecuritySubline),
                     nameof(SecurityIcon), nameof(CodeSummary), nameof(BiometricStateLabel), nameof(BiometricSummary) })
            Raise(name);
        Raise(nameof(BiometricsEnabled));
        Raise(nameof(OtherBiometricAccount));
        Raise(nameof(BiometricEnrollmentReady));
        Raise(nameof(BiometricSecurityStatus));
        Raise(nameof(BiometricTitle));
        Raise(nameof(BiometricVerificationHelp));
        Raise(nameof(BiometricActionLabel));
        Raise(nameof(BiometricSecurityAllowed));
    }

    /// <summary>Chaque changement sensible exige une preuve fraîche, jamais la seule session ouverte.</summary>
    private async Task ManageBiometricsAsync()
    {
        if (BiometricBusy || shell.Session is not { } session || !ShowBiometricSecurity) return;
        BiometricBusy = true;
        try
        {
            SecurityNotice = string.Empty;
            if (OtherBiometricAccount)
            {
                SecurityNotice = "Ce téléphone est déjà associé à un autre compte : déconnectez-vous et utilisez ce compte.";
                return;
            }
    
            var enabled = BiometricsEnabled;
            var verified = false;
            if (enabled && Platform.UnlockBiometricAsync is { } unlock)
                verified = BiometricAccess.Open(await unlock()) is { } opened
                    && BiometricAccess.AccountMatches(opened.UserName, session.UserName);
            // Le code vérifié par le serveur est celui que l'empreinte gardera pour les connexions suivantes.
            var secret = SecurityPassword;
    
            // Activer requiert systématiquement le mot de passe SCOLIA ; désactiver
            // admet soit une preuve Android fraîche, soit le mot de passe du même compte.
            if (!enabled || !verified)
            {
                if (string.IsNullOrWhiteSpace(SecurityPassword))
                {
                    SecurityNotice = enabled
                        ? $"Confirmez avec {Platform.BiometricInSentence} ou saisissez le mot de passe actuel pour désactiver."
                        : $"Saisissez votre mot de passe actuel pour activer {Platform.BiometricInSentence}.";
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
                SecurityNotice = $"{BiometricTitle} désactivée. Votre code sera demandé à chaque connexion.";
            }
            else if (Platform.EnrollBiometricAsync is { } enroll && await enroll(BiometricAccess.Seal(session.UserName, secret)))
            {
                BiometricAccess.Enable(shell.Api.Server, session.UserName);
                SecurityNotice = $"{BiometricTitle} activée. À la prochaine connexion, touchez « Déverrouiller » : votre code ne sera plus demandé.";
            }
            else SecurityNotice = "Activation annulée ou indisponible : le mot de passe reste nécessaire.";
    
            Raise(nameof(HasSecurityNotice));
            RefreshSecurity();
            shell.Login.RefreshBiometrics();
            }
        finally
        {
            // Même après une annulation ou une erreur réseau : réautoriser une tentative.
            SecurityPassword = string.Empty;
            BiometricBusy = false;
            RefreshSecurity();
        }
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
        var form = new FormViewModel("Changer le mot de passe", "Votre mot de passe protège l'accès aux dossiers scolaires. Choisissez-en un nouveau, différent de l'ancien.", async f =>
        {
            if (f.Text("nouveau") != f.Text("confirmation"))
            {
                f.Find("confirmation").Error = "Les deux mots de passe ne correspondent pas.";
                return false;
            }
            await shell.Api.SendAsync(HttpMethod.Post, "api/session/mot-de-passe", new { currentPassword = f.Text("actuel"), newPassword = f.Text("nouveau") });
            // Le cache chiffré hors ligne doit immédiatement reconnaître le nouveau
            // mot de passe, jamais l'ancien, après la confirmation du serveur.
            if (shell.Api.Offline is { } cache && shell.Session is { } authenticatedSession)
                cache.SaveCredential(authenticatedSession.UserName, f.Text("nouveau"), authenticatedSession, shell.Api.SessionCookie);
            shell.Notify("Mot de passe modifié.");
            PasswordForm = BuildPasswordForm();
            shell.CloseBackstage();
            return true;
        })
        { SubmitText = "Enregistrer", Embedded = true, ShowCancel = false };
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
                new("Ctrl+K", "Rechercher", $"Un {shell.StudentTerms.Singular}, un reçu ou une page."),
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

    public bool CanCheckPhoneUpdates => Platform.IsMobile && Platform.CheckAppUpdatesAsync is not null;
    public string PhoneUpdateStatus => Platform.AppUpdateStatus?.Invoke() ?? "Aucune recherche effectuée.";
    public AsyncCommand CheckPhoneUpdates => checkPhoneUpdates ??= new AsyncCommand(async () =>
    {
        if (Platform.CheckAppUpdatesAsync is not { } check) return;
        await check();
        Raise(nameof(PhoneUpdateStatus));
    });
    private AsyncCommand? checkPhoneUpdates;

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
    public string Accounts => "Quatre espaces : Secrétariat, Comptabilité, Promoteur (consultation des indicateurs uniquement) et Enseignant (encodage de ses cours, par code anonyme).";

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
