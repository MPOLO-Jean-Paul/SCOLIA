using Avalonia;
using Avalonia.Media;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Pinceaux du thème courant, lus au moment de construire un élément de l'interface.</summary>
public static class ThemeBrush
{
    public static IBrush Get(string key)
    {
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Avalonia.Media.Brushes.Gray;
    }
}
