using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;

namespace Scolia.Tests;

/// <summary>Contrôles du calendrier des mensualités.</summary>
public sealed class SchoolMonthRuleTests
{
    [Theory]
    [InlineData(9, 6, 10)]
    [InlineData(10, 4, 7)]
    [InlineData(12, 2, 3)]
    [InlineData(1, 12, 12)]
    public void Beginning_and_end_define_month_count(int first, int last, int expected) =>
        Assert.Equal(expected, AcademicPeriod.CountBetween(first, last));

    [Fact]
    public void October_payment_is_on_time_until_november_fifth()
    {
        var period = AcademicPeriod.Monthly(Guid.NewGuid(), 2026, 10, 1, dueDay: 6)[0];
        Assert.Equal("Octobre", period.Name);
        Assert.Equal(new DateOnly(2026, 11, 6), period.DueDate);
        var fee = new FeeLineInput(Guid.NewGuid(), period.Name, 55000m, period.DueDate, 1, period.Id);
        var before = StudentAccount.Compute([fee], new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), new DateOnly(2026, 11, 5));
        var after = StudentAccount.Compute([fee], new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>(), new DateOnly(2026, 11, 6));
        Assert.False(Assert.Single(before.Lines).IsOverdue);
        Assert.True(Assert.Single(after.Lines).IsOverdue);
    }

    [Fact]
    public void Leap_february_last_day_is_respected()
    {
        var period = AcademicPeriod.Monthly(Guid.NewGuid(), 2023, 1, 1, dueDay: 29)[0];
        Assert.Equal(new DateOnly(2023, 3, 1), period.DueDate);
        var leap = AcademicPeriod.Monthly(Guid.NewGuid(), 2024, 1, 1, dueDay: 29)[0];
        Assert.Equal(new DateOnly(2024, 2, 29), leap.DueDate);
    }

    [Fact]
    public void Invalid_calendar_values_are_rejected()
    {
        Assert.Throws<DomainException>(() => AcademicPeriod.CountBetween(0, 10));
        Assert.Throws<DomainException>(() => AcademicPeriod.Monthly(Guid.NewGuid(), 2026, 9, 10, 30));
    }
}
