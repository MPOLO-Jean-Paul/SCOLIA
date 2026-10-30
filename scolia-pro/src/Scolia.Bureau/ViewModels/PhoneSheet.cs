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

    public PhoneToolGroupVm(string title, IReadOnlyList<RibbonCommandVm> commands)
    {
        Title = title;
        Commands = commands;
        Icon = FeatureVisuals.Icon(title);
        Brush = ThemeBrush.Get(FeatureVisuals.Tone(title));
        Toggle = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Title { get; }
    public MaterialIconKind Icon { get; }
    public IBrush Brush { get; }
    public IReadOnlyList<RibbonCommandVm> Commands { get; }
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }
    public RelayCommand Toggle { get; }
}
