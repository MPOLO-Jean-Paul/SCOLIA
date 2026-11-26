using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class AdaptiveResizeTests
{
    [Theory]
    [InlineData(759, 760, false, true)]
    [InlineData(760, 760, false, false)]
    [InlineData(763, 760, true, true)]
    [InlineData(768, 760, true, false)]
    [InlineData(400, 0, true, false)]
    public void Le_rangement_reste_stable_autour_du_seuil(double width, double threshold, bool stacked, bool expected)
    {
        Assert.Equal(expected, Adaptive.ShouldStack(width, threshold, stacked));
    }

    [Theory]
    [InlineData(800, 100, 490)]
    [InlineData(400, 350, 42)]
    [InlineData(300, 350, 0)]
    public void Le_volet_de_recherche_ne_depasse_pas_la_fenetre(double height, double top, double expected)
    {
        Assert.Equal(expected, Adaptive.PopupHeight(height, top, 490));
    }
}
