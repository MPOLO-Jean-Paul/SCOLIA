namespace Scolia.Mobile;

/// <summary>Le lancement n'est terminé qu'après l'initialisation et la première image de sa destination.</summary>
internal static class StartupReadiness
{
    public static async Task WaitAsync(Task startup, TimeSpan minimum,
        Func<CancellationToken, Task> firstFrame, CancellationToken cancellationToken)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Annuler l'attente d'une activité détruite ne coupe pas la préparation de l'application partagée.
        await startup.WaitAsync(cancellationToken).ConfigureAwait(false);
        var remaining = minimum - watch.Elapsed;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await firstFrame(cancellationToken).ConfigureAwait(false);
    }
}
