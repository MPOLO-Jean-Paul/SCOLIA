using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class MobileUpdateDiscoverabilityTests
{
    private static string Source(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable"),
            path.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void VerificationManuelleAccessibleDirectementDansLeMenuAndroid()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/PhoneFrame.axaml"));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" &&
                a.Value.Contains("Backstage.PosteSection.CheckMobileUpdates", StringComparison.Ordinal)));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Text" &&
                a.Value.Contains("Backstage.PosteSection.MobileUpdateStatus", StringComparison.Ordinal)));
    }

    [Fact]
    public void VerificationManuelleAccessibleDepuisMonCompteSansOuvrirLeReseau()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/Phone/BackstageView.axaml"));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" &&
                a.Value.Contains("PosteSection.CheckMobileUpdates", StringComparison.Ordinal)));
        Assert.Contains(doc.Descendants(), e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Text" &&
                a.Value.Contains("PosteSection.MobileUpdateStatus", StringComparison.Ordinal)));
    }

    [Fact]
    public void LaRechercheDuMenuConnaitLeMotMiseAJourEtOuvreLaVerification()
    {
        var source = File.ReadAllText(Source("src/Scolia.Bureau/ViewModels/PhoneSheet.cs"));
        Assert.Contains("Mises à jour de SCOLIA", source, StringComparison.Ordinal);
        Assert.Contains("Platform.CheckAppUpdatesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RechercheAutomatiqueAussiQuandSCOLIAResteOuvert()
    {
        var source = File.ReadAllText(Source("src/Scolia.Mobile/AndroidAppUpdates.cs"));
        Assert.Contains("StartForegroundReleaseWatch(activity)", source, StringComparison.Ordinal);
        Assert.Contains("StopForegroundReleaseWatch()", source, StringComparison.Ordinal);
        Assert.Contains("WatchReleasesWhileOpenAsync", source, StringComparison.Ordinal);
        Assert.Contains("forceRefresh: true, manual: true", source, StringComparison.Ordinal);
        Assert.Contains("CheckLatestAsync(activity, showDialog: true)", source, StringComparison.Ordinal);
    }
}
