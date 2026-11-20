using Scolia.Bureau;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Un serveur doit survivre à la seule absence de la fenêtre ; seuls les choix explicites l'arrêtent.</summary>
public sealed class BackgroundServerLifecycleTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    public void WatchdogOnlyRestoresAnUnintentionallyStoppedLocalHost(
        bool configuredAsServer, bool intentionalStop, bool processAlive, bool expected)
    {
        Assert.Equal(expected, ServerProcess.ShouldRestore(configuredAsServer, intentionalStop, processAlive));
    }

    [Fact]
    public void RunningServerDoesNotNeedRestartWhenWindowIsMinimized()
    {
        // La fenêtre n'entre pas dans la décision : le seul critère est la santé du processus.
        Assert.False(ServerProcess.ShouldRestore(configuredAsServer: true, intentionalStop: false, processAlive: true));
    }
}