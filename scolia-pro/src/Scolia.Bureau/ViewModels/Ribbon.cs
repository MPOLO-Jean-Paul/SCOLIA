using System.Windows.Input;
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
    public string Tip { get; } = shortcut is null ? tip : $"{tip} ({shortcut})";
    public ICommand Command { get; } = command;
    public bool Large { get; } = large;
    public bool IsToggle { get; } = toggle;
    public IBrush Brush { get; } = Infrastructure.ThemeBrush.Get(tone);

    /// <summary>Bouton à deux états : enfoncé quand l'option est active.</summary>
    public bool IsOn { get => isOn; set => Set(ref isOn, value); }
}

/// <summary>Groupe de commandes, titré en bas comme dans Office.</summary>
public sealed record RibbonGroupVm(string Title, IReadOnlyList<RibbonCommandVm> Commands);

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
