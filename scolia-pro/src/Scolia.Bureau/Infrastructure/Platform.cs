namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Services du système qui diffèrent entre l'ordinateur et le téléphone. L'application Android les fournit au
/// démarrage ; sur l'ordinateur, ils restent vides et SCOLIA utilise Windows directement.
/// </summary>
public static class Platform
{
    /// <summary>Vrai dans l'application du téléphone : pas de serveur sur l'appareil, il se connecte à celui de l'établissement.</summary>
    public static bool IsMobile { get; set; }

    /// <summary>Libellé présenté par le système de l'appareil : Android, Face ID ou Touch ID.</summary>
    public static string BiometricDisplayName { get; set; } = "Empreinte digitale";

    /// <summary>Initialisation du téléphone : l'écran natif garde le logo jusqu'à ce que la première page soit prête.</summary>
    public static Task? StartupTask { get; set; }

    /// <summary>Relance une initialisation en échec depuis l'écran de démarrage Android.</summary>
    public static Func<Task>? RetryStartup { get; set; }

    /// <summary>Le système confirme une biométrie enrôlée et disponible.</summary>
    public static Func<bool>? IsBiometricAvailable { get; set; }
    /// <summary>Le système présente son propre dialogue d'authentification ; aucune empreinte n'est lue par SCOLIA.</summary>
    public static Func<Task<bool>>? VerifyBiometricAsync { get; set; }
    /// <summary>L'activation nécessite une première authentification SCOLIA par mot de passe puis une empreinte forte.</summary>
    public static Func<Task<bool>>? EnrollBiometricAsync { get; set; }
    /// <summary>Retire la clé biométrique native et son consentement local, sans toucher aux données scolaires.</summary>
    public static Action? DisableBiometricAccess { get; set; }
    /// <summary>Vrai seulement si l'appareil possède la preuve native liée à une activation biométrique.</summary>
    public static Func<bool>? HasBiometricProof { get; set; }

    /// <summary>Ouvre un document enregistré, un PDF ou un classeur, avec l'application du téléphone.</summary>
    public static Action<string>? OpenFile { get; set; }

    /// <summary>Ouvre une adresse : WhatsApp, SMS, appel, e-mail ou page web.</summary>
    public static Action<string>? OpenUri { get; set; }

    /// <summary>
    /// À la demande de l'utilisateur, ouvre Tailscale ou son installateur officiel.
    /// La permission VPN et la connexion au tailnet restent sous contrôle d'Android.
    /// </summary>
    public static Func<string>? OpenTailscale { get; set; }

    /// <summary>Activation VPN demandée sans ouvrir la fenêtre Tailscale.</summary>
    public static Func<Task<string>>? ConnectTailscaleInBackground { get; set; }

    public static Func<bool>? IsTailscaleInstalled { get; set; }

    /// <summary>VPN Android actif, sans certitude de son fournisseur.</summary>
    public static Func<bool?>? IsVpnActive { get; set; }

    /// <summary>Diagnostic du VPN et adresse locale éventuelle ; aucune donnée de compte Tailscale.</summary>
    public static Func<TailnetSnapshot>? ReadTailnetState { get; set; }
    public static Func<string>? OpenTailscaleInstallPage { get; set; }

    /// <summary>
    /// Plateforme dont SCOLIA peut télécharger et installer lui-même le paquet officiel de Tailscale :
    /// « android » (APK universel) ou « windows » (installateur). Rien ailleurs (iPhone : App Store seulement).
    /// </summary>
    public static string? TailscalePackageKind { get; set; } = OperatingSystem.IsWindows() ? "windows" : null;

    /// <summary>Dossier où SCOLIA range le paquet téléchargé avant son installation.</summary>
    public static string? DownloadFolder { get; set; }

    /// <summary>
    /// Remet un paquet téléchargé à l'installateur du système. Android : vérifie qu'il s'agit bien de
    /// com.tailscale.ipn, demande au besoin l'autorisation « Installer des applications », puis ouvre la
    /// confirmation d'installation. Le texte rendu décrit ce que l'utilisateur doit faire, ou le résultat.
    /// </summary>
    public static Func<string, Task<TailscaleInstallResult>>? InstallTailscalePackage { get; set; }

    /// <summary>Android signale le retour à SCOLIA après une installation externe.</summary>
    public static event Action? AppResumed;
    public static void NotifyAppResumed() => AppResumed?.Invoke();


    /// <summary>Dossier où les documents sont enregistrés sur le téléphone.</summary>
    public static string? DocumentsFolder { get; set; }

    /// <summary>Joue un son au format WAV, 16 bits mono : les notes de SCOLIA sur le haut-parleur du téléphone.</summary>
    public static Action<byte[]>? PlayWave { get; set; }

    /// <summary>
    /// Serveur que ce téléphone peut porter lui-même, quand l'établissement n'a pas d'ordinateur : fourni par l'application
    /// Android. Null sur un appareil qui ne peut que rejoindre un serveur.
    /// </summary>
    public static ILocalServer? LocalServer { get; set; }

    /// <summary>Vrai quand ce poste peut être le serveur de l'établissement : tout ordinateur, et le téléphone qui en a les moyens.</summary>
    public static bool CanHostServer => !IsMobile || LocalServer is not null;

    /// <summary>Nom de l'appareil, affiché au serveur lors de la demande d'accès.</summary>
    public static string? DeviceName { get; set; }
}

/// <summary>Issue d'une installation lancée par SCOLIA.</summary>
public enum TailscaleInstallOutcome { Installed, WaitingForUser, NeedsPermission, Refused, Failed }

public sealed record TailscaleInstallResult(TailscaleInstallOutcome Outcome, string Message);
