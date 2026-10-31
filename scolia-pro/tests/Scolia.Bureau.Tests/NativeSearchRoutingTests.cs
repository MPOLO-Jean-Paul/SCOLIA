using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Recherche native : une zone de saisie ne doit jamais changer le périmètre
/// de la page ou ramener le lecteur à l'accueil par défaut.
/// Ces tests utilisent le Shell et les commandes de production, sans interface web.
/// </summary>
public sealed class NativeSearchRoutingTests
{
    private static ShellViewModel Shell()
        => new(new ScoliaClient(new Uri("http://127.0.0.1:59999/")), new Preferences());

    [Theory]
    [InlineData("police", "Texte et polices")]
    [InlineData("thème", "Thème et couleurs")]
    [InlineData("son", "Sons")]
    public async Task GlobalSearch_ouvre_le_reglage_demande_et_non_accueil(string query, string expected)
    {
        var shell = Shell();
        var search = new SearchViewModel(shell) { Query = query };
        await search.SearchAsync();
        var hit = Assert.Single(search.Results, r => r.Title == expected);
        Assert.NotNull(hit.OpenAction);
        search.Selected = hit;
        search.Open.Execute(null);
        var page = Assert.IsType<SettingsPage>(shell.Current);
        Assert.Equal(expected, page.SectionTitle);
        Assert.False(shell.BackstageOpen);
    }

    [Fact]
    public async Task Recherche_globale_ouvre_la_securite_directement_avec_mot_de_passe()
    {
        var shell = Shell();
        var search = new SearchViewModel(shell) { Query = "empreinte" };
        await search.SearchAsync();
        search.Selected = Assert.Single(search.Results, r => r.Title == "Sécurité du compte");
        search.Open.Execute(null);
        Assert.True(shell.BackstageOpen);
        Assert.True(shell.Backstage.IsPassword);
        Assert.Null(shell.Current); // aucune redirection parasite à Accueil
    }

    [Fact]
    public async Task Recherche_dans_les_parametres_n_interroge_pas_les_dossiers_scolaires()
    {
        var shell = Shell();
        var search = new SearchViewModel(shell, settingsOnly: true) { Query = "police" };
        await search.SearchAsync();
        Assert.True(search.SettingsOnly);
        Assert.All(search.Results, r => Assert.Equal("Paramètre", r.Type));
        Assert.Contains(search.Results, r => r.Title == "Texte et polices");
    }

    [Fact]
    public void Recherche_menu_ouvre_la_securite_du_compte_sans_page_intermediaire()
    {
        var shell = Shell();
        shell.Phone = true;
        shell.PhoneMenuOpen = true;
        shell.SettingsSearch.Activate.Execute(null);
        shell.SettingsSearch.Query = "mot de passe";
        var result = Assert.Single(shell.SettingsSearch.Results, r => r.Title == "Sécurité du compte");
        result.Open.Execute(null);
        Assert.True(shell.BackstageOpen);
        Assert.True(shell.Backstage.IsPassword);
        Assert.False(shell.PhoneMenuOpen);
    }

    [Fact]
    public async Task En_tete_sur_liste_apprenants_recherche_uniquement_dans_les_apprenants()
    {
        var shell = Shell();
        await shell.Open(() => new StudentsPage(shell));
        Assert.True(shell.ContextSearchAvailable);
        Assert.Contains("apprenant", shell.ContextSearchPlaceholder, StringComparison.OrdinalIgnoreCase);

        shell.ContextSearchCommand.Execute(null);
        Assert.True(shell.HeaderSearch.IsOpen);
        Assert.Null(shell.Dialog); // aucun dialogue de recherche générale
        shell.HeaderSearch.Query = "INF293";
        Assert.Equal("INF293", Assert.IsType<StudentsPage>(shell.Current).Search);
    }

    [Fact]
    public async Task En_tete_des_recus_reutilise_le_filtre_du_journal_et_pas_la_recherche_globale()
    {
        var shell = Shell();
        await shell.Open(() => new ReceiptsPage(shell));
        shell.ContextSearchCommand.Execute(null);
        shell.HeaderSearch.Query = "RC-2026";
        Assert.Equal("RC-2026", Assert.IsType<ReceiptsPage>(shell.Current).Search);
        Assert.Equal("recus", PhoneHeaderSearchVm.Scope(shell.Current));
        Assert.Null(shell.Dialog);
    }

    [Fact]
    public async Task Changer_de_page_ferme_le_filtre_sans_le_copier_dans_le_nouveau_metier()
    {
        var shell = Shell();
        await shell.Open(() => new StudentsPage(shell));
        shell.ContextSearchCommand.Execute(null);
        shell.HeaderSearch.Query = "KABUYA";
        var students = Assert.IsType<StudentsPage>(shell.Current);
        await shell.Open(() => new ReceiptsPage(shell));
        Assert.False(shell.HeaderSearch.IsOpen);
        Assert.Equal(string.Empty, shell.HeaderSearch.Query);
        Assert.Equal("KABUYA", students.Search);
    }

    [Fact]
    public async Task Accueil_utilise_une_recherche_globale_et_configuration_une_recherche_locale()
    {
        var shell = Shell();
        await shell.Open(() => new HomePage(shell));
        shell.ContextSearchCommand.Execute(null);
        Assert.False(Assert.IsType<SearchViewModel>(shell.Dialog).SettingsOnly);
        shell.CloseDialog();

        await shell.Open(() => new SettingsPage(shell, "apparence"));
        shell.ContextSearchCommand.Execute(null);
        Assert.True(Assert.IsType<SearchViewModel>(shell.Dialog).SettingsOnly);
    }

    [Fact]
    public void Masquage_du_mot_de_passe_est_une_action_explicitement_reversible()
    {
        var shell = Shell();
        var security = shell.Backstage;
        Assert.False(security.SecurityPasswordVisible);
        Assert.Equal('•', security.SecurityPasswordMask);
        security.ToggleSecurityPassword.Execute(null);
        Assert.True(security.SecurityPasswordVisible);
        Assert.Equal('\0', security.SecurityPasswordMask);
        security.ToggleSecurityPassword.Execute(null);
        Assert.False(security.SecurityPasswordVisible);
    }
}
