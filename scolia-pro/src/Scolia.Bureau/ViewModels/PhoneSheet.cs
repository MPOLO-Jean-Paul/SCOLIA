using System.Windows.Input;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Ligne d'une feuille d'actions du téléphone : grande, pour le doigt, avec son icône. Dans une feuille de choix
/// (thème, couleur), la ligne de la valeur en cours porte une coche à droite.
/// </summary>
public sealed class SheetItemVm(string label, MaterialIconKind icon, ICommand command, bool danger = false, bool emphasis = false,
    bool isChecked = false, string? detail = null, IBrush? brush = null)
{
    public string Label { get; } = label;
    public MaterialIconKind Icon { get; } = icon;
    public ICommand Command { get; } = command;
    public bool Danger { get; } = danger;
    public bool Emphasis { get; } = emphasis || isChecked;
    public bool IsChecked { get; } = isChecked;
    public string? Detail { get; } = detail;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public IBrush Brush => brush ?? ThemeBrush.Get(Danger ? "ToneRed" : "AccentBrush");
}

/// <summary>Option d'une feuille de choix : son libellé, son icône, sa couleur éventuelle et ce qu'elle fait.</summary>
public sealed record SheetChoice(string Label, MaterialIconKind Icon, bool IsChecked, Action Choose, string? Detail = null, IBrush? Brush = null);

/// <summary>
/// Feuille d'actions du téléphone, qui monte du bas de l'écran : les actions de la page ou d'une ligne,
/// à la place des menus contextuels de l'ordinateur, trop petits pour le doigt.
/// </summary>
public sealed class SheetVm(string title, string subtitle, IReadOnlyList<SheetItemVm> items)
{
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public bool HasSubtitle => Subtitle.Length > 0;
    public IReadOnlyList<SheetItemVm> Items { get; } = items;
}


    
/// <summary>Une destination de réglage accessible sans rechercher dans les dossiers scolaires.</summary>
public sealed record PhoneSettingsResultVm(string Title, string Section, MaterialIconKind Icon,
    IBrush Brush, string Keywords, ICommand Open);

/// <summary>
/// Recherche des paramètres directement dans l'en-tête du menu Android.
/// Index local uniquement : aucun apprenant, reçu ou autre donnée personnelle n'est consulté.
/// </summary>
public sealed class PhoneSettingsSearchVm : Observable
{
    private readonly ShellViewModel shell;
    private bool isOpen;
    private string query = string.Empty;
    private IReadOnlyList<PhoneSettingsResultVm> results = [];

    public PhoneSettingsSearchVm(ShellViewModel shell)
    {
        this.shell = shell;
        Activate = new RelayCommand(() =>
        {
            shell.PhoneMenuOpen = true;
            IsOpen = true;
        });
        Toggle = new RelayCommand(() =>
        {
            if (IsOpen) Reset();
            else Activate.Execute(null);
        });
        Clear = new RelayCommand(() => Query = string.Empty);
    }

    public RelayCommand Activate { get; }
    public RelayCommand Toggle { get; }
    public RelayCommand Clear { get; }

