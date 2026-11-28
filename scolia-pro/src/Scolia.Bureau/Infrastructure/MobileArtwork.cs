using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Media;
using Material.Icons;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Repères mobiles : symboles arrondis sur des carrés bleus ou verts, avec un relief fixe.</summary>
public static partial class MobileArtwork
{
    private static readonly Dictionary<MaterialIconKind, string> Symbols = CreateSymbols();
    private static readonly HashSet<MaterialIconKind> Green = CreateGreen();
    private static readonly ConcurrentDictionary<MaterialIconKind, Geometry> Geometries = new();
    private static readonly ConcurrentDictionary<MaterialIconKind, IImage> Images = new();
    private static readonly Lazy<Geometry> Tile = new(() => Geometry.Parse(
        "M8,0 H24 C28.4183,0 32,3.5817 32,8 V24 C32,28.4183 28.4183,32 24,32 H8 C3.5817,32 0,28.4183 0,24 V8 C0,3.5817 3.5817,0 8,0 Z"));

    public static bool HasSymbol(MaterialIconKind kind) => Symbols.ContainsKey(kind);
    public static IImage? ForDevice(MaterialIconKind kind) => Platform.IsMobile || PhoneLayout.Active ? For(kind) : null;
    public static IImage For(MaterialIconKind kind) => Images.GetOrAdd(kind, Create);

    public static Geometry Glyph(MaterialIconKind kind) => Geometries.GetOrAdd(kind, static icon =>
    {
        if (!Symbols.TryGetValue(icon, out var path)) return FluentGlyphs.For(icon);
        var geometry = Geometry.Parse(path);
        // Le SVG Google utilise une grille de 960 unités, avec son origine verticale à -960.
        geometry.Transform = new MatrixTransform(new Matrix(.025, 0, 0, .025, 0, 24));
        return geometry;
    });

    private static IImage Create(MaterialIconKind kind)
    {
        var green = Green.Contains(kind);
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse(green ? "#20A451" : "#2A8FE7"), 0),
                new GradientStop(Color.Parse(green ? "#168D43" : "#1D6CE0"), .55),
                new GradientStop(Color.Parse(green ? "#10763B" : "#1D4ED8"), 1)
            ]
        };
        var shine = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(36, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), .4)
            ]
        };
        var glyph = Glyph(kind);
        return new DrawingImage(new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Geometry = Tile.Value, Brush = fill },
                new GeometryDrawing { Geometry = Tile.Value, Brush = shine },
                new DrawingGroup
                {
                    Transform = new TranslateTransform(4, 4.5),
                    Children = { new GeometryDrawing { Geometry = glyph, Brush = new SolidColorBrush(Color.FromArgb(35, 0, 30, 70)) } }
                },
                new DrawingGroup
                {
                    Transform = new TranslateTransform(4, 4),
                    Children = { new GeometryDrawing { Geometry = glyph, Brush = new SolidColorBrush(Color.Parse("#FAFDFF")) } }
                }
            }
        });
    }
}
