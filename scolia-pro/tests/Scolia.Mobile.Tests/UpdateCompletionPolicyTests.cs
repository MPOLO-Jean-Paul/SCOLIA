using Scolia.Mobile;

namespace Scolia.Mobile.Tests;

public sealed class UpdateCompletionPolicyTests
{
    [Theory]
    [InlineData(8, true, UpdateCompletionAction.PresentInstall)]
    [InlineData(8, false, UpdateCompletionAction.NotifyReady)]
    [InlineData(16, true, UpdateCompletionAction.DownloadFailed)]
    [InlineData(16, false, UpdateCompletionAction.DownloadFailed)]
    [InlineData(4, true, UpdateCompletionAction.Ignore)]
    [InlineData(2, true, UpdateCompletionAction.Ignore)]
    [InlineData(null, true, UpdateCompletionAction.Ignore)]
    public void DownloadCompletionRespectsAndroidVisibilityAndStatus(
        int? status, bool foreground, UpdateCompletionAction expected)
    {
        Assert.Equal(expected, UpdateCompletionPolicy.Decide(status, foreground));
    }

    [Theory]
    [InlineData(true, 23, 8, true)]
    [InlineData(true, 23, 16, true)]
    [InlineData(true, 23, null, true)]
    [InlineData(true, 23, 2, false)]
    [InlineData(false, 23, 8, false)]
    [InlineData(true, 0, 8, false)]
    public void PollingChecksOnlyTerminalOrMissingDownloadWhileAppVisible(
        bool foreground, long downloadId, int? status, bool expected)
    {
        Assert.Equal(expected,
            UpdateCompletionPolicy.NeedsForegroundCheck(foreground, downloadId, status));
    }

    [Theory]
    [InlineData("1.2.20", null, true)]
    [InlineData("1.2.20", "1.2.19", true)]
    [InlineData("1.2.20", "1.2.20", false)]
    [InlineData("1.2.19", "1.2.20", false)]
    [InlineData("1.2.20", "incorrect", true)]
    [InlineData("incorrect", "1.2.19", false)]
    public void ContinueCheckingReleasesWhileAnotherApkIsPending(
        string latest, string? pending, bool offerReplacement)
    {
        // Le contrôle continue quelle que soit la présence de l'APK en attente ;
        // seule cette comparaison décide s'il faut proposer un nouveau téléchargement.
        Assert.Equal(offerReplacement,
            UpdateCompletionPolicy.IsNewerThanPending(latest, pending));
    }

    [Theory]
    [InlineData("1.2.16", "1.2.16", true)]
    [InlineData("1.2.16", "1.2.17", true)]
    [InlineData("1.2.16", "1.2.15", false)]
    [InlineData("invalide", "1.2.16", false)]
    [InlineData(null, "1.2.16", false)]
    public void UpdatedApplicationDoesNotReopenTheInstaller(
        string? downloaded, string? installed, bool expected)
    {
        Assert.Equal(expected, UpdateCompletionPolicy.AlreadyInstalled(downloaded, installed));
    }
}
