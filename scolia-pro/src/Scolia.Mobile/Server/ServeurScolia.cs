using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.Net;
using Android.OS;

namespace Scolia.Mobile.Server;

/// <summary>
/// Service de premier plan du téléphone serveur : tant qu'il tourne, Android garde SCOLIA en vie même écran éteint
/// ou application quittée, et la notification rappelle que les autres postes en dépendent. Le Wi-Fi reste éveillé
/// et reçoit les recherches des autres postes.
/// </summary>
// Le serveur maintient volontairement une connexion réseau vers des appareils
// autorisés : connectedDevice est le type Android prévu pour cette activité.
[Service(Name = "com.jpservices.scolia.ServeurScolia", Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class ServeurScolia : Service
{
    private const string Channel = "serveur";
    private const int NotificationId = 5080;

    private static int active;
    private static WeakReference<ServeurScolia>? activeInstance;
    private string currentStatus = "Démarrage de l'API scolaire…";

    /// <summary>
    /// L'état affiché correspond au diagnostic réel, pas à la seule présence du service.
    /// </summary>
    internal static void UpdateServerNotification(string status)
    {
        if (activeInstance?.TryGetTarget(out var service) != true || !IsActive) return;
        if (string.Equals(service.currentStatus, status, StringComparison.Ordinal)) return;
        service.currentStatus = status;
        try
        {
            if (service.GetSystemService(NotificationService) is NotificationManager manager)
                manager.Notify(NotificationId, service.BuildNotification());
        }
        catch (Java.Lang.Exception ex)
        { Android.Util.Log.Warn("SCOLIA-Serveur", $"Actualisation notification : {ex.GetType().Name}"); }
    }

    /// <summary>
    /// Le passage vers l'écran d'accueil Android déclenche un vrai test réseau
    /// après que l'activité est passée en arrière-plan, sans arrêter l'hébergement.
    /// </summary>
    internal static void CheckAfterInterfacePaused()
    {
        if (activeInstance?.TryGetTarget(out var service) == true)
            _ = service.VerifyAfterPauseAsync();
    }

    private async Task VerifyAfterPauseAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (IsActive) await VerifyServerAsync();
    }
    /// <summary>Le service a effectivement été promu au premier plan Android.</summary>
    public static bool IsActive => System.Threading.Volatile.Read(ref active) != 0;

    private PowerManager.WakeLock? awake;
    private WifiManager.WifiLock? wifi;
    private WifiManager.MulticastLock? multicast;
    // Ce contrôle vit dans le service, pas dans l'Activity. Il continue donc
    // lorsque l'écran s'éteint et ne recrée jamais inutilement une base.
    private System.Threading.Timer? serverWatchdog;
    private ConnectivityManager? connectivity;
    private ConnectivityManager.NetworkCallback? networkObserver;

    public static void Begin(Context context)
    {
        var intent = new Intent(context, typeof(ServeurScolia));
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
        else context.StartService(intent);
    }

    public static void End(Context context) => context.StopService(new Intent(context, typeof(ServeurScolia)));

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Une invitation ou un changement de poste peut avoir désactivé l'hébergement
        // pendant qu'Android préparait le service. Ne jamais conserver un serveur
        // sur un appareil désormais client.
        if (Scolia.Bureau.Infrastructure.ConnectionStore.Load()?.Mode !=
            Scolia.Bureau.Infrastructure.ConnectionSettings.Local)
        {
            StopSelfResult(startId);
            return StartCommandResult.NotSticky;
        }

        var notification = BuildNotification();
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34)) StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
            else StartForeground(NotificationId, notification);
        }
        catch (Exception ex) when (ex is Java.Lang.RuntimeException or Java.Lang.SecurityException or InvalidOperationException)
        {
            // Android 12+ peut refuser un lancement de premier plan trop tardif,
            // notamment depuis un contexte déjà en arrière-plan. Ne jamais
            // laisser croire qu'une notification active héberge les données.
            Android.Util.Log.Error("SCOLIA", $"Service serveur refusé par Android : {ex.Message}");
            StopSelfResult(startId);
            return StartCommandResult.NotSticky;
        }
        System.Threading.Interlocked.Exchange(ref active, 1);
        activeInstance = new WeakReference<ServeurScolia>(this);
        Hold();
        // Si Android recrée le service après une récupération mémoire, rétablir aussi
        // le serveur de données. Une notification seule ne rendrait pas SCOLIA accessible.
        // Aucune tentative si ce téléphone est configuré comme simple client.
        try
        {
            if (Scolia.Bureau.Infrastructure.ConnectionStore.Load()?.Mode ==
                Scolia.Bureau.Infrastructure.ConnectionSettings.Local)
            {
                Scolia.Mobile.Security.AndroidSecrets.Initialize();
                PhoneServer.Get(ApplicationContext ?? this).RestoreAfterServiceRestart();
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("SCOLIA", $"Serveur non restauré : {ex.Message}");
        }
        // Si une erreur locale coupe l'API alors que l'écran est éteint,
        // une vérification légère du serveur tente de la restaurer. La vérification
        // ne démarre ni Activity ni nouveau service de premier plan.
        serverWatchdog ??= new System.Threading.Timer(_ => _ = VerifyServerAsync(),
            null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45));
        // La liaison locale reste ouverte même quand la connexion Wi-Fi/4G change.
        // Le callback réseau vit dans le service, pas dans la fenêtre Android.
        RegisterNetworkObserver();
        // La reprise reste soumise au système Android et à l'alimentation de l'appareil.
        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Retirer la fenêtre de l'écran des applications récentes ne demande pas l'arrêt
    /// du serveur de l'établissement. Android garde le service au premier plan et
    /// sa notification ; l'utilisateur peut arrêter le serveur en changeant de mode
    /// ou en arrêtant explicitement le service système.
    /// </summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        Android.Util.Log.Info("SCOLIA", "Interface retirée : le serveur reste hébergé en service de premier plan.");
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        System.Threading.Interlocked.Exchange(ref active, 0);
        if (activeInstance?.TryGetTarget(out var registered) == true && ReferenceEquals(registered, this))
            activeInstance = null;
        serverWatchdog?.Dispose();
        serverWatchdog = null;
        if (connectivity is not null && networkObserver is not null)
        {
            try { connectivity.UnregisterNetworkCallback(networkObserver); }
            catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
            { Android.Util.Log.Warn("SCOLIA", $"Observateur réseau retiré : {ex.GetType().Name}"); }
        }
        networkObserver = null;
        connectivity = null;
        Release();
        base.OnDestroy();
    }

    private async Task VerifyServerAsync()
    {
        try
        {
            if (!IsActive) return;
            // Android peut relâcher certaines ressources après de longues périodes
            // de veille ; le service récupère celles que le système autorise encore.
            Hold();
            await PhoneServer.Get(ApplicationContext ?? this).VerifyAndRecoverInBackgroundAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { Android.Util.Log.Warn("SCOLIA", $"Contrôle d'hébergement différé : {ex.GetType().Name}"); }
    }

    private void RegisterNetworkObserver()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(24) || networkObserver is not null) return;
        try
        {
            if (GetSystemService(ConnectivityService) is not ConnectivityManager manager) return;
            var observer = new HostNetworkCallback(this);
            manager.RegisterDefaultNetworkCallback(observer);
            connectivity = manager;
            networkObserver = observer;
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.RuntimeException)
        { Android.Util.Log.Warn("SCOLIA", $"Observation réseau indisponible : {ex.GetType().Name}"); }
    }

    /// <summary>Une connexion disponible provoque une vérification sans redémarrer Kestrel à tort.</summary>
    private sealed class HostNetworkCallback(ServeurScolia host) : ConnectivityManager.NetworkCallback
    {
        public override void OnAvailable(Android.Net.Network network) => _ = host.VerifyServerAsync();
    }

    private Notification BuildNotification()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && GetSystemService(NotificationService) is NotificationManager manager)
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "Serveur de l'établissement", NotificationImportance.Low)
            {
                Description = "SCOLIA est actif : les autres postes de l'établissement travaillent sur ce téléphone.",
            });
        var open = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        var tap = open is null ? null : PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, Channel) : new Notification.Builder(this);
        builder.SetContentTitle("SCOLIA · serveur de l'établissement")
            .SetContentText(currentStatus)
            .SetSmallIcon(Resource.Mipmap.icon)
            .SetOngoing(true);
        if (tap is not null) builder.SetContentIntent(tap);
        return builder.Build()!;
    }

    private void Hold()
    {
        try
        {
            if (awake is null && GetSystemService(PowerService) is PowerManager power)
            {
                awake = power.NewWakeLock(WakeLockFlags.Partial, "scolia:serveur");
                awake?.SetReferenceCounted(false);
            }
            if (awake?.IsHeld != true) awake?.Acquire();
            if (GetSystemService(WifiService) is WifiManager manager)
            {
                if (wifi is null)
                {
                    // Le Wi-Fi doit rester éveillé lorsque le téléphone héberge l'établissement.
#pragma warning disable CA1422 // FullHighPerf reste requis sur les anciennes versions d'Android.
                    wifi = manager.CreateWifiLock(
                        OperatingSystem.IsAndroidVersionAtLeast(34)
                            ? Android.Net.WifiMode.FullLowLatency
                            : Android.Net.WifiMode.FullHighPerf,
                        "scolia:serveur");
#pragma warning restore CA1422
                    wifi?.SetReferenceCounted(false);
                }
                if (wifi?.IsHeld != true) wifi?.Acquire();
                if (multicast is null)
                {
                    multicast = manager.CreateMulticastLock("scolia:decouverte");
                    multicast?.SetReferenceCounted(false);
                }
                if (multicast?.IsHeld != true) multicast?.Acquire();
            }
        }
        catch (Java.Lang.SecurityException)
        {
            Android.Util.Log.Warn("SCOLIA", "Android a refusé un verrou de disponibilité du serveur.");
        }
    }

    private void Release()
    {
        if (awake?.IsHeld == true) awake.Release();
        if (wifi?.IsHeld == true) wifi.Release();
        if (multicast?.IsHeld == true) multicast.Release();
        awake = null;
        wifi = null;
        multicast = null;
    }
}
