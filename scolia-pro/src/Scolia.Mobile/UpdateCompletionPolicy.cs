namespace Scolia.Mobile;

/// <summary>
/// Décisions indépendantes d'Android : utiles pour vérifier que la fin du
/// téléchargement déclenche une action, sans lancer d'installation sans accord.
/// </summary>
public enum UpdateCompletionAction
{
    Ignore,
    DownloadFailed,
    PresentInstall,
    NotifyReady,
}

internal enum PendingUpdateAction { OfferDownload, ShowProgress, Install }

internal static class UpdateCompletionPolicy
{
    internal const int Successful = 8; // DownloadManager.STATUS_SUCCESSFUL
    internal const int Failed = 16; // DownloadManager.STATUS_FAILED

    internal static UpdateCompletionAction Decide(int? downloadStatus, bool foreground)
        => downloadStatus switch
        {
            Successful when foreground => UpdateCompletionAction.PresentInstall,
            Successful => UpdateCompletionAction.NotifyReady,
            Failed => UpdateCompletionAction.DownloadFailed,
            _ => UpdateCompletionAction.Ignore,
        };

    internal static bool NeedsForegroundCheck(bool foreground, long downloadId, int? status)
        => foreground && downloadId > 0 && (status is null or Successful or Failed);

    internal static PendingUpdateAction PendingAction(int? status, long startedAtUtc, long nowUtc)
        => status == Successful ? PendingUpdateAction.Install
            : status is null or Failed || IsStalePendingDownload(startedAtUtc, nowUtc)
                ? PendingUpdateAction.OfferDownload : PendingUpdateAction.ShowProgress;

    /// <summary>
    /// Une mise à jour déjà téléchargée n'empêche jamais de découvrir une
    /// version publiée ensuite. Seule la comparaison des versions détermine
    /// s'il faut proposer le remplacement du téléchargement en attente.
    /// </summary>
    internal static bool IsNewerThanPending(string latestVersion, string? pendingVersion)
    {
        if (!Version.TryParse(latestVersion, out var latest)) return false;
        if (string.IsNullOrWhiteSpace(pendingVersion)) return true;
        return !Version.TryParse(pendingVersion, out var pending)
            || latest.CompareTo(pending) > 0;
    }

    // Une connexion mobile peut rester interrompue longtemps : le gestionnaire
    // Android doit pouvoir reprendre un téléchargement, sans perdre son état
    // au bout de quelques heures. La recherche GitHub reste indépendante.
    internal const long PendingDownloadRetentionSeconds = 7L * 24 * 60 * 60;

    internal static bool IsStalePendingDownload(long startedAtUtc, long nowUtc)
        => startedAtUtc <= 0 || nowUtc < startedAtUtc
            || nowUtc - startedAtUtc > PendingDownloadRetentionSeconds;

    /// <summary>
    /// Android peut annoncer un ancien versionName alors que le code embarqué a déjà
    /// changé de version. Prendre la plus récente des deux valeurs connues pour
    /// éviter de proposer indéfiniment le même APK.
    /// </summary>
    internal static Version? EffectiveInstalledVersion(string? androidVersion, string? bundledVersion)
    {
        Version? android = Version.TryParse(androidVersion, out var a) ? a : null;
        Version? bundled = Version.TryParse(bundledVersion, out var b) ? b : null;
        if (android is null) return bundled;
        if (bundled is null) return android;
        return android >= bundled ? android : bundled;
    }

    internal static bool IsReleaseNewer(string? candidateVersion, string? androidVersion, string? bundledVersion)
        => Version.TryParse(candidateVersion, out var candidate)
           && EffectiveInstalledVersion(androidVersion, bundledVersion) is { } installed
           && candidate > installed;

    // Une ancienne distribution 1.2.91 a versionCode 1.2.91 mais versionName 1.2.90.
    // Pour cette version aussi, la présence du binaire courant doit vider le cache
    // de l'installateur au lieu d'afficher « APK refusé ».
    internal static bool AlreadyInstalled(string? downloadedVersion, string? installedVersion,
        string? bundledVersion = null)
        => Version.TryParse(downloadedVersion, out var downloaded)
           && EffectiveInstalledVersion(installedVersion, bundledVersion) is { } installed
           && installed >= downloaded;
}

/// <summary>État lisible du vrai téléchargement Android, indépendant des notifications système.</summary>
internal sealed record UpdateDownloadProgress(int? Status, int Reason, long ReceivedBytes, long TotalBytes)
{
    internal int? Percent => Status == UpdateCompletionPolicy.Successful ? 100 : TotalBytes > 0
        ? (int)Math.Clamp(Math.Max(0, ReceivedBytes) * 100d / TotalBytes, 0, 99) : null;

    internal string Message
    {
        get
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            string Mb(long bytes) => (Math.Max(0, bytes) / (1024d * 1024)).ToString("0.#", culture);
            var progress = TotalBytes > 0
                ? $"{Mb(ReceivedBytes)} / {Mb(TotalBytes)} Mo · {Percent} %"
                : $"{Mb(ReceivedBytes)} Mo reçus";
            var state = Status switch
            {
                1 => "Android prépare le téléchargement…",
                2 => "Téléchargement en cours…",
                4 when Reason == 2 => "En attente d'une connexion Internet.",
                4 when Reason == 3 => "Android attend un réseau Wi-Fi pour ce fichier. Vous pouvez utiliser le navigateur pour choisir une autre méthode.",
                4 when Reason == 1 => "Connexion interrompue. Android attend avant de réessayer.",
                4 => "Le téléchargement est en pause dans Android.",
                UpdateCompletionPolicy.Successful => "Téléchargement terminé. Vérification de l'APK…",
                UpdateCompletionPolicy.Failed when Reason == 1006 => "Espace libre insuffisant pour télécharger la mise à jour.",
                UpdateCompletionPolicy.Failed when Reason == 1007 => "Le stockage de téléchargement n'est pas disponible.",
                UpdateCompletionPolicy.Failed when Reason == 1008 => "Android ne peut pas reprendre ce fichier. Relancez le téléchargement.",
                UpdateCompletionPolicy.Failed when Reason == 1009 => "Un ancien fichier empêche le téléchargement. Vous pouvez le relancer.",
                UpdateCompletionPolicy.Failed when Reason is >= 400 and <= 599 => $"Le serveur de téléchargement a répondu HTTP {Reason}. Réessayez ou utilisez le navigateur.",
                UpdateCompletionPolicy.Failed => "Le téléchargement Android a échoué. Réessayez ou utilisez le navigateur.",
                _ => "Ce téléchargement n'est plus suivi par Android. Vous pouvez le relancer.",
            };
            return progress + "\n\n" + state;
        }
    }
}
