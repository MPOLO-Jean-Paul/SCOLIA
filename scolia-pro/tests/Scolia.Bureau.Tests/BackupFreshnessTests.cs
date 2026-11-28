using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

public sealed class BackupFreshnessTests
{
    [Theory]
    [InlineData(1, 65, true)]
    [InlineData(1, 80, false)]
    [InlineData(1, 170, false)]
    [InlineData(24, 1500, true)]
    [InlineData(24, 1560, false)]
    [InlineData(168, 10201, false)]
    [InlineData(1, -60, false)]
    public void Protection_status_follows_the_configured_frequency(int intervalHours, int ageMinutes, bool expected)
    {
        var now = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, BackupText.IsFresh(now.AddMinutes(-ageMinutes), now, intervalHours));
    }
}
