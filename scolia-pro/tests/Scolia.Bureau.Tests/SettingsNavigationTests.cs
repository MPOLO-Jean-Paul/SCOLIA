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

    [Theory]
    [InlineData("police", "Texte et polices")]
    [InlineData("thème", "Thème et couleurs")]
    [InlineData("animations", "Animations et fluidité")]
    [InlineData("son", "Sons")]
    [InlineData("empreinte", "Sécurité du compte")]
    public void Recherche_du_menu_ouvre_directement_le_bon_reglage_et_non_l_accueil(string query, string titre)
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        shell.PhoneMenuOpen = true;
        shell.SettingsSearch.Activate.Execute(null);
        shell.SettingsSearch.Query = query;
        var result = Assert.Single(shell.SettingsSearch.Results, r => r.Title == titre);
        result.Open.Execute(null);

        Assert.False(shell.PhoneMenuOpen);
        var page = Assert.IsType<SettingsPage>(shell.Current);
        Assert.True(page.SettingsDetailVisible);
        Assert.Equal(titre, page.SectionTitle);
    }

    [Fact]
    public void Recherche_en_tete_est_limitee_au_metier_de_la_page_active()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };

        Assert.Equal("apprenants", PhoneHeaderSearchVm.Scope(new StudentsPage(shell)));
        Assert.Equal("recus", PhoneHeaderSearchVm.Scope(new ReceiptsPage(shell)));
        Assert.Equal("mensualites", PhoneHeaderSearchVm.Scope(new MonthlyPage(shell)));
        Assert.Equal("debiteurs", PhoneHeaderSearchVm.Scope(new DebtorsPage(shell)));
        Assert.Equal("encaissement", PhoneHeaderSearchVm.Scope(new CashierPage(shell)));
        Assert.Equal("depenses", PhoneHeaderSearchVm.Scope(new ExpensesPage(shell)));
        Assert.Equal(string.Empty, PhoneHeaderSearchVm.Scope(new HomePage(shell)));
        Assert.Equal(string.Empty, PhoneHeaderSearchVm.Scope(new SettingsPage(shell)));
    }

    [Fact]
    public void Le_menu_conserve_la_recherche_des_parametres_et_l_accueil_une_recherche_globale()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        shell.PhoneMenuOpen = true;
        shell.HeaderSearch.Toggle.Execute(null);
        Assert.True(shell.SettingsSearch.IsOpen);
        Assert.True(shell.PhoneMenuOpen);
        shell.PhoneMenuOpen = false;
        Assert.False(shell.SettingsSearch.IsOpen);
    }
}
