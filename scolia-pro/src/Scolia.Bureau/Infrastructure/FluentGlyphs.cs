using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Material.Icons;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Catalogue Fluent commun à toutes les fonctionnalités. Les identifiants d'action existants
/// conservent leur sens ; seuls les tracés changent. Aucun chargement réseau ni police système.
/// </summary>
public static partial class FluentGlyphs
{
    private static readonly Dictionary<MaterialIconKind, string> Paths = CreatePaths();
    private static readonly Dictionary<MaterialIconKind, string> ArtworkKeys = CreateArtworkKeys();
    private static readonly ConcurrentDictionary<MaterialIconKind, Geometry> Geometries = new();
    private static readonly ConcurrentDictionary<MaterialIconKind, IImage> FallbackArtwork = new();

    /// <summary>Installe les tracés avant la création des vues, y compris les icônes des menus dynamiques.</summary>
    public static void Install()
    {
        if (MaterialIconDataProvider.Instance is not FluentProvider)
            MaterialIconDataProvider.Instance = new FluentProvider();
    }

    public static bool HasPath(MaterialIconKind kind) => Paths.ContainsKey(kind);
    public static string? ArtworkKey(MaterialIconKind kind) => ArtworkKeys.GetValueOrDefault(kind);

    public static Geometry For(MaterialIconKind kind) => Geometries.GetOrAdd(kind, static icon =>
        Geometry.Parse(Paths.TryGetValue(icon, out var path) ? path : MaterialIconDataProvider.Instance.ProvideData(icon)));

    /// <summary>Illustration couleur pour les repères de navigation et les en-têtes, au trait dans les commandes.</summary>
    public static IImage? ArtworkFor(MaterialIconKind? kind)
    {
        if (kind is not { } icon) return null;
        if (ArtworkKeys.TryGetValue(icon, out var key) && Application.Current is { } app &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IImage image)
            return image;
        // Les marques tierces gardent leur dessin ; un nouvel identifiant reste visible pendant son ajout au catalogue.
        return FallbackArtwork.GetOrAdd(icon, static glyph => new DrawingImage(new GeometryDrawing
        {
            Geometry = For(glyph), Brush = ThemeBrush.Get("ToneGreen")
        }));
    }

    public static readonly IValueConverter ArtworkConverter =
        new FuncValueConverter<MaterialIconKind?, IImage?>(ArtworkFor);

    private sealed class FluentProvider : MaterialIconDataProvider
    {
        public override string ProvideData(MaterialIconKind kind) =>
            Paths.TryGetValue(kind, out var path) ? path : base.ProvideData(kind);
    }
}
