using Scolia.Mobile;

namespace Scolia.Mobile.Tests;

public sealed class UpdateDownloadProgressTests
{
    [Theory]
    [InlineData(1, 0L, 100L, 0)]
    [InlineData(2, 50L, 100L, 50)]
    [InlineData(2, 100L, 100L, 99)]
    [InlineData(4, 120L, 100L, 99)]
    [InlineData(2, -1L, 100L, 0)]
    [InlineData(2, long.MaxValue, 1L, 99)]
    [InlineData(8, 100L, 100L, 100)]
    public void Progress_does_not_announce_completion_before_Android_finishes(int status, long received, long total, int percent)
        => Assert.Equal(percent, new UpdateDownloadProgress(status, 0, received, total).Percent);

    [Theory]
    [InlineData(4, 2, "connexion Internet")]
    [InlineData(4, 3, "Wi-Fi")]
    [InlineData(4, 1, "Connexion interrompue")]
    [InlineData(16, 1006, "Espace libre insuffisant")]
    [InlineData(16, 1007, "stockage")]
    [InlineData(16, 1008, "reprendre")]
    [InlineData(16, 1009, "ancien fichier")]
    [InlineData(16, 403, "HTTP 403")]
    public void Waiting_and_failure_explain_why_the_file_is_not_downloading(int status, int reason, string expected)
        => Assert.Contains(expected, new UpdateDownloadProgress(status, reason, 0, 124 * 1024 * 1024).Message);

    [Fact]
    public void An_unknown_size_never_displays_a_fake_percentage()
    {
        var progress = new UpdateDownloadProgress(2, 0, 1024 * 1024, -1);
        Assert.Null(progress.Percent);
        Assert.Contains("1 Mo reçus", progress.Message);
        Assert.DoesNotContain("%", progress.Message);
    }

    [Theory]
    [InlineData(null, 1000L, (int)PendingUpdateAction.OfferDownload)]
    [InlineData(16, 1000L, (int)PendingUpdateAction.OfferDownload)]
    [InlineData(1, 1000L, (int)PendingUpdateAction.ShowProgress)]
    [InlineData(2, 1000L, (int)PendingUpdateAction.ShowProgress)]
    [InlineData(4, 1000L, (int)PendingUpdateAction.ShowProgress)]
    [InlineData(4, 0L, (int)PendingUpdateAction.OfferDownload)]
    [InlineData(8, 0L, (int)PendingUpdateAction.Install)]
    public void A_manual_check_can_recover_missing_or_failed_downloads_without_discarding_active_or_completed_files(
        int? status, long started, int expected)
        => Assert.Equal(expected, (int)UpdateCompletionPolicy.PendingAction(status, started, 2000));
}
