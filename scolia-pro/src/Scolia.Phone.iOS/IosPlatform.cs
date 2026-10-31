using AVFoundation;
using CoreGraphics;
using Foundation;
using Scolia.Bureau.Infrastructure;
using UIKit;

namespace Scolia.Phone.iOS;

/// <summary>
/// SCOLIA Phone iOS : client natif sans serveur permanent et sans tentative de commander silencieusement le VPN
/// d'une autre application. Trousseau pour les secrets, Face ID ou Touch ID, aperçu et partage des documents,
/// sons discrets et reprise au retour dans l'application.
/// </summary>
public static class IosPlatform
{
    private static NSObject? foregroundObserver;
    private static AVAudioPlayer? player;
    private static UIDocumentInteractionController? preview;

    public static void Prepare()
    {
        Platform.IsMobile = true;
        Preferences.MobileLayout = true;
        IosSecrets.Initialize();

        Platform.BiometricDisplayName = IosBiometrics.DisplayName();
        Platform.IsBiometricAvailable = IosBiometrics.IsAvailable;
        Platform.VerifyBiometricAsync = IosBiometrics.VerifyAsync;
        Platform.EnrollBiometricAsync = IosBiometrics.EnrollAsync;
        Platform.HasBiometricProof = IosBiometrics.HasProof;
        Platform.DisableBiometricAccess = IosBiometrics.Disable;

        Platform.DeviceName = UIDevice.CurrentDevice.Name;
        Platform.LocalServer = null;
        Platform.OpenUri = OpenUri;
        Platform.OpenFile = OpenFile;
        Platform.PlayWave = PlayWave;
        Platform.OpenTailscale = OpenTailscale;
        Platform.OpenTailscaleInstallPage = OpenTailscale;
        // iOS ne laisse ni voir les autres applications ni lire l'état d'un VPN : l'assistant le dit clairement.
        Platform.IsTailscaleInstalled = null;
        Platform.IsVpnActive = null;
        Platform.ReadTailnetState = null;
        Platform.ConnectTailscaleInBackground = null;
        // Dossier Documents de l'application : visible dans l'app Fichiers, rubrique « Sur mon iPhone ».
        Platform.DocumentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        foregroundObserver ??= UIApplication.Notifications.ObserveWillEnterForeground((_, _) => Platform.NotifyAppResumed());
    }

    private static void OpenUri(string uri)
    {
        if (NSUrl.FromString(uri) is { } address)
            UIApplication.SharedApplication.OpenUrl(address, new UIApplicationOpenUrlOptions(), null);
    }

    /// <summary>Un reçu ou un relevé s'ouvre en aperçu, avec le bouton Partager d'iOS (Imprimer, Fichiers, WhatsApp…).</summary>
    private static void OpenFile(string path)
    {
        if (TopController() is not { } host || !File.Exists(path)) return;
        var url = NSUrl.FromFilename(path);
        preview = UIDocumentInteractionController.FromUrl(url);
        preview.Delegate = new PreviewHost(host);
        if (preview.PresentPreview(true)) return;
        // Format sans aperçu (classeur…) : la feuille de partage propose les applications capables de l'ouvrir.
        var share = new UIActivityViewController([url], null);
        if (share.PopoverPresentationController is { } popover && host.View is { } view)
        {
            popover.SourceView = view;
            popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
            popover.PermittedArrowDirections = 0;
        }
        host.PresentViewController(share, true, null);
    }

    private static UIViewController? TopController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes.ToArray()
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        var controller = window?.RootViewController;
        while (controller?.PresentedViewController is { } presented) controller = presented;
        return controller;
    }

    /// <summary>Sons discrets : ils se mêlent à la musique sans l'interrompre et respectent le mode silencieux.</summary>
    private static void PlayWave(byte[] wave)
    {
        try
        {
            AVAudioSession.SharedInstance().SetCategory(AVAudioSessionCategory.Ambient);
            player = AVAudioPlayer.FromData(NSData.FromArray(wave));
            player?.Play();
        }
        catch (Exception ex) when (ex is NSErrorException or ObjCRuntime.ObjCException or InvalidOperationException) { }
    }

    private static string OpenTailscale()
    {
        OpenUri("https://tailscale.com/download/ios");
        return "La page officielle de Tailscale s'ouvre et mène à l'App Store. Installez-le, connectez-vous au réseau de l'établissement et activez « VPN à la demande » dans ses réglages, puis revenez dans SCOLIA Phone.";
    }

    private sealed class PreviewHost(UIViewController host) : UIDocumentInteractionControllerDelegate
    {
        public override UIViewController ViewControllerForPreview(UIDocumentInteractionController controller) => host;
    }
}
