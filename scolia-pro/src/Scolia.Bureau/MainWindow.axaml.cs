using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Controls.Primitives.PopupPositioning;
using Material.Icons;
using Material.Icons.Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau;

/// <summary>Fenêtre principale : relie les commandes clavier, le plein écran et le sélecteur de fichiers au shell.</summary>
public partial class MainWindow : Window
{
    /// <summary>Durée du fondu entre deux pages : assez courte pour ne pas ralentir la saisie.</summary>
    private static readonly TimeSpan PageFade = TimeSpan.FromMilliseconds(180);

    public MainWindow()
    {
        InitializeComponent();
        // Toute l'interface suit la taille de la fenêtre, sans chevauchement ni taille figée.
        SizeChanged += (_, e) => FitWindow(e.NewSize);
        PageBar.SizeChanged += (_, _) => FitPageActions();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ShellViewModel shell) return;
            shell.Prefs.LayoutScaleChanged += _ => FitWindow(ClientSize);
            shell.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ShellViewModel.Current)) return;
                if (shell.Current is { } page)
                {
                    page.Actions.CollectionChanged -= OnActionsChanged;
                    page.Actions.CollectionChanged += OnActionsChanged;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(FitPageActions, Avalonia.Threading.DispatcherPriority.Loaded);
            };
            FitWindow(ClientSize);
            shell.FullscreenHandler = full => WindowState = full ? WindowState.FullScreen : WindowState.Normal;
            shell.PickFileHandler = PickFileAsync;
            ApplyPageTransition(shell.Prefs);
            shell.Prefs.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Preferences.Animations)) ApplyPageTransition(shell.Prefs);
            };
        };
    }

    /// <summary>Largeur, en points de la mise en page, sous laquelle le volet se replie pour laisser la place à la page.</summary>
    private const double NarrowBelow = 1200;

    /// <summary>Place occupée dans l'en-tête par le bouton du volet, les flèches, l'icône, le menu et les marges.</summary>
    private const double PageBarFixed = 290;

    /// <summary>Met toute l'interface à l'échelle de la fenêtre, puis replie le volet si la page manque de place.</summary>
    private void FitWindow(Size size)
    {
        if (Shell is not { } shell || size.Width <= 0) return;
        shell.Prefs.Fit(size.Width, size.Height);
        var k = shell.Prefs.LayoutScale;
        Scaler.LayoutTransform = Math.Abs(k - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(k, k);
        shell.Narrow = size.Width / k < NarrowBelow;
    }

    private void OnActionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(FitPageActions, Avalonia.Threading.DispatcherPriority.Loaded);

    /// <summary>
    /// Garde dans l'en-tête autant de boutons que la largeur le permet, à côté d'un titre lisible.
    /// Les autres passent dans le menu « … ».
    /// </summary>
    private void FitPageActions()
    {
        if (Shell?.Current is not { } page || PageBar.Bounds.Width <= 0) return;
        var font = Shell.Prefs.Factor * 13.5;
        var title = Math.Clamp(TextWidth(page.Heading, 17 * Shell.Prefs.Factor, FontWeight.Bold) + 8, 140, 260);
        var room = PageBar.Bounds.Width - PageBarFixed - title;
        var widths = page.Actions.Select(a => TextWidth(a.Label, font, FontWeight.Normal) + 58).ToList();
        const double more = 46;
        int count;
        if (widths.Count <= 4 && widths.Sum() <= room) count = widths.Count;
        else
        {
            count = 0;
            for (var k = Math.Min(3, widths.Count - 1); k > 0; k--)
                if (widths.Take(k).Sum() + more <= room) { count = k; break; }
        }
        page.FitActions(count);
    }

    private double TextWidth(string text, double size, FontWeight weight) =>
        new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, weight), size, null).Width;

    /// <summary>Fondu entre les pages, désactivé par la préférence « Animations ».</summary>
    private void ApplyPageTransition(Preferences prefs) =>
        PageHost.PageTransition = prefs.Animations ? new CrossFade(PageFade) : null;

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void OnTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: RibbonTabVm tab } && Shell is { } shell) shell.SelectTab(tab);
    }

    private void OnToggleRibbon(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is { } shell) shell.Prefs.RibbonCollapsed = !shell.Prefs.RibbonCollapsed;
    }

    private void OnZoomIn(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is { } shell) shell.Prefs.Scale += 0.1;
    }

    private void OnZoomOut(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is { } shell) shell.Prefs.Scale -= 0.1;
    }

    /// <summary>
    /// Menu de toutes les commandes : chaque métier en sous-menu avec ses commandes et leurs raccourcis,
    /// puis le compte et la déconnexion. Il est construit à l'ouverture, d'après les droits et l'écran affiché.
    /// </summary>
    private void OnMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is not { } shell || sender is not Control anchor) return;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var tab in shell.Tabs.Where(t => t.Id != "fichier"))
        {
            var sub = new MenuItem { Header = tab.Title, Icon = Glyph(TabIcon(tab.Id)) };
            var first = true;
            foreach (var group in tab.Groups.Where(g => g.Title != "Récents"))
            {
                if (!first) sub.Items.Add(new Separator());
                first = false;
                foreach (var command in group.Commands)
                    sub.Items.Add(new MenuItem { Header = command.Label, Icon = Glyph(command.Icon), Command = command.Command, InputGesture = command.Gesture });
            }
            menu.Items.Add(sub);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Rechercher…", Icon = Glyph(MaterialIconKind.Magnify), Command = shell.SearchCommand, InputGesture = new KeyGesture(Key.K, KeyModifiers.Control) });
        menu.Items.Add(new MenuItem { Header = "Précédent", Icon = Glyph(MaterialIconKind.ChevronLeft), Command = shell.BackCommand, InputGesture = new KeyGesture(Key.Left, KeyModifiers.Alt) });
        menu.Items.Add(new MenuItem { Header = "Suivant", Icon = Glyph(MaterialIconKind.ChevronRight), Command = shell.ForwardCommand, InputGesture = new KeyGesture(Key.Right, KeyModifiers.Alt) });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Compte et poste…", Icon = Glyph(MaterialIconKind.AccountCircleOutline), Command = new RelayCommand(shell.OpenBackstage) });
        menu.Items.Add(new MenuItem { Header = "Se déconnecter", Icon = Glyph(MaterialIconKind.Logout), Command = new AsyncCommand(shell.LogoutAsync) });
        menu.ShowAt(anchor);
    }

    private static MaterialIcon Glyph(MaterialIconKind kind) => new() { Kind = kind, Width = 16, Height = 16 };

    private static MaterialIconKind TabIcon(string id) => id switch
    {
        "accueil" => MaterialIconKind.HomeOutline,
        "secretariat" => MaterialIconKind.AccountGroupOutline,
        "finances" => MaterialIconKind.CashMultiple,
        "scolarite" => MaterialIconKind.SchoolOutline,
        "administration" => MaterialIconKind.OfficeBuildingOutline,
        "donnees" => MaterialIconKind.DatabaseOutline,
        "affichage" => MaterialIconKind.MonitorEye,
        "aide" => MaterialIconKind.HelpCircleOutline,
        _ => MaterialIconKind.FileDocumentOutline,
    };

    private void OnHelpClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is { } shell) _ = shell.Run(() => ShortcutsDialog.ShowAsync(shell));
    }

    private void OnAccountClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Shell?.OpenBackstage();

    /// <summary>Raccourcis : F5 actualiser, Ctrl+K recherche, F2 encaisser, Ctrl+P imprimer, Alt+Gauche et Alt+Droite pour naviguer, Échap fermer.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Shell is not { IsLoggedIn: true } shell) return;
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Left or Key.Right && !shell.HasDialog)
        {
            if (e.Key == Key.Left) shell.GoBack();
            else shell.GoForward();
            e.Handled = true;
            return;
        }
        // Les raccourcis des commandes du ruban passent d'abord.
        if (shell.RunShortcut(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape) return;
        if (shell.Dialog is FormViewModel { Busy: false } form) form.Cancel.Execute(null);
        else if (shell.HasDialog) shell.CloseDialog();
        else if (shell.BackstageOpen) shell.CloseBackstage();
        e.Handled = true;
    }

    private async Task<PickedFile?> PickFileAsync(IReadOnlyList<string> extensions)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Fichiers acceptés") { Patterns = extensions.Select(e => "*" + e).ToList() }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return new PickedFile(files[0].Name, memory.ToArray());
    }
}
