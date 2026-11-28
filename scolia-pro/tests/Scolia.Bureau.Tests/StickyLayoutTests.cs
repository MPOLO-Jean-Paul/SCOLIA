using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class StickyLayoutTests
{
    [Theory]
    [InlineData(false, 160, 20, 112, 20)]
    [InlineData(true, 0, 20, 112, 112)]
    [InlineData(true, 70, 20, 112, 112)]
    [InlineData(true, 130.2, 20, 112, 143)]
    [InlineData(true, 105, 16, 92, 117)]
    [InlineData(true, double.NaN, 16, 92, 92)]
    public void Le_contenu_reste_accessible_sous_la_barre(bool visible, double height, double normal, double minimum, double expected)
        => Assert.Equal(expected, StickyLayout.BottomSpace(visible, height, normal, minimum));
}
