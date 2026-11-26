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
        Infrastructure.StickyLayout.Track(PhoneStickyBar, PhoneHost, new Avalonia.Thickness(12, 12, 12, 20), 112);
        // Choisir un outil, depuis le menu ou un onglet, referme le menu, même si l'outil est déjà à l'écran.
        MenuPanel.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Button button && button.Command is not null
                && !button.Classes.Contains("phonecategory")
                && !button.Classes.Contains("settingssearchcontrol")
                && !button.Classes.Contains("settingssearchresult"))
                CloseMenu();
        }, RoutingStrategies.Bubble);
        Tabs.AddHandler(Button.ClickEvent, (_, _) => CloseMenu(), RoutingStrategies.Bubble);
        RailPanel.AddHandler(Button.ClickEvent, (_, e) => { if (e.Source is Button button && button.Command is not null && !button.Classes.Contains("phonecategory")) CloseMenu(); }, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => AttachShell();
        AttachedToVisualTree += (_, _) => AttachShell();
        DetachedFromVisualTree += (_, _) => ReleaseShell();
        SizeChanged += (_, e) => ActionSheet.MaxHeight = Math.Max(0, e.NewSize.Height - 24);
        // Le bouton Menu occupe la même largeur qu'un onglet ordinaire, quel que
        // soit le nombre d'outils visibles pour le rôle (enseignant/secrétariat).
        TabGrid.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) QueueAlignBottomTabs();
        };
    }

    private ShellViewModel? subscribedShell;
    private Func<Action>? captureScroll;
    private long navigationRevision;

    private void AttachShell()
    {
        ReleaseShell();
        if (Shell is not { } shell) return;
        subscribedShell = shell;
        ApplyTransition();
        QueueAlignBottomTabs();
        shell.Prefs.PropertyChanged += OnPreferencesChanged;
        shell.PropertyChanged += OnShellChanged;
        shell.SettingsSearch.PropertyChanged += OnSettingsSearchChanged;
        shell.HeaderSearch.PropertyChanged += OnHeaderSearchChanged;
        // Restauration après la mesure naturelle : aucune mise en page forcée pendant le défilement.
        captureScroll = () =>
        {
            if (!shell.Phone) return () => { };
            var page = shell.PhonePage;
            var revision = navigationRevision;
            var offset = PhoneScroll.Offset;
            return () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Même si l'utilisateur revient à la même page, une ancienne actualisation
                // ne doit pas lui imposer la position d'une visite précédente.
                if (ReferenceEquals(subscribedShell, shell) && ReferenceEquals(Shell, shell)
                    && navigationRevision == revision && shell.PhonePage == page && shell.Phone
                    && PhoneScroll.Offset != offset) PhoneScroll.Offset = offset;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        };
        shell.KeepScrollPosition = captureScroll;
    }

    private void ReleaseShell()
    {
        navigationRevision++;
        if (subscribedShell is { } shell)
        {
            shell.Prefs.PropertyChanged -= OnPreferencesChanged;
            shell.PropertyChanged -= OnShellChanged;
            shell.SettingsSearch.PropertyChanged -= OnSettingsSearchChanged;
            shell.HeaderSearch.PropertyChanged -= OnHeaderSearchChanged;
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
        // Axe partagé d'Android 16 : glissement de quelques points et fondu, jamais toute la largeur.
        // Version légère : la nouvelle page arrive seule en 150 ms, sans attendre le fondu de l'ancienne.
        PhoneHost.PageTransition = new Infrastructure.SharedAxisTransition(
            TimeSpan.FromMilliseconds(Math.Clamp(150 * shell.Prefs.SpeedFactor, 110, 220)), 8, lightweight: true);
    }

    private void OnPreferencesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Infrastructure.Preferences.Animations) or nameof(Infrastructure.Preferences.PageTransitions) or nameof(Infrastructure.Preferences.Speed))
            ApplyTransition();
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // Une nouvelle page s'ouvre en haut, sans toucher la position d'une page simplement actualisée.
        if (args.PropertyName == nameof(ShellViewModel.PhonePage))
        {
            navigationRevision++;
            if (PhoneScroll.Offset != default) PhoneScroll.Offset = default;
        }
        if (args.PropertyName == nameof(ShellViewModel.PhoneTabs)) QueueAlignBottomTabs();
    }

    private void OnSettingsSearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhoneSettingsSearchVm.IsOpen) or nameof(PhoneSettingsSearchVm.Query))
        {
            if (subscribedShell is not { } shell || !ReferenceEquals(sender, shell.SettingsSearch)
                || !shell.SettingsSearch.IsOpen) return;
            var focus = e.PropertyName == nameof(PhoneSettingsSearchVm.IsOpen);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Une demande différée d'un ancien compte ne doit jamais déplacer
                // le défilement ni ouvrir le clavier dans le compte suivant.
                if (!ReferenceEquals(subscribedShell, shell) || !ReferenceEquals(Shell, shell)
                    || !shell.SettingsSearch.IsOpen) return;
                MenuScroll.Offset = default;
                if (focus) MenuSearchBox.Focus();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnHeaderSearchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PhoneHeaderSearchVm.IsOpen)
            || subscribedShell is not { } shell || !ReferenceEquals(sender, shell.HeaderSearch)
            || !shell.HeaderSearch.IsOpen) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(subscribedShell, shell) && ReferenceEquals(Shell, shell)
                && shell.HeaderSearch.IsOpen) ContextSearchBox.Focus();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnContextSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Shell is not { } shell) return;
        shell.HeaderSearch.Close.Execute(null);
        e.Handled = true;
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

    private bool tabAlignmentQueued;

    private void QueueAlignBottomTabs()
    {
        if (tabAlignmentQueued) return;
        tabAlignmentQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            tabAlignmentQueued = false;
            if (subscribedShell == Shell && Shell is not null) AlignBottomTabs();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void AlignBottomTabs()
    {
        if (Shell is not { } shell || TabGrid.Bounds.Width <= 0) return;
        var count = shell.PhoneTabs.Count + 1; // dernier onglet = Menu
        // Un rôle sans raccourci conserve un bouton Menu centré, sans
        // réutiliser la largeur d'un autre compte.
        var width = count <= 1 ? TabGrid.Bounds.Width : TabGrid.Bounds.Width / count;
        if (double.IsNaN(MenuTab.Width) || Math.Abs(MenuTab.Width - width) > 0.5)
            MenuTab.Width = width;
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
        if (Shell is not { } shell) return;
        try { await shell.LogoutAsync(); }
        catch (Exception ex)
        {
            // Ne jamais laisser remonter une exception hors de l'événement Android.
            System.Diagnostics.Trace.WriteLine($"SCOLIA : déconnexion mobile ({ex.GetType().Name}) : {ex.Message}");
            shell.Notify("La déconnexion a rencontré une erreur. Réessayez.");
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Shell?.CloseSheet();

    private void OnSheetCancel(object? sender, RoutedEventArgs e) => Shell?.CloseSheet();
}
