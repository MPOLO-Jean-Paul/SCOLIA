using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Mise en page qui suit la place disponible, sur l'ordinateur comme sur le téléphone, sans doubler les vues :
/// une grille de blocs côte à côte se range en une colonne quand elle devient étroite, une grille de cartes
/// réduit son nombre de colonnes, et une fenêtre de dialogue ne dépasse jamais la largeur de l'écran.
/// </summary>
public static class Adaptive
{
    /// <summary>Largeur sous laquelle les colonnes de la grille passent l'une sous l'autre.</summary>
    public static readonly AttachedProperty<double> StackBelowProperty =
        AvaloniaProperty.RegisterAttached<Grid, double>("StackBelow", typeof(Adaptive));

    /// <summary>Lignes de la grille une fois rangée, par exemple « Auto,* » pour garder un tableau défilant en bas.</summary>
    public static readonly AttachedProperty<string?> StackedRowsProperty =
        AvaloniaProperty.RegisterAttached<Grid, string?>("StackedRows", typeof(Adaptive));

    /// <summary>Largeur minimale d'une carte : la grille garde autant de colonnes qu'il en tient.</summary>
    public static readonly AttachedProperty<double> MinItemWidthProperty =
        AvaloniaProperty.RegisterAttached<UniformGrid, double>("MinItemWidth", typeof(Adaptive));

    /// <summary>Fenêtre de dialogue : sa largeur se limite à celle de l'écran, moins une marge.</summary>
    public static readonly AttachedProperty<bool> FitDialogProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("FitDialog", typeof(Adaptive));

    public static double GetStackBelow(Grid g) => g.GetValue(StackBelowProperty);
    public static void SetStackBelow(Grid g, double v) => g.SetValue(StackBelowProperty, v);
    public static string? GetStackedRows(Grid g) => g.GetValue(StackedRowsProperty);
    public static void SetStackedRows(Grid g, string? v) => g.SetValue(StackedRowsProperty, v);
    public static double GetMinItemWidth(UniformGrid g) => g.GetValue(MinItemWidthProperty);
    public static void SetMinItemWidth(UniformGrid g, double v) => g.SetValue(MinItemWidthProperty, v);
    public static bool GetFitDialog(Control c) => c.GetValue(FitDialogProperty);
    public static void SetFitDialog(Control c, bool v) => c.SetValue(FitDialogProperty, v);

    private sealed class GridState
    {
        public bool Stacked;
        public bool Pending;
        public ColumnDefinitions Columns = [];
        public RowDefinitions Rows = [];
        public double RowSpacing;
        public readonly List<(Control Child, int Row, int Column, int RowSpan, int ColumnSpan)> Children = [];
    }

    private sealed class UniformState
    {
        public int Columns;
        public int Rows;
        public bool Pending;
    }

    private sealed class DialogState
    {
        public double Width;
        public double MinWidth;
        public double MaxWidth;
        public double MinHeight;
        public double MaxHeight;
        public Control? Host;
        public EventHandler<SizeChangedEventArgs>? OnHostSize;
    }

    private static readonly ConditionalWeakTable<Grid, GridState> Grids = [];
    private static readonly ConditionalWeakTable<UniformGrid, UniformState> Uniforms = [];
    private static readonly ConditionalWeakTable<Control, DialogState> Dialogs = [];

    static Adaptive()
    {
        StackBelowProperty.Changed.AddClassHandler<Grid>((grid, e) =>
        {
            grid.SizeChanged -= OnGridSize;
            if (e.NewValue is double v && v > 0) grid.SizeChanged += OnGridSize;
        });
        MinItemWidthProperty.Changed.AddClassHandler<UniformGrid>((grid, e) =>
        {
            grid.SizeChanged -= OnUniformSize;
            if (e.NewValue is double v && v > 0) grid.SizeChanged += OnUniformSize;
        });
        FitDialogProperty.Changed.AddClassHandler<Control>((dialog, e) =>
        {
            dialog.AttachedToVisualTree -= OnDialogAttached;
            dialog.DetachedFromVisualTree -= OnDialogDetached;
            if (Dialogs.TryGetValue(dialog, out var state)) Release(state);
            if (e.NewValue is true)
            {
                dialog.AttachedToVisualTree += OnDialogAttached;
                dialog.DetachedFromVisualTree += OnDialogDetached;
            }
        });
    }

