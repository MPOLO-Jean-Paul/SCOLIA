using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le menu Android de réglages n'affiche plus toutes les options d'un seul bloc.</summary>
public class SettingsNavigationTests
{
    [Fact]
    public void Parametres_android_affichent_les_rubriques_avant_le_detail()
    {
        var shell = new ShellViewModel(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());
        shell.Phone = true;
        var page = new SettingsPage(shell);

        Assert.True(page.MobileCategoriesVisible);
        Assert.False(page.SettingsDetailVisible);

        var appearance = Assert.Single(page.PosteSections, s => s.Value == "apparence");
        page.OpenMobileSection.Execute(appearance);

        Assert.False(page.MobileCategoriesVisible);
        Assert.True(page.SettingsDetailVisible);
        Assert.Equal("Thème et couleurs", page.SectionTitle);

        Assert.True(page.TryBackToMobileSections());
        Assert.False(page.TryBackToMobileSections());
        Assert.True(page.MobileCategoriesVisible);
        Assert.False(page.SettingsDetailVisible);
    }

    [Fact]
    public void Ouverture_directe_d_une_rubrique_de_parametres_est_conservee()
    {
        var shell = new ShellViewModel(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());
        shell.Phone = true;

        var page = new SettingsPage(shell, "texte");

        Assert.True(page.SettingsDetailVisible);
        Assert.False(page.MobileCategoriesVisible);
        Assert.Equal("Texte et polices", page.SectionTitle);
    }

    [Fact]
    public void Sur_ordinateur_les_reglages_restent_directement_disponibles()
    {
        var shell = new ShellViewModel(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());
        shell.Phone = false;

        var page = new SettingsPage(shell);

        Assert.True(page.SettingsDetailVisible);
        Assert.False(page.MobileCategoriesVisible);
    }
}
