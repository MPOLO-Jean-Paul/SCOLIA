using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Apparence native de chaque appareil, posée par-dessus la palette commune : Fluent de Windows 11 sur
/// l'ordinateur, Material 3 d'Android sur le téléphone. Seules les couleurs et les arrondis changent ;
/// la disposition des pages reste la même.
/// </summary>
public static class PlatformTheme
{
    private static IResourceProvider? applied;
    private static bool? appliedMobile;

    public static void Apply(bool mobile)
    {
        // L'iPhone garde le style iOS de SCOLIA : Material 3 est le langage d'Android.
        if (mobile && OperatingSystem.IsIOS()) return;
        if (Avalonia.Application.Current is not { } app || appliedMobile == mobile) return;
        if (applied is not null) app.Resources.MergedDictionaries.Remove(applied);
        var source = new Uri(mobile ? "avares://SCOLIA/Themes/Material3.axaml" : "avares://SCOLIA/Themes/Windows11.axaml");
        applied = (IResourceProvider)AvaloniaXamlLoader.Load(source);
        app.Resources.MergedDictionaries.Add(applied);
        appliedMobile = mobile;
    }
}
