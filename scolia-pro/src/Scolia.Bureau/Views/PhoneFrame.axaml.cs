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
        MenuPanel.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button button && button.Command is not null
                && !button.Classes.Contains("phonecategory")
                && !button.Classes.Contains("settingssearchcontrol"))
                CloseMenu();
        }, RoutingStrategies.Bubble);
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
        shell.SettingsSearch.PropertyChanged += OnSettingsSearchChanged;
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
            shell.SettingsSearch.PropertyChanged -= OnSettingsSearchChanged;
            if (shell.KeepScrollPosition == captureScroll) shell.KeepScrollPosition = null;
        }
        subscribedShell = null;
        captureScroll = null;
    }

    /// <summary>
    /// Motion Android : transition native Avalonia non bloquante.
    /// Onglets du bas, raccourcis et pages utilisent le même mouvement court,
    /// tandis que l'utilisateur peut déjà toucher la nouvelle page.
    /// </summary>
    private void ApplyTransition()
    {
        if (subscribedShell is not { } shell || !shell.Prefs.Animations || !shell.Prefs.PageTransitions)
        {
            PhoneHost.PageTransition = null;
            return;
        }

        // Les durées restent bornées même si l'utilisateur choisit « Douce ».
        // Aucun Task.Delay ni animation de toute la fenêtre : le menu demeure aussi réactif que les onglets.
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(175 * shell.Prefs.SpeedFactor, 105, 220));
        PhoneHost.PageTransition = new Avalonia.Animation.CompositePageTransition
        {
            PageTransitions =
            {
                new Avalonia.Animation.PageSlide(duration, Avalonia.Animation.PageSlide.SlideAxis.Horizontal)
                {
                    SlideInEasing = new Avalonia.Animation.Easings.CubicEaseOut(),
                    SlideOutEasing = new Avalonia.Animation.Easings.CubicEaseIn()
                },
                new Avalonia.Animation.CrossFade(TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.8))
            }
        };
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

    private void OnSettingsSearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhoneSettingsSearchVm.IsOpen) or nameof(PhoneSettingsSearchVm.Query))
        {
            if (Shell?.SettingsSearch.IsOpen != true) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Shell?.SettingsSearch.IsOpen != true) return;
                MenuScroll.Offset = default;
                if (e.PropertyName == nameof(PhoneSettingsSearchVm.IsOpen))
                    MenuSearchBox.Focus();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Le clavier entre dans le résultat sans ouvrir de recherche globale.</summary>
    private void OnSettingsSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Shell?.SettingsSearch.IsOpen == true)
        {
            Shell.SettingsSearch.Reset();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Shell is { } shell && shell.SettingsSearch.Results.Count > 0)
        {
            var result = shell.SettingsSearch.Results[0];
            if (result.Open.CanExecute(null)) result.Open.Execute(null);
            e.Handled = true;
        }
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
