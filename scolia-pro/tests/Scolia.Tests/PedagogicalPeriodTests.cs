using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Tests;

public sealed class PedagogicalPeriodTests
{
    private static readonly Guid YearId = Guid.NewGuid();
    private static readonly DateOnly YearStart = new(2026, 9, 1);
    private static readonly DateOnly YearEnd = new(2027, 7, 31);

    [Fact]
    public void Semester_period_has_its_own_dates_unrelated_to_payment_months()
    {
        var period = PedagogicalPeriod.Define(YearId, PedagogicalPeriodKind.Semestre, 1,
            "Premier semestre", YearStart, new DateOnly(2027, 1, 31), YearStart, YearEnd);
        Assert.Equal(PedagogicalPeriodKind.Semestre, period.Kind);
        Assert.Equal(1, period.Number);
        Assert.False(period.IsClosed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Lmd_semester_number_outside_one_two_is_rejected(int number)
    {
        Assert.Throws<DomainException>(() => PedagogicalPeriod.Define(YearId,
            PedagogicalPeriodKind.Semestre, number, "Semestre",
            YearStart, new DateOnly(2027, 1, 31), YearStart, YearEnd));
    }

    [Fact]
    public void Dates_outside_academic_year_are_rejected()
    {
        Assert.Throws<DomainException>(() => PedagogicalPeriod.Define(YearId,
            PedagogicalPeriodKind.Trimestre, 1, "Trimestre",
            new DateOnly(2026, 8, 31), YearEnd, YearStart, YearEnd));
    }

    [Fact]
    public void Two_overlap_periods_with_same_kind_are_detected()
    {
        var first = PedagogicalPeriod.Define(YearId, PedagogicalPeriodKind.Trimestre, 1,
            "T1", YearStart, new DateOnly(2026, 12, 20), YearStart, YearEnd);
        var second = PedagogicalPeriod.Define(YearId, PedagogicalPeriodKind.Trimestre, 2,
            "T2", new DateOnly(2026, 12, 20), new DateOnly(2027, 3, 20), YearStart, YearEnd);
        Assert.True(PedagogicalPeriod.Conflicts(first, second));
    }

    [Fact]
    public void Distinct_semesters_do_not_overlap()
    {
        var s1 = PedagogicalPeriod.Define(YearId, PedagogicalPeriodKind.Semestre, 1,
            "S1", YearStart, new DateOnly(2027, 1, 31), YearStart, YearEnd);
        var s2 = PedagogicalPeriod.Define(YearId, PedagogicalPeriodKind.Semestre, 2,
            "S2", new DateOnly(2027, 2, 1), YearEnd, YearStart, YearEnd);
        Assert.False(PedagogicalPeriod.Conflicts(s1, s2));
    }
}
