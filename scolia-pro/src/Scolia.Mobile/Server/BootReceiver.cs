using Android.App;
using Android.Content;
using Android.Util;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Server;

/// <summary>
/// Si cet appareil a été configuré volontairement comme serveur SCOLIA,
/// réactive sa notification et son serveur local après un redémarrage Android.
/// Android reste libre de différer/refuser le lancement ; aucune tentative
/// n'est faite sur les téléphones clients.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted) return;
        try
        {
            if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
            ServeurScolia.Begin(context.ApplicationContext ?? context);
        }
        catch (Exception ex)
        {
            // Sur certains Android 15/16, la politique batterie interdit
            // le redémarrage en arrière-plan. L'ouverture manuelle reste possible.
            Log.Warn("SCOLIA", $"Redémarrage du serveur différé : {ex.Message}");
        }
    }
}
