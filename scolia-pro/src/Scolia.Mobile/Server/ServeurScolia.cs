using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;

namespace Scolia.Mobile.Server;

/// <summary>
/// Service de premier plan du téléphone serveur : tant qu'il tourne, Android garde SCOLIA en vie même écran éteint
/// ou application quittée, et la notification rappelle que les autres postes en dépendent. Le Wi-Fi reste éveillé
/// et reçoit les recherches des autres postes.
/// </summary>
[Service(Name = "com.jpservices.scolia.ServeurScolia", Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
[MetaData(Name = "android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE",
    Value = "Local school records server voluntarily enabled by the user, with visible notification for authorized devices")]
public sealed class ServeurScolia : Service
{
    private const string Channel = "serveur";
    private const int NotificationId = 5080;

    private PowerManager.WakeLock? awake;
    private WifiManager.WifiLock? wifi;
    private WifiManager.MulticastLock? multicast;

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
        var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else StartForeground(NotificationId, notification);
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
        // La reprise reste soumise au système Android et à l'alimentation de l'appareil.
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        Release();
        base.OnDestroy();
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
        builder.SetContentTitle("SCOLIA · serveur actif")
            .SetContentText("Le serveur SCOLIA partage les données locales. Gardez Tailscale connecté si vous travaillez à distance.")
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
                awake?.Acquire();
            }
            if (GetSystemService(WifiService) is WifiManager manager)
            {
                if (wifi is null)
                {
#pragma warning disable CA1422 // FullHighPerf reste le mode qui garde le Wi-Fi éveillé sur les anciens Android.
                    wifi = manager.CreateWifiLock(OperatingSystem.IsAndroidVersionAtLeast(29) ? Android.Net.WifiMode.FullLowLatency : Android.Net.WifiMode.FullHighPerf, "scolia:serveur");
#pragma warning restore CA1422
                    wifi?.SetReferenceCounted(false);
                    wifi?.Acquire();
                }
                if (multicast is null)
                {
                    multicast = manager.CreateMulticastLock("scolia:decouverte");
                    multicast?.SetReferenceCounted(false);
                    multicast?.Acquire();
                }
            }
        }
        catch (Java.Lang.SecurityException) { }
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
