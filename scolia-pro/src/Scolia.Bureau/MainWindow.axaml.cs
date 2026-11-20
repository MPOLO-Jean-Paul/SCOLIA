using Avalonia;
using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau;

/// <summary>
/// Fenêtre Windows : maximisée dès l'ouverture, même avant l'authentification.
/// Les dimensions logiques restent dans la zone de travail de l'écran, même à forte mise à l'échelle.
/// </summary>
public partial class MainWindow : Window
{
    private ShellViewModel? subscribedShell;
    private readonly Action<bool> fullscreenHandler;

    public MainWindow()
    {
        fullscreenHandler = ApplyFullscreen;
        InitializeComponent();
        // La barre de titre native reste distincte du bandeau bleu : pas de texte superposé
        // aux décorations système sur Windows, même avec une mise à l’échelle élevée.
        if (OperatingSystem.IsWindows()) ExtendClientAreaToDecorationsHint = false;
        RequestMica();
        Opened += (_, _) => ApplyWindowMode();
        Closing += (_, _) => RememberPlacement();
        Closed += (_, _) => ReleaseShell();
        PositionChanged += (_, _) => TrackNormalBounds();
        DataContextChanged += (_, _) =>
        {
            ReleaseShell();
            if (Shell is not { } shell) return;
            // Avant la première image : la fenêtre s'ouvre directement à sa place, sans saut visible.
            if (!IsVisible) ApplyInitialPlacement(shell.Prefs);
            subscribedShell = shell;
            shell.PropertyChanged += OnShellChanged;
            shell.FullscreenHandler = fullscreenHandler;
            shell.SyncFullscreenState(WindowState == WindowState.FullScreen);
            if (IsVisible) ApplyWindowMode();
        };
    }

