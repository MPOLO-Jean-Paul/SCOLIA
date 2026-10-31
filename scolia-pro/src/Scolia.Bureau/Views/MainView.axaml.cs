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

namespace Scolia.Bureau.Views;

/// <summary>
/// Toute l'interface de SCOLIA : connexion, volet, pages, fenêtres et barre d'état. La même vue sert dans la fenêtre
/// de l'ordinateur et en plein écran sur le téléphone, où elle montre le cadre du téléphone à la place du volet.
/// Elle relie le clavier, le sélecteur de fichiers et le presse-papiers au shell, et adapte la mise en page à la place disponible.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>Durée du fondu entre deux pages : assez courte pour ne pas ralentir la saisie.</summary>
    private static readonly TimeSpan PageFade = TimeSpan.FromMilliseconds(135);

    private ShellViewModel? subscribedShell;
    private PageViewModel? observedPage;
    private TopLevel? backHost;
    private bool fitActionsQueued;
    private bool fittingWindow;
    private bool themeChanging;
    private CancellationTokenSource? themeFadeCancellation;
    private readonly Func<IReadOnlyList<string>, Task<PickedFile?>> pickFileHandler;
    private readonly Func<string, Task> copyHandler;
    private readonly Func<bool> isTypingHandler;

    public MainView()
    {
        pickFileHandler = PickFileAsync;
        copyHandler = CopyAsync;
        isTypingHandler = IsTypingInView;
        InitializeComponent();
        SizeChanged += (_, e) => FitWindow(e.NewSize);
        AttachedToVisualTree += (_, _) =>
        {
            ShowThemeIcon();
            ReleaseBackHost();
            backHost = TopLevel.GetTopLevel(this);
            if (backHost is not null) backHost.BackRequested += OnBackRequested;
            AttachShell();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            themeFadeCancellation?.Cancel();
            ReleaseBackHost();
            ReleaseShell();
        };
        ActualThemeVariantChanged += (_, _) => ShowThemeIcon();
        PageBar.SizeChanged += (_, _) => QueueFitPageActions();
        PageHost.AddHandler(PointerReleasedEvent, OnPagePointerReleased, Avalonia.Interactivity.RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => AttachShell();
    }

    private void ReleaseBackHost()
    {
        if (backHost is not null) backHost.BackRequested -= OnBackRequested;
        backHost = null;
    }

    private void AttachShell()
    {
        themeFadeCancellation?.Cancel();
        ReleaseShell();
        if (Shell is not { } shell) return;
        subscribedShell = shell;
        shell.Prefs.LayoutScaleChanged += OnLayoutScaleChanged;
        shell.Prefs.PropertyChanged += OnPreferencesChanged;
        shell.PropertyChanged += OnShellChanged;
        shell.PickFileHandler = pickFileHandler;
        shell.IsTyping = isTypingHandler;
        shell.CopyHandler = copyHandler;
        WatchPage(shell.Current);
        ApplyPageTransition(shell.Prefs);
        FitWindow(Bounds.Size);
        QueueFitPageActions();
    }

    private void ReleaseShell()
    {
        WatchPage(null);
        if (subscribedShell is { } shell)
        {
            shell.Prefs.LayoutScaleChanged -= OnLayoutScaleChanged;
            shell.Prefs.PropertyChanged -= OnPreferencesChanged;
            shell.PropertyChanged -= OnShellChanged;
            if (shell.PickFileHandler == pickFileHandler) shell.PickFileHandler = null;
            if (shell.IsTyping == isTypingHandler) shell.IsTyping = null;
            if (shell.CopyHandler == copyHandler) shell.CopyHandler = null;
        }
        subscribedShell = null;
    }

    private void WatchPage(PageViewModel? page)
    {
        if (observedPage is not null)
        {
            observedPage.Actions.CollectionChanged -= OnActionsChanged;
            observedPage.PropertyChanged -= OnPageTextChanged;
        }
        observedPage = page;
        if (page is null) return;
        page.Actions.CollectionChanged += OnActionsChanged;
        page.PropertyChanged += OnPageTextChanged;
    }

    private void OnLayoutScaleChanged(double _) => FitWindow(Bounds.Size);

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (subscribedShell is not { } shell) return;
        if (args.PropertyName is nameof(Preferences.Animations) or nameof(Preferences.PageTransitions) or nameof(Preferences.Speed))
            ApplyPageTransition(shell.Prefs);
        // Ne pas mesurer de nouveau l'en-tête quand un son, une animation ou
        // une préférence sans incidence géométrique est modifié.
        if (args.PropertyName is nameof(Preferences.Scale) or nameof(Preferences.Font)
            or nameof(Preferences.Compact) or nameof(Preferences.Sidebar)
            or nameof(Preferences.RibbonCollapsed))
            QueueFitPageActions();
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (subscribedShell is not { } shell) return;
        if (args.PropertyName == nameof(ShellViewModel.CompactWindow)) FitWindow(Bounds.Size);
        if (args.PropertyName != nameof(ShellViewModel.Current)) return;
        WatchPage(shell.Current);
        QueueFitPageActions();
        PageScroll.Offset = default;
    }

    /// <summary>La recherche desktop suit le contexte : filtre local sur une liste,
    /// palette globale uniquement sur l'accueil et sur les écrans sans filtre.</summary>
    private void OnDesktopSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is not { } shell) return;
        if (PhoneHeaderSearchVm.Scope(shell.Current).Length == 0)
        {
            shell.ShowSearch();
            return;
        }
        shell.HeaderSearch.Toggle.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Shell == shell && shell.HeaderSearch.IsOpen)
                DesktopContextSearchBox.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnDesktopContextSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Shell is not { } shell) return;
        shell.HeaderSearch.Close.Execute(null);
        e.Handled = true;
    }

    private bool IsTypingInView() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }

    private void QueueFitPageActions()
    {
        if (fitActionsQueued) return;
        fitActionsQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            fitActionsQueued = false;
            if (subscribedShell is not null) FitPageActions();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Largeur, en points de la mise en page, sous laquelle le volet se replie pour laisser la place à la page.</summary>
    private const double NarrowBelow = 1200;

    /// <summary>Place occupée dans l'en-tête par le bouton du volet, les flèches, le menu et les marges.</summary>
    private const double PageBarFixed = 250;

    /// <summary>Largeur, en points de la mise en page, sous laquelle l'écran est traité comme un téléphone.</summary>
    private const double PhoneBelow = 720;

    /// <summary>Bascule du thème clair au thème sombre : l'ancien rendu s'efface en fondu sur le nouveau.</summary>
    private async void OnThemeToggle(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is not { } shell || themeChanging) return;
        var next = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "light" : "dark";
        var size = Scaler.Bounds.Size;
        // Une capture bitmap du plein écran est coûteuse sur Android : la plateforme
        // applique le thème directement, sans surcharge GPU ni gel de la saisie.
        if (shell.Phone || Platform.IsMobile || !shell.Prefs.Animations || size.Width <= 0 || size.Height <= 0)
        {
            shell.Prefs.Theme = next;
            return;
        }
        themeChanging = true;
        using var cancellation = new CancellationTokenSource();
        themeFadeCancellation = cancellation;
        Avalonia.Media.Imaging.RenderTargetBitmap? snapshot = null;
        try
        {
            // Le fondu reste léger sur les écrans à haute densité : au plus 32 Mo de pixels.
            var scaling = Math.Min(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1,
                Math.Sqrt(8_000_000 / (size.Width * size.Height)));
            snapshot = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scaling)), Math.Max(1, (int)Math.Ceiling(size.Height * scaling))),
                new Vector(96 * scaling, 96 * scaling));
            snapshot.Render(Scaler);
            ThemeFade.Source = snapshot;
            ThemeFade.Opacity = 1;
            ThemeFade.IsVisible = true;
            shell.Prefs.Theme = next;
            var fade = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(220) * shell.Prefs.SpeedFactor,
                Easing = new Avalonia.Animation.Easings.QuadraticEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } },
                },
            };
            await fade.RunAsync(ThemeFade, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // Une plateforme sans capture garde une bascule immédiate et opérationnelle.
            shell.Prefs.Theme = next;
        }
        finally
        {
            ThemeFade.IsVisible = false;
            ThemeFade.Source = null;
            snapshot?.Dispose();
            themeFadeCancellation = null;
            themeChanging = false;
        }
    }

    private void ShowThemeIcon() =>
        ThemeIcon.Kind = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? MaterialIconKind.WeatherSunny : MaterialIconKind.WeatherNight;

    /// <summary>
    /// Met toute l'interface à l'échelle de la place disponible, replie le volet si la page manque de place,
    /// et passe en mise en page de téléphone sur un écran étroit : les blocs côte à côte se rangent l'un sous l'autre.
    /// </summary>
    private void FitWindow(Size size)
    {
        if (Shell is not { } shell || size.Width <= 0 || fittingWindow) return;
        fittingWindow = true;
        try
        {
            // La barre de titre dessinée par SCOLIA garde la taille du système : seule la place en dessous compte.
            if (TitleRow.IsVisible) size = size.WithHeight(Math.Max(0, size.Height - TitleRow.Height));
            // La petite fenêtre de connexion garde la taille normale : son formulaire y tient tout entier.
            if (shell.CompactWindow && !Preferences.MobileLayout) shell.Prefs.Fit(1280, 720);
            else shell.Prefs.Fit(size.Width, size.Height);
            var k = shell.Prefs.LayoutScale;
            if (Math.Abs(k - 1) < 0.001) Scaler.LayoutTransform = null;
            else if (Scaler.LayoutTransform is not ScaleTransform currentScale || Math.Abs(currentScale.ScaleX - k) >= 0.001)
                Scaler.LayoutTransform = new ScaleTransform(k, k);
            shell.Narrow = size.Width / k < NarrowBelow;
            // L'application mobile garde sa forme sur une tablette aussi ; sur l'ordinateur, une fenêtre très étroite la prend.
            var phone = Preferences.MobileLayout || size.Width / k < PhoneBelow;
            // Connue avant le changement de page : les vues construites ensuite prennent leur forme de téléphone.
            PhoneLayout.Set(phone);
            shell.PhoneWidth = size.Width / k;
            Scaler.Classes.Set("phone", phone);
            shell.Phone = phone;
            QueueFitPageActions();
        }
        finally { fittingWindow = false; }
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled || e.InitialPressMouseButton != MouseButton.Right || Shell?.Current is not { } page) return;
        if (e.Source is Avalonia.Visual v && (v is TextBox || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<TextBox>(v) is not null)) return;
        if (RowMenu.PageMenu(page) is not { } menu) return;
        menu.ShowAt(PageHost, true);
        e.Handled = true;
    }

    private void OnPageTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageViewModel.Subtitle) or nameof(PageViewModel.Heading) && sender == Shell?.Current)
            QueueFitPageActions();
    }

    private void OnActionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => QueueFitPageActions();

    /// <summary>
    /// Garde dans l'en-tête autant de boutons que la largeur le permet, à côté d'un titre lisible.
    /// Les autres passent dans le menu « … ».
    /// </summary>
    private void FitPageActions()
    {
        if (Shell?.Current is not { } page || PageBar.Bounds.Width <= 0) return;
        var font = Shell.Prefs.Factor * 13.5;
        // Le bloc du titre prend la largeur du titre ou du sous-titre, le plus long des deux, dans sa limite.
        var heading = TextWidth(page.Heading, 17 * Shell.Prefs.Factor, FontWeight.Bold);
        var sub = TextWidth(page.Subtitle, 12 * Shell.Prefs.Factor, FontWeight.Normal);
        var title = Math.Clamp(Math.Max(heading, sub) + 8 + 42, 140, TitleMax);
        // Le bandeau garde aussi la recherche, au moins sur une largeur lisible, l'état du réseau et le compte.
        var extras = HeaderExtras.Bounds.Width > 0 ? HeaderExtras.Bounds.Width + 12 : 200;
        var room = PageBar.Bounds.Width - PageBarFixed - title - extras - SearchMin;
        var widths = page.HeaderActions.Select(a => TextWidth(a.Label, font, FontWeight.Normal) + 58).ToList();
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
        PageHost.PageTransition = prefs.Animations && prefs.PageTransitions ? new CrossFade(PageFade * prefs.SpeedFactor) : null;

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    /// <summary>Largeur gardée au champ de recherche du bandeau avant de replier les actions de la page.</summary>
    private const double SearchMin = 220;

    /// <summary>Largeur maximale du bloc titre du bandeau (icône, titre et sous-titre), la même que dans la vue.</summary>
    private const double TitleMax = 300;

    /// <summary>
    /// Barre de titre dessinée par SCOLIA, quand la fenêtre s'étend sous celle de Windows : elle prend la hauteur
    /// de la barre du système, et 0 la masque, en plein écran par exemple.
    /// </summary>
    public void ShowTitleBar(double height)
    {
        TitleRow.Height = Math.Max(0, height);
        TitleRow.IsVisible = height > 0;
        FitWindow(Bounds.Size);
    }

    private void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Shell is not { } shell) return;
        if (shell.HasSheet) shell.CloseSheet();
        else if (shell.Dialog is FormViewModel form) { if (!form.Busy) form.Cancel.Execute(null); }
        else if (shell.HasDialog) shell.CloseDialog();
        else if (shell.BackstageOpen) { if (shell.Backstage.ShowDetail) shell.Backstage.BackToList.Execute(null); else shell.CloseBackstage(); }
        else if (shell.PhoneMenuOpen && shell.SettingsSearch.IsOpen) shell.SettingsSearch.Reset();
        else if (shell.PhoneMenuOpen && shell.SettingsSearch.IsOpen) shell.SettingsSearch.Reset();
        else if (shell.PhoneMenuOpen) shell.PhoneMenuOpen = false;
        else if (shell.Current is SettingsPage settings && settings.TryBackToMobileSections()) { }
        else if (!shell.Phone && shell.Narrow && shell.SidebarShown) shell.ToggleSidebar();
        else if (shell.IsLoggedIn && shell.CanGoBack) shell.GoBack();
        else return;
        e.Handled = true;
    }

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
        // Le voile de traitement et les fenêtres modales bloquent aussi la navigation au clavier.
        if (shell.Working || (e.Key != Key.Escape && (shell.HasDialog || shell.HasSheet || shell.BackstageOpen))) return;
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
        if (shell.HasSheet) shell.CloseSheet();
        else if (shell.Dialog is FormViewModel form) { if (!form.Busy) form.Cancel.Execute(null); }
        else if (shell.HasDialog) shell.CloseDialog();
        else if (shell.BackstageOpen) { if (shell.Backstage.ShowDetail) shell.Backstage.BackToList.Execute(null); else shell.CloseBackstage(); }
        else if (shell.PhoneMenuOpen) shell.PhoneMenuOpen = false;
        else return;
        e.Handled = true;
    }

    private async Task<PickedFile?> PickFileAsync(IReadOnlyList<string> extensions)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
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
