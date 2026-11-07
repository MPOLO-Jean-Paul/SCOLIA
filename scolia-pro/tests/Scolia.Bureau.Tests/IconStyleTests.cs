using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

/// <summary>Style des icônes : au trait dans les barres et les menus, sauf les pictogrammes déjà faits d'un trait.</summary>
public sealed class IconStyleTests
{
    [Theory]
    [InlineData(MaterialIconKind.Pencil, MaterialIconKind.PencilOutline)]
    [InlineData(MaterialIconKind.Printer, MaterialIconKind.PrinterOutline)]
    [InlineData(MaterialIconKind.PencilOutline, MaterialIconKind.PencilOutline)]
    [InlineData(MaterialIconKind.Close, MaterialIconKind.Close)]
    [InlineData(MaterialIconKind.Check, MaterialIconKind.Check)]
    [InlineData(MaterialIconKind.Magnify, MaterialIconKind.Magnify)]
    public void Les_icones_de_barre_passent_au_trait(MaterialIconKind kind, MaterialIconKind expected) =>
        Assert.Equal(expected, Icons.Line(kind));

    [Fact]
    public void Une_icone_sans_variante_au_trait_reste_inchangee() =>
        Assert.Equal(MaterialIconKind.Whatsapp, Icons.Line(MaterialIconKind.Whatsapp));

    [Theory]
    [InlineData("#1F7A35", true)]   // vert à contraste élevé (clair) : pictogramme blanc
    [InlineData("#0040DD", true)]   // bleu à contraste élevé (clair)
    [InlineData("#30D158", false)]  // vert vif (sombre) : pictogramme foncé
    [InlineData("#40C8E0", false)]  // turquoise vif (sombre)
    public void Le_pictogramme_d_une_tuile_atteint_le_contraste_eleve(string tile, bool white)
    {
        var color = Color.Parse(tile);
        var glyph = TileGlyph.GlyphFor(color);
        Assert.Equal(white, glyph == Colors.White);
        Assert.True(ThemeContrast.Ratio(glyph, color) >= TileGlyph.Minimum);
    }
}
