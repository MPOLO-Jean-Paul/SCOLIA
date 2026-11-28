using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Colonnes adaptatives des tableaux. Le niveau 1 reste toujours affiché. Quand les colonnes ne tiennent plus
/// dans la largeur du tableau, le niveau 3 se masque d'abord, puis le niveau 2, en partant de la droite.
/// Le calcul porte sur la largeur réelle du tableau : un tableau en demi-page s'adapte comme un tableau en pleine page.
/// </summary>
public static class GridPriority
{
    /// <summary>Place réservée à la barre de défilement verticale et aux bordures.</summary>
    private const double Chrome = 18;

    private static double factor = 1;
    private sealed class GridState { public bool Pending; }
    private static readonly ConditionalWeakTable<DataGrid, GridState> ActiveGrids = new();

    public static readonly AttachedProperty<int> LevelProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, int>("Level", typeof(GridPriority), 1);

    public static readonly AttachedProperty<bool> AdaptiveProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Adaptive", typeof(GridPriority));

    /// <summary>Une colonne retirée selon les droits ne peut pas être réaffichée par l'adaptation à la largeur.</summary>
    public static readonly AttachedProperty<bool> AllowedProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, bool>("Allowed", typeof(GridPriority), true);

    public static bool GetAllowed(DataGridColumn column) => column.GetValue(AllowedProperty);
    public static void SetAllowed(DataGridColumn column, bool allowed)
    {
        column.SetValue(AllowedProperty, allowed);
        column.IsVisible = allowed;
    }

    /// <summary>Largeur minimale prévue dans la vue, avant agrandissement du texte.</summary>
    private static readonly AttachedProperty<double> BaseMinWidthProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, double>("BaseMinWidth", typeof(GridPriority), double.NaN);

    /// <summary>Dernière largeur affichée d'une colonne automatique : son contenu peut dépasser le minimum prévu.</summary>
    private static readonly AttachedProperty<double> SeenWidthProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, double>("SeenWidth", typeof(GridPriority));

    static GridPriority()
    {
        AdaptiveProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            grid.SizeChanged -= OnGridSizeChanged;
            grid.AttachedToVisualTree -= OnAttached;
            grid.DetachedFromVisualTree -= OnDetached;
            ActiveGrids.Remove(grid);
            if (e.NewValue is not true) return;
            grid.SizeChanged += OnGridSizeChanged;
            grid.AttachedToVisualTree += OnAttached;
            grid.DetachedFromVisualTree += OnDetached;
            if (grid.IsAttachedToVisualTree()) Queue(grid);
        });
    }

    private static void OnGridSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        // Les lignes qui arrivent ou changent de hauteur ne modifient pas les colonnes.
        if (sender is DataGrid grid && Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.01)
            Queue(grid);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) Queue(grid);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is DataGrid grid) ActiveGrids.Remove(grid);
    }

    private static void Queue(DataGrid grid)
    {
        if (!GetAdaptive(grid) || !grid.IsAttachedToVisualTree()) return;
        var state = ActiveGrids.GetValue(grid, _ => new GridState());
        if (state.Pending) return;
        state.Pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            if (ActiveGrids.TryGetValue(grid, out var current) && ReferenceEquals(current, state)
                && GetAdaptive(grid) && grid.IsAttachedToVisualTree())
                Apply(grid);
        }, DispatcherPriority.Loaded);
    }

    public static int GetLevel(DataGridColumn column) => column.GetValue(LevelProperty);

    public static void SetLevel(DataGridColumn column, int level) => column.SetValue(LevelProperty, level);

    public static bool GetAdaptive(DataGrid grid) => grid.GetValue(AdaptiveProperty);

    public static void SetAdaptive(DataGrid grid, bool value) => grid.SetValue(AdaptiveProperty, value);

    /// <summary>Facteur de texte courant : les préférences le transmettent à chaque application.</summary>
    public static void SetTextFactor(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || Math.Abs(factor - value) < 0.001) return;
        factor = value;
        // Le zoom du texte ne modifie pas forcément la largeur du tableau.
        foreach (var pair in ActiveGrids)
            Queue(pair.Key);
    }

    /// <summary>Affiche ou masque chaque colonne selon la largeur courante de la grille.</summary>
    public static void Apply(DataGrid grid)
    {
        var width = grid.Bounds.Width - Chrome;
        if (width <= 0) return;
        var columns = grid.Columns.ToList();
        var needs = new double[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            // Un texte plus grand demande des colonnes plus larges : sans cela, un numéro de reçu serait coupé.
            var baseMin = column.GetValue(BaseMinWidthProperty);
            if (double.IsNaN(baseMin))
            {
                baseMin = column.MinWidth;
                column.SetValue(BaseMinWidthProperty, baseMin);
            }
            if (baseMin > 0 && Math.Abs(column.MinWidth - baseMin * factor) > 0.5) column.MinWidth = baseMin * factor;
            var star = column.Width.IsStar;
            if (!star && column.IsVisible && column.ActualWidth > 0) column.SetValue(SeenWidthProperty, column.ActualWidth);
            needs[i] = star ? column.MinWidth : Math.Max(column.MinWidth, column.GetValue(SeenWidthProperty));
        }

        // Tout afficher, puis retirer les colonnes secondaires une à une tant que le tableau déborde.
        var visible = columns.Select(GetAllowed).ToArray();
        var total = needs.Where((_, index) => visible[index]).Sum();
        while (total > width)
        {
            var drop = -1;
            for (var i = columns.Count - 1; i >= 0; i--)
                if (visible[i] && GetLevel(columns[i]) > 1 && (drop < 0 || GetLevel(columns[i]) > GetLevel(columns[drop]))) drop = i;
            if (drop < 0) break;
            visible[drop] = false;
            total -= needs[drop];
        }
        for (var i = 0; i < columns.Count; i++)
            if (columns[i].IsVisible != visible[i]) columns[i].IsVisible = visible[i];
    }
}
