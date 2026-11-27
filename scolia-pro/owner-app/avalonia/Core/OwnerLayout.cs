using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace JPServices.Owner;

/// <summary>Colonnes adaptatives sans reconstruire les champs ni perdre la saisie pendant une rotation.</summary>
internal sealed class OwnerTiles : Panel
{
    internal double MinimumWidth { get; init; } = 170;
    internal int MaximumColumns { get; init; } = 4;
    internal double Gap { get; init; } = 12;
    private int Columns(double width) => double.IsFinite(width)
        ? Math.Clamp((int)Math.Floor((width + Gap) / (MinimumWidth + Gap)), 1, MaximumColumns) : MaximumColumns;

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns(availableSize.Width);
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, (availableSize.Width - Gap * (columns - 1)) / columns) : MinimumWidth;
        var height = 0d;
        for (var row = 0; row < Children.Count; row += columns)
        {
            var rowHeight = 0d;
            for (var col = row; col < Math.Min(row + columns, Children.Count); col++)
            {
                Children[col].Measure(new Size(width, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[col].DesiredSize.Height);
            }
            height += rowHeight + (row > 0 ? Gap : 0);
        }
        return new Size(width * columns + Gap * (columns - 1), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns(finalSize.Width);
        var width = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
        var y = 0d;
        for (var row = 0; row < Children.Count; row += columns)
        {
            var rowHeight = Children.Skip(row).Take(columns).Max(c => c.DesiredSize.Height);
            for (var col = 0; col < columns && row + col < Children.Count; col++)
                Children[row + col].Arrange(new Rect(col * (width + Gap), y, width, rowHeight));
            y += rowHeight + Gap;
        }
        return finalSize;
    }
}

internal static class OwnerLayout
{
    internal static void StackBelow(Grid grid, double threshold)
    {
        var definitions = grid.ColumnDefinitions.ToString();
        var rows = grid.RowDefinitions.ToString();
        var positions = grid.Children.Select(c => (Control: c, Row: Grid.GetRow(c), Column: Grid.GetColumn(c), Span: Grid.GetColumnSpan(c))).ToArray();
        bool? stacked = null;
        grid.SizeChanged += (_, e) =>
        {
            var next = e.NewSize.Width < threshold;
            if (stacked == next) return;
            stacked = next;
            grid.ColumnDefinitions = new ColumnDefinitions(next ? "*" : definitions);
            grid.RowDefinitions = new RowDefinitions(next ? string.Join(',', positions.Select(_ => "Auto")) : rows);
            for (var i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                Grid.SetRow(p.Control, next ? i : p.Row);
                Grid.SetColumn(p.Control, next ? 0 : p.Column);
                Grid.SetColumnSpan(p.Control, next ? 1 : p.Span);
            }
        };
    }
}
