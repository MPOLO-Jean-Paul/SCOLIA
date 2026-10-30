using Android.Content;
using AndroidX.Work;
using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Synchronization;

/// <summary>Reprise différée sans interface. Android choisit le créneau ; aucune garantie en cas d'arrêt forcé.</summary>
[Android.Runtime.Register("com/jpservices/scolia/SyncWorker")]
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
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        { return Result.InvokeRetry(); }
        // Une session expirée ou un accès refusé ne doit pas relancer la synchronisation
        // indéfiniment : conserver la file locale et demander la reconnexion à l'utilisateur.
        catch (ApiException ex) when (ex.Status is 401 or 403 or 409)
        { Android.Util.Log.Warn("SCOLIA-Sync", $"Opérations en attente, intervention nécessaire ({ex.Status})."); return Result.InvokeSuccess(); }
        catch (ApiException ex) when (ex.Status is 408 or 429 or >= 500)
        { return Result.InvokeRetry(); }
        catch (Exception ex) when (ex is ApiException or JsonException
            or System.Security.Cryptography.CryptographicException or FormatException)
        { Android.Util.Log.Warn("SCOLIA-Sync", $"Synchronisation différée : {ex.Message}"); return Result.InvokeFailure(); }
    }

    private async Task<Result> RunAsync()
    {
        Security.AndroidSecrets.Initialize();
        // Un téléphone client peut rejoindre un serveur privé Tailscale sans relais cloud.
        // WorkManager reste différé (~15 min minimum) : jamais une promesse de temps réel
        // quand Android a arrêté l'application cliente.
        var connection = ConnectionStore.Load();
        Uri? server = null;
        Scolia.Transport.RemoteLink? relay = null;
        if (connection is { Mode: ConnectionSettings.Remote, Url: { Length: > 0 } url }
            && Uri.TryCreate(url, UriKind.Absolute, out var privateServer)
            && TailscaleEndpoint.IsTailnetAddress(privateServer))
        {
            server = privateServer;
        }
        else if (connection?.Mode == ConnectionSettings.Internet)
        {
            var saved = RemoteConnection.Load();
            if (saved is null || saved.Host || !saved.Enabled || saved.Link.ExpiresUtc <= DateTime.UtcNow)
                return Result.InvokeSuccess();
            relay = saved.Link;
            server = relay.VirtualServer;
        }
        if (server is null) return Result.InvokeSuccess();
        using var api = new ScoliaClient(server);
        if (relay is not null) api.UseRemote(relay);
        var store = new OfflineStore(api.Server);
        if (store.SavedCredential is not { SessionCookie.Length: > 0 } credential) return Result.InvokeSuccess();
        store.User = credential.UserName;
        api.Offline = store;
        api.RestoreSessionCookie(credential.SessionCookie);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        if (!await api.IsRunningAsync(timeout.Token)) return Result.InvokeRetry();
        // Une session expirée attend une connexion normale ; aucun droit auto-déclaré, aucun mot de passe en tâche native.
        if (await api.CurrentSessionAsync(timeout.Token) is null) return Result.InvokeSuccess();
        await api.FlushAsync(timeout.Token);
        foreach (var path in store.CachedPaths())
        {
            if (IsStopped) break;
            try { await api.GetAsync<JsonElement>(path, timeout.Token); }
            catch (ApiException ex) when (ex.Status is 400 or 403 or 404) { }
        }
        return store.Outbox().Count > 0 ? Result.InvokeRetry() : Result.InvokeSuccess();
    }
}