    /// <summary>Micro du champ, comme la recherche de l'accueil.</summary>
    public bool VoiceAvailable => VoiceInput.Available;
    private bool listening;
    public bool Listening { get => listening; private set { if (Set(ref listening, value)) Raise(nameof(Placeholder)); } }
    public string Placeholder => listening ? "Parlez maintenant…" : "Outil ou paramètre à ouvrir…";
    public AsyncCommand Listen => listen ??= new AsyncCommand(async () =>
    {
        Listening = true;
        try { if (await VoiceInput.ListenAsync() is { } text) Query = text; }
        finally { Listening = false; }
    });
    private AsyncCommand? listen;
    // À chaque ouverture, le micro suit le service vocal du moment (installé, coupé ou refusé entre-temps).
    public bool IsOpen { get => isOpen; private set { if (Set(ref isOpen, value) && value) Raise(nameof(VoiceAvailable)); } }
    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value ?? string.Empty)) return;
            Refresh();
        }
    }
    public IReadOnlyList<PhoneSettingsResultVm> Results
    {
        get => results;
        private set
        {
            if (!Set(ref results, value)) return;
            Raise(nameof(HasResults));
            Raise(nameof(NoResults));
        }
    }
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool HasResults => Results.Count > 0;
    public bool NoResults => HasQuery && !HasResults;

    public void Reset()
    {
        IsOpen = false;
        Query = string.Empty;
    }

    private static string Normalize(string text) => string.Concat(
        text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        .ToLowerInvariant();

    /// <summary>Recherche sans casse ni accents, avec tous les mots de la saisie.</summary>
    public static bool Matches(string query, string labelAndKeywords)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var haystack = Normalize(labelAndKeywords);
        return words.Length > 0 && words.All(word => haystack.Contains(word, StringComparison.Ordinal));
    }

    private void Refresh()
    {
        Raise(nameof(HasQuery));
        if (!HasQuery) { Results = []; return; }
        var catalog = new List<PhoneSettingsResultVm>();

        void Add(string name, string section, MaterialIconKind icon, string tone, string keywords, ICommand command)
            => catalog.Add(new PhoneSettingsResultVm(name, section, icon, ThemeBrush.Get(tone), keywords, command));

        // Naviguer d'abord : Shell.Current ferme lui-même le menu après avoir choisi
        // le nouvel écran. Effacer les résultats avant de naviguer pouvait retirer
        // le bouton sélectionné pendant son propre clic et laisser l'accueil visible.
        ICommand Page(string? section) => new AsyncCommand(() =>
            shell.Run(() => shell.Open(() => new SettingsPage(shell, section))));
        ICommand Account(string key) => new RelayCommand(() =>
        {
            shell.PhoneMenuOpen = false;
            if (shell.Phone || Platform.IsMobile)
            {
                // Les vues Android gardent leur fenêtre de compte existante.
                // Le shell peut être simulé en mode téléphone sur desktop dans
                // les tests : l'entrée est alors créée à la demande.
                shell.OpenBackstage();
                shell.Backstage.Section = shell.Backstage.Items.FirstOrDefault(i => i.Value == key)
                    ?? new BackstageItemVm(key, key == "poste" ? "Poste et réseau" : "Sécurité du compte",
                        key == "poste" ? MaterialIconKind.LanConnect : MaterialIconKind.ShieldLockOutline,
                        key == "poste" ? "ToneGreen" : "ToneOrange");
                return;
            }
            if (key == "poste")
                _ = shell.Run(() => shell.Open(() => new NetworkHubPage(shell)));
            else
                _ = shell.Run(() => shell.Open(() => new SettingsPage(shell, "securite")));
        });
        ICommand OpenPage(Func<PageViewModel> page) => new AsyncCommand(() =>
            shell.Run(() => shell.Open(page)));

        Add("Configuration", "Paramètres", MaterialIconKind.Cog, "TonePurple",
            "personnalisation options preferences reglages parametres", Page(null));
        Add("Thème et couleurs", "Cet appareil", MaterialIconKind.Palette, "TonePurple",
            "clair sombre noir blanc nuit mode couleur accent fonds", Page("apparence"));
        Add("Texte et polices", "Cet appareil", MaterialIconKind.FormatFont, "ToneBlue",
            "taille lettres caractere police texte agrandir zoom typographie", Page("texte"));
        if (!Platform.IsMobile)
            Add("Fenêtres et affichage", "Cet appareil", MaterialIconKind.WindowMaximize, "ToneTeal",
                "fenetre volet ruban barre", Page("fenetres"));
        Add("Animations et fluidité", "Cet appareil", MaterialIconKind.AnimationPlay, "ToneOrange",
            "transition mouvement vitesse rapidite animation performance", Page("animations"));
        Add("Sons", "Cet appareil", MaterialIconKind.VolumeHigh, "TonePink",
            "audio musique notifications notification banniere bannieres alerte alertes encaissement sauvegarde volume silence activation son", Page("sons"));
        Add("Sécurité du compte", shell.Phone || Platform.IsMobile ? "Mon compte" : "Paramètres",
            MaterialIconKind.ShieldLockOutline, "ToneTeal",
            "biometrie empreinte digitale identification connexion authentification code secret mot de passe veille mise en veille verrouillage verrouiller ecran inactivite",
            Account("mot-de-passe"));
        Add("Poste et réseau", shell.Phone || Platform.IsMobile ? "Mon compte" : "Réseau et postes",
            MaterialIconKind.LanConnect, "ToneGreen",
            "tailscale vpn wifi internet serveur partage reseaux connexion poste magicdns",
            Account("poste"));

        if (!shell.Phone && !Platform.IsMobile)
            Add("À propos et mises à jour", "Paramètres", MaterialIconKind.InformationOutline, "ToneIndigo",
                "version application logiciel developpeur editeur mise a jour actualisation telecharger",
                Page("apropos"));

        if (shell.Can(Perm.SettingsWrite))
        {
            Add("Établissement", "Établissement", MaterialIconKind.OfficeBuilding, "ToneNeutral",
                "ecole institut logo nom coordonnees identite monnaie carte", Page("etablissement"));
            Add("Années scolaires", "Établissement", MaterialIconKind.Calendar, "ToneRed",
                "annee cours rentree periode mois paiement", Page("annees"));
            Add("Sections", "Établissement", MaterialIconKind.ViewGrid, "ToneIndigo",
                "section filiere options infirmiere laboratoire", Page("sections"));
            Add("Promotions et frais", "Établissement", MaterialIconKind.School, "ToneBlue",
                "classes promotions mensualite cout frais scolarite", Page("promotions"));
            Add("Catégories de frais", "Établissement", MaterialIconKind.TagMultipleOutline, "ToneGreen",
                "categorie frais inscription examen tenue carte montant", Page("frais"));
            Add("Nouvelle promotion", "Établissement", MaterialIconKind.FolderPlusOutline, "ToneBlue",
                "ajouter creer classe promotion frais", new AsyncCommand(async () => { shell.PhoneMenuOpen = false; await shell.Run(() => SettingsPage.NewPromotionAsync(shell)); }));
            Add("Nouvelle année", "Établissement", MaterialIconKind.CalendarPlusOutline, "ToneRed",
                "ajouter creer annee scolaire", new AsyncCommand(async () => { shell.PhoneMenuOpen = false; await shell.Run(() => SettingsPage.NewYearAsync(shell)); }));
        }
        if (shell.Can(Perm.UsersManage))
            Add("Utilisateurs", "Paramètres", MaterialIconKind.AccountCog, "ToneBlue",
                "compte membre permission roles acces utilisateur personnel",
                OpenPage(() => new UsersPage(shell)));

        // Rechercher aussi les fonctionnalités du menu (pas leurs dossiers) en réutilisant
        // exactement la navigation filtrée par permissions. Aucune liste d'apprenants/reçus n'est lue ici.
        foreach (var group in shell.Navigation)
        {
            foreach (var item in group.Items)
            {
                if (item.PageType == typeof(SettingsPage) || item.PageType == typeof(UsersPage))
                    continue; // déjà indexés plus haut avec leurs rubriques ciblées
                var destination = item;
                var category = string.IsNullOrWhiteSpace(group.Title) ? "Navigation" : group.Title;
                catalog.Add(new PhoneSettingsResultVm(destination.Label, category, destination.Icon,
                    destination.Brush, destination.TabLabel,
                    new RelayCommand(() =>
                    {
                        // Même commande que dans le volet natif, sans retour intermédiaire à Accueil.
                        if (destination.Open.CanExecute(null)) destination.Open.Execute(null);
                    })));
            }
        }

        // Rechercher AUSSI dans les commandes de métier du Menu (nouvelle inscription,
        // nouveaux frais, export, actions métier). Ne pas se limiter aux pages de navigation.
        // PhoneTools contient déjà les commandes autorisées pour le rôle connecté.
        foreach (var group in shell.PhoneTools)
        {
            foreach (var command in group.Commands)
            {
                var action = command;
                catalog.Add(new PhoneSettingsResultVm(action.Label, group.Title, action.Icon,
                    action.Brush, action.Label, new RelayCommand(() =>
                    {
                        if (!action.Command.CanExecute(null)) return;
                        action.Command.Execute(null);
                        // Une commande non navigante (formulaire/export) doit aussi fermer le menu.
                        if (shell.PhoneMenuOpen) shell.PhoneMenuOpen = false;
                    })));
            }
        }

        // Les commandes et le volet peuvent nommer la même destination : montrer un seul résultat.
        Results = catalog.Where(item => Matches(Query, item.Title + " " + item.Section + " " + item.Keywords))
            .GroupBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }
}

