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
