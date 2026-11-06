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
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);
    private static readonly object ResumeGate = new();
    private static DateTime lastResumeUtc;

    /// <summary>Reprise opportuniste à l'ouverture de SCOLIA Client.
    /// WorkManager reste soumis aux règles d'Android : pas de temps réel garanti.</summary>
    public static void ScheduleOnResume(Context context)
    {
        var saved = ConnectionStore.Load();
        if (saved?.Mode is not (ConnectionSettings.Remote or ConnectionSettings.Internet)) return;
        lock (ResumeGate)
        {
            var now = DateTime.UtcNow;
            if (now - lastResumeUtc < TimeSpan.FromMinutes(2)) return;
            lastResumeUtc = now;
        }
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected)!.Build();
        var once = new OneTimeWorkRequest.Builder(typeof(SyncWorker))
            .SetConstraints(constraints)!.Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork(
            "scolia-synchronisation-reprise", ExistingWorkPolicy.Keep!, once);
    }

    /// <summary>
    /// Une opération ajoutée à l'outbox déclenche une tâche réseau unique.
    /// Contrairement au périodique de 15 minutes, Android peut lancer celle-ci
    /// dès qu'une connexion est disponible ; Doze et le système restent prioritaires.
    /// </summary>
    public static void SchedulePending(Context context)
    {
        var saved = ConnectionStore.Load();
        if (saved?.Mode is not (ConnectionSettings.Remote or ConnectionSettings.Internet)) return;
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected)!.Build();
        var once = new OneTimeWorkRequest.Builder(typeof(SyncWorker))
            .SetConstraints(constraints)!
            .SetBackoffCriteria(BackoffPolicy.Exponential, 30, Java.Util.Concurrent.TimeUnit.Seconds!)!
            .Build();
        WorkManager.GetInstance(context).EnqueueUniqueWork(
            "scolia-synchronisation-pending", ExistingWorkPolicy.Keep!, once);
    }

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
        // La reprise et la tâche périodique ne doivent pas accéder
        // simultanément au même cache et à la même file hors ligne.
        if (!WorkerGate.Wait(0)) return Result.InvokeRetry();
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
        finally { WorkerGate.Release(); }
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
            && Uri.TryCreate(url, UriKind.Absolute, out var privateServer))
        {
            server = privateServer;
            // Les clients Wi-Fi étaient totalement ignorés par ce travail de fond.
            // Si l'IP change, ne mémoriser qu'un serveur prouvé par l'ancienne
            // autorisation cryptographique. Le cache garde son origine stable.
            if (!TailscaleEndpoint.IsTailnetAddress(privateServer))
            {
                using var check = new ScoliaClient(privateServer);
                if (!await check.IsRunningAsync() &&
                    await TrustedLanReconnect.FindAsync(connection, TimeSpan.FromSeconds(2.5)) is { } moved)
                {
                    try
                    {
                        TrustedLanReconnect.PreserveGrant(privateServer, moved.Address);
                        if (ConnectionStore.TrySave(connection with
                        {
                            Url = moved.Address.ToString(),
                            CacheUrl = connection.CacheUrl ?? connection.Url,
                            SchoolId = moved.SchoolId, School = moved.School, Machine = moved.Machine,
                        }))
                            server = moved.Address;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                        System.Security.Cryptography.CryptographicException)
                    {
                        Android.Util.Log.Warn("SCOLIA-Sync", $"Liaison historique non transférée : {ex.GetType().Name}");
                    }
                }
            }
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
        // Même hors interface, ne jamais envoyer les cookies de session à un
        // nom MagicDNS qui pointerait désormais hors de l'espace privé Tailscale.
        if (TailscaleEndpoint.IsTailnetAddress(server)
            && !await TailscaleEndpoint.ResolvesPrivatelyAsync(server))
            return Result.InvokeRetry();
        using var api = new ScoliaClient(server);
        if (relay is not null) api.UseRemote(relay);
        var store = new OfflineStore(ConnectionStore.CacheAddress(api.Server));
        if (store.SavedCredential is { SessionCookie.Length: > 0 } credential)
            api.RestoreSessionCookie(credential.SessionCookie);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        if (!await api.IsRunningAsync(timeout.Token)) return Result.InvokeRetry();
        // QR déjà autorisé : rouvrir la session d'appareil sans mot de passe.
        // En cas d'expiration/retrait, ne pas contourner les droits du serveur.
        var active = await api.CurrentSessionAsync(timeout.Token) ??
            await api.RestoreDeviceSessionAsync(timeout.Token);
        if (active is null) return Result.InvokeSuccess();
        store.User = active.UserName;
        api.Offline = store;
        await api.FlushAsync(timeout.Token);
        // Faire tourner les copies locales les plus anciennes, en paquets bornés :
        // actualiser indéfiniment les mêmes 32 entrées empêchait les autres
        // dossiers préchargés d'être remis à jour.
        foreach (var path in store.CachedPaths(limit: 24))
        {
            if (IsStopped || timeout.IsCancellationRequested) break;
            try { await api.GetAsync<JsonElement>(path, timeout.Token); }
            catch (ApiException ex) when (ex.Status is 400 or 403 or 404) { }
        }
        return store.Outbox().Count > 0 ? Result.InvokeRetry() : Result.InvokeSuccess();
    }
}
