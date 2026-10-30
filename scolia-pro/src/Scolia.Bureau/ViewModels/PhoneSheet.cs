using System.Windows.Input;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;

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

        // Réunir les réglages d'affichage et les autres outils des Paramètres
        // dans le même accordéon de navigation, sans deuxième rubrique « Paramètres ».
        foreach (var group in navigation) group.SetSupplementalCommands([]);
        var settings = navigation.FirstOrDefault(g => g.Title == "Paramètres");
        if (settings is not null)
        {
            var settingsTools = tools.Where(g => g.Title is "Paramètres" or "Affichage").ToArray();
            var commands = settingsTools.SelectMany(g => g.Commands)
                .DistinctBy(c => c.Label.Trim(), StringComparer.OrdinalIgnoreCase).ToArray();
            settings.SetSupplementalCommands(commands);
            foreach (var group in settingsTools) usedTools.Add(group);
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

        Add("ACCUEIL", [""], []);
        Add("DONNÉES ET SAUVEGARDES", ["Données"], ["Données", "Sauvegardes", "Export", "Exports"]);
        Add("GESTION SCOLAIRE", ["Secrétariat", "Scolarité"], ["Secrétariat", "Scolarité", "Apprenants"]);
        Add("GESTION FINANCIÈRE", ["Finances"], ["Finances", "Paiements", "Rapports"]);
        Add("ADMINISTRATION", ["Administration", "Paramètres"], ["Administration", "Paramètres", "Affichage", "Sécurité"]);
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
        Add("SCOLARITÉ ET APPRENANTS", "Scolarité", "Secrétariat", "Apprenants");
        Add("FINANCES ET RAPPORTS", "Paiements", "Finances", "Rapports", "Export", "Exports", "Données");
        Add("ADMINISTRATION", "Administration", "Paramètres", "Affichage", "Sécurité");
        Add("AIDE", "Aide");
        var extra = groups.Where(g => !used.Contains(g)).ToArray();
        if (extra.Length != 0) blocks.Add(new PhoneToolBlockVm("AUTRES COMMANDES", extra));
        return blocks;
    }
}
