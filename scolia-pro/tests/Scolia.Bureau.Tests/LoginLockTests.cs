using Scolia.Bureau.Api;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

public sealed class LoginLockTests
{
    [Fact]
    public void Countdown_runs_to_zero_then_gives_access_back()
    {
        var login = new LoginViewModel(new ScoliaClient(new Uri("http://localhost:5080/"))) { UserName = "admin", Password = "mauvais" };
        var unlocked = 0;
        login.Unlocked += () => unlocked++;
        var start = new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);

        login.StartLock(120, start);
        Assert.True(login.Locked);
        Assert.False(login.CanType);
        Assert.Equal("2:00", login.CountdownText);
        Assert.Equal(string.Empty, login.Password);

        login.Tick(start.AddSeconds(61.2));
        Assert.Equal("0:59", login.CountdownText);
        login.Tick(start.AddSeconds(119.5));
        Assert.Equal("0:01", login.CountdownText);
        Assert.Equal(0, unlocked);

        login.Tick(start.AddSeconds(120));
        Assert.Equal("0:00", login.CountdownText);
        Assert.False(login.Locked);
        Assert.True(login.CanType);
        Assert.True(login.Restored);
        Assert.Equal(1, unlocked);
    }
}
