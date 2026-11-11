using Scolia.Mobile;

namespace Scolia.Mobile.Tests;

public sealed class StartupReadinessTests
{
    [Fact]
    public async Task KeepsTheLogoUntilInitializationAndDestinationFrameAreReady()
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = StartupReadiness.WaitAsync(startup.Task, TimeSpan.Zero, _ =>
        {
            frameRequested.SetResult();
            return frame.Task;
        }, CancellationToken.None);

        Assert.False(opening.IsCompleted);
        Assert.False(frameRequested.Task.IsCompleted);
        startup.SetResult();
        await frameRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(opening.IsCompleted);
        frame.SetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FailedInitializationReachesTheRetryScreenWithoutWaitingForAFrame()
    {
        var frameRequested = false;
        var failure = new InvalidOperationException("Le serveur n'a pas pu démarrer.");
        var opening = StartupReadiness.WaitAsync(Task.FromException(failure), TimeSpan.FromSeconds(1), _ =>
        {
            frameRequested = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => opening));
        Assert.False(frameRequested);
    }

    [Fact]
    public async Task DestroyingTheActivityCancelsItsWaitWithoutStoppingSharedInitialization()
    {
        using var destroyed = new CancellationTokenSource();
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frameRequested = false;
        var opening = StartupReadiness.WaitAsync(startup.Task, TimeSpan.Zero, _ =>
        {
            frameRequested = true;
            return Task.CompletedTask;
        }, destroyed.Token);

        destroyed.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        Assert.False(startup.Task.IsCompleted);
        Assert.False(frameRequested);
        startup.SetResult();
    }

}
