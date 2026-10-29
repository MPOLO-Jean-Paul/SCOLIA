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
}