    private static void OnGridSize(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || e.NewSize.Width <= 0) return;
        var state = Grids.GetValue(grid, _ => new GridState());
        if (state.Pending) return;
        state.Pending = true;
        // Changer la grille pendant sa propre mise en page la relancerait : le rangement attend le passage suivant.
        // Lire la dernière largeur évite qu'un ancien redimensionnement ne gagne sur le plus récent.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            if (grid.Bounds.Width > 0 && GetStackBelow(grid) > 0)
                Apply(grid, state, grid.Bounds.Width < GetStackBelow(grid));
        });
    }

    private static void Apply(Grid grid, GridState state, bool stack)
    {
        if (stack == state.Stacked) return;
        if (stack)
        {
            state.Columns = [.. grid.ColumnDefinitions.Select(c => new ColumnDefinition(c.Width))];
            state.Rows = [.. grid.RowDefinitions.Select(r => new RowDefinition(r.Height))];
            state.RowSpacing = grid.RowSpacing;
            state.Children.Clear();
            var children = grid.Children.OfType<Control>().ToList();
            foreach (var c in children)
                state.Children.Add((c, Grid.GetRow(c), Grid.GetColumn(c), Grid.GetRowSpan(c), Grid.GetColumnSpan(c)));
            // Ordre de lecture : ligne par ligne, mais un bloc qui couvre plusieurs lignes vient après les blocs
            // des lignes qu'il couvre (la saisie des notes après le cours et l'évaluation qu'elle concerne).
            var ordered = state.Children.OrderBy(c => c.Row + c.RowSpan - 1).ThenBy(c => c.Column).ToList();
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            var rows = GetStackedRows(grid) is { Length: > 0 } custom ? custom.Split(',') : [];
            grid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Range(0, ordered.Count).Select(i => i < rows.Length ? rows[i].Trim() : "Auto")));
            grid.RowSpacing = 12;
            for (var i = 0; i < ordered.Count; i++)
            {
                Grid.SetRow(ordered[i].Child, i);
                Grid.SetColumn(ordered[i].Child, 0);
                Grid.SetRowSpan(ordered[i].Child, 1);
                Grid.SetColumnSpan(ordered[i].Child, 1);
            }
        }
        else
        {
            grid.ColumnDefinitions = [.. state.Columns.Select(c => new ColumnDefinition(c.Width))];
            grid.RowDefinitions = [.. state.Rows.Select(r => new RowDefinition(r.Height))];
            grid.RowSpacing = state.RowSpacing;
            foreach (var (child, row, column, rowSpan, columnSpan) in state.Children)
            {
                Grid.SetRow(child, row);
                Grid.SetColumn(child, column);
                Grid.SetRowSpan(child, rowSpan);
                Grid.SetColumnSpan(child, columnSpan);
            }
        }
        state.Stacked = stack;
    }

    private static void OnUniformSize(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not UniformGrid grid || e.NewSize.Width <= 0) return;
        var state = Uniforms.GetValue(grid, g => new UniformState { Columns = g.Columns, Rows = g.Rows });
        if (state.Pending) return;
        state.Pending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            ApplyUniform(grid, state);
        });
    }

    private static void ApplyUniform(UniformGrid grid, UniformState state)
    {
        if (grid.Bounds.Width <= 0 || GetMinItemWidth(grid) <= 0) return;
        var fit = Math.Max(1, (int)(grid.Bounds.Width / GetMinItemWidth(grid)));
        var count = grid.Children.Count;
        int columns, rows;
        if (state.Rows == 1)
        {
            // Bande de chiffres sur une ligne : elle passe sur plusieurs lignes quand elle ne tient plus.
            if (count == 0 || fit >= count) { columns = state.Columns; rows = 1; }
            else { columns = fit; rows = 0; }
        }
        else
        {
            columns = state.Columns > 0 ? Math.Min(state.Columns, fit) : fit;
            rows = state.Rows;
        }
        if (grid.Columns == columns && grid.Rows == rows) return;
        grid.Columns = columns;
        grid.Rows = rows;
    }

    private static void OnDialogAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control dialog) return;
        var state = Dialogs.GetValue(dialog, d => new DialogState
        {
            Width = d.Width, MinWidth = d.MinWidth, MaxWidth = d.MaxWidth,
            MinHeight = d.MinHeight, MaxHeight = d.MaxHeight,
        });
        Release(state);
        // L'hôte est le voile qui couvre l'interface ; à défaut, la fenêtre ou l'écran du téléphone.
        state.Host = dialog.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("scrim")) as Control
            ?? TopLevel.GetTopLevel(dialog);
        if (state.Host is null) return;
        state.OnHostSize = (_, args) => Fit(dialog, state, args.NewSize);
        state.Host.SizeChanged += state.OnHostSize;
        Fit(dialog, state, state.Host.Bounds.Size);
    }

    private static void OnDialogDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control dialog && Dialogs.TryGetValue(dialog, out var state)) Release(state);
    }

    private static void Release(DialogState state)
    {
        if (state.Host is not null && state.OnHostSize is not null) state.Host.SizeChanged -= state.OnHostSize;
        state.Host = null;
        state.OnHostSize = null;
    }

    private static void Fit(Control dialog, DialogState state, Size host)
    {
        if (host.Width <= 0) return;
        // Les marges du contenu intermédiaire font partie de la place à réserver, même sur le bureau.
        var horizontalMargin = dialog.Margin.Left + dialog.Margin.Right;
        var verticalMargin = dialog.Margin.Top + dialog.Margin.Bottom;
        foreach (var ancestor in dialog.GetVisualAncestors().OfType<Control>())
        {
            if (ReferenceEquals(ancestor, state.Host)) break;
            horizontalMargin += ancestor.Margin.Left + ancestor.Margin.Right;
            verticalMargin += ancestor.Margin.Top + ancestor.Margin.Bottom;
        }
        var room = Math.Max(0, host.Width - Math.Max(24, horizontalMargin));
        if (!double.IsNaN(state.Width)) dialog.Width = Math.Min(state.Width, room);
        dialog.MinWidth = Math.Min(state.MinWidth, room);
        dialog.MaxWidth = Math.Min(state.MaxWidth, room);
        // En hauteur aussi : une fiche plus haute que la fenêtre défile au lieu d'être coupée.
        if (host.Height > 0)
        {
            var height = Math.Max(0, host.Height - Math.Max(48, verticalMargin));
            dialog.MinHeight = Math.Min(state.MinHeight, height);
            dialog.MaxHeight = Math.Min(state.MaxHeight, height);
        }
    }
}
