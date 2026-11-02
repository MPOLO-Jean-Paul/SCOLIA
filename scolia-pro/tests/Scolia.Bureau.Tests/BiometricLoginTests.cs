using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Les appareils partagés ne doivent pas mélanger les comptes biométriques.</summary>
public sealed class BiometricLoginTests
{
    [Theory]
    [InlineData("secretaire", "SECRETAIRE", true)]
    [InlineData("responsable01", "responsable02", false)]
    [InlineData("comptabilite", "secretaire", false)]
    public void Le_compte_doit_correspondre_avant_de_restaurer_la_session(string enrolled, string authenticated, bool expected)
        => Assert.Equal(expected, BiometricAccess.AccountMatches(enrolled, authenticated));

    [Fact]
    public void Le_bouton_biometrique_ne_depend_pas_d_une_empreinte_collectee()
    {
        Assert.False(BiometricAccess.AccountMatches("responsable", "autre-compte"));
    }
}
