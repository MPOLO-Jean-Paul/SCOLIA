using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class InstitutionTerminologyTests
{
    [Theory]
    [InlineData("ItmIem", "apprenant", "apprenants")]
    [InlineData("ItmIm", "apprenant", "apprenants")]
    [InlineData("Primaire", "élève", "élèves")]
    [InlineData("Secondaire", "élève", "élèves")]
    [InlineData("SuperieurLmd", "étudiant", "étudiants")]
    [InlineData(null, "apprenant", "apprenants")]
    public void Terminology_matches_school_profile(string? profile, string singular, string plural)
    {
        var terms = InstitutionTerminology.Students(profile);
        Assert.Equal(singular, terms.Singular);
        Assert.Equal(plural, terms.Plural);
        Assert.Equal(char.ToUpperInvariant(singular[0]) + singular[1..], terms.SingularTitle);
        Assert.Equal(char.ToUpperInvariant(plural[0]) + plural[1..], terms.PluralTitle);
    }
}
