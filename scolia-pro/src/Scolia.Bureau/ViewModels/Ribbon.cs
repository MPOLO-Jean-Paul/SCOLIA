using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Commande du ruban : grande (icône au-dessus du libellé) ou petite (icône à gauche).</summary>
public sealed class RibbonCommandVm(string label, MaterialIconKind icon, string tip, ICommand command, bool large, string tone, bool toggle = false, string? shortcut = null) : Observable
{
    private bool isOn;

    public string Label { get; } = label;
    public MaterialIconKind Icon { get; } = icon;

    /// <summary>Info-bulle : la description, puis le raccourci clavier (ou l'absence de raccourci).</summary>
    /// <summary>Vide quand les info-bulles sont désactivées dans les préférences : aucune bulle ne s'affiche.</summary>
    public string? Tip { get; } = string.IsNullOrEmpty(tip) ? null : shortcut is null
        ? $"{tip}{Environment.NewLine}Aucun raccourci."
        : $"{tip}{Environment.NewLine}Raccourci : {ShortcutDisplay(shortcut)}";

    /// <summary>Raccourci tel qu'il s'affiche : Ctrl+Shift+A, Suppr, Plus.</summary>
    public string? ShortcutText { get; } = shortcut is null ? null : ShortcutDisplay(shortcut);

    /// <summary>Raccourci clavier qui lance la commande, s'il en a un.</summary>
    public KeyGesture? Gesture { get; } = shortcut is null ? null : KeyGesture.Parse(shortcut);

    public ICommand Command { get; } = command;
    public bool Large { get; } = large;
    public bool IsToggle { get; } = toggle;
    public IBrush Brush { get; } = Infrastructure.ThemeBrush.Get(tone);

    /// <summary>Bouton à deux états : enfoncé quand l'option est active.</summary>
    public bool IsOn { get => isOn; set => Set(ref isOn, value); }

    private static string ShortcutDisplay(string shortcut) =>
        shortcut.Replace("OemComma", ",").Replace("OemMinus", "-").Replace("OemPlus", "Plus").Replace("Delete", "Suppr").Replace("D0", "0");
}

/// <summary>Groupe de commandes, titré en bas comme dans Office.</summary>
public sealed record RibbonGroupVm(string Title, IReadOnlyList<RibbonCommandVm> Commands)
{
    /// <summary>Petites commandes empilées par trois au plus, comme dans Office.</summary>
    private const int SmallPerColumn = 3;

    /// <summary>
    /// Colonnes du groupe : chaque grande commande occupe sa colonne, les petites qui se suivent
    /// s'empilent par trois. Le ruban reste compact, sans défilement horizontal.
    /// </summary>
    public IReadOnlyList<RibbonColumnVm> Columns
    {
        get
        {
            var columns = new List<RibbonColumnVm>();
            var smalls = new List<RibbonCommandVm>();
            void Flush()
            {
                if (smalls.Count == 0) return;
                columns.Add(new RibbonColumnVm(smalls.ToList()));
                smalls.Clear();
            }
            foreach (var command in Commands)
            {
                if (command.Large)
                {
                    Flush();
                    columns.Add(new RibbonColumnVm([command]));
                    continue;
                }
                smalls.Add(command);
                if (smalls.Count == SmallPerColumn) Flush();
            }
            Flush();
            return columns;
        }
    }
}

/// <summary>Colonne d'un groupe du ruban : une grande commande, ou jusqu'à trois petites l'une sous l'autre.</summary>
public sealed record RibbonColumnVm(IReadOnlyList<RibbonCommandVm> Commands);

/// <summary>Onglet du ruban. Un onglet contextuel n'apparaît que sur certains écrans, avec une couleur d'accent.</summary>
public sealed class RibbonTabVm(string id, string title, IReadOnlyList<RibbonGroupVm> groups, string? contextLabel = null, string? contextTone = null) : Observable
{
    private bool isSelected;

    public string Id { get; } = id;
    public string Title { get; } = title;
    public IReadOnlyList<RibbonGroupVm> Groups { get; } = groups;
    public bool IsContext => contextLabel is not null;
    public bool IsFile => Id == "fichier";
    public string ContextLabel { get; } = contextLabel ?? string.Empty;
    public IBrush ContextBrush { get; } = Infrastructure.ThemeBrush.Get(contextTone ?? "ToneNeutral");
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
}
