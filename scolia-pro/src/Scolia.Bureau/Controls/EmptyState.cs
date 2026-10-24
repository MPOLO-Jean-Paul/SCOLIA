using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Material.Icons;
using Material.Icons.Avalonia;

namespace Scolia.Bureau.Controls;

/// <summary>
/// Liste vide : une icône grise, une phrase qui dit pourquoi, une autre qui dit quoi faire.
/// Posée sur un tableau, elle s'affiche seulement quand le tableau n'a aucune ligne.
/// </summary>
public sealed class EmptyState : StackPanel
{
    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<EmptyState, MaterialIconKind>(nameof(Icon), MaterialIconKind.TrayRemove);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Title), "Rien à afficher");

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Hint), string.Empty);

    private readonly MaterialIcon icon = new() { Width = 32, Height = 32 };
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock hint = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };

    public EmptyState()
    {
        Spacing = 6;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(24, 48, 24, 24);
        IsHitTestVisible = false;

        icon.Bind(MaterialIcon.ForegroundProperty, icon.GetResourceObservable("MutedBrush"));
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 0, 4);

        title.Bind(TextBlock.FontSizeProperty, title.GetResourceObservable("FsBase"));
        hint.Classes.Add("muted");
        Children.Add(icon);
        Children.Add(title);
        Children.Add(hint);
        Sync();
    }

    public MaterialIconKind Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty || change.Property == TitleProperty || change.Property == HintProperty) Sync();
    }

    private void Sync()
    {
        icon.Kind = Icon;
        title.Text = Title;
        hint.Text = Hint;
        hint.IsVisible = Hint.Length > 0;
    }
}
