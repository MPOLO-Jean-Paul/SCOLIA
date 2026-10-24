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
    public MainWindow()
    {
        InitializeComponent();
        // Le texte et les contrôles suivent la taille de la fenêtre, sans chevauchement ni taille figée.
        SizeChanged += (_, e) =>
        {
            if (Shell is { } shell) shell.Prefs.Fit(e.NewSize.Width, e.NewSize.Height);
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ShellViewModel shell) return;
            shell.FullscreenHandler = full => WindowState = full ? WindowState.FullScreen : WindowState.Normal;
            shell.PickFileHandler = PickFileAsync;
        };
    }

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

    /// <summary>Raccourcis : F5 actualiser, Ctrl+K recherche, F2 encaisser, Ctrl+P imprimer, Ctrl+F1 ruban, Échap fermer.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Shell is not { IsLoggedIn: true } shell) return;
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
