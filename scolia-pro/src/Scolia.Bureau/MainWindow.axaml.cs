using Avalonia;
using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau;

/// <summary>
/// Fenêtre de l'ordinateur : connexion compacte, puis taille de travail et plein écran.
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
        if (OperatingSystem.IsWindows()) ExtendClientAreaToDecorationsHint = true;
        Opened += (_, _) => ApplyWindowMode();
        Closed += (_, _) => ReleaseShell();
        DataContextChanged += (_, _) =>
        {
            ReleaseShell();
            if (Shell is not { } shell) return;
            subscribedShell = shell;
            shell.PropertyChanged += OnShellChanged;
            shell.FullscreenHandler = fullscreenHandler;
            shell.SyncFullscreenState(WindowState == WindowState.FullScreen);
            if (IsVisible) ApplyWindowMode();
        };
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
    private bool compact;
    private bool applyingWindowMode;
    private Size? fullSize;
    private WindowState fullState;
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
        var wanted = shell.CompactWindow;
        if (wanted == compact) return;
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
        }
        if (change.Property == WindowDecorationMarginProperty || change.Property == IsExtendedIntoWindowDecorationsProperty)
            View?.ShowTitleBar(IsExtendedIntoWindowDecorations ? WindowDecorationMargin.Top : 0);
    }
}
