using Android.App;
using Android.Gms.Tasks;
using Android.Runtime;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;
using GoogleTask = Android.Gms.Tasks.Task;

namespace Scolia.Mobile;

/// <summary>
/// Vrai lecteur en direct : Google Play services affiche sa fenêtre de visée
/// avec zoom automatique, sans permission caméra pour SCOLIA. Si ce composant
/// est absent (ou pas téléchargé), la photo Android reste utilisable hors ligne.
/// Aucune image du QR n'est envoyée au serveur.
/// </summary>
public static class AndroidQrScanner
{
    private static WeakReference<Activity>? foreground;
    public static void Attach(Activity activity) => foreground = new WeakReference<Activity>(activity);

    public static async System.Threading.Tasks.Task<string?> ScanAsync()
    {
        if (foreground?.TryGetTarget(out var activity) != true ||
            activity is null || activity.IsFinishing || activity.IsDestroyed) return null;

        try
        {
            using var options = new GmsBarcodeScannerOptions.Builder()
                .SetBarcodeFormats(Barcode.FormatQrCode)
                .EnableAutoZoom()
                .Build();
            using var scanner = GmsBarcodeScanning.GetClient(activity, options);
            var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var listener = new ScannerListener(result);
            using var request = scanner.StartScan().AddOnCompleteListener(listener);
            return await result.Task.WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException
            or TimeoutException or TaskCanceledException)
        {
            Android.Util.Log.Warn("SCOLIA-QR", $"Scanner natif indisponible ({ex.GetType().Name}) : prise de photo de secours.");
            var photo = await AndroidCamera.CaptureAsync();
            return photo is null ? null : await System.Threading.Tasks.Task.Run(() =>
                Scolia.Bureau.Infrastructure.QrCodes.Read(photo));
        }
    }

    private sealed class ScannerListener(TaskCompletionSource<string?> completion)
        : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(GoogleTask task)
        {
            if (task.IsCanceled) completion.TrySetResult(null);
            else if (task.IsSuccessful)
                completion.TrySetResult(task.Result?.JavaCast<Barcode>()?.RawValue);
            else completion.TrySetException(new InvalidOperationException(
                "Le scanner QR du téléphone n'est pas disponible."));
        }
    }
}
