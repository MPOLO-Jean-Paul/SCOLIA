using Scolia.Bureau.Infrastructure;
using Scolia.Domain.School;

namespace Scolia.Bureau.Tests;

/// <summary>Écrans communs : « apprenant » devient « élève » dans une école et « étudiant » dans une université.</summary>
public sealed class VocabularyTests
{
    [Theory]
    [InlineData("ItmIm", "Les apprenants cochés", "Les apprenants cochés")]
    [InlineData("Primaire", "Les apprenants cochés", "Les élèves cochés")]
    [InlineData("Secondaire", "Aucun apprenant pour ces critères", "Aucun élève pour ces critères")]
    [InlineData("SuperieurLmd", "Apprenant", "Étudiant")]
    [InlineData("SuperieurLmd", "Fiche de l'apprenant · Apprenants inscrits", "Fiche de l'étudiant · Étudiants inscrits")]
    public void Common_screens_speak_the_profile_vocabulary(string type, string text, string expected) =>
        Assert.Equal(expected, Vocabulary.Adapt(text, InstitutionTerminology.For(type)));

    [Fact]
    public void Words_that_only_contain_the_root_are_left_alone() =>
        Assert.Equal("apprentissage", Vocabulary.Adapt("apprentissage", InstitutionTerminology.For("Primaire")));
}
