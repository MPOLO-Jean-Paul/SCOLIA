using Android.App;
using Android.Content;

namespace Scolia.Mobile;

/// <summary>
/// Réception du signal système lorsque DownloadManager achève un téléchargement.
/// Le système n'autorise pas une boîte de dialogue par-dessus une autre application :
/// un rappel Android est affiché et la fenêtre Installer s'ouvre dès que SCOLIA est visible.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { DownloadManager.ActionDownloadComplete })]
public sealed class ScoliaUpdateDownloadReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != DownloadManager.ActionDownloadComplete) return;
        var id = intent.GetLongExtra(DownloadManager.ExtraDownloadId, -1);
        AndroidAppUpdates.DownloadCompleted(context, id);
    }
}
