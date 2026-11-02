using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile;

/// <summary>
/// Notifications d'Android pour les alertes de SCOLIA : canal à haute importance, pour que l'encaissement ou la
/// sauvegarde faits sur un autre poste apparaissent en bannière en haut de l'écran, même hors de SCOLIA.
/// </summary>
public static class AndroidAlerts
{
    private const string Channel = "scolia-alertes";
    private static int next = 7000;
    private static volatile bool foreground;

    public static bool InForeground() => foreground;

    public static void SetForeground(bool value) => foreground = value;

    /// <summary>Android 13 et plus : la permission de notifier se demande une fois, à la première ouverture.</summary>
    public static void AskPermission(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return;
        if (activity.CheckSelfPermission(Manifest.Permission.PostNotifications) == Permission.Granted) return;
        try { activity.RequestPermissions([Manifest.Permission.PostNotifications], 4100); }
        catch (Java.Lang.Exception) { /* La bannière dans SCOLIA reste disponible. */ }
    }

    public static void Show(Context context, AppAlert alert)
    {
        try
        {
            if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                manager.CreateNotificationChannel(new NotificationChannel(Channel, "Encaissements et sauvegardes", NotificationImportance.High)
                {
                    Description = "Un encaissement ou une sauvegarde faits sur un autre poste de l'établissement.",
                });
            var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
            open?.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
            var tap = open is null ? null
                : PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var text = string.IsNullOrWhiteSpace(alert.Origin) ? alert.Text : $"{alert.Text}\n{alert.Origin}";
            var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(context, Channel) : new Notification.Builder(context);
            builder.SetContentTitle(alert.Title)
                .SetContentText(alert.Text)
                .SetStyle(new Notification.BigTextStyle().BigText(text))
                .SetSmallIcon(Resource.Mipmap.icon)
                .SetAutoCancel(true)
                .SetCategory(Notification.CategoryEvent)
                .SetShowWhen(true);
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            {
#pragma warning disable CA1422 // Avant Android 8, la priorité haute fait apparaître la bannière.
                builder.SetPriority((int)NotificationPriority.High);
                builder.SetDefaults(NotificationDefaults.All);
#pragma warning restore CA1422
            }
            if (tap is not null) builder.SetContentIntent(tap);
            manager.Notify(alert.Tag, Interlocked.Increment(ref next), builder.Build());
        }
        catch (Java.Lang.Exception ex)
        {
            Android.Util.Log.Warn("SCOLIA-Alertes", $"Notification impossible : {ex.GetType().Name}");
        }
    }
}
