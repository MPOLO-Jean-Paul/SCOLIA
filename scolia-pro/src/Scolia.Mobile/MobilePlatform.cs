using Android.Content;
using Android.Media;
using Android.Net.Wifi;
using Android.Widget;
using Scolia.Bureau.Infrastructure;
using AndroidUri = Android.Net.Uri;

namespace Scolia.Mobile;

/// <summary>
/// Services d'Android fournis à SCOLIA : dossier des documents, ouverture d'un PDF ou d'un classeur avec l'application
/// du téléphone, liens WhatsApp, SMS et e-mail, sons, et recherche du serveur sur le Wi-Fi.
/// </summary>
public static class MobilePlatform
{
    private static Context? context;
    private static WifiManager.MulticastLock? multicast;

    public static void Prepare(Context app)
    {
        context = app;
        Security.AndroidSecrets.Initialize();
        Synchronization.SyncWorker.Schedule(app);
        Platform.IsMobile = true;
        Preferences.MobileLayout = true;
        Platform.DeviceName = $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}".Trim();
        Platform.DocumentsFolder = app.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)?.AbsolutePath
            ?? Path.Combine(app.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "Documents");
        Platform.OpenFile = OpenFile;
        Platform.OpenUri = OpenUri;
        Platform.OpenTailscale = OpenTailscale;
        Platform.PlayWave = AndroidAudio.PlayWave;
        // Sans ordinateur dans l'établissement, ce téléphone peut en être le serveur.
        Platform.LocalServer = Server.PhoneServer.Get(app);
    }

    public static void Attach(Context activity) => context ??= activity.ApplicationContext ?? activity;

    /// <summary>Garde la réception des diffusions du Wi-Fi pendant que SCOLIA est à l'écran, et la rend ensuite.</summary>
    public static void HoldMulticast(bool hold)
    {
        try
        {
            if (hold)
            {
                if (multicast is null && context?.GetSystemService(Context.WifiService) is WifiManager wifi)
                {
                    multicast = wifi.CreateMulticastLock("scolia-reseau");
                    multicast?.SetReferenceCounted(false);
                }
                multicast?.Acquire();
            }
            else if (multicast?.IsHeld == true) multicast.Release();
        }
        catch (Java.Lang.SecurityException) { }
    }

    private static void OpenFile(string path)
    {
        if (context is not { } ctx) return;
        try
        {
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, $"{ctx.PackageName}.fichiers", new Java.IO.File(path));
            var view = new Intent(Intent.ActionView);
            view.SetDataAndType(uri, MimeOf(path));
            view.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            var chooser = Intent.CreateChooser(view, "Ouvrir avec");
            chooser!.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
            ctx.StartActivity(chooser);
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.IllegalArgumentException)
        {
            Toast.MakeText(ctx, "Aucune application du téléphone ne sait ouvrir ce document.", ToastLength.Long)?.Show();
        }
    }

    private static void OpenUri(string address)
    {
        if (context is not { } ctx) return;
        try
        {
            var intent = new Intent(Intent.ActionView, AndroidUri.Parse(address));
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(ctx, "Aucune application du téléphone ne sait ouvrir ce lien.", ToastLength.Long)?.Show();
        }
    }

    /// <summary>
    /// Lance le client VPN officiel quand il est installé. Sinon, ouvre son installation
    /// par une action volontaire ; SCOLIA n'installe ni n'active un VPN en silence.
    /// </summary>
    private static string OpenTailscale()
    {
        if (context is not { } ctx)
            return "Le téléphone n'est pas prêt à ouvrir Tailscale.";
        try
        {
            var launch = ctx.PackageManager?.GetLaunchIntentForPackage("com.tailscale.ipn");
            if (launch is not null)
            {
                launch.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(launch);
                return "Tailscale est ouvert. Connectez-vous au réseau privé de l'établissement, activez le VPN et revenez dans SCOLIA.";
            }

            var market = new Intent(Intent.ActionView,
                AndroidUri.Parse("market://details?id=com.tailscale.ipn"));
            market.AddFlags(ActivityFlags.NewTask);
            try { ctx.StartActivity(market); }
            catch (ActivityNotFoundException)
            {
                var official = new Intent(Intent.ActionView,
                    AndroidUri.Parse("https://tailscale.com/download/android"));
                official.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(official);
            }
            return "Installation officielle de Tailscale ouverte. Confirmez l'installation et la permission VPN demandée par Android, puis revenez dans SCOLIA.";
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            return "Impossible d'ouvrir Tailscale automatiquement. Consultez le site officiel tailscale.com/download/android.";
        }
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".csv" => "text/csv",
        ".png" => "image/png",
        ".zip" => "application/zip",
        _ => "*/*",
    };

}
