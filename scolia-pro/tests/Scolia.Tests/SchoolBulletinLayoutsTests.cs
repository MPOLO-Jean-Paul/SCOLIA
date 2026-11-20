using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class SchoolBulletinLayoutsTests
{
    [Theory]
    [InlineData(SchoolCycleKind.Maternelle, "MATERNELLE_OBSERVATIONS", false)]
    [InlineData(SchoolCycleKind.Primaire, "PRIMAIRE_TRIMESTRES", true)]
    [InlineData(SchoolCycleKind.Secondaire, "SECONDAIRE_SEMESTRES", true)]
    public void Each_school_cycle_has_its_own_preliminary_layout(
        SchoolCycleKind kind, string expectedCode, bool numeric)
    {
        var layout = SchoolBulletinLayouts.For(kind);
        Assert.Equal(expectedCode, layout.Code);
        Assert.Equal(numeric, layout.UsesNumericScores);
        Assert.False(layout.IsOfficial);
        Assert.False(string.IsNullOrWhiteSpace(layout.StatusNote));
    }

    [Theory]
    [InlineData(SchoolCycleKind.Maternelle, 1, "MATERNELLE_1_OBSERVATIONS")]
    [InlineData(SchoolCycleKind.Maternelle, 3, "MATERNELLE_3_OBSERVATIONS")]
    [InlineData(SchoolCycleKind.Primaire, 1, "PRIMAIRE_DEGRE_ELEMENTAIRE")]
    [InlineData(SchoolCycleKind.Primaire, 2, "PRIMAIRE_DEGRE_ELEMENTAIRE")]
    [InlineData(SchoolCycleKind.Primaire, 3, "PRIMAIRE_DEGRE_MOYEN")]
    [InlineData(SchoolCycleKind.Primaire, 4, "PRIMAIRE_DEGRE_MOYEN")]
    [InlineData(SchoolCycleKind.Primaire, 5, "PRIMAIRE_DEGRE_TERMINAL")]
    [InlineData(SchoolCycleKind.Primaire, 6, "PRIMAIRE_DEGRE_TERMINAL")]
    [InlineData(SchoolCycleKind.Secondaire, 1, "SECONDAIRE_CTEB")]
    [InlineData(SchoolCycleKind.Secondaire, 2, "SECONDAIRE_CTEB")]
    [InlineData(SchoolCycleKind.Secondaire, 3, "SECONDAIRE_HUMANITES")]
    [InlineData(SchoolCycleKind.Secondaire, 6, "SECONDAIRE_HUMANITES")]
    public void Each_grade_uses_the_expected_non_official_pedagogical_variant(
        SchoolCycleKind kind, int level, string code)
    {
        var layout = SchoolBulletinLayouts.For(kind, level);
        Assert.Equal(code, layout.Code);
        Assert.False(layout.IsOfficial);
        Assert.NotEqual("Cycle non précisé", layout.GradeBand);
    }

    [Theory]
    [InlineData(SchoolCycleKind.Maternelle, 8)]
    [InlineData(SchoolCycleKind.Primaire, 0)]
    [InlineData(SchoolCycleKind.Secondaire, 99)]
    public void Unknown_class_level_keeps_generic_layout_instead_of_guessing(
        SchoolCycleKind kind, int level)
    {
        Assert.Equal(SchoolBulletinLayouts.For(kind).Code,
            SchoolBulletinLayouts.For(kind, level).Code);
    }

    [Fact]
    public void A_unknown_cycle_cannot_select_a_bulletin_template()
    {
        Assert.Throws<DomainException>(() =>
            SchoolBulletinLayouts.For((SchoolCycleKind)999));
    }

    [Fact]
    public void The_templates_never_define_a_passing_grade_or_rank()
    {
        foreach (var kind in new[]
        {
            SchoolCycleKind.Maternelle, SchoolCycleKind.Primaire, SchoolCycleKind.Secondaire,
        })
        {
            var layout = SchoolBulletinLayouts.For(kind);
            Assert.DoesNotContain("moyenne", layout.Code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("classement", layout.Code, StringComparison.OrdinalIgnoreCase);
        }
    }
}
