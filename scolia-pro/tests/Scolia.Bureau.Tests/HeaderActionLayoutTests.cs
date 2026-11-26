using Scolia.Bureau.Views;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class HeaderActionLayoutTests
{
    [Theory]
    [InlineData(0, 500, 0)]
    [InlineData(3, 300, 3)]
    [InlineData(3, 246, 2)]
    [InlineData(5, 500, 2)]
    [InlineData(5, 146, 1)]
    [InlineData(5, 145, 0)]
    [InlineData(1, 99, 0)]
    public void Les_commandes_restent_dans_la_largeur_disponible(int total, double room, int expected)
    {
        Assert.Equal(expected, MainView.FitHeaderActionCount(total, new double[] { 100, 100, 100 }, room));
    }
}
