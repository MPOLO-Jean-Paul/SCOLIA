using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Views;

/// <summary>
/// Cadre de SCOLIA sur le téléphone : une barre de titre avec le retour, la page qui défile en entier, des onglets
/// en bas pour les outils du quotidien, et le menu de tous les outils en plein écran. Les actions de la page et des
/// lignes s'ouvrent en feuille, depuis le bas de l'écran, à portée du pouce.
/// </summary>
public partial class PhoneFrame : UserControl
{
    public PhoneFrame()
    {
        InitializeComponent();
        // Choisir un outil, depuis le menu ou un onglet, referme le menu, même si l'outil est déjà à l'écran.
        MenuPanel.AddHandler(Button.ClickEvent, (_, e) => { if (e.Source is Button button && button.Command is not null && !button.Classes.Contains("phonecategory")) CloseMenu(); }, RoutingStrategies.Bubble);
        Tabs.AddHandler(Button.ClickEvent, (_, _) => CloseMenu(), RoutingStrategies.Bubble);
        RailPanel.AddHandler(Button.ClickEvent, (_, e) => { if (e.Source is Button button && button.Command is not null && !button.Classes.Contains("phonecategory")) CloseMenu(); }, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => AttachShell();
        AttachedToVisualTree += (_, _) => AttachShell();
        DetachedFromVisualTree += (_, _) => ReleaseShell();
        SizeChanged += (_, e) => ActionSheet.MaxHeight = Math.Max(0, e.NewSize.Height - 24);
    }

    private ShellViewModel? subscribedShell;
    private Func<Action>? captureScroll;

    private void AttachShell()
    {
        ReleaseShell();
        if (Shell is not { } shell) return;
        subscribedShell = shell;
        ApplyTransition();
        shell.Prefs.PropertyChanged += OnPreferencesChanged;
        shell.PropertyChanged += OnShellChanged;
        // Restauration après la mesure naturelle : aucune mise en page forcée pendant le défilement.
        captureScroll = () =>
        {
            if (!shell.Phone) return () => { };
            var page = shell.PhonePage;
            var offset = PhoneScroll.Offset;
            return () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (subscribedShell == shell && shell.PhonePage == page) PhoneScroll.Offset = offset;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        };
        shell.KeepScrollPosition = captureScroll;
    }

    private void ReleaseShell()
    {
        if (subscribedShell is { } shell)
        {
            shell.Prefs.PropertyChanged -= OnPreferencesChanged;
            shell.PropertyChanged -= OnShellChanged;
            if (shell.KeepScrollPosition == captureScroll) shell.KeepScrollPosition = null;
        }
        subscribedShell = null;
        captureScroll = null;
    }

    private void ApplyTransition()
    {
        PhoneHost.PageTransition = subscribedShell is { } shell && shell.Prefs.Animations && shell.Prefs.PageTransitions
            ? new Avalonia.Animation.CrossFade(TimeSpan.FromMilliseconds(150) * shell.Prefs.SpeedFactor) : null;
    }

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Infrastructure.Preferences.Animations) or nameof(Infrastructure.Preferences.PageTransitions) or nameof(Infrastructure.Preferences.Speed))
            ApplyTransition();
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // Une nouvelle page s'ouvre en haut, sans toucher la position d'une page simplement actualisée.
        if (args.PropertyName == nameof(ShellViewModel.PhonePage)) PhoneScroll.Offset = default;
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    private void CloseMenu()
    {
        if (Shell is { } shell) shell.PhoneMenuOpen = false;
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e) => Shell?.TogglePhoneMenu();

    private void OnActionsClick(object? sender, RoutedEventArgs e) => Shell?.ShowPageSheet();

    private void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is not { } shell) return;
        shell.PhoneMenuOpen = false;
        shell.OpenBackstage();
    }

    private async void OnLogoutClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell) await shell.LogoutAsync();
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Shell?.CloseSheet();

    private void OnSheetCancel(object? sender, RoutedEventArgs e) => Shell?.CloseSheet();
}
