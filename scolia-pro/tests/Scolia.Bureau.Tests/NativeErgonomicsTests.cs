using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class NativeErgonomicsTests
{
    [Theory]
    [InlineData(320, 3, 4)]
    [InlineData(360, 4, 6)]
    [InlineData(240, 2, 0)]
    [InlineData(1, 4, 6)]
    public void Les_segments_ne_debordent_pas_et_ne_recoivent_pas_de_largeur_negative(
        double width, int count, double spacing)
    {
        var each = PhoneFit.SegmentWidth(width, count, spacing);
        Assert.True(each >= 0);
        if (width >= spacing * (count - 1))
            Assert.True(each * count + spacing * (count - 1) <= width);
    }

    [Fact]
    public void Une_liste_sans_segment_ne_divise_pas_par_zero()
    {
        Assert.Equal(0d, PhoneFit.SegmentWidth(360, 0, 6));
    }
}
