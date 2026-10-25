using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Retouches du téléphone sur les vues partagées, au moment où chaque élément s'affiche :
/// une colonne de libellés à largeur fixe prend la largeur de son texte, pour laisser la place à la valeur ;
/// un sélecteur à segments s'étend sur toute la largeur, chaque segment à parts égales.
/// Rien ne change sur l'ordinateur.
/// </summary>
public static class PhoneFit
{
    /// <summary>Colonne fixe à partir de laquelle la largeur suit le texte : en dessous, c'est une icône ou un bouton.</summary>
    private const double LabelColumn = 60;

    private static bool installed;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        // La grille est retouchée dès qu'elle prend place dans l'interface, avant sa première mesure :
        // la modifier une fois mesurée dérègle sa mise en page.
        Visual.VisualParentProperty.Changed.AddClassHandler<Grid>((grid, e) => { if (e.NewValue is not null) FitGrid(grid); });
        Control.LoadedEvent.AddClassHandler<Border>(OnBorderLoaded);
    }

    private static void FitGrid(Grid grid)
    {
        // Les modèles des contrôles (listes, champs) gardent leurs colonnes : elles sont faites pour leur contenu.
        if (!PhoneLayout.Active || grid.TemplatedParent is not null) return;
        var changed = false;
        foreach (var column in grid.ColumnDefinitions)
            if (column.Width.IsAbsolute && column.Width.Value >= LabelColumn)
            {
                column.Width = GridLength.Auto;
                changed = true;
            }
        // Le libellé suit son texte : un espace le sépare de la valeur, que la largeur fixe ménageait.
        if (changed && grid.ColumnSpacing == 0) grid.ColumnSpacing = 12;
        // Les classes de la grille ne sont pas encore posées quand elle rejoint son parent : la ligne de réglage
        // est reconnue à son entrée dans l'écran, toujours avant sa première mesure.
        void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            grid.AttachedToVisualTree -= Attached;
            StackPreference(grid);
        }
        grid.AttachedToVisualTree += Attached;
    }

    /// <summary>
    /// Ligne de réglage : à côté de son libellé sur l'ordinateur, le réglage passe dessous sur le téléphone quand il est large
    /// (curseur, liste) ; l'interrupteur reste à droite. Le libellé garde ainsi toute la largeur et se lit d'un trait.
    /// </summary>
    private static void StackPreference(Grid grid)
    {
        if (!grid.Classes.Contains("prefrow") || grid.ColumnDefinitions.Count != 2 || grid.RowDefinitions.Count > 0) return;
        if (grid.Children.FirstOrDefault(c => Grid.GetColumn(c) == 1) is not Control value || value is ToggleSwitch or CheckBox) return;
        grid.ColumnDefinitions = new ColumnDefinitions("*");
        grid.RowDefinitions = new RowDefinitions("Auto,Auto");
        Grid.SetColumn(value, 0);
        Grid.SetRow(value, 1);
        value.Margin = new Thickness(0, 8, 0, 0);
        value.HorizontalAlignment = value is ComboBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    private static void OnBorderLoaded(Border border, RoutedEventArgs e)
    {
        if (!PhoneLayout.Active || !border.Classes.Contains("segmented") || border.Child is not StackPanel { Orientation: Orientation.Horizontal } row) return;
        border.SetValue(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Stretch, BindingPriority.Animation);
        void Share()
        {
            var segments = row.Children.OfType<Control>().Where(c => c.IsVisible).ToList();
            var width = border.Bounds.Width - border.Padding.Left - border.Padding.Right - border.BorderThickness.Left - border.BorderThickness.Right;
            if (segments.Count == 0 || width <= 0) return;
            var each = Math.Floor(width / segments.Count) - row.Spacing;
            foreach (var segment in segments)
            {
                segment.SetValue(Layoutable.WidthProperty, each, BindingPriority.Animation);
                segment.SetValue(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center, BindingPriority.Animation);
            }
        }
        border.SizeChanged += (_, args) =>
        {
            if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.5) Avalonia.Threading.Dispatcher.UIThread.Post(Share);
        };
        Avalonia.Threading.Dispatcher.UIThread.Post(Share, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
