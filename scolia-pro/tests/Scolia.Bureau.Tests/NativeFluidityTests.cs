using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class NativeFluidityTests
{
    [Fact]
    public void Une_nouvelle_direction_annule_la_destination_precedente()
    {
        Assert.True(SmoothScroll.ShouldInterrupt(100, 100, 200, 1));
        Assert.True(SmoothScroll.ShouldInterrupt(100, 100, 0, -1));
        Assert.False(SmoothScroll.ShouldInterrupt(100, 100, 200, -1));
        Assert.False(SmoothScroll.ShouldInterrupt(100, 100, 0, 1));
    }

    [Fact]
    public void Un_repositionnement_externe_est_prioritaire()
    {
        Assert.True(SmoothScroll.ShouldInterrupt(0, 100, 200, -1));
        Assert.False(SmoothScroll.ShouldInterrupt(100.2, 100, 200, -1));
    }

    [Fact]
    public void Le_defilement_ne_depasse_jamais_sa_destination()
    {
        var down = SmoothScroll.AdvanceOffset(0, 100, 0.016);
        var up = SmoothScroll.AdvanceOffset(100, 0, 0.016);
        Assert.InRange(down, 0.01, 99.99);
        Assert.InRange(up, 0.01, 99.99);
        Assert.Equal(50, SmoothScroll.AdvanceOffset(50, 100, 0));
        Assert.Equal(100, SmoothScroll.AdvanceOffset(100, 100, 0.016));
    }

    [Fact]
    public void Le_defilement_est_independant_du_nombre_d_images()
    {
        var single = SmoothScroll.AdvanceOffset(0, 100, 0.032);
        var two = SmoothScroll.AdvanceOffset(SmoothScroll.AdvanceOffset(0, 100, 0.016), 100, 0.016);
        Assert.Equal(single, two, 9);
    }
}
