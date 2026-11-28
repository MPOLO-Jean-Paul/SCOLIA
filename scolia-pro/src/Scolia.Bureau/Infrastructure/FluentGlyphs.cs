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
    private static readonly Dictionary<MaterialIconKind, string> Paths16 = CompleteOpticalAliases(CreatePaths16());
    private static readonly Dictionary<MaterialIconKind, string> Paths20 = CompleteOpticalAliases(CreatePaths20());
    private static readonly Dictionary<MaterialIconKind, string> ArtworkKeys = CreateArtworkKeys();
    private static readonly ConcurrentDictionary<MaterialIconKind, Geometry> Geometries = new();
    private static readonly ConcurrentDictionary<(MaterialIconKind Kind, int Size), Geometry> OpticalGeometries = new();
    private static readonly ConcurrentDictionary<Geometry, MaterialIconKind> GeometryKinds = new(ReferenceEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<MaterialIconKind, IImage> FallbackArtwork = new();

    /// <summary>Installe les tracés avant la création des vues, y compris les icônes des menus dynamiques.</summary>
    public static void Install()
    {
        if (MaterialIconDataProvider.Instance is not FluentProvider)
            MaterialIconDataProvider.Instance = new FluentProvider();
    }

    public static bool HasPath(MaterialIconKind kind) => Paths.ContainsKey(kind);
    public static string? ArtworkKey(MaterialIconKind kind) => ArtworkKeys.GetValueOrDefault(kind);
    /// <summary>Le client peut adapter les repères à son appareil ; les consommateurs partagés gardent les ressources Fluent.</summary>
    public static Func<MaterialIconKind, IImage?>? DeviceArtwork { get; set; }

    private static Dictionary<MaterialIconKind, string> CompleteOpticalAliases(Dictionary<MaterialIconKind, string> optical)
    {
        // Only share a small glyph when the original 24 px shapes are identical. An explicit
        // Outline action may have its own meaning and no 16 px variant (e.g. CertificateOutline).
        var byOriginal = optical.ToLookup(pair => Paths[pair.Key], pair => pair.Value);
        foreach (var (kind, original) in Paths)
            if (!optical.ContainsKey(kind) && byOriginal[original].FirstOrDefault() is { } path)
                optical.Add(kind, path);
        return optical;
    }

    public static Geometry For(MaterialIconKind kind) => Geometries.GetOrAdd(kind, static icon =>
    {
        var geometry = Geometry.Parse(Paths.TryGetValue(icon, out var path) ? path : MaterialIconDataProvider.Instance.ProvideData(icon));
        GeometryKinds.TryAdd(geometry, icon);
        return geometry;
    });

    /// <summary>Tracé dessiné pour la taille affichée ; la grille d'origine garde ses marges et ses proportions.</summary>
    public static Geometry ForSize(MaterialIconKind kind, double size, out double canvas)
    {
        var opticalSize = size <= 16 && Paths16.ContainsKey(kind) ? 16
            : size <= 20 && Paths20.ContainsKey(kind) ? 20 : 24;
        canvas = opticalSize;
        if (opticalSize == 24) return For(kind);
        return OpticalGeometries.GetOrAdd((kind, opticalSize), static key =>
            Geometry.Parse((key.Size == 16 ? Paths16 : Paths20)[key.Kind]));
    }

    /// <summary>Les PathIcon partagés suivent les mêmes tailles ; un dessin personnalisé reste sur sa grille de 24 points.</summary>
    public static Geometry ForSize(Geometry data, double size, out double canvas)
    {
        if (GeometryKinds.TryGetValue(data, out var kind)) return ForSize(kind, size, out canvas);
        canvas = 24;
        return data;
    }

    /// <summary>Illustration couleur pour les repères de navigation et les en-têtes, au trait dans les commandes.</summary>
    public static IImage? ArtworkFor(MaterialIconKind? kind)
    {
        if (kind is not { } icon) return null;
        if (DeviceArtwork?.Invoke(icon) is { } deviceArtwork) return deviceArtwork;
        if (ArtworkKeys.TryGetValue(icon, out var key) && Avalonia.Application.Current is { } app &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IImage image)
            return image;
        // Les marques tierces gardent leur dessin ; un nouvel identifiant reste visible pendant son ajout au catalogue.
        return FallbackArtwork.GetOrAdd(icon, static glyph => new DrawingImage(new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 24, 24)), Brush = Brushes.Transparent },
                new GeometryDrawing { Geometry = For(glyph), Brush = ThemeBrush.Get("ToneBlue") }
            }
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
