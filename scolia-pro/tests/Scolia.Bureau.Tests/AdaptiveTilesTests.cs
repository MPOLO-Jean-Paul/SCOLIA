using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class AdaptiveTilesTests
{
    [Theory]
    [InlineData(280, 4, 170, 4, true, 1)]
    [InlineData(320, 4, 170, 4, true, 1)]
    [InlineData(340, 4, 170, 4, true, 2)]
    [InlineData(360, 4, 170, 4, true, 2)]
    [InlineData(412, 4, 170, 4, true, 2)]
    [InlineData(720, 4, 170, 4, true, 2)]
    [InlineData(720, 4, 170, 4, false, 4)]
    [InlineData(1366, 7, 175, 3, false, 3)]
    [InlineData(412, 0, 170, 4, true, 1)]
    [InlineData(412, 1, 170, 4, true, 1)]
    public void Les_indicateurs_s_adaptent_aux_dimensions_et_aux_donnees(double width, int count, double minimum, int maximum, bool phone, int expected)
        => Assert.Equal(expected, AdaptiveTiles.ColumnsFor(width, count, minimum, maximum, phone));
}