/// <summary>Rubrique des commandes du menu du téléphone : un métier et ses commandes qui ne sont pas des pages.</summary>
public sealed class PhoneToolGroupVm : Observable
{
    private bool isExpanded;

    public PhoneToolGroupVm(string title, IReadOnlyList<RibbonCommandVm> commands, Action<PhoneToolGroupVm>? toggleGroup = null)
    {
        Title = title;
        Commands = commands;
        Icon = FeatureVisuals.Icon(title);
        Brush = ThemeBrush.Get(FeatureVisuals.Tone(title));
        Toggle = new RelayCommand(() =>
        {
            if (toggleGroup is not null) toggleGroup(this);
            else IsExpanded = !IsExpanded;
        });
    }

    public string Title { get; }
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public IReadOnlyList<RibbonCommandVm> Commands { get; }
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }
    public RelayCommand Toggle { get; }
}

/// <summary>
/// Menu mobile unifié : les pages et leurs commandes apparaissent dans le même
/// bloc métier. Chaque groupe n'est présent qu'une fois, sans relancer les
/// mêmes fonctionnalités dans un deuxième bloc plus bas.
/// </summary>
public sealed record PhoneMenuBlockVm(string Title, IReadOnlyList<NavGroupVm> NavigationGroups,
    IReadOnlyList<PhoneToolGroupVm> ToolGroups)
{
    public static IReadOnlyList<PhoneMenuBlockVm> Build(IReadOnlyList<NavGroupVm> navigation,
        IReadOnlyList<PhoneToolGroupVm> tools)
    {
        var usedNav = new HashSet<NavGroupVm>();
        var usedTools = new HashSet<PhoneToolGroupVm>();
        var result = new List<PhoneMenuBlockVm>();

        // Fusionner les outils de chaque métier avec son accordéon de navigation.
        // Évite deux lignes « Scolarité », « Finances », « Paramètres », etc.
        foreach (var nav in navigation)
        {
            var aliases = nav.Title switch
            {
                "Paramètres" => new[] { "Paramètres", "Affichage", "Sécurité" },
                "Données" => new[] { "Données", "Sauvegardes", "Contrôle", "Effacement" },
                "Pilotage" => new[] { "Pilotage", "Exports", "Export", "Rapports" },
                _ => new[] { nav.Title },
            };
            var matched = tools.Where(g => aliases.Contains(g.Title, StringComparer.Ordinal)).ToArray();
            var commands = matched.SelectMany(g => g.Commands)
                .DistinctBy(c => c.Label.Trim(), StringComparer.OrdinalIgnoreCase).ToArray();
            nav.SetSupplementalCommands(commands);
            foreach (var group in matched) usedTools.Add(group);
        }

        void Add(string title, string[] navigationTitles, string[] toolTitles)
        {
            var nav = navigation.Where(g => navigationTitles.Contains(g.Title, StringComparer.Ordinal)
                && usedNav.Add(g)).ToArray();
            var actions = tools.Where(g => toolTitles.Contains(g.Title, StringComparer.Ordinal)
                && usedTools.Add(g)).ToArray();
            if (nav.Length > 0 || actions.Length > 0)
                result.Add(new PhoneMenuBlockVm(title, nav, actions));
        }

        // Mêmes blocs que le volet de l'ordinateur, dans le même ordre.
        Add("VUE D’ENSEMBLE", ["", "Pilotage"], ["Pilotage", "Exports", "Export", "Rapports"]);
        Add("ESPACE ENSEIGNANT", ["Espace enseignant"], ["Mon espace"]);
        Add("GESTION SCOLAIRE", ["Secrétariat", "Scolarité"], ["Secrétariat", "Scolarité", "Apprenants"]);
        Add("GESTION DES DÉLIBÉRATIONS", ["Enseignants et cours", "Délibération"], ["Délibérations"]);
        Add("GESTION FINANCIÈRE", ["Finances"], ["Finances", "Paiements"]);
        Add("ORGANISATION", ["Personnel"], ["Personnel"]);
        Add("RÉGLAGES ET DONNÉES", ["Paramètres", "Données"], ["Paramètres", "Affichage", "Sécurité", "Données", "Sauvegardes", "Contrôle", "Effacement"]);
        Add("AIDE", [], ["Aide"]);

        var remainingNav = navigation.Where(g => usedNav.Add(g)).ToArray();
        var remainingTools = tools.Where(g => usedTools.Add(g)).ToArray();
        if (remainingNav.Length > 0 || remainingTools.Length > 0)
            result.Add(new PhoneMenuBlockVm("AUTRES OUTILS", remainingNav, remainingTools));
        return result;
    }
}

