using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Menus contextuels des tableaux, sur toutes les pages : le clic droit choisit la ligne et propose ses actions,
/// puis la copie de la cellule ou de la ligne ; le double-clic lance l'action principale de la ligne.
/// Les actions viennent de la page : celles marquées « de ligne » sont proposées ici.
/// </summary>
public static class RowMenu
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enabled", typeof(RowMenu));

    static RowMenu()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            if (e.NewValue is not true) return;
            grid.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            grid.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            grid.DoubleTapped += OnDoubleTapped;
            grid.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        });
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    /// <summary>Clic droit : la ligne visée devient la ligne choisie, avant que le menu ne s'ouvre.</summary>
    private static void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || !e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
        if (Find<DataGridRow>(e.Source) is { DataContext: { } item }) grid.SelectedItem = item;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not DataGrid grid || e.InitialPressMouseButton != MouseButton.Right) return;
        if (Find<DataGridRow>(e.Source) is not { } row) return;
        Show(grid, row, Find<DataGridCell>(e.Source));
        e.Handled = true;
    }

    /// <summary>Ouvre le menu d'une ligne : ses actions, la copie, puis les actions de la page.</summary>
    public static MenuFlyout Show(DataGrid grid, DataGridRow row, DataGridCell? target, bool atPointer = true)
    {
        var page = PageOf(grid);
        var menu = new MenuFlyout();
        if (page is not null)
            foreach (var action in page.RowActions) menu.Items.Add(Item(action, action == page.DefaultRowAction));
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());

        var cell = CellText(target);
        if (cell.Length > 0) menu.Items.Add(Item($"Copier « {Shorten(cell)} »", MaterialIconKind.ContentCopy, () => CopyAsync(grid, cell)));
        var line = string.Join('\t', row.GetVisualDescendants().OfType<DataGridCell>().Where(c => c.IsVisible).Select(CellText));
        menu.Items.Add(Item("Copier la ligne", MaterialIconKind.ContentCopy, () => CopyAsync(grid, line)));

        // Les actions générales de la page restent à portée, après celles de la ligne.
        if (page is not null && page.PageActions.Count > 0)
        {
            var more = new MenuItem { Header = "Actions de la page", Icon = Icon(MaterialIconKind.DotsHorizontal) };
            foreach (var action in page.PageActions) more.Items.Add(Item(action, false));
            menu.Items.Add(new Separator());
            menu.Items.Add(more);
        }
        if (!atPointer) menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        menu.ShowAt(row, atPointer);
        return menu;
    }

    /// <summary>Double-clic sur une ligne : l'action principale, sauf dans une case à cocher ou une liste de choix.</summary>
    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not DataGrid grid || Find<DataGridRow>(e.Source) is null) return;
        if (Find<CheckBox>(e.Source) is not null || Find<ComboBox>(e.Source) is not null || Find<TextBox>(e.Source) is not null) return;
        if (PageOf(grid)?.DefaultRowAction is { } action && action.Command.CanExecute(null)) action.Command.Execute(null);
    }

    /// <summary>
    /// Clavier : Entrée lance l'action principale de la ligne choisie, la touche Menu ou Maj+F10 ouvre son menu.
    /// Une case à cocher ou une liste de choix en cours d'édition garde ses propres touches.
    /// </summary>
    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || grid.SelectedItem is null) return;
        if (Find<CheckBox>(e.Source) is not null || Find<ComboBox>(e.Source) is not null || Find<TextBox>(e.Source) is not null) return;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (PageOf(grid)?.DefaultRowAction is not { } action || !action.Command.CanExecute(null)) return;
            action.Command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => ReferenceEquals(r.DataContext, grid.SelectedItem));
            if (row is null) return;
            Show(grid, row, null, atPointer: false);
            e.Handled = true;
        }
    }

    /// <summary>Menu de la page, au clic droit hors des lignes : ses actions générales.</summary>
    public static MenuFlyout? PageMenu(PageViewModel page)
    {
        if (page.PageActions.Count == 0) return null;
        var menu = new MenuFlyout();
        foreach (var action in page.PageActions) menu.Items.Add(Item(action, action.IsPrimary));
        return menu;
    }

    private static MenuItem Item(ActionVm action, bool emphasis)
    {
        var item = new MenuItem { Header = action.Label, Icon = Icon(action.Icon), Command = action.Command };
        if (emphasis) item.FontWeight = FontWeight.SemiBold;
        if (action.Danger) item.Foreground = ThemeBrush.Get("ToneRed");
        return item;
    }

    private static MenuItem Item(string label, MaterialIconKind icon, Func<Task> run) =>
        new() { Header = label, Icon = Icon(icon), Command = new AsyncCommand(run) };

    private static MaterialIcon Icon(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    private static PageViewModel? PageOf(Control grid) =>
        grid.DataContext as PageViewModel ?? grid.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<PageViewModel>().FirstOrDefault();

    private static T? Find<T>(object? source) where T : class =>
        source as T ?? (source as Visual)?.GetVisualAncestors().OfType<T>().FirstOrDefault();

    private static string CellText(DataGridCell? cell) => cell is null ? string.Empty
        : string.Join(' ', cell.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))).Trim();

    private static string Shorten(string text) => text.Length <= 28 ? text : text[..27] + "…";

    private static async Task CopyAsync(Visual anchor, string text)
    {
        if (TopLevel.GetTopLevel(anchor)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
