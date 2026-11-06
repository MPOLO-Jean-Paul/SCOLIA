using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class CardServerCompatibilityTests
{
    [Theory]
    [InlineData("1.2.31", true)]
    [InlineData("1.2.31.0", true)]
    [InlineData("1.2.32.0", true)]
    [InlineData("1.3.0", true)]
    [InlineData("1.2.30.0", false)]
    [InlineData("1.2.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Le_nouveau_modele_depend_de_la_version_du_serveur(string? version, bool expected) =>
        Assert.Equal(expected, CardServerCompatibility.HasRevisedLayout(version));

    [Fact]
    public void Le_message_explique_que_le_serveur_doit_etre_mis_a_jour()
    {
        var message = CardServerCompatibility.OutdatedServerMessage("1.2.30");
        Assert.Contains("1.2.30", message, StringComparison.Ordinal);
        Assert.Contains("serveur", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("téléphone", message, StringComparison.OrdinalIgnoreCase);
    }
}
