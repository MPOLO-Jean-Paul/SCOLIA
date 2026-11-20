using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class PedagogicalCalendarTests
{
    [Theory]
    [InlineData("ItmIm", CourseSemester.Annuel, true)]
    [InlineData("ItmIm", CourseSemester.S1, true)]
    [InlineData("ItmIm", CourseSemester.S2, true)]
    [InlineData("ItmIm", CourseSemester.JuryEpreuve, true)]
    [InlineData("Maternelle", CourseSemester.Annuel, true)]
    [InlineData("Maternelle", CourseSemester.S1, false)]
    [InlineData("ComplexeScolaire", CourseSemester.Annuel, true)]
    [InlineData("Primaire", CourseSemester.Annuel, true)]
    [InlineData("Primaire", CourseSemester.S1, false)]
    [InlineData("Secondaire", CourseSemester.Annuel, true)]
    [InlineData("Secondaire", CourseSemester.S2, false)]
    [InlineData("SuperieurLmd", CourseSemester.S1, true)]
    [InlineData("SuperieurLmd", CourseSemester.S2, true)]
    [InlineData("SuperieurLmd", CourseSemester.Annuel, false)]
    [InlineData("SuperieurLmd", CourseSemester.JuryBloc, false)]
    public void Course_periods_are_allowed_only_for_the_correct_profile(
        string type, CourseSemester semester, bool expected)
    {
        Assert.Equal(expected, PedagogicalCalendarRules.AllowsCourse(type, semester));
    }

    [Fact]
    public void Existing_itm_still_uses_its_own_calendar_and_financial_months()
    {
        var year = ExampleYear();
        PedagogicalCalendarRules.Validate(InstitutionProfiles.ItmIm, year, Array.Empty<PedagogicalPeriod>());

        var fees = AcademicPeriod.Monthly(year.Id, 2026, 9, 10);
        Assert.Equal(10, fees.Count);
        Assert.Equal(new DateOnly(2026, 10, 6), fees[0].DueDate);
        Assert.Equal("Juin", fees[^1].Name);
    }

    [Fact]
    public void School_evaluations_can_be_configured_without_semester_courses()
    {
        var year = ExampleYear();
        var periods = new[]
        {
            NewPeriod(year, "P1", 1, PedagogicalPeriodKind.Evaluation, 9, 10),
            NewPeriod(year, "P2", 2, PedagogicalPeriodKind.Evaluation, 11, 12),
            new PedagogicalPeriod
            {
                AcademicYearId = year.Id, Code = "P3", Name = "Période 3", Order = 3,
                Kind = PedagogicalPeriodKind.Evaluation,
                StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 3, 31),
            },
        };

        PedagogicalCalendarRules.Validate(InstitutionProfiles.Primaire, year, periods);
        PedagogicalCalendarRules.Validate(InstitutionProfiles.Secondaire, year, periods);
        Assert.Throws<DomainException>(() =>
            PedagogicalCalendarRules.RequireCourse(InstitutionProfiles.Primaire, CourseSemester.S1));
    }

    [Fact]
    public void Nursery_and_mixed_complex_use_periods_specific_to_each_cycle()
    {
        var year = ExampleYear();
        var period = NewPeriod(year, "P1", 1, PedagogicalPeriodKind.Evaluation, 9, 10);
        Assert.Throws<DomainException>(() => PedagogicalCalendarRules.Validate(
            InstitutionProfiles.Maternelle, year, new[] { period }));
        Assert.Throws<DomainException>(() => PedagogicalCalendarRules.Validate(
            InstitutionProfiles.ComplexeScolaire, year, new[] { period }));
        Assert.True(PedagogicalCalendarRules.AllowsCourse(
            InstitutionProfiles.ComplexeScolaire, CourseSemester.Annuel));
    }

    [Fact]
    public void University_requires_two_distinct_semesters_and_no_annual_courses()
    {
        var year = ExampleYear();
        var periods = new[]
        {
            NewPeriod(year, "S1", 1, PedagogicalPeriodKind.Semester, 9, 12),
            new PedagogicalPeriod
            {
                AcademicYearId = year.Id, Code = "S2", Name = "Semestre 2", Order = 2,
                Kind = PedagogicalPeriodKind.Semester,
                StartDate = new DateOnly(2027, 1, 1), EndDate = year.EndDate,
            },
        };
        PedagogicalCalendarRules.Validate(InstitutionProfiles.SuperieurLmd, year, periods);
        Assert.Throws<DomainException>(() =>
            PedagogicalCalendarRules.RequireCourse(InstitutionProfiles.SuperieurLmd, CourseSemester.Annuel));
        Assert.Throws<DomainException>(() =>
            PedagogicalCalendarRules.Validate(InstitutionProfiles.SuperieurLmd, year, periods[..1]));
    }

    [Fact]
    public void Overlapping_school_periods_are_rejected()
    {
        var year = ExampleYear();
        var periods = new[]
        {
            NewPeriod(year, "P1", 1, PedagogicalPeriodKind.Evaluation, 9, 11),
            NewPeriod(year, "P2", 2, PedagogicalPeriodKind.Evaluation, 11, 12),
        };
        Assert.Throws<DomainException>(() =>
            PedagogicalCalendarRules.Validate(InstitutionProfiles.Secondaire, year, periods));
    }

    [Fact]
    public void New_calendar_cannot_override_existing_itm_rules()
    {
        var year = ExampleYear();
        Assert.Throws<DomainException>(() =>
            PedagogicalCalendarRules.Validate(InstitutionProfiles.ItmIm, year,
                new[] { NewPeriod(year, "S1", 1, PedagogicalPeriodKind.Semester, 9, 12) }));
    }

    private static AcademicYear ExampleYear() => new()
    {
        Label = "2026-2027",
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2027, 6, 30),
    };

    private static PedagogicalPeriod NewPeriod(
        AcademicYear year, string code, int order, PedagogicalPeriodKind kind, int from, int to) =>
        new()
        {
            AcademicYearId = year.Id, Code = code, Name = code, Order = order, Kind = kind,
            StartDate = new DateOnly(2026, from, 1),
            EndDate = new DateOnly(2026, to, DateTime.DaysInMonth(2026, to)),
        };
}
