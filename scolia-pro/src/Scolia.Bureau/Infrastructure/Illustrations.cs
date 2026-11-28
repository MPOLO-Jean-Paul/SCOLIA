using Avalonia.Media;
using Material.Icons;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Le même repère Fluent dans les cartes, menus, indicateurs et en-têtes, sur tous les appareils.</summary>
public static class Illustrations
{
    public static IImage For(MaterialIconKind icon, IBrush tone) => FluentGlyphs.ArtworkFor(icon)!;
}
