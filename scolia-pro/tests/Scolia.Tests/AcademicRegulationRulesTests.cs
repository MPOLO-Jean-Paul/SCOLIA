using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Tests;

/// <summary>Règlement pédagogique par année : propositions RDC et garde-fous.</summary>
public sealed class AcademicRegulationRulesTests
{
    private static readonly Guid Year = Guid.NewGuid();

    [Fact]
    public void Primary_proposal_uses_three_terms_fifty_percent_and_two_repechage_subjects()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Primaire, Year);
        Assert.Equal((3, 2, 2m), (r.TermsCount, r.PeriodsPerTerm, r.ExamMaxFactor));
        Assert.Equal((50m, 50m, 2, true), (r.PassPercent, r.SubjectPassPercent, r.MaxRepechageSubjects, r.ShowRank));
        Assert.Equal(RegulationStatus.Brouillon, r.Status);
        AcademicRegulationRules.Validate(r);
    }

    [Fact]
    public void Secondary_proposal_uses_two_semesters()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Secondaire, Year);
        Assert.Equal(2, r.TermsCount);
        Assert.Equal(50m, r.PassPercent);
        AcademicRegulationRules.Validate(r);
    }

    [Fact]
    public void Lmd_proposal_validates_ue_at_ten_compensates_inside_ue_only_and_tolerates_fifteen_debt_credits()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.SuperieurLmd, Year);
        Assert.Equal((20m, 10m), (r.MarkScale, r.UeValidationMark));
        Assert.True(r.CompensateWithinUe);
        Assert.False(r.CompensateBetweenUe);
        Assert.Equal(15m, r.MaxDebtCredits);
        AcademicRegulationRules.Validate(r);
    }

    [Fact]
    public void Itm_has_no_regulation_scope_and_complex_school_gets_one_per_active_cycle()
    {
        Assert.Empty(AcademicRegulationRules.ScopesFor(InstitutionProfiles.ItmIm, []));
        Assert.Empty(AcademicRegulationRules.ScopesFor(null, []));
        Assert.Equal([RegulationScope.SuperieurLmd],
            AcademicRegulationRules.ScopesFor(InstitutionProfiles.SuperieurLmd, []));
        Assert.Equal([RegulationScope.Maternelle, RegulationScope.Primaire],
            AcademicRegulationRules.ScopesFor(InstitutionProfiles.ComplexeScolaire,
                [SchoolCycleKind.Primaire, SchoolCycleKind.Maternelle, SchoolCycleKind.Primaire]));
    }

    [Theory]
    [InlineData(4, 2, 2, 50, 50, 2)]
    [InlineData(3, 0, 2, 50, 50, 2)]
    [InlineData(3, 2, 5, 50, 50, 2)]
    [InlineData(3, 2, 2, 0, 50, 2)]
    [InlineData(3, 2, 2, 50, 101, 2)]
    [InlineData(3, 2, 2, 50, 50, 6)]
    public void Epst_values_out_of_range_are_rejected(int terms, int periods, int factor, int pass, int subject, int repechage)
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Primaire, Year);
        (r.TermsCount, r.PeriodsPerTerm, r.ExamMaxFactor) = (terms, periods, factor);
        (r.PassPercent, r.SubjectPassPercent, r.MaxRepechageSubjects) = (pass, subject, repechage);
        Assert.Throws<DomainException>(() => AcademicRegulationRules.Validate(r));
    }

    [Fact]
    public void Lmd_rejects_debt_of_a_full_year_mark_above_scale_and_compensation_between_ue_alone()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.SuperieurLmd, Year);
        r.MaxDebtCredits = 60;
        Assert.Throws<DomainException>(() => AcademicRegulationRules.Validate(r));
        r.MaxDebtCredits = 15; r.UeValidationMark = 21;
        Assert.Throws<DomainException>(() => AcademicRegulationRules.Validate(r));
        r.UeValidationMark = 10; r.CompensateWithinUe = false; r.CompensateBetweenUe = true;
        Assert.Throws<DomainException>(() => AcademicRegulationRules.Validate(r));
    }

    [Fact]
    public void Validated_regulation_is_frozen()
    {
        var r = AcademicRegulationRules.DefaultFor(RegulationScope.Secondaire, Year);
        AcademicRegulationRules.EnsureEditable(r);
        r.Status = RegulationStatus.Valide;
        Assert.Throws<DomainException>(() => AcademicRegulationRules.EnsureEditable(r));
    }
}
