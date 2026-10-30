using Android.Content;
using AndroidX.Work;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Synchronization;

/// <summary>Reprise différée sans interface. Android choisit le créneau ; aucune garantie en cas d'arrêt forcé.</summary>
public sealed class SyncWorker : Worker
{
    public SyncWorker(Context context, WorkerParameters parameters) : base(context, parameters) { }

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected)!.SetRequiresBatteryNotLow(true)!.Build();
        var periodic = new PeriodicWorkRequest.Builder(typeof(SyncWorker), 15, Java.Util.Concurrent.TimeUnit.Minutes!)
            .SetConstraints(constraints)!
            .SetBackoffCriteria(BackoffPolicy.Exponential, 30, Java.Util.Concurrent.TimeUnit.Seconds!)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork("scolia-synchronisation", ExistingPeriodicWorkPolicy.Keep!, periodic);
    }

    public override Result DoWork()
    {
        try { return RunAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { return Result.InvokeRetry(); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException) { return Result.InvokeFailure(); }
    }

    private async Task<Result> RunAsync()
    {
        Security.AndroidSecrets.Initialize();
        var saved = RemoteConnection.Load();
        if (saved is null || saved.Host || saved.Link.ExpiresUtc <= DateTime.UtcNow ||
            ConnectionStore.Load()?.Mode != ConnectionSettings.Internet) return Result.InvokeSuccess();
        using var api = new ScoliaClient(saved.Link.VirtualServer);
        api.UseRemote(saved.Link);
        var store = new OfflineStore(api.Server);
        if (store.SavedCredential is not { SessionCookie.Length: > 0 } credential) return Result.InvokeSuccess();
        store.User = credential.UserName;
        if (store.Outbox().Count == 0) return Result.InvokeSuccess();
        api.Offline = store;
        api.RestoreSessionCookie(credential.SessionCookie);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        if (!await api.IsRunningAsync(timeout.Token)) return Result.InvokeRetry();
        // Une session expirée attend une connexion normale ; aucun droit auto-déclaré, aucun mot de passe en tâche native.
        if (await api.CurrentSessionAsync(timeout.Token) is null) return Result.InvokeSuccess();
        await api.FlushAsync(timeout.Token);
        return store.Outbox().Count > 0 ? Result.InvokeRetry() : Result.InvokeSuccess();
    }
}