/// <summary>Les commandes du ruban apparaissent en blocs cohérents sous les pages.</summary>
public sealed record PhoneToolBlockVm(string Title, IReadOnlyList<PhoneToolGroupVm> Groups)
{
    /// <summary>Une action n'est affichée qu'une fois, même si plusieurs onglets du ruban la proposent.</summary>
    public static IReadOnlyList<PhoneToolGroupVm> UniqueGroups(IReadOnlyList<PhoneToolGroupVm> groups)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PhoneToolGroupVm>();
        foreach (var group in groups)
        {
            var unique = group.Commands.Where(c => labels.Add(c.Label.Trim())).ToList();
            if (unique.Count == 0) continue;
            result.Add(new PhoneToolGroupVm(group.Title, unique) { IsExpanded = group.IsExpanded });
        }
        return result;
    }

    public static IReadOnlyList<PhoneToolBlockVm> Organize(IReadOnlyList<PhoneToolGroupVm> groups)
    {
        var blocks = new List<PhoneToolBlockVm>();
        var used = new HashSet<PhoneToolGroupVm>();
        void Add(string title, params string[] titles)
        {
            var entries = groups.Where(g => titles.Contains(g.Title, StringComparer.Ordinal)).ToArray();
            if (entries.Length == 0) return;
            foreach (var entry in entries) used.Add(entry);
            blocks.Add(new PhoneToolBlockVm(title, entries));
        }
        Add("VUE D’ENSEMBLE", "Pilotage", "Rapports", "Export", "Exports");
        Add("ESPACE ENSEIGNANT", "Mon espace");
        Add("GESTION SCOLAIRE", "Secrétariat", "Scolarité", "Apprenants");
        Add("GESTION DES DÉLIBÉRATIONS", "Délibérations");
        Add("GESTION FINANCIÈRE", "Finances", "Paiements");
        Add("ORGANISATION", "Personnel");
        Add("RÉGLAGES ET DONNÉES", "Paramètres", "Affichage", "Sécurité", "Données", "Sauvegardes", "Contrôle", "Effacement");
        Add("AIDE", "Aide");
        var extra = groups.Where(g => !used.Contains(g)).ToArray();
        if (extra.Length != 0) blocks.Add(new PhoneToolBlockVm("AUTRES COMMANDES", extra));
        return blocks;
    }
}
