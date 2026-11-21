using Android.Content;
using AndroidX.Work;
using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Mobile.Synchronization;

/// <summary>Reprise différée sans interface. Android choisit le créneau ; aucune garantie en cas d'arrêt forcé.</summary>
[Android.Runtime.Register("com/jpservices/scolia/SyncWorker")]
public sealed class SyncWorker : Worker
{
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);
    private static readonly object ResumeGate = new();
    private static DateTime lastResumeUtc;

    // La reprise Cloudflare ne requiert aucun serveur SCOLIA historique.
    // Pour un appareil configuré "local", ne programmer un accès réseau que
    // si une session locale et une origine centrale approuvée existent.
    private static bool HasLocalCentralSession(ConnectionSettings? connection)
    {
        if (connection is not null && connection.Mode != ConnectionSettings.Local)
            return false;
        try
        {
            var store = new OfflineStore(
                ConnectionStore.CacheAddress(ConnectionStore.LocalAddress));
            if (store.LastAccount is not { Length: > 0 } account ||
                !store.CanSyncCentralInBackgroundFor(account))
                return false;
            using var pairing = new CentralPairingClient();
            return pairing.ReadActiveOrigin(account, DeviceIdentity.Id) is not null;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or System.Security.Cryptography.CryptographicException
            or JsonException or ArgumentException)
        {
            Android.Util.Log.Warn("SCOLIA-Central",
                $"Autorisation locale illisible ({exception.GetType().Name}).");
            return false;
        }
    }

    /// <summary>Reprise opportuniste à l'ouverture de SCOLIA Client.
    /// WorkManager reste soumis aux règles d'Android : pas de temps réel garanti.</summary>
    public static void ScheduleOnResume(Context context)
    {
        var saved = ConnectionStore.Load();
        if (saved?.Mode is not (ConnectionSettings.Remote or ConnectionSettings.Internet) &&
            !HasLocalCentralSession(saved)) return;
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
        if (saved?.Mode is not (ConnectionSettings.Remote or ConnectionSettings.Internet) &&
            !HasLocalCentralSession(saved)) return;
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
        // Un téléphone dont l'établissement a basculé sur Cloudflare peut
        // être configuré comme "local" sans PC principal. Dans ce cas, ne
        // pas retourner avant la synchronisation des sections D1.
        if (connection is null || connection.Mode == ConnectionSettings.Local)
        {
            if (!HasLocalCentralSession(connection)) return Result.InvokeSuccess();
            return await RunCloudOnlyAsync(ConnectionStore.LocalAddress);
        }
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
            if (saved is null || saved.Host)
                return Result.InvokeSuccess();
            if (!saved.Enabled || saved.Link.ExpiresUtc <= DateTime.UtcNow)
                return await RunCloudOnlyAsync(saved.Link.VirtualServer);
            relay = saved.Link;
            server = relay.VirtualServer;
        }
        if (server is null)
        {
            // Un ancien relais sans adresse valide n'empêche pas la
            // synchronisation D1, si son cache reste identifiable.
            if (connection.Mode == ConnectionSettings.Remote &&
                connection.CacheUrl is { Length: > 0 } cached &&
                Uri.TryCreate(cached, UriKind.Absolute, out var original))
                return await RunCloudOnlyAsync(original);
            return Result.InvokeSuccess();
        }
        // Même hors interface, ne jamais envoyer les cookies de session à un
        // nom MagicDNS qui pointerait désormais hors de l'espace privé Tailscale.
        if (TailscaleEndpoint.IsTailnetAddress(server)
            && !await TailscaleEndpoint.ResolvesPrivatelyAsync(server))
        {
            // Bloquer la liaison privée devenue non fiable, mais continuer
            // exclusivement vers l'origine HTTPS Cloudflare déjà approuvée.
            _ = await RunCloudOnlyAsync(server);
            return Result.InvokeRetry();
        }
        using var api = new ScoliaClient(server);
        if (relay is not null) api.UseRemote(relay);
        var store = new OfflineStore(ConnectionStore.CacheAddress(api.Server));
        // Une boîte aux lettres chiffrée existe indépendamment du serveur principal.
        // Garder la même copie de travail et le même magasin, y compris hors connexion.
        api.Offline = store;
        if (store.SavedCredential is { SessionCookie.Length: > 0 } credential)
            api.RestoreSessionCookie(credential.SessionCookie);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        // Cloudflare et le serveur historique sont indépendants : ce premier
        // passage conserve les opérations D1 si le PC est injoignable.
        var centralNeedsRetry = await SyncCentralSectionsInBackgroundAsync(
            api, store, timeout.Token);
        if (!await api.IsRunningAsync(timeout.Token))
        {
            // Même lorsque le PC serveur est éteint, déposer les opérations déjà
            // autorisées auprès de la console HTTPS. L'outbox locale n'est jamais
            // soldée sur simple accusé de stockage ; seul le serveur la valide.
            foreach (var account in store.AccountsAwaitingCloudDeposit())
            {
                if (IsStopped || timeout.IsCancellationRequested) break;
                store.User = account;
                await api.DepositPendingAsync(timeout.Token);
            }
            return Result.InvokeRetry();
        }
        // Le jumelage donne seulement accès au serveur, jamais une session de compte.
        // Une session utilisateur encore valide peut reprendre en arrière-plan ;
        // sinon la synchronisation attend sa prochaine authentification personnelle.
        if (!await api.VerifyLinkedDeviceAsync(timeout.Token)) return Result.InvokeSuccess();
        var active = await api.CurrentSessionAsync(timeout.Token);
        if (active is null) return Result.InvokeSuccess();
        store.User = active.UserName;
        // Une opération que le serveur a déjà appliquée depuis la boîte cloud
        // doit disparaître de l'outbox avant la reprise directe. Aucun paiement
        // ne reçoit de reçu définitif lors d'un simple dépôt cloud.
        await api.ReconcileCloudAsync(timeout.Token);
        await api.FlushAsync(timeout.Token);
        // Sans interface, reprendre les ressources déjà reçues plutôt que repartir
        // depuis la première fiche. Le curseur est chiffré et propre au compte.
        // Aucun droit scolaire n'est inventé : seuls les droits de la session
        // réellement rétablie sont utilisés par ce travail différé.
        var preparationKind = OfflinePreparation.KindForPermissions(active.Permissions);
        if (preparationKind != OfflinePreparationKind.None)
        {
            try
            {
                if ((preparationKind & OfflinePreparationKind.StudentRecords) != 0)
                {
                    var school = await OfflinePreparation.RunAsync(api,
                        includeFinancialAccounts: active.Permissions.Contains(
                            Perm.PaymentsRead, StringComparer.OrdinalIgnoreCase),
                        ct: timeout.Token);
                    if (!school.Complete && school.Expected <= OfflinePreparation.MaximumStudents)
                        return Result.InvokeRetry();
                }

                if ((preparationKind & OfflinePreparationKind.AnonymousTeacherSheets) != 0)
                {
                    // Le cache pédagogique dispose de son propre curseur chiffré.
                    // Un même compte peut consulter les dossiers ET saisir des notes,
                    // sans que ces deux préparations effacent la progression mutuelle.
                    var teaching = await OfflinePreparation.RunAsync(api,
                        includeTeacherSheets: true, ct: timeout.Token);
                    if (!teaching.Complete) return Result.InvokeRetry();
                }
            }
            catch (ApiException ex) when (ex.Status is 401 or 403)
            {
                // Session ou permission révoquée : rien n'est effacé localement ;
                // la prochaine reconnexion personnelle sera nécessaire.
                return Result.InvokeSuccess();
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return Result.InvokeRetry();
            }
        }
        // Faire tourner les copies locales les plus anciennes, en paquets bornés :
        // actualiser indéfiniment les mêmes 32 entrées empêchait les autres
        // dossiers préchargés d'être remis à jour.
        foreach (var path in store.CachedPaths(limit: 24))
        {
            if (IsStopped || timeout.IsCancellationRequested) break;
            try { await api.GetAsync<JsonElement>(path, timeout.Token); }
            catch (ApiException ex) when (ex.Status is 400 or 403 or 404) { }
        }
        return store.Outbox().Count > 0 || centralNeedsRetry
            ? Result.InvokeRetry() : Result.InvokeSuccess();
    }

    /// <summary>
    /// N'effectue aucun appel vers l'ancien serveur. L'URI sert uniquement
    /// à retrouver la base SQLite chiffrée de l'école déjà jumelée.
    /// Même après l'expiration d'un relais Render, D1 peut être joignable.
    /// </summary>
    private async Task<Result> RunCloudOnlyAsync(Uri cachedServer)
    {
        using var cloudApi = new ScoliaClient(cachedServer);
        var cache = new OfflineStore(ConnectionStore.CacheAddress(cachedServer));
        cloudApi.Offline = cache;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        return await SyncCentralSectionsInBackgroundAsync(cloudApi, cache, timeout.Token)
            ? Result.InvokeRetry() : Result.InvokeSuccess();
    }

    /// <summary>
    /// Reprendre le pilote D1 avec la seule origine HTTPS approuvée, jamais
    /// avec le cookie du PC principal. Aucun JWT n'est conservé sur disque.
    /// Les erreurs temporaires conservent SQLite et déclenchent le backoff.
    /// </summary>
    private async Task<bool> SyncCentralSectionsInBackgroundAsync(
        ScoliaClient api, OfflineStore store, CancellationToken ct)
    {
        if (store.LastAccount is not { Length: > 0 } account ||
            !store.CanSyncCentralInBackgroundFor(account))
            return false;
        try
        {
            store.User = account;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(35));
            var state = await api.SyncActiveCentralSectionsAsync(account, budget.Token);
            return state is { Offline: true } or { HasMorePages: true };
        }
        catch (CentralPairingException exception)
        {
            Android.Util.Log.Warn("SCOLIA-Central",
                $"Autorisation centrale à renouveler ({exception.Status}).");
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or
            TaskCanceledException or IOException)
        {
            Android.Util.Log.Warn("SCOLIA-Central",
                $"Reprise différée des sections ({exception.GetType().Name}).");
            return true;
        }
        catch (Exception exception) when (exception is
            System.Security.Cryptography.CryptographicException or
            UnauthorizedAccessException or JsonException or ArgumentException or
            InvalidOperationException)
        {
            // N'effacer aucune file SQLite en cas de problème de chiffrement.
            Android.Util.Log.Warn("SCOLIA-Central",
                $"Réplique chiffrée indisponible ({exception.GetType().Name}).");
            return false;
        }
    }
}
