using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Les résultats de recherche natifs doivent ouvrir l'objet choisi, sans revenir à l'accueil.</summary>
public sealed class NativeSearchNavigationTests
{
    [Fact]
    public void Un_seul_clic_sur_un_resultat_ouvre_son_identifiant_exact()
    {
        string? destination = null;
        var row = new SearchHitRow(
            new SearchHitDto("Apprenant", "Kabeya", "INF1", "/apprenants/1f4dc0cd-4ca7-482b-ab1b-9ae0ea6aca67"),
            selected =>
            {
                destination = selected.Hit.Url;
                return Task.CompletedTask;
            });

        row.Open.Execute(null);

        Assert.Equal("/apprenants/1f4dc0cd-4ca7-482b-ab1b-9ae0ea6aca67", destination);
    }

    [Theory]
    [InlineData("police", "Texte et polices")]
    [InlineData("thème", "Thème et couleurs")]
    [InlineData("sons", "Sons")]
    [InlineData("empreinte", "Sécurité du compte")]
    [InlineData("animation", "Animations et fluidité")]
    public void Depuis_le_menu_le_resultat_mene_a_la_rubrique_precise(string query, string expected)
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        shell.PhoneMenuOpen = true;
        shell.SettingsSearch.Activate.Execute(null);
        shell.SettingsSearch.Query = query;
        var result = Assert.Single(shell.SettingsSearch.Results, r => r.Title == expected);

        result.Open.Execute(null);

        Assert.False(shell.PhoneMenuOpen);
        var page = Assert.IsType<SettingsPage>(shell.Current);
        Assert.True(page.SettingsDetailVisible);
        Assert.Equal(expected, page.SectionTitle);
    }

    [Fact]
    public void Configuration_ouvre_la_liste_des_reglages_et_non_accueil()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        shell.PhoneMenuOpen = true;
        shell.SettingsSearch.Query = "configuration";

        var result = Assert.Single(shell.SettingsSearch.Results, r => r.Title == "Configuration");
        result.Open.Execute(null);

        Assert.IsType<SettingsPage>(shell.Current);
        Assert.True(((SettingsPage)shell.Current!).MobileCategoriesVisible);
    }

    [Fact]
    public async Task Recherche_entete_filtre_seulement_la_page_active()
    {
        using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        var students = new StudentsPage(shell);
        await shell.Open(() => students);
        Assert.Equal("apprenants", PhoneHeaderSearchVm.Scope(shell.Current));
        shell.HeaderSearch.Query = "KABEYA";
        Assert.Equal("KABEYA", students.Search);

        var receipts = new ReceiptsPage(shell);
        await shell.Open(() => receipts);
        Assert.Equal("recus", PhoneHeaderSearchVm.Scope(shell.Current));
        Assert.Equal(string.Empty, shell.HeaderSearch.Query);
        shell.HeaderSearch.Query = "RC-2026";
        Assert.Equal("RC-2026", receipts.Search);
        Assert.Equal("KABEYA", students.Search);
        Assert.True(shell.HeaderSearch.IsContextual);
        Assert.Equal("Filtre", shell.HeaderSearch.ShortcutText);

        var home = new HomePage(shell);
        await shell.Open(() => home);
        Assert.False(shell.HeaderSearch.IsContextual);
        Assert.Equal("Ctrl+K", shell.HeaderSearch.ShortcutText);
        Assert.Equal(string.Empty, shell.HeaderSearch.Query);
    }
}
