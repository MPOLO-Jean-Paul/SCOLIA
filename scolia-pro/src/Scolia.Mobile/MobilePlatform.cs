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
    /// Le VPN Tailscale est une application système distincte. Android ne permet pas à
    /// SCOLIA de l'installer ou de l'autoriser silencieusement. On ouvre directement
    /// son écran si elle est installée, sinon sa fiche officielle d'installation.
    /// </summary>
    private static string OpenTailscale()
    {
        if (context is not { } ctx)
            return "Le téléphone n'est pas prêt pour ouvrir Tailscale.";
        try
        {
            var installed = ctx.PackageManager?.GetLaunchIntentForPackage("com.tailscale.ipn");
            if (installed is not null)
            {
                installed.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(installed);
                return "Tailscale est ouvert. Activez le VPN et connectez-vous au réseau privé de votre établissement, puis revenez dans SCOLIA.";
            }

            var playStore = new Intent(Intent.ActionView,
                AndroidUri.Parse("market://details?id=com.tailscale.ipn"));
            playStore.AddFlags(ActivityFlags.NewTask);
            try { ctx.StartActivity(playStore); }
            catch (ActivityNotFoundException)
            {
                var browser = new Intent(Intent.ActionView,
                    AndroidUri.Parse("https://play.google.com/store/apps/details?id=com.tailscale.ipn"));
                browser.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(browser);
            }
            return "Installation officielle de Tailscale ouverte. Confirmez l'installation et l'autorisation VPN demandée par Android, puis revenez dans SCOLIA.";
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.SecurityException)
        {
            return "Impossible d'ouvrir Tailscale. Vous pouvez installer l'APK officiel présent dans le kit SCOLIA (dossier Téléphone).";
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
