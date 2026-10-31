using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Adaptation à la taille de l'écran : boutons d'en-tête, volet et échelle de l'interface.</summary>
public class ResponsiveTests
{
    private static ShellViewModel NewShell() => new(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());

    [Fact]
    public void Les_boutons_qui_ne_tiennent_pas_passent_dans_le_menu()
    {
        var page = new DataPage(NewShell());
        var total = page.Actions.Count;
        Assert.Equal(3, page.VisibleActions.Count);
        Assert.Equal(total - 3, page.MoreActions.Count);

        // Fenêtre étroite : un seul bouton à côté du titre, les autres dans « … ».
        page.FitActions(1);
        Assert.Single(page.VisibleActions);
        Assert.Equal(total - 1, page.MoreActions.Count);
        Assert.True(page.HasMoreActions);

        page.FitActions(0);
        Assert.Empty(page.VisibleActions);
        Assert.Equal(total, page.MoreActions.Count);
    }

    [Fact]
    public void Le_volet_se_replie_en_fenetre_etroite_puis_revient()
    {
        var shell = NewShell();
        Assert.True(shell.SidebarShown);
        shell.Narrow = true;
        Assert.False(shell.SidebarShown);
        shell.ToggleSidebar();
        Assert.True(shell.SidebarShown);
        shell.Narrow = false;
        // Le choix enregistré n'a pas changé : le volet revient en fenêtre large.
        Assert.True(shell.SidebarShown);
        Assert.True(shell.Prefs.Sidebar);
    }

    [Theory]
    [InlineData(1280, 720, 1.0)]
    [InlineData(1093, 560, 0.8)]
    [InlineData(1920, 1040, 1.3)]
    [InlineData(760, 520, 0.8)]
    public void L_interface_suit_la_taille_de_la_fenetre(double width, double height, double expected)
    {
        var prefs = new Preferences();
        prefs.Fit(width, height);
        Assert.Equal(expected, prefs.LayoutScale, 2);
        // La taille du texte choisie par l'utilisateur ne dépend pas de la fenêtre.
        Assert.Equal(1.0, prefs.Factor, 2);
    }
}

/// <summary>Couleur d'accent : choix enregistré, valeur inconnue ramenée au bleu.</summary>
public class AccentTests
{
    [Fact]
    public void La_couleur_d_accent_inconnue_revient_au_bleu()
    {
        var prefs = new Preferences { Accent = "vert" };
        Assert.Equal("vert", prefs.Accent);
        prefs.Accent = "fuchsia";
        Assert.Equal("bleu", prefs.Accent);
        Assert.Equal(6, Preferences.Accents.Count);
    }
}
