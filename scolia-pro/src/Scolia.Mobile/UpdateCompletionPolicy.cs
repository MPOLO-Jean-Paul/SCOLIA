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

    // Après une installation réussie, l'APK en cache n'est plus une nouvelle version.
    // Éviter d'afficher à tort « APK incorrect » lors du redémarrage de SCOLIA.
    internal static bool AlreadyInstalled(string? downloadedVersion, string? installedVersion)
        => Version.TryParse(downloadedVersion, out var downloaded)
        && Version.TryParse(installedVersion, out var installed)
        && installed.CompareTo(downloaded) >= 0;
}
