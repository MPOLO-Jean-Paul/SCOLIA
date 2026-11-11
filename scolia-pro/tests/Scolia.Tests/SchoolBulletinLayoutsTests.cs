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
