using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le contrôle Android doit rester trouvable et fonctionner quel que soit le mode poste/serveur.</summary>
[Collection("Plateforme")]
public sealed class AndroidUpdatesNavigationTests
{
    [Fact]
    public async Task Mon_compte_expose_une_recherche_manuelle_sans_acces_au_serveur()
    {
        var previousMobile = Platform.IsMobile;
        var previousCheck = Platform.CheckAppUpdatesAsync;
        var previousStatus = Platform.AppUpdateStatus;
        try
        {
            int calls = 0;
            Platform.IsMobile = true;
            Platform.CheckAppUpdatesAsync = () =>
            {
                calls++;
                return Task.CompletedTask;
            };
            Platform.AppUpdateStatus = () => "20:24 · Vérification officielle terminée.";
            using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
            var shell = new ShellViewModel(api, new Preferences());
            var backstage = shell.Backstage;
            Assert.Contains(backstage.Items, item => item.Value == "updates");
            backstage.Section = backstage.Items.Single(item => item.Value == "updates");
            Assert.True(backstage.IsUpdates);
            Assert.True(backstage.ShowDetail);
            Assert.False(backstage.ShowList);
            Assert.Equal("Mises à jour", backstage.DetailTitle);
            Assert.True(backstage.CanCheckMobileUpdates);
            await backstage.CheckMobileUpdates.ExecuteAsync();
            Assert.Equal(1, calls);
            Assert.False(backstage.IsCheckingMobileUpdates);
            Assert.Contains("Vérification officielle terminée", backstage.MobileUpdateStatus);
        }
        finally
        {
            Platform.IsMobile = previousMobile;
            Platform.CheckAppUpdatesAsync = previousCheck;
            Platform.AppUpdateStatus = previousStatus;
        }
    }

    [Fact]
    public void Le_bouton_est_visible_sur_le_telephone_et_non_cache_dans_poste_et_reseau()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Scolia.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        var view = Path.Combine(current!.FullName, "src", "Scolia.Bureau",
            "Views", "Phone", "BackstageView.axaml");
        var doc = XDocument.Load(view);
        Assert.Contains(doc.Descendants(), node => node.Name.LocalName == "Button"
            && node.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("CheckMobileUpdates")));
        Assert.Contains(doc.Descendants(), node => node.Name.LocalName == "StackPanel"
            && node.Attributes().Any(a => a.Name.LocalName == "IsVisible" && a.Value.Contains("IsUpdates")));
    }
}
