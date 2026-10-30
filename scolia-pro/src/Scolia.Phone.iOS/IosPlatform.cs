using Foundation;
using Scolia.Bureau.Infrastructure;
using UIKit;

namespace Scolia.Phone.iOS;

/// <summary>
/// SCOLIA Phone iOS : client natif sans Android, sans serveur permanent et sans
/// tentative de commander silencieusement le VPN d'une autre application.
/// </summary>
public static class IosPlatform
{
    public static void Prepare()
    {
        Platform.IsMobile = true;
        Preferences.MobileLayout = true;
        Platform.DeviceName = UIDevice.CurrentDevice.Name;
        Platform.LocalServer = null;
        Platform.OpenUri = OpenUri;
        Platform.OpenTailscale = OpenTailscale;
        Platform.OpenTailscaleInstallPage = OpenTailscale;
        Platform.IsTailscaleInstalled = null;
        Platform.IsVpnActive = null;
        Platform.ReadTailnetState = null;
        Platform.ConnectTailscaleInBackground = null;
        Platform.DocumentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static void OpenUri(string uri)
    {
        if (NSUrl.FromString(uri) is { } address)
            UIApplication.SharedApplication.OpenUrl(address);
    }

    private static string OpenTailscale()
    {
        OpenUri("https://tailscale.com/download/ios");
        return "Configurez Tailscale dans l'App Store et activez VPN On Demand dans ses réglages. Revenez dans SCOLIA Phone et saisissez l'adresse privée du serveur.";
    }
}
