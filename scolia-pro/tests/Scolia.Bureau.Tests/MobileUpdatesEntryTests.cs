using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>La mise à jour manuelle doit être trouvable sur Android sans devenir serveur.</summary>
[Collection("Plateforme")]
public sealed class MobileUpdatesEntryTests
{
    private static string ViewFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable"),
            "src", "Scolia.Bureau", relative);
    }

    [Theory]
    [InlineData("Views/Phone/BackstageView.axaml")]
    [InlineData("Views/AboutView.axaml")]
    public void La_recherche_manuelle_est_directement_visible_dans_l_application(string view)
    {
        var root = XDocument.Load(ViewFile(view));
        var nodes = root.Descendants().ToArray();
        Assert.Contains(nodes, node => node.Name.LocalName == "Button"
            && node.Attributes().Any(a => a.Name.LocalName == "Command"
                && a.Value.Contains("CheckPhoneUpdates", StringComparison.Ordinal)));
        Assert.Contains(nodes, node => node.Name.LocalName == "TextBlock"
            && node.Attributes().Any(a => a.Name.LocalName == "Text"
                && a.Value.Contains("PhoneUpdateStatus", StringComparison.Ordinal)));
        Assert.Contains(nodes, node => node.Name.LocalName == "Border"
            && node.Attributes().Any(a => a.Name.LocalName == "IsVisible"
                && a.Value.Contains("CanCheckPhoneUpdates", StringComparison.Ordinal)));
    }

    [Fact]
    public void Verification_manuelle_est_disponible_aussi_sur_un_telephone_client()
    {
        bool previousMobile = Platform.IsMobile;
        var previousCheck = Platform.CheckAppUpdatesAsync;
        var previousStatus = Platform.AppUpdateStatus;
        try
        {
            Platform.IsMobile = true;
            Platform.CheckAppUpdatesAsync = () => Task.CompletedTask;
            Platform.AppUpdateStatus = () => "12:00 · Version 1.2.25 disponible.";
            using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
            var shell = new ShellViewModel(api, new Preferences());
            Assert.True(shell.Backstage.CanCheckPhoneUpdates);
            Assert.Contains("1.2.25", shell.Backstage.PhoneUpdateStatus);
            var about = new AboutViewModel(shell);
            Assert.True(about.CanCheckPhoneUpdates);
            Assert.Contains("1.2.25", about.PhoneUpdateStatus);
        }
        finally
        {
            Platform.IsMobile = previousMobile;
            Platform.CheckAppUpdatesAsync = previousCheck;
            Platform.AppUpdateStatus = previousStatus;
        }
    }
}
