namespace Scolia.Mobile;

/// <summary>
/// Décisions indépendantes d'Android : utiles pour vérifier que la fin du
/// téléchargement déclenche une action, sans lancer d'installation sans accord.
/// </summary>
internal enum UpdateCompletionAction
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
}
