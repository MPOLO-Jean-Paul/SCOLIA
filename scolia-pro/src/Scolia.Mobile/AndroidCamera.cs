using Android.App;
using Android.Content;
using Android.Provider;

namespace Scolia.Mobile;

/// <summary>
/// Code QR du serveur : une photo prise avec l'appareil photo du téléphone, puis lue par SCOLIA sur l'appareil.
/// L'application Appareil photo d'Android fait la prise de vue : SCOLIA ne demande pas l'accès à la caméra.
/// </summary>
public static class AndroidCamera
{
    private const int RequestCode = 4721;
    private static TaskCompletionSource<byte[]?>? pending;
    private static string? photoPath;
    private static WeakReference<Activity>? current;

    public static void Attach(Activity activity) => current = new WeakReference<Activity>(activity);

    public static Task<byte[]?> CaptureAsync()
    {
        if (current?.TryGetTarget(out var activity) != true || activity is null) return Task.FromResult<byte[]?>(null);
        pending?.TrySetResult(null);
        var waiting = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = waiting;
        try
        {
            // Dossier interne partagé par le FileProvider de SCOLIA : la photo n'est visible d'aucune autre application.
            var folder = Path.Combine(activity.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "liaison");
            Directory.CreateDirectory(folder);
            photoPath = Path.Combine(folder, "code-qr.jpg");
            if (File.Exists(photoPath)) File.Delete(photoPath);
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, $"{activity.PackageName}.fichiers", new Java.IO.File(photoPath));
            var intent = new Intent(MediaStore.ActionImageCapture);
            intent.PutExtra(MediaStore.ExtraOutput, uri);
            intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
            if (intent.ResolveActivity(activity.PackageManager!) is null)
            {
                waiting.TrySetResult(null);
                return waiting.Task;
            }
            activity.StartActivityForResult(intent, RequestCode);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Android.Util.Log.Warn("SCOLIA", $"Appareil photo indisponible : {ex.GetType().Name}");
            waiting.TrySetResult(null);
        }
        return waiting.Task;
    }

    /// <summary>Retour de l'appareil photo ; faux si la réponse concerne une autre demande.</summary>
    public static bool Complete(int requestCode, Result resultCode)
    {
        if (requestCode != RequestCode) return false;
        byte[]? data = null;
        try
        {
            if (resultCode == Result.Ok && photoPath is not null && File.Exists(photoPath))
                data = File.ReadAllBytes(photoPath);
        }
        catch (IOException) { }
        finally
        {
            try { if (photoPath is not null && File.Exists(photoPath)) File.Delete(photoPath); } catch (IOException) { }
        }
        pending?.TrySetResult(data);
        pending = null;
        return true;
    }
}
