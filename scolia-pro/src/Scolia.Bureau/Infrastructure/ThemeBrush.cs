using Avalonia;
using Avalonia.Media;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Pinceaux conservés par les lignes et indicateurs : leur couleur suit le thème sans recharger les données.</summary>
public static class ThemeBrush
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);
    private static Avalonia.Application? observed;

    public static IBrush Get(string key)
    {
        var app = Avalonia.Application.Current;
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
        if (Avalonia.Application.Current is not { } app) return;
        foreach (var (key, brush) in Brushes.ToArray())
            if (app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is ISolidColorBrush source)
            { brush.Color = source.Color; brush.Opacity = source.Opacity; }
    }
}

/// <summary>
/// Palette d'icônes permanente, identique en mode clair et sombre.
/// Les fonds colorés et les pictogrammes blancs restent inchangés lorsque le thème bascule.
/// Les surfaces et textes ordinaires restent gérés par ThemeBrush.
/// </summary>
public static class IconPalette
{
    // Les teintes restent constantes : chaque appel crée son pinceau sur le thread
    // de l'interface, pour éviter de partager un objet Avalonia lié à un autre thread.
    private static readonly IReadOnlyDictionary<string, Color> Colors =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["ToneBlue"] = Color.Parse("#0040DD"),
            ["ToneGreen"] = Color.Parse("#1F7A35"),
            ["ToneOrange"] = Color.Parse("#B53000"),
            ["TonePurple"] = Color.Parse("#8944AB"),
            ["ToneRed"] = Color.Parse("#C9001A"),
            ["ToneTeal"] = Color.Parse("#006D9E"),
            ["ToneIndigo"] = Color.Parse("#3634A3"),
            ["TonePink"] = Color.Parse("#C7083E"),
            ["ToneNeutral"] = Color.Parse("#636366")
        };

    public static IBrush Get(string tone) =>
        new SolidColorBrush(Colors.TryGetValue(tone, out var color) ? color : Colors["ToneNeutral"]);
}
