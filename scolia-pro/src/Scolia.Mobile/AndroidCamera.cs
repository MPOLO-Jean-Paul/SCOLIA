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
    private const int GalleryRequestCode = 4722;
    private const int MaximumQrImageBytes = 12 * 1024 * 1024;
    private static TaskCompletionSource<byte[]?>? galleryPending;
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

    /// <summary>Importe un QR déjà enregistré ou une capture d'écran, sans permission de lecture générale.</summary>
    public static Task<byte[]?> PickImageAsync()
    {
        if (current?.TryGetTarget(out var activity) != true || activity is null)
            return Task.FromResult<byte[]?>(null);
        galleryPending?.TrySetResult(null);
        var waiting = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        galleryPending = waiting;
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.SetType("image/*");
            intent.AddCategory(Intent.CategoryOpenable);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            activity.StartActivityForResult(intent, GalleryRequestCode);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException or ArgumentException)
        {
            Android.Util.Log.Warn("SCOLIA-QR", $"Importation d'image indisponible : {ex.GetType().Name}");
            waiting.TrySetResult(null);
        }
        return waiting.Task;
    }

    /// <summary>Retour de l'appareil photo ou du sélecteur de photo Android.</summary>
    public static bool Complete(int requestCode, Result resultCode, Intent? intentData = null)
    {
        if (requestCode == GalleryRequestCode)
        {
            byte[]? qrImage = null;
            try
            {
                if (resultCode == Result.Ok && intentData?.Data is { } uri &&
                    current?.TryGetTarget(out var activity) == true)
                {
                    using var input = activity.ContentResolver?.OpenInputStream(uri);
                    if (input is not null)
                    {
                        using var output = new MemoryStream();
                        var buffer = new byte[64 * 1024];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + count > MaximumQrImageBytes)
                                throw new IOException("La photo QR dépasse la limite de 12 Mo.");
                            output.Write(buffer, 0, count);
                        }
                        qrImage = output.ToArray();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or Java.Lang.Exception or UnauthorizedAccessException)
            { Android.Util.Log.Warn("SCOLIA-QR", $"Photo QR non lisible : {ex.GetType().Name}"); }
            galleryPending?.TrySetResult(qrImage);
            galleryPending = null;
            return true;
        }
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
