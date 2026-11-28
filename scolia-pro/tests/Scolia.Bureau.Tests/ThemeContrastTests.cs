using Avalonia.Media;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public class ThemeContrastTests
{
    [Theory]
    [InlineData("#78B9FF", "#93C7FF")]
    [InlineData("#B7B4FF", "#C5C3FF")]
    [InlineData("#D09BFF", "#D9AFFF")]
    [InlineData("#63E58A", "#82EAA1")]
    [InlineData("#FFB45E", "#FFC37E")]
    [InlineData("#B8B8BE", "#C6C6CB")]
    public void Le_texte_reste_lisible_sur_chaque_accent_sombre(string normal, string hover)
    {
        var background = Color.Parse(normal);
        var hovered = Color.Parse(hover);
        var text = ThemeContrast.OnAccent(background, hovered);
        Assert.True(ThemeContrast.Ratio(text, background) >= 4.5);
        Assert.True(ThemeContrast.Ratio(text, hovered) >= 4.5);
    }


    [Theory]
    [InlineData("ToneBlue", "#1D4ED8")]
    [InlineData("ToneGreen", "#15803D")]
    [InlineData("ToneOrange", "#B53000")]
    [InlineData("TonePurple", "#1D4ED8")]
    [InlineData("ToneRed", "#C9001A")]
    [InlineData("ToneTeal", "#15803D")]
    [InlineData("ToneIndigo", "#1D4ED8")]
    [InlineData("TonePink", "#15803D")]
    [InlineData("ToneNeutral", "#636366")]
    public void Les_icones_gardent_leurs_teintes_et_un_pictogramme_blanc(string tone, string expected)
    {
        var first = Assert.IsAssignableFrom<ISolidColorBrush>(IconPalette.Get(tone));
        var second = Assert.IsAssignableFrom<ISolidColorBrush>(IconPalette.Get(tone));
        Assert.NotSame(first, second); // Aucun pinceau Avalonia n'est partagé entre threads.
        Assert.Equal(first.Color, second.Color);
        Assert.Equal(Color.Parse(expected), first.Color);
        Assert.Equal(Colors.White, TileGlyph.GlyphFor(first.Color));
        Assert.True(ThemeContrast.Ratio(Colors.White, first.Color) >= 4.5);
    }

    [Theory]
    [InlineData("#78B9FF", "#1D4ED8")]
    [InlineData("#6CE09C", "#15803D")]
    [InlineData("#409CFF", "#1D4ED8")]
    [InlineData("#30D158", "#15803D")]
    [InlineData("#FF9F0A", "#B53000")]
    [InlineData("#BF5AF2", "#1D4ED8")]
    [InlineData("#FF453A", "#C9001A")]
    [InlineData("#40C8E0", "#15803D")]
    [InlineData("#8B88FF", "#1D4ED8")]
    [InlineData("#FF375F", "#15803D")]
    [InlineData("#98989D", "#636366")]
    public void La_tuile_sombre_retrouve_sa_couleur_originale(string sombre, string clair)
    {
        Assert.True(IconPalette.TryOriginalColor(Color.Parse(sombre), out var restored));
        Assert.Equal(Color.Parse(clair), restored);
        Assert.Equal(Colors.White, TileGlyph.GlyphFor(restored));
    }

    [Fact]
    public void Les_fonds_personnalises_ne_sont_pas_modifies()
    {
        var custom = Color.Parse("#254762");
        Assert.False(IconPalette.TryOriginalColor(custom, out var returned));
        Assert.Equal(custom, returned);
    }

    [Fact]
    public void Le_contraste_noir_blanc_est_21_et_symetrique()
    {
        Assert.Equal(21, ThemeContrast.Ratio(Colors.Black, Colors.White), 6);
        Assert.Equal(ThemeContrast.Ratio(Colors.Black, Colors.White), ThemeContrast.Ratio(Colors.White, Colors.Black));
    }

    [Fact]
    public void Le_texte_blanc_est_conserve_sur_un_accent_fonce()
    {
        Assert.Equal(Colors.White, ThemeContrast.OnAccent(Color.Parse("#0842A0"), Color.Parse("#063480")));
    }
    [Theory]
    [InlineData("#152C4D", "#D3DEED")]
    [InlineData("#E6E6EB", "#44444C")]
    [InlineData("#1A1C1E", "#D1DCE9")]
    [InlineData("#E2E2E6", "#41454C")]
    [InlineData("#1B1B1B", "#E9E9E9")]
    [InlineData("#FFFFFF", "#3B3B3B")]
    public void Le_texte_reste_lisible_sur_le_fond_appuye(string text, string pressed)
    {
        Assert.True(ThemeContrast.Ratio(Color.Parse(text), Color.Parse(pressed)) >= 4.5);
    }
}
