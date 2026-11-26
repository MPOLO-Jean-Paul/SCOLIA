using Avalonia;
using Avalonia.Controls;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Grille d'indicateurs mesurée avec son contenu réel. La largeur disponible
/// décide des colonnes dès la première mesure, y compris après un chargement tardif.
/// Les tuiles d'une même rangée ont une hauteur commune ; les suivantes restent accessibles.
/// </summary>
public sealed class AdaptiveTiles : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveTiles, double>(nameof(MinItemWidth), 170);
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveTiles, int>(nameof(MaxColumns), int.MaxValue);

    static AdaptiveTiles() => AffectsMeasure<AdaptiveTiles>(MinItemWidthProperty, MaxColumnsProperty);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    private int measuredColumns = 1;
    private readonly List<double> rowHeights = [];

    public static int ColumnsFor(double width, int count, double minimum, int maximum, bool phone)
    {
        if (count <= 0) return 1;
        var cap = Math.Max(1, Math.Min(count, maximum));
        if (phone) cap = Math.Min(cap, 2);
        if (!double.IsFinite(width)) return cap;
        if (width <= 0) return 1;
        return (int)Math.Clamp(Math.Floor(width / Math.Max(1, minimum)), 1, cap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        measuredColumns = ColumnsFor(width, Children.Count, MinItemWidth, MaxColumns, PhoneLayout.Active);
        if (!double.IsFinite(width)) width = measuredColumns * Math.Max(1, MinItemWidth);
        var cellWidth = Math.Max(0, width / measuredColumns);
        rowHeights.Clear();
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            var row = i / measuredColumns;
            if (row == rowHeights.Count) rowHeights.Add(0);
            rowHeights[row] = Math.Max(rowHeights[row], child.DesiredSize.Height);
        }
        return new Size(width, rowHeights.Sum());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cellWidth = Math.Max(0, finalSize.Width / measuredColumns);
        var y = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var row = i / measuredColumns;
            var col = i % measuredColumns;
            var height = row < rowHeights.Count ? rowHeights[row] : Children[i].DesiredSize.Height;
            Children[i].Arrange(new Rect(col * cellWidth, y, cellWidth, height));
            if (col == measuredColumns - 1) y += height;
        }
        return finalSize;
    }
}
