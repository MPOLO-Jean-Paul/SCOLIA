using Avalonia;
using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau;

/// <summary>
/// Fenêtre de l'ordinateur : elle accueille l'interface, s'ouvre petite pour la connexion, reprend sa taille de
/// travail une fois connecté et passe en plein écran à la demande.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyWindowMode();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ShellViewModel shell) return;
            shell.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ShellViewModel.CompactWindow) or nameof(ShellViewModel.ShowLogin) or nameof(ShellViewModel.IsLoggedIn)) ApplyWindowMode();
            };
            shell.FullscreenHandler = full => WindowState = full ? WindowState.FullScreen : WindowState.Normal;
        };
    }

    /// <summary>Fenêtre de connexion : juste la taille du formulaire.</summary>
    private const double CompactWidth = 460;
    private const double CompactHeight = 640;

    private bool compact;
    private Size? fullSize;
    private bool fullMaximized;

    /// <summary>
    /// La connexion s'ouvre dans une petite fenêtre à la taille du formulaire. Une fois connecté, la fenêtre reprend
    /// sa taille de travail, ou s'agrandit en plein écran sur un petit écran ; elle revient petite à la déconnexion.
    /// </summary>
    private void ApplyWindowMode()
    {
        // Aperçu du téléphone dans une fenêtre : la taille de l'écran ne change pas.
        if (Shell is not { } shell || Preferences.MobileLayout) return;
        var wanted = shell.CompactWindow;
        if (wanted == compact) return;
        if (wanted)
        {
            fullMaximized = WindowState == WindowState.Maximized;
            if (!fullMaximized) fullSize = new Size(Width, Height);
            WindowState = WindowState.Normal;
            MinWidth = 0;
            MinHeight = 0;
            Width = CompactWidth;
            Height = CompactHeight;
            CanResize = false;
        }
        else
        {
            CanResize = true;
            MinWidth = 760;
            MinHeight = 520;
            var target = fullSize ?? new Size(1280, 820);
            var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen ? screen.WorkingArea.Size.ToSize(screen.Scaling) : (Size?)null;
            Width = area is { } a ? Math.Min(target.Width, a.Width) : target.Width;
            Height = area is { } b ? Math.Min(target.Height, b.Height) : target.Height;
            if (fullMaximized || (area is { } c && (target.Width > c.Width || target.Height > c.Height))) WindowState = WindowState.Maximized;
            // Taille choisie dans Paramètres, Fenêtres : agrandie ou plein écran dès la connexion.
            if (shell.Prefs.Startup == "agrandie") WindowState = WindowState.Maximized;
            else if (shell.Prefs.Startup == "pleinecran") WindowState = WindowState.FullScreen;
        }
        compact = wanted;
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen || WindowState != WindowState.Normal) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.X + (int)((area.Width - Width * screen.Scaling) / 2), area.Y + (int)((area.Height - Height * screen.Scaling) / 2));
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;
}
