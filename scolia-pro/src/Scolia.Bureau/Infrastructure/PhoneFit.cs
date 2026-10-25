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
        foreach (var column in grid.ColumnDefinitions)
            if (column.Width.IsAbsolute && column.Width.Value >= LabelColumn) column.Width = GridLength.Auto;
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
