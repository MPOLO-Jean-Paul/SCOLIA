using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;

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
        if (PhoneLayout.Active && border.Classes.Contains("band")) FoldFilters(border);
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

    /// <summary>Nombre de champs de filtre au-delà duquel le téléphone replie les filtres secondaires.</summary>
    private const int FoldFrom = 3;

    /// <summary>
    /// Bandeau de filtres d'une liste, sur le téléphone : le champ principal (la recherche, s'il y en a une) reste
    /// visible, les autres filtres se replient derrière « Filtres », qui dit combien sont actifs. La liste se voit
    /// ainsi dès l'ouverture, sans faire défiler cinq ou six champs. Rien ne change sur l'ordinateur.
    /// </summary>
    private static void FoldFilters(Border band)
    {
        if (band.Child is not Panel panel || panel.Children.OfType<Button>().Any(b => b.Classes.Contains("filtertoggle"))) return;
        var fields = panel.Children.OfType<StackPanel>().Where(f => f.Classes.Contains("field")).ToList();
        if (fields.Count < FoldFrom) return;
        var primary = fields.FirstOrDefault(f => f.Children.OfType<TextBox>().Any()) ?? fields[0];
        var folded = panel.Children.OfType<Control>().Where(c => c != primary && c.IsVisible).ToList();
        var toggle = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        toggle.Classes.Add("filtertoggle");
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var icon = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.FilterVariant, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
        var chevron = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.ChevronDown, Width = 18, Height = 18 };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(chevron);
        toggle.Content = row;
        var open = false;
        var inputs = folded.SelectMany(c => c.GetLogicalDescendants().Prepend(c)).OfType<Control>()
            .Where(c => c is ComboBox or CalendarDatePicker or TextBox or CheckBox).ToList();
        static object? Read(Control c) => c switch
        {
            ComboBox box => box.SelectedIndex,
            CalendarDatePicker date => date.SelectedDate,
            TextBox text => text.Text ?? string.Empty,
            CheckBox check => check.IsChecked,
            _ => null,
        };
        // Les valeurs par défaut de l'écran (date du jour, mois en cours…) ne sont pas des filtres choisis :
        // on compte seulement ce que l'utilisateur a changé depuis qu'il a ouvert les filtres.
        Dictionary<Control, object?>? baseline = null;
        int Active() => baseline is null ? 0 : inputs.Count(c => !Equals(Read(c), baseline[c]));
        void Show()
        {
            foreach (var control in folded) control.IsVisible = open;
            var active = Active();
            label.Text = open ? "Masquer les filtres" : active > 0 ? $"Filtres · {active} actif{(active > 1 ? "s" : string.Empty)}" : "Filtres";
            chevron.Kind = open ? Material.Icons.MaterialIconKind.ChevronUp : Material.Icons.MaterialIconKind.ChevronDown;
            toggle.Classes.Set("active", !open && active > 0);
        }
        toggle.Click += (_, _) =>
        {
            baseline ??= inputs.ToDictionary(c => c, Read);
            open = !open;
            Show();
        };
        // Un filtre changé ailleurs (actualisation, « Effacer les filtres ») met le compte à jour.
        foreach (var control in folded.SelectMany(c => c.GetLogicalDescendants().Prepend(c)))
            switch (control)
            {
                case ComboBox box: box.SelectionChanged += (_, _) => { if (!open) Show(); }; break;
                case CalendarDatePicker date: date.SelectedDateChanged += (_, _) => { if (!open) Show(); }; break;
                case CheckBox check: check.IsCheckedChanged += (_, _) => { if (!open) Show(); }; break;
            }
        panel.Children.Insert(panel.Children.IndexOf(primary) + 1, toggle);
        Show();
    }
}
