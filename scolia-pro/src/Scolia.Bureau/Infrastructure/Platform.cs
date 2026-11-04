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
    /// <summary>Pour une phrase : « l’empreinte » sur Android, « Face ID » ou « Touch ID » sur iPhone.</summary>
    public static string BiometricInSentence => BiometricDisplayName.StartsWith("Empreinte", StringComparison.Ordinal)
        ? "l’empreinte" : BiometricDisplayName;

    /// <summary>Initialisation du téléphone : l'écran natif garde le logo jusqu'à ce que la première page soit prête.</summary>
    public static Task? StartupTask { get; set; }

    /// <summary>Relance une initialisation en échec depuis l'écran de démarrage Android.</summary>
    public static Func<Task>? RetryStartup { get; set; }

    /// <summary>Le système confirme une biométrie enrôlée et disponible.</summary>
    public static Func<bool>? IsBiometricAvailable { get; set; }
    /// <summary>
    /// Le système présente son propre dialogue (empreinte, Face ID) ; s'il le valide, il rend l'accès scellé à
    /// l'activation. Annulation, échec ou empreinte modifiée : rien. Aucune empreinte n'est lue par SCOLIA.
    /// </summary>
    public static Func<Task<string?>>? UnlockBiometricAsync { get; set; }
    /// <summary>
    /// Activation, après un code vérifié par le serveur : le système demande l'empreinte, puis scelle l'accès dans
    /// sa puce de sécurité (Android Keystore, trousseau d'Apple). Il ne s'ouvre ensuite qu'avec une empreinte.
    /// </summary>
    public static Func<string, Task<bool>>? EnrollBiometricAsync { get; set; }
    /// <summary>Retire la clé biométrique native et son consentement local, sans toucher aux données scolaires.</summary>
    public static Action? DisableBiometricAccess { get; set; }
    /// <summary>Vrai seulement si l'appareil possède la preuve native liée à une activation biométrique.</summary>
    public static Func<bool>? HasBiometricProof { get; set; }

    /// <summary>Ouvre un document enregistré, un PDF ou un classeur, avec l'application du téléphone.</summary>
    public static Action<string>? OpenFile { get; set; }

    /// <summary>Décodeur d'images du téléphone (photo HEIC, TIFF…) : rend un PNG, ou rien s'il ne sait pas lire.</summary>
    public static Func<byte[], byte[]?>? DecodeImage { get; set; }

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

    /// <summary>
    /// Notification du système (Android, iPhone), visible même quand SCOLIA est en arrière-plan : bannière en haut
    /// de l'écran et son du téléphone. Sur l'ordinateur, elle reste vide : la bannière de SCOLIA suffit.
    /// </summary>
    public static Action<AppAlert>? ShowSystemNotification { get; set; }

    /// <summary>Vrai tant que SCOLIA est à l'écran ; en arrière-plan, l'alerte passe par la notification du système.</summary>
    public static Func<bool>? IsInForeground { get; set; }

    /// <summary>
    /// Recherche à la voix : le système écoute (Google sur Android, reconnaissance vocale d'Apple sur iPhone) et rend
    /// le texte dicté, ou rien si la personne annule. Vide sur l'ordinateur : le micro n'y est pas proposé.
    /// </summary>
    public static Func<Task<string?>>? RecognizeSpeechAsync { get; set; }

    /// <summary>Vrai si l'appareil sait reconnaître la voix (service vocal présent).</summary>
    public static Func<bool>? CanRecognizeSpeech { get; set; }

    /// <summary>Android signale le retour à SCOLIA après une installation externe.</summary>
    public static event Action? AppResumed;
    public static void NotifyAppResumed() => AppResumed?.Invoke();


    /// <summary>Dossier où les documents sont enregistrés sur le téléphone.</summary>
    public static string? DocumentsFolder { get; set; }

    /// <summary>Joue un son au format WAV, 16 bits mono : les notes de SCOLIA sur le haut-parleur du téléphone.</summary>
    public static Action<byte[]>? PlayWave { get; set; }

    /// <summary>Vibration légère du téléphone (toucher, réussite, refus), selon les réglages de vibration du système.</summary>
    public static Action<HapticKind>? Haptic { get; set; }

    /// <summary>
    /// Serveur que ce téléphone peut porter lui-même, quand l'établissement n'a pas d'ordinateur : fourni par l'application
    /// Android. Null sur un appareil qui ne peut que rejoindre un serveur.
    /// </summary>
    public static ILocalServer? LocalServer { get; set; }

    /// <summary>La notification Android existe réellement et le service est au premier plan.</summary>
    public static Func<bool>? IsHostForegroundActive { get; set; }

    /// <summary>Le serveur ASP.NET local écoute effectivement sur l'appareil.</summary>
    public static Func<bool>? IsHostApiServing { get; set; }

    /// <summary>Résultat des tests de santé du téléphone serveur après minimisation, sans exposer de données scolaires.</summary>
    public static Func<string>? HostNetworkDiagnostic { get; set; }

    /// <summary>Relance un test de santé explicite, indépendant de la fenêtre.</summary>
    public static Func<Task>? CheckHostNetworkAsync { get; set; }

    /// <summary>Contrôle manuel GitHub Releases, disponible sur Android depuis Poste et réseau.</summary>
    public static Func<Task>? CheckAppUpdatesAsync { get; set; }

    /// <summary>Message de diagnostic de la dernière vérification Android.</summary>
    public static Func<string>? AppUpdateStatus { get; set; }

    /// <summary>Répare l'hébergement sur demande depuis une interface visible, jamais depuis un client.</summary>
    public static Action? EnsureHostAvailable { get; set; }

    /// <summary>
    /// Téléphone serveur : vrai quand le système le laisse travailler écran éteint, sans le freiner pour économiser la
    /// batterie. Null sur un appareil où la question ne se pose pas.
    /// </summary>
    public static Func<bool>? BackgroundAllowed { get; set; }

    /// <summary>Demande au système de laisser SCOLIA travailler en arrière-plan, sans économie de batterie.</summary>
    public static Action? AllowBackground { get; set; }

    /// <summary>Ouvre la page de SCOLIA dans les réglages du téléphone (batterie, démarrage automatique).</summary>
    public static Action? OpenAppSettings { get; set; }

    /// <summary>Réglage propre à la marque du téléphone, qui peut couper SCOLIA malgré tout ; vide sinon.</summary>
    public static string BackgroundHint { get; set; } = string.Empty;

    /// <summary>
    /// Photo prise avec l'appareil photo du téléphone, pour lire un code QR : les octets de l'image, ou null si
    /// l'utilisateur annule. Null sur un ordinateur, qui lit plutôt une image enregistrée ou un code collé.
    /// </summary>
    public static Func<Task<byte[]?>>? CaptureImageAsync { get; set; }

    /// <summary>Ouvre les photos du téléphone pour importer un QR sans accès global à la galerie.</summary>
    public static Func<Task<byte[]?>>? PickQrImageAsync { get; set; }

    /// <summary>Vrai quand ce poste peut être le serveur de l'établissement : tout ordinateur, et le téléphone qui en a les moyens.</summary>
    public static bool CanHostServer => !IsMobile || LocalServer is not null;

    /// <summary>Nom de l'appareil, affiché au serveur lors de la demande d'accès.</summary>
    public static string? DeviceName { get; set; }
}

/// <summary>Issue d'une installation lancée par SCOLIA.</summary>
public enum TailscaleInstallOutcome { Installed, WaitingForUser, NeedsPermission, Refused, Failed }

public sealed record TailscaleInstallResult(TailscaleInstallOutcome Outcome, string Message);
