using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class SchoolCycleRulesTests
{
    private static SchoolProfile School(string profile = InstitutionProfiles.ComplexeScolaire,
        SchoolGovernanceKind governance = SchoolGovernanceKind.Prive) =>
        new() { InstitutionType = profile, Name = "Complexe scolaire", Governance = governance };

    private static AcademicYear Year() =>
        new() { Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 7, 2) };

    private static SchoolCycle Cycle(SchoolProfile school, SchoolCycleKind kind) =>
        new() { SchoolId = school.Id, Kind = kind, Name = kind.ToString() };

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Private_school_can_operate_two_or_three_cycles_on_one_school_profile(
        bool nursery, bool primary, bool secondary)
    {
        var school = School();
        var cycles = new List<SchoolCycle>();
        if (nursery) cycles.Add(Cycle(school, SchoolCycleKind.Maternelle));
        if (primary) cycles.Add(Cycle(school, SchoolCycleKind.Primaire));
        if (secondary) cycles.Add(Cycle(school, SchoolCycleKind.Secondaire));
        SchoolCycleRules.ValidateOffering(school, cycles);
        Assert.All(cycles, x => Assert.Equal(school.Id, x.SchoolId));
    }

    [Theory]
    [InlineData("Maternelle", SchoolCycleKind.Maternelle)]
    [InlineData("Primaire", SchoolCycleKind.Primaire)]
    [InlineData("Secondaire", SchoolCycleKind.Secondaire)]
    public void Single_cycle_school_has_its_own_type(string profile, SchoolCycleKind kind)
    {
        var school = School(profile);
        SchoolCycleRules.ValidateOffering(school, new[] { Cycle(school, kind) });
    }

    [Fact]
    public void Complex_rejects_one_cycle_and_duplicate_cycles()
    {
        var school = School();
        var nursery = Cycle(school, SchoolCycleKind.Maternelle);
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidateOffering(school, new[] { nursery }));
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidateOffering(school,
            new[] { nursery, Cycle(school, SchoolCycleKind.Maternelle) }));
    }

    [Fact]
    public void Itm_im_legacy_configuration_does_not_need_school_cycles()
    {
        var school = School(InstitutionProfiles.ItmIm);
        SchoolCycleRules.ValidateOffering(school, Array.Empty<SchoolCycle>());
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidateOffering(school,
            new[] { Cycle(school, SchoolCycleKind.Primaire) }));
        var oldPromotion = new Promotion { AcademicYearId = Year().Id, Code = "INF1" };
        SchoolCycleRules.ValidatePromotionCycle(school, null, oldPromotion);
    }

    [Fact]
    public void Private_primary_is_not_forced_to_be_free_and_each_cycle_has_its_own_fees()
    {
        var school = School();
        var year = Year();
        var nursery = Cycle(school, SchoolCycleKind.Maternelle);
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var secondary = Cycle(school, SchoolCycleKind.Secondaire);
        var amounts = new (SchoolCycle cycle, decimal amount)[]
        {
            (nursery, 80000m), (primary, 95000m), (secondary, 120000m)
        };
        foreach (var (cycle, amount) in amounts)
        {
            var plan = new SchoolCycleFeePolicy
            {
                SchoolCycleId = cycle.Id, AcademicYearId = year.Id,
                ChargeMonthlyTuition = true, MonthlyAmount = amount,
            };
            SchoolCycleRules.ValidateFeePolicy(school, cycle, year, plan);
            Assert.Equal(amount, plan.MonthlyAmount);
        }
    }

    [Fact]
    public void Private_cycle_can_have_no_monthly_tuition_when_it_bills_by_other_categories()
    {
        var school = School(governance: SchoolGovernanceKind.Prive);
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var year = Year();
        var policy = new SchoolCycleFeePolicy
        {
            SchoolCycleId = primary.Id, AcademicYearId = year.Id,
            ChargeMonthlyTuition = false, MonthlyAmount = 0,
        };
        SchoolCycleRules.ValidateFeePolicy(school, primary, year, policy);
        Assert.False(policy.ChargeMonthlyTuition);
        Assert.Equal(0, policy.MonthlyAmount);
    }

    [Fact]
    public void Negative_school_cycle_monthly_tuition_is_rejected()
    {
        var school = School();
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var year = Year();
        var policy = new SchoolCycleFeePolicy
        {
            SchoolCycleId = primary.Id, AcademicYearId = year.Id,
            ChargeMonthlyTuition = true, MonthlyAmount = -1,
        };
        Assert.Throws<DomainException>(() =>
            SchoolCycleRules.ValidateFeePolicy(school, primary, year, policy));
    }

    [Fact]
    public void Public_school_fee_collection_requires_a_separate_regulatory_workflow()
    {
        var school = School(InstitutionProfiles.Primaire, SchoolGovernanceKind.Public);
        var cycle = Cycle(school, SchoolCycleKind.Primaire);
        var year = Year();
        var plan = new SchoolCycleFeePolicy
        {
            SchoolCycleId = cycle.Id, AcademicYearId = year.Id,
            ChargeMonthlyTuition = false, MonthlyAmount = 0,
        };
        SchoolCycleRules.ValidateFeePolicy(school, cycle, year, plan);
        plan.ChargeMonthlyTuition = true;
        plan.MonthlyAmount = 1000;
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidateFeePolicy(school, cycle, year, plan));
    }

    [Fact]
    public void Complex_class_is_identified_by_its_cycle()
    {
        var school = School();
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var promotion = new Promotion
        {
            AcademicYearId = Year().Id, SchoolCycleId = primary.Id, Code = "PRI1",
        };
        SchoolCycleRules.ValidatePromotionCycle(school, primary, promotion);
        promotion.SchoolCycleId = null;
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidatePromotionCycle(school, primary, promotion));
    }

    [Fact]
    public void Nursery_uses_observation_not_mark_based_periods()
    {
        var school = School();
        var year = Year();
        var nursery = Cycle(school, SchoolCycleKind.Maternelle);
        var p = Period(nursery, year, SchoolCycleAssessmentMode.Observation);
        SchoolCycleRules.ValidateCalendar(nursery, year, new[] { p });
        p.Mode = SchoolCycleAssessmentMode.Evaluation;
        Assert.Throws<DomainException>(() => SchoolCycleRules.ValidateCalendar(nursery, year, new[] { p }));
    }

    [Fact]
    public void Primary_and_secondary_can_both_use_P1_in_the_same_year()
    {
        var school = School();
        var year = Year();
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var secondary = Cycle(school, SchoolCycleKind.Secondaire);
        var p1 = Period(primary, year, SchoolCycleAssessmentMode.Evaluation);
        var p2 = Period(secondary, year, SchoolCycleAssessmentMode.Evaluation);
        SchoolCycleRules.ValidateCalendar(primary, year, new[] { p1 });
        SchoolCycleRules.ValidateCalendar(secondary, year, new[] { p2 });
        Assert.Equal(p1.Code, p2.Code);
        Assert.NotEqual(p1.SchoolCycleId, p2.SchoolCycleId);
    }

    [Fact]
    public void Nursery_activities_are_annual_without_lmd_credits_and_cycles_cannot_be_mixed()
    {
        var school = School();
        var year = Year();
        var nursery = Cycle(school, SchoolCycleKind.Maternelle);
        var primary = Cycle(school, SchoolCycleKind.Primaire);
        var promotionId = Guid.NewGuid();
        var activity = new CurriculumItem
        {
            AcademicYearId = year.Id, PromotionId = promotionId,
            Code = "ACT1", Name = "Langage et motricité", Kind = CurriculumItemKind.AnnualSubject,
            Semester = Scolia.Domain.Deliberation.CourseSemester.Annuel, Credits = 0,
            Coefficient = 1, Hours = 30,
        };

        CurriculumStructureRules.ValidateForSchoolCycle(school, nursery, year, new[] { activity });
        CurriculumStructureRules.ValidateForSchoolCycle(school, primary, year, new[] { activity });

        Assert.Throws<DomainException>(() => CurriculumStructureRules.ValidateCurriculum(
            InstitutionProfiles.ComplexeScolaire, year, new[] { activity },
            Array.Empty<AcademicStructureUnit>()));
    }

    private static SchoolCyclePeriod Period(
        SchoolCycle cycle, AcademicYear year, SchoolCycleAssessmentMode mode) =>
        new()
        {
            SchoolCycleId = cycle.Id, AcademicYearId = year.Id, Code = "P1",
            Name = "Période 1", TermCode = "T1", Order = 1, Mode = mode,
            StartDate = year.StartDate, EndDate = year.StartDate.AddDays(30),
        };
}
