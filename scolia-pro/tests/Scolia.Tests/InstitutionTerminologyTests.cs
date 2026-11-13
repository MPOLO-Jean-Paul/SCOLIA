using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class InstitutionTerminologyTests
{
    [Theory]
    [InlineData("ItmIm", "apprenant", "apprenants")]
    [InlineData("Maternelle", "élève", "élèves")]
    [InlineData("Primaire", "élève", "élèves")]
    [InlineData("Secondaire", "élève", "élèves")]
    [InlineData("ComplexeScolaire", "élève", "élèves")]
    [InlineData("SuperieurLmd", "étudiant", "étudiants")]
    public void Vocabulary_follows_the_institution_not_the_program(string profile, string singular, string plural)
    {
        var words = InstitutionTerminology.For(profile);
        Assert.Equal(singular, words.Singular);
        Assert.Equal(plural, words.Plural);
        Assert.Equal("L'" + singular, words.ElidedTitle);
        Assert.Equal("Un " + singular, words.IndefiniteTitle);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("profil-ancien")]
    public void Old_clients_and_unknown_profile_preserve_itm_terminology(string? profile)
    {
        var terms = InstitutionTerminology.For(profile);
        Assert.Equal("apprenant", terms.Singular);
        Assert.Equal("Apprenants", terms.PluralTitle);
    }

    [Fact]
    public void Do_not_change_data_identifiers_to_translate_ui_labels()
    {
        var itm = InstitutionTerminology.For(InstitutionProfiles.ItmIm);
        var primary = InstitutionTerminology.For(InstitutionProfiles.Primaire);
        var higher = InstitutionTerminology.For(InstitutionProfiles.SuperieurLmd);
        Assert.NotEqual(itm.PluralTitle, primary.PluralTitle);
        Assert.NotEqual(primary.PluralTitle, higher.PluralTitle);
        Assert.True(InstitutionProfiles.IsEnabled(InstitutionProfiles.ItmIm));
        Assert.False(InstitutionProfiles.IsEnabled(InstitutionProfiles.SuperieurLmd));
    }
}