    /// <summary>
    /// Fond Mica de Windows 11 (build 22000 et plus) : demandé à l'ouverture, appliqué seulement si Windows le
    /// confirme. Sur Windows 10, avec la transparence désactivée par l'utilisateur, ou sur un autre système,
    /// la fenêtre garde son fond opaque habituel.
    /// </summary>
    private void RequestMica()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        Background = Avalonia.Media.Brushes.Transparent;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty) ApplyMica();
        };
        Opened += (_, _) => ApplyMica();
    }

    private void ApplyMica()
    {
        var mica = ActualTransparencyLevel == WindowTransparencyLevel.Mica;
        View.Classes.Set("mica", mica);
        // Sans Mica, aucun fond transparent : la fenêtre reprend son fond opaque.
        if (!mica) ClearValue(BackgroundProperty);
        else Background = Avalonia.Media.Brushes.Transparent;
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ShellViewModel.CompactWindow) or nameof(ShellViewModel.ShowLogin) or nameof(ShellViewModel.IsLoggedIn))
            ApplyWindowMode();
    }

    private void ReleaseShell()
    {
        if (subscribedShell is { } shell)
        {
            shell.PropertyChanged -= OnShellChanged;
            if (shell.FullscreenHandler == fullscreenHandler) shell.FullscreenHandler = null;
        }
        subscribedShell = null;
    }

    private const double CompactWidth = 460;
    private const double CompactHeight = 640;
    private bool? compact;
    private bool applyingWindowMode;
    private Size? fullSize;
    private WindowState fullState = WindowState.Maximized;
    private WindowState beforeFullscreen;

    private void ApplyFullscreen(bool full)
    {
        if (full)
        {
            if (WindowState != WindowState.FullScreen)
                beforeFullscreen = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            WindowState = WindowState.FullScreen;
        }
        else WindowState = beforeFullscreen;
    }

    private void ApplyWindowMode()
    {
        if (Shell is not { } shell || Preferences.MobileLayout || applyingWindowMode) return;
        // SCOLIA est une application de bureau : la connexion et le jumelage
        // doivent occuper le vrai espace de travail Windows, jamais un mini-dialogue.
        // Le Shell conserve son état compact historique pour d'autres surfaces,
        // mais MainWindow n'applique plus ce format sur ordinateur.
        var wanted = !OperatingSystem.IsWindows() && shell.CompactWindow;
        if (wanted == compact) return;
        if (OperatingSystem.IsWindows())
        {
            // Sous Windows, la taille et l'état viennent de ApplyInitialPlacement : ne fixer que les limites.
            compact = false;
            CanResize = true;
            var work = (Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } scr ? scr.WorkingArea.Size.ToSize(scr.Scaling) : (Size?)null;
            MinWidth = work is { } w ? Math.Min(760, w.Width) : 760;
            MinHeight = work is { } h ? Math.Min(520, h.Height) : 520;
            EnsureOnScreen();
            return;
        }
        applyingWindowMode = true;
        try
        {
            var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen
                ? screen.WorkingArea.Size.ToSize(screen.Scaling) : (Size?)null;
            if (wanted)
            {
                fullState = WindowState is WindowState.Maximized or WindowState.FullScreen ? WindowState : WindowState.Normal;
                if (WindowState == WindowState.Normal) fullSize = new Size(Width, Height);
                WindowState = WindowState.Normal;
                MinWidth = 0;
                MinHeight = 0;
                // Une marge couvre aussi le cadre du système sur un petit écran à 150 % ou 200 %.
                Width = area is { } a ? Math.Min(CompactWidth, Math.Max(1, a.Width - 24)) : CompactWidth;
                Height = area is { } b ? Math.Min(CompactHeight, Math.Max(1, b.Height - 24)) : CompactHeight;
                CanResize = false;
            }
            else
            {
                CanResize = true;
                MinWidth = area is { } a ? Math.Min(760, a.Width) : 760;
                MinHeight = area is { } b ? Math.Min(520, b.Height) : 520;
                var target = fullSize ?? new Size(1280, 820);
                Width = area is { } c ? Math.Min(target.Width, c.Width) : target.Width;
                Height = area is { } d ? Math.Min(target.Height, d.Height) : target.Height;
                var restore = fullState;
                if (restore == WindowState.Normal && area is { } e && (target.Width > e.Width || target.Height > e.Height))
                    restore = WindowState.Maximized;
                if (shell.Prefs.Startup == "agrandie") restore = WindowState.Maximized;
                else if (shell.Prefs.Startup == "pleinecran") restore = WindowState.FullScreen;
                if (restore == WindowState.FullScreen)
                {
                    if (fullState != WindowState.FullScreen)
                        beforeFullscreen = fullState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                    WindowState = WindowState.FullScreen;
                }
                else WindowState = restore;
            }
            compact = wanted;
            CenterOnScreen();
        }
        finally { applyingWindowMode = false; }
    }

    /// <summary>Dernière position et taille « normales » (non agrandies), retenues pour la prochaine ouverture.</summary>
    private PixelPoint? normalPosition;
    private Size? normalSize;
    private bool placementApplied;
    /// <summary>Dernier état visible : une fenêtre réduite dans la barre des tâches rouvre comme avant.</summary>
    private WindowState lastShownState = WindowState.Maximized;

    /// <summary>
    /// Ouverture sous Windows : « Comme à la dernière fermeture » reprend la taille, la position et l'état
    /// (agrandie ou non) ; « Agrandie » et « Plein écran » s'imposent. Première ouverture : agrandie, avec une
    /// taille normale de 90 % de l'écran au plus, centrée, pour le bouton « Niveau inférieur ».
    /// </summary>
    private void ApplyInitialPlacement(Preferences prefs)
    {
        if (placementApplied || !OperatingSystem.IsWindows() || Preferences.MobileLayout) return;
        placementApplied = true;
        var screen = Screens.Primary;
        var area = screen is null ? (Size?)null : screen.WorkingArea.Size.ToSize(screen.Scaling);
        var saved = prefs.Window;
        if (saved is not null && area is { } a && screen is not null && IsVisibleOnSomeScreen(saved))
        {
            Width = Math.Min(saved.Width, a.Width);
            Height = Math.Min(saved.Height, a.Height);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(saved.X, saved.Y);
        }
        else
        {
            var fit = Fit(area);
            Width = fit.Width;
            Height = fit.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        normalSize = new Size(Width, Height);
        WindowState = prefs.Startup switch
        {
            "agrandie" => WindowState.Maximized,
            "pleinecran" => WindowState.FullScreen,
            _ => saved is null || saved.Maximized ? WindowState.Maximized : WindowState.Normal
        };
    }

    /// <summary>Taille normale par défaut : 1280 × 820 au plus, 90 % de la zone de travail sur un petit écran.</summary>
    private static Size Fit(Size? area) => area is { } a
        ? new Size(Math.Max(Math.Min(1280, a.Width * 0.9), Math.Min(760, a.Width)), Math.Max(Math.Min(820, a.Height * 0.9), Math.Min(520, a.Height)))
        : new Size(1280, 820);

    private bool IsVisibleOnSomeScreen(WindowPlacement p)
    {
        // Au moins le haut de la fenêtre (barre de titre) doit tomber dans la zone de travail d'un écran branché.
        var title = new PixelRect(p.X + 40, p.Y, 200, 30);
        return Screens.All.Any(s => s.WorkingArea.Intersects(title));
    }

    private void TrackNormalBounds()
    {
        if (WindowState != WindowState.Normal || !IsVisible) return;
        normalPosition = Position;
        normalSize = new Size(Width, Height);
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        TrackNormalBounds();
    }

    /// <summary>
    /// Retour à « Niveau inférieur » : la fenêtre ne doit jamais dépasser l'écran ni s'ouvrir hors de vue
    /// (écran débranché, résolution changée). Sinon elle reprend 90 % de l'écran, centrée.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (WindowState != WindowState.Normal || !OperatingSystem.IsWindows()) return;
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        var area = screen.WorkingArea.Size.ToSize(screen.Scaling);
        var outside = !Screens.All.Any(s => s.WorkingArea.Intersects(new PixelRect(Position.X + 40, Position.Y, 200, 30)));
        if (!outside && Width <= area.Width + 1 && Height <= area.Height + 1) return;
        var fit = Fit(area);
        Width = fit.Width;
        Height = fit.Height;
        CenterOnScreen();
    }

    private void RememberPlacement()
    {
        if (!OperatingSystem.IsWindows() || Preferences.MobileLayout || Shell is not { } shell) return;
        TrackNormalBounds();
        var size = normalSize ?? new Size(Width, Height);
        var position = normalPosition ?? Position;
        var shown = WindowState == WindowState.Minimized ? lastShownState : WindowState;
        var maximized = shown is WindowState.Maximized
            || (shown == WindowState.FullScreen && beforeFullscreen == WindowState.Maximized);
        shell.Prefs.Window = new WindowPlacement(position.X, position.Y, size.Width, size.Height, maximized);
        shell.Prefs.Save();
    }

    private void CenterOnScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen || WindowState != WindowState.Normal) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.X + (int)((area.Width - Width * screen.Scaling) / 2), area.Y + (int)((area.Height - Height * screen.Scaling) / 2));
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
        {
            if (!applyingWindowMode && WindowState == WindowState.FullScreen && change.OldValue is WindowState previous && previous != WindowState.FullScreen)
                beforeFullscreen = previous == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            Shell?.SyncFullscreenState(WindowState == WindowState.FullScreen);
            if (WindowState != WindowState.Minimized) lastShownState = WindowState;
            // « Niveau inférieur » après agrandissement : taille et position vérifiées une fois l'état appliqué.
            if (WindowState == WindowState.Normal && change.OldValue is WindowState.Maximized or WindowState.FullScreen && !applyingWindowMode)
                Avalonia.Threading.Dispatcher.UIThread.Post(EnsureOnScreen);
        }
        if (change.Property == WindowDecorationMarginProperty || change.Property == IsExtendedIntoWindowDecorationsProperty)
            View?.ShowTitleBar(IsExtendedIntoWindowDecorations ? WindowDecorationMargin.Top : 0);
    }
}
