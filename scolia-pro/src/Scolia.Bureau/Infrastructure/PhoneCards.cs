using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Tableaux du téléphone : chaque ligne devient une carte, lisible en entier sans défiler de côté. Le titre de la carte
/// est la colonne principale du tableau, le montant se place à sa droite, les autres colonnes suivent en lignes
/// « libellé : valeur ». Toucher une carte choisit la ligne ; le bouton « … » ou l'appui long ouvre ses actions,
/// le double appui lance l'action principale, comme le double-clic sur l'ordinateur.
/// Le tableau reste le même contrôle : ses liaisons, sa sélection et ses actions de page ne changent pas.
/// </summary>
public static class PhoneCards
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("Enabled", typeof(PhoneCards));

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    /// <summary>En-têtes de colonnes qui portent un montant : il se place à droite du titre, en gras.</summary>
    private static readonly string[] AmountHeaders = ["Montant", "Solde", "Reste", "Total", "Net", "Payé", "Encaissé", "Salaire"];

    static PhoneCards()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>((grid, e) =>
        {
            if (e.NewValue is not true || !PhoneLayout.Active) return;
            grid.Template = new FuncControlTemplate<DataGrid>((g, _) => List(g));
            // Sur l'ordinateur, la hauteur d'un tableau est bornée ; sur le téléphone, c'est la page entière qui défile.
            grid.SetValue(Layoutable.MaxHeightProperty, double.PositiveInfinity, BindingPriority.Animation);
            grid.SetValue(Layoutable.MinHeightProperty, 0d, BindingPriority.Animation);
            grid.SetValue(TemplatedControl.BackgroundProperty, Brushes.Transparent, BindingPriority.Animation);
        });
    }

    /// <summary>Largeur sous laquelle les cartes restent sur une colonne : un téléphone, même en largeur.</summary>
    private const double TwoColumnsFrom = 680;
    private const double ThreeColumnsFrom = 1080;

    private static Control List(DataGrid grid)
    {
        var list = new ItemsControl { ItemTemplate = new FuncDataTemplate<object>((_, _) => Card(grid)) };
        UseColumns(list, 1);
        // Une tablette range les cartes sur deux ou trois colonnes ; un téléphone les garde sur une seule.
        list.SizeChanged += (_, e) =>
        {
            var columns = e.NewSize.Width >= ThreeColumnsFrom ? 3 : e.NewSize.Width >= TwoColumnsFrom ? 2 : 1;
            if (columns != (list.Tag as int? ?? 1)) Avalonia.Threading.Dispatcher.UIThread.Post(() => UseColumns(list, columns));
        };
        // Les cartes s'affichent par paquets : les premières tout de suite, les suivantes à la demande, en bas de la liste.
        // Une longue liste s'ouvre ainsi vite même sur un téléphone modeste, sans liste virtualisée qui ferait sauter la page.
        var shown = new System.Collections.ObjectModel.ObservableCollection<object?>();
        list.ItemsSource = shown;
        var more = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 8), IsVisible = false };
        more.Classes.Add("phoneshowmore");
        var limit = FirstCards;
        System.Collections.IEnumerable? source = null;
        var pending = false;
        void Fill()
        {
            pending = false;
            var all = source?.Cast<object?>().ToList() ?? [];
            // La liste s'est vidée puis remplie (actualisation) : on garde au moins ce que l'utilisateur avait déjà affiché.
            var count = Math.Min(all.Count, Math.Max(limit, FirstCards));
            CollectionSync.Apply(shown, all.Take(count));
            var left = all.Count - shown.Count;
            more.IsVisible = left > 0;
            more.Content = $"Afficher {Math.Min(left, MoreCards)} de plus · {Format.Count(left, "restant", "restants")}";
        }
        void Later() { if (pending) return; pending = true; Avalonia.Threading.Dispatcher.UIThread.Post(Fill, Avalonia.Threading.DispatcherPriority.Background); }
        void OnChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Later();
        void Attach(System.Collections.IEnumerable? items)
        {
            if (source is System.Collections.Specialized.INotifyCollectionChanged old) old.CollectionChanged -= OnChanged;
            source = items;
            if (source is System.Collections.Specialized.INotifyCollectionChanged now) now.CollectionChanged += OnChanged;
            limit = FirstCards;
            shown.Clear();
            Fill();
        }
        more.Click += (_, _) => { limit = shown.Count + MoreCards; Fill(); };
        grid.PropertyChanged += (_, e) => { if (e.Property == DataGrid.ItemsSourceProperty) Attach(grid.ItemsSource); };
        Attach(grid.ItemsSource);
        return new StackPanel { Children = { list, more } };
    }

    /// <summary>Cartes affichées à l'ouverture d'une liste, puis à chaque « Afficher plus ».</summary>
    private const int FirstCards = 40;
    private const int MoreCards = 40;

    private static void UseColumns(ItemsControl list, int columns)
    {
        list.Tag = columns;
        // Pas de liste virtualisée : dans la page qui défile en entier, elle estimait la hauteur des cartes encore
        // cachées puis la corrigeait en cours de route, et la page sautait ou glissait seule sous le doigt.
        // Toutes les cartes sont mesurées au chargement : la hauteur de la page ne change plus pendant le défilement.
        list.ItemsPanel = columns == 1
            ? new FuncTemplate<Panel?>(() => new StackPanel())
            : new FuncTemplate<Panel?>(() => new CardColumns { Columns = columns });
    }

    private static Control Card(DataGrid grid)
    {
        var columns = grid.Columns.ToList();
        // Une colonne de cases à cocher, qu'elle soit une colonne de cases ou un modèle qui ne contient qu'une case,
        // se place à gauche de la carte, comme dans les listes à cocher des Réglages.
        var checks = columns.Where(IsCheck).ToList();
        var others = columns.Where(c => !IsCheck(c)).ToList();
        var title = others.FirstOrDefault(c => c.Width.IsStar) ?? others.FirstOrDefault();
        var amount = others.FirstOrDefault(c => c != title && IsAmount(c));
        var subtitle = title is null ? null : others.Take(others.IndexOf(title)).OfType<DataGridTextColumn>().FirstOrDefault();
        var fields = others.Where(c => c != title && c != amount && c != subtitle).ToList();

        var body = new StackPanel { Spacing = 3 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        if (title is not null)
        {
            var main = Value(grid, title, emphasis: true);
            head.Children.Add(main);
        }
        if (amount is not null)
        {
            var money = Value(grid, amount, emphasis: true);
            money.Margin = new Thickness(10, 0, 0, 0);
            money.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(money, 1);
            head.Children.Add(money);
        }
        body.Children.Add(head);
        if (subtitle is not null)
        {
            var sub = Value(grid, subtitle, emphasis: false);
            sub.Classes.Add("phonesub");
            body.Children.Add(sub);
        }
        foreach (var column in fields)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 0) };
            var label = new TextBlock { Text = HeaderText(column), TextWrapping = TextWrapping.Wrap, MaxWidth = 150, VerticalAlignment = VerticalAlignment.Top };
            label.Classes.Add("phonelabel");
            var value = Value(grid, column, emphasis: false);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.Margin = new Thickness(12, 0, 0, 0);
            if (value is TextBlock text) text.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(value, 1);
            line.Children.Add(label);
            line.Children.Add(value);
            body.Children.Add(line);
        }

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (checks.Count > 0)
        {
            var boxes = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            foreach (var column in checks)
                boxes.Children.Add(column is DataGridCheckBoxColumn box ? Check(grid, box) : ((DataGridTemplateColumn)column).CellTemplate!.Build(null)!);
            layout.Children.Add(boxes);
        }
        Grid.SetColumn(body, 1);
        layout.Children.Add(body);

        var card = new Border { Child = layout };
        card.Classes.Add("phonecard");

        var page = PageOf(grid);
        if (page is { RowActions.Count: > 0 })
        {
            var more = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.DotsHorizontal, Width = 20, Height = 20 }, VerticalAlignment = VerticalAlignment.Top };
            more.Classes.Add("phonemore");
            more.Click += (_, e) =>
            {
                Select(grid, card);
                ShowRowSheet(grid, card);
                e.Handled = true;
            };
            Grid.SetColumn(more, 2);
            layout.Children.Add(more);
        }

        card.Tapped += (_, e) =>
        {
            if (FromInput(e.Source)) return;
            Select(grid, card);
        };
        card.DoubleTapped += (_, e) =>
        {
            if (FromInput(e.Source)) return;
            Select(grid, card);
            if (PageOf(grid)?.DefaultRowAction is { } action && action.Command.CanExecute(null)) action.Command.Execute(null);
            e.Handled = true;
        };
        card.AddHandler(InputElement.HoldingEvent, (object? _, HoldingRoutedEventArgs e) =>
        {
            if (e.HoldingState != HoldingState.Started || FromInput(e.Source)) return;
            Select(grid, card);
            ShowRowSheet(grid, card);
            e.Handled = true;
        });

        // La carte de la ligne choisie est marquée, comme la ligne bleue du tableau.
        void Mark(object? s, SelectionChangedEventArgs a) => card.Classes.Set("selected", card.DataContext is not null && ReferenceEquals(grid.SelectedItem, card.DataContext));
        card.AttachedToVisualTree += (_, _) => { grid.SelectionChanged += Mark; Mark(null, null!); };
        card.DetachedFromVisualTree += (_, _) => grid.SelectionChanged -= Mark;
        card.DataContextChanged += (_, _) => Mark(null, null!);
        return card;
    }

    private static Control Value(DataGrid grid, DataGridColumn column, bool emphasis)
    {
        switch (column)
        {
            case DataGridTextColumn text when Editable(grid, column):
            {
                var box = new TextBox { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
                if (text.Binding is { } b) box.Bind(TextBox.TextProperty, b);
                return box;
            }
            case DataGridTextColumn text:
            {
                var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
                if (emphasis) block.Classes.Add("phonetitle");
                if (text.Binding is { } b) block.Bind(TextBlock.TextProperty, b);
                return block;
            }
            case DataGridTemplateColumn template when template.CellTemplate?.Build(null) is { } cell:
                return cell;
            default:
                return new TextBlock { Text = HeaderText(column), TextWrapping = TextWrapping.Wrap };
        }
    }

    private static CheckBox Check(DataGrid grid, DataGridCheckBoxColumn column)
    {
        var box = new CheckBox { IsEnabled = Editable(grid, column), MinWidth = 0, Padding = new Thickness(0) };
        ToolTip.SetTip(box, HeaderText(column));
        if (column.Binding is { } b) box.Bind(ToggleButton.IsCheckedProperty, b);
        return box;
    }

    private static bool Editable(DataGrid grid, DataGridColumn column) => !grid.IsReadOnly && !column.IsReadOnly;

    /// <summary>Nature de chaque colonne, établie une fois : le modèle n'est pas reconstruit pour chaque carte.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataGridColumn, object> CheckColumns = [];

    private static bool IsCheck(DataGridColumn column) =>
        (bool)CheckColumns.GetValue(column, c =>
            c is DataGridCheckBoxColumn || c is DataGridTemplateColumn { CellTemplate: { } template } && template.Build(null) is CheckBox);

    private static bool IsAmount(DataGridColumn column) =>
        HeaderText(column) is var header && AmountHeaders.Any(h => header.StartsWith(h, StringComparison.OrdinalIgnoreCase));

    private static string HeaderText(DataGridColumn column) => column.Header as string ?? column.Header?.ToString() ?? string.Empty;

    private static void Select(DataGrid grid, Control card)
    {
        if (card.DataContext is { } item && !ReferenceEquals(grid.SelectedItem, item)) grid.SelectedItem = item;
    }

    /// <summary>Un appui dans un champ, une case ou une liste de la carte agit sur ce contrôle, pas sur la carte.</summary>
    private static bool FromInput(object? source) =>
        source is Visual v && (v is TextBox or CheckBox or ComboBox or Button
            || v.GetVisualAncestors().Any(a => a is TextBox or CheckBox or ComboBox or Button));

    /// <summary>Feuille des actions de la ligne : celles de la page marquées « de ligne », puis la copie de la ligne.</summary>
    private static void ShowRowSheet(DataGrid grid, Control card)
    {
        if (PageOf(grid) is not { } page || ShellOf(grid) is not { } shell) return;
        var line = string.Join(" · ", card.GetVisualDescendants().OfType<TextBlock>().Where(t => !t.Classes.Contains("phonelabel"))
            .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        var title = card.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("phonetitle"))?.Text ?? page.Title;
        var items = page.RowActions.Select(a => (a.Label, a.Icon, a.Command, a.Danger, a == page.DefaultRowAction)).ToList();
        items.Add(("Copier la ligne", MaterialIconKind.ContentCopy, new AsyncCommand(async () =>
        {
            if (TopLevel.GetTopLevel(grid)?.Clipboard is { } clipboard) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, line);
        }), false, false));
        shell.ShowSheet(title, page.Title, items);
    }

    private static PageViewModel? PageOf(Control grid) =>
        grid.DataContext as PageViewModel ?? grid.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<PageViewModel>().FirstOrDefault();

    private static ShellViewModel? ShellOf(Control grid) =>
        grid.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ShellViewModel>().FirstOrDefault();
}

/// <summary>
/// Cartes en colonnes, comme une mosaïque : chaque carte se place sous la colonne la moins haute,
/// pour qu'aucun trou ne reste entre des cartes de hauteurs différentes.
/// </summary>
public sealed class CardColumns : Panel
{
    public int Columns { get; set; } = 2;

    /// <summary>Espace entre deux colonnes ; l'espace vertical vient de la marge des cartes.</summary>
    private const double Gap = 10;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ColumnWidth(availableSize.Width);
        var heights = new double[Columns];
        foreach (var child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var shortest = Array.IndexOf(heights, heights.Min());
            heights[shortest] += child.DesiredSize.Height;
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width * Columns : availableSize.Width, heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = ColumnWidth(finalSize.Width);
        var heights = new double[Columns];
        foreach (var child in Children)
        {
            var shortest = Array.IndexOf(heights, heights.Min());
            child.Arrange(new Rect(shortest * (width + Gap), heights[shortest], width, child.DesiredSize.Height));
            heights[shortest] += child.DesiredSize.Height;
        }
        return finalSize;
    }

    private double ColumnWidth(double total) =>
        double.IsInfinity(total) ? 340 : Math.Max(0, (total - Gap * (Columns - 1)) / Columns);
}
