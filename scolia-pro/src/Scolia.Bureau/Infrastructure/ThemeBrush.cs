using Avalonia;
using Avalonia.Media;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Pinceaux conservés par les lignes et indicateurs : leur couleur suit le thème sans recharger les données.</summary>
public static class ThemeBrush
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);
    private static Application? observed;

    public static IBrush Get(string key)
    {
        var app = Application.Current;
        if (app is null) return Avalonia.Media.Brushes.Gray;
        if (observed != app)
        {
            if (observed is not null) observed.ActualThemeVariantChanged -= OnThemeChanged;
            observed = app;
            observed.ActualThemeVariantChanged += OnThemeChanged;
        }
        if (!app.TryGetResource(key, app.ActualThemeVariant, out var value) || value is not IBrush source)
            return Avalonia.Media.Brushes.Gray;
        if (source is not ISolidColorBrush solid) return source;
        if (!Brushes.TryGetValue(key, out var brush)) Brushes[key] = brush = new SolidColorBrush(solid.Color, solid.Opacity);
        else { brush.Color = solid.Color; brush.Opacity = solid.Opacity; }
        return brush;
    }

    private static void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    public static void Refresh()
    {
        if (Application.Current is not { } app) return;
        foreach (var (key, brush) in Brushes.ToArray())
            if (app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is ISolidColorBrush source)
            { brush.Color = source.Color; brush.Opacity = source.Opacity; }
    }
}
