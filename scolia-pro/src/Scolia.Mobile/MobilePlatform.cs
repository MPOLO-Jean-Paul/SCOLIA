using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Net;
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
        Platform.IsBiometricAvailable = Security.AndroidBiometrics.IsAvailable;
        Platform.VerifyBiometricAsync = Security.AndroidBiometrics.VerifyAsync;
        Preferences.MobileLayout = true;
        Platform.DeviceName = $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}".Trim();
        Platform.DocumentsFolder = app.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)?.AbsolutePath
            ?? Path.Combine(app.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "Documents");
        Platform.OpenFile = OpenFile;
        Platform.OpenUri = OpenUri;
        Platform.OpenTailscale = OpenTailscale;
        Platform.IsTailscaleInstalled = IsTailscaleInstalled;
        Platform.IsVpnActive = IsVpnActive;
        Platform.ReadTailnetState = ReadTailnetState;
        Platform.OpenTailscaleInstallPage = OpenTailscaleInstallPage;
        Platform.ConnectTailscaleInBackground = ConnectTailscaleInBackgroundAsync;
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
    // GetLaunchIntentForPackage renvoie parfois null même si le paquet est présent.
    // Le manifeste SCOLIA contient uniquement <queries><package name="com.tailscale.ipn"/>.
    private static bool IsTailscaleInstalled()
    {
        try
        {
            return context?.PackageManager?.GetPackageInfo(
                "com.tailscale.ipn", PackageInfoFlags.MatchAll) is not null;
        }
        catch (Java.Lang.Exception) { return false; }
    }

    private static TailnetSnapshot ReadTailnetState()
    {
        bool installed = IsTailscaleInstalled();
        bool? vpnFound = null;
        string? address = null;
        try
        {
            if (context?.GetSystemService(Context.ConnectivityService) is ConnectivityManager manager)
            {
                // ActiveNetwork est pris en charge par la version Android/.NET du projet.
                // AllNetworks ne l'est pas : il provoquait une erreur CS1061 à la compilation.
                if (manager.ActiveNetwork is { } network)
                {
                    var capabilities = manager.GetNetworkCapabilities(network);
                    if (capabilities is not null)
                    {
                        vpnFound = capabilities.HasTransport(TransportType.Vpn);
                        if (vpnFound == true)
                        {
                            var links = manager.GetLinkProperties(network)?.LinkAddresses;
                            if (links is not null)
                                foreach (var link in links)
                                {
                                    var candidate = link?.Address?.HostAddress;
                                    if (!TailnetSnapshot.IsCandidateTailscaleIp(candidate)) continue;
                                    address = candidate;
                                    break;
                                }
                        }
                    }
                }
            }
        }
        catch (Java.Lang.SecurityException) { vpnFound = null; }
        catch (Java.Lang.IllegalArgumentException) { vpnFound = null; }
        return new TailnetSnapshot(installed, vpnFound, address);
    }

    /// <summary>La présence d'un VPN n'établit pas son identité.</summary>
    private static bool? IsVpnActive() => ReadTailnetState().VpnActive;

    /// <summary>
    /// Lance l'installation officielle : le Play Store affiche la vraie progression.
    /// Le pourcentage du téléchargement Play Store n'est pas accessible depuis SCOLIA.
    /// </summary>
    private static string OpenTailscaleInstallPage()
    {
        if (context is not { } ctx)
            return "Android n'est pas encore prêt pour l'installation.";
        try
        {
            var market = new Intent(Intent.ActionView, AndroidUri.Parse("market://details?id=com.tailscale.ipn"));
            market.AddFlags(ActivityFlags.NewTask);
            try { ctx.StartActivity(market); }
            catch (ActivityNotFoundException)
            {
                var browser = new Intent(Intent.ActionView, AndroidUri.Parse("https://play.google.com/store/apps/details?id=com.tailscale.ipn"));
                browser.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(browser);
            }
            return "Le magasin d'applications est ouvert. Suivez le téléchargement et confirmez l'installation, puis revenez dans SCOLIA.";
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            return "Impossible d'ouvrir le magasin d'applications. Vérifiez Internet et Google Play sur cet appareil.";
        }
    }

    private static string OpenTailscale()
    {
        if (context is not { } ctx)
            return "Le téléphone n'est pas prêt à ouvrir Tailscale.";
        try
        {
            var installed = IsTailscaleInstalled();
            var launch = installed ? ctx.PackageManager?.GetLaunchIntentForPackage("com.tailscale.ipn") : null;
            if (launch is not null)
            {
                launch.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(launch);
                return "Tailscale est ouvert. Connectez-vous au réseau privé de l'établissement, activez le VPN et revenez dans SCOLIA.";
            }
            if (installed)
                return "Tailscale est déjà installé. Android n'a pas transmis son écran de lancement : ouvrez-le depuis la liste de vos applications, activez le VPN, puis revenez dans SCOLIA.";

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


    /// <summary>
    /// Active Tailscale par sa diffusion Android exportée, sans ouvrir sa fenêtre.
    /// Ne peut ni installer l'application ni accorder la permission VPN à sa place.
    /// </summary>
    private static async Task<string> ConnectTailscaleInBackgroundAsync()
    {
        if (context is not { } ctx)
            return "SCOLIA n'est pas encore prêt à contacter le VPN.";
        try
        {
            if (!IsTailscaleInstalled())
                return "Tailscale n'est pas installé. Terminez d'abord l'installation depuis SCOLIA.";

            var request = new Intent("com.tailscale.ipn.CONNECT_VPN");
            request.SetClassName("com.tailscale.ipn", "com.tailscale.ipn.IPNReceiver");
            ctx.SendBroadcast(request);
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

            if (ReadTailnetState().VpnActive == true)
                return "Demande envoyée sans quitter SCOLIA. Android détecte un VPN actif, sans confirmer son identité : vérifiez la liaison Tailscale depuis un autre téléphone.";
            return "Demande d'activation envoyée en arrière-plan. Le VPN n'est pas encore confirmé. Vérifiez la première autorisation et la connexion au compte Tailscale.";
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException or InvalidOperationException)
        {
            return "Android a refusé la demande de connexion silencieuse. Vérifiez la configuration initiale et les restrictions du téléphone.";
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
