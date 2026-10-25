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
        MenuPanel.AddHandler(Button.ClickEvent, (_, e) => { if (e.Source is Button { Command: not null }) CloseMenu(); }, RoutingStrategies.Bubble);
        Tabs.AddHandler(Button.ClickEvent, (_, _) => CloseMenu(), RoutingStrategies.Bubble);
        RailPanel.AddHandler(Button.ClickEvent, (_, e) => { if (e.Source is Button { Command: not null }) CloseMenu(); }, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) =>
        {
            if (Shell is not { } shell) return;
            shell.PropertyChanged += (_, args) =>
            {
                // Une nouvelle page s'ouvre en haut, pas à la hauteur où la précédente était lue.
                if (args.PropertyName == nameof(ShellViewModel.PhonePage)) PhoneScroll.Offset = default;
            };
        };
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
