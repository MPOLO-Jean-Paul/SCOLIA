using Avalonia;
using Avalonia.Controls;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Colonnes adaptatives des tableaux. Le niveau 1 reste toujours affiché. Le niveau 2 disparaît
/// quand la grille devient étroite, le niveau 3 disparaît plus tôt. Les seuils suivent la taille
/// du texte, car des colonnes plus grandes demandent plus de place.
/// </summary>
public static class GridPriority
{
    /// <summary>Largeur de grille, en pixels de référence, en dessous de laquelle le niveau 2 se masque.</summary>
    private const double NarrowBelow = 1000;

    /// <summary>Largeur de grille, en pixels de référence, en dessous de laquelle le niveau 3 se masque.</summary>
    private const double VeryNarrowBelow = 1200;

    private static double factor = 1;

    public static readonly AttachedProperty<int> LevelProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, int>("Level", typeof(GridPriority), 1);

    public static readonly AttachedProperty<bool> AdaptiveProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Adaptive", typeof(GridPriority));

    static GridPriority()
    {
        AdaptiveProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            if (e.NewValue is not true) return;
            grid.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.BoundsProperty) Apply(grid);
            };
            Apply(grid);
        });
    }

    public static int GetLevel(DataGridColumn column) => column.GetValue(LevelProperty);

    public static void SetLevel(DataGridColumn column, int level) => column.SetValue(LevelProperty, level);

    public static bool GetAdaptive(DataGrid grid) => grid.GetValue(AdaptiveProperty);

    public static void SetAdaptive(DataGrid grid, bool value) => grid.SetValue(AdaptiveProperty, value);

    /// <summary>Facteur de texte courant : les préférences le transmettent à chaque application.</summary>
    public static void SetTextFactor(double value)
    {
        factor = value;
    }

    /// <summary>Affiche ou masque chaque colonne selon la largeur courante de la grille.</summary>
    public static void Apply(DataGrid grid)
    {
        var width = grid.Bounds.Width;
        if (width <= 0) return;
        foreach (var column in grid.Columns)
        {
            var level = GetLevel(column);
            column.IsVisible = level switch
            {
                <= 1 => true,
                2 => width >= NarrowBelow * factor,
                _ => width >= VeryNarrowBelow * factor,
            };
        }
    }
}
