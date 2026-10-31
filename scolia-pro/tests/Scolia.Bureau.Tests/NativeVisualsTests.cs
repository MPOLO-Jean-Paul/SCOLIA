using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Repères sémantiques identiques sur Windows et Android, sans pictogrammes génériques répétés.</summary>
public class NativeVisualsTests
{
    [Fact]
    public void Chaque_metier_possede_une_icone_distincte()
    {
        var groups = new[] { "Accueil", "Données", "Secrétariat", "Finances",
            "Scolarité", "Administration", "Paramètres" };
        Assert.Equal(groups.Length, groups.Select(FeatureVisuals.Icon).Distinct().Count());
        Assert.All(groups, title => Assert.NotEqual(MaterialIconKind.ViewDashboard, FeatureVisuals.Icon(title)));
    }

    [Fact]
    public void Les_groupes_de_commandes_et_la_navigation_partagent_les_memes_reperes()
    {
        var navigation = new NavGroupVm("Scolarité", Array.Empty<NavItemVm>());
        var tools = new PhoneToolGroupVm("Scolarité", Array.Empty<RibbonCommandVm>());
        Assert.Equal(navigation.Icon, tools.Icon);
        Assert.Equal("TonePurple", FeatureVisuals.Tone(navigation.Title));
        Assert.False(navigation.IsExpanded);
        Assert.False(tools.IsExpanded);

        navigation.Toggle.Execute(null);
        tools.Toggle.Execute(null);
        Assert.True(navigation.IsExpanded);
        Assert.True(tools.IsExpanded);
    }

    [Theory]
    [InlineData("Nouvelle dépense", "ToneGreen")]
    [InlineData("Effacer des dossiers", "ToneRed")]
    [InlineData("Reçus (PDF)", "ToneOrange")]
    [InlineData("Vérifier la base", "ToneTeal")]
    [InlineData("Taille du texte", "TonePurple")]
    [InlineData("Situation (PDF)", "ToneIndigo")]
    public void Les_commandes_secondaires_recoivent_une_couleur_metier(string label, string expectedTone)
    {
        Assert.Equal(expectedTone, FeatureVisuals.CommandTone(label, "ToneNeutral"));
        Assert.Equal("TonePink", FeatureVisuals.CommandTone(label, "TonePink"));
    }

    [Fact]
    public void Les_rubriques_desktop_peuvent_etre_ouvertes_immediatement()
    {
        var group = new NavGroupVm("Finances", Array.Empty<NavItemVm>(), expandedByDefault: true);
        Assert.True(group.IsExpanded);
        group.Toggle.Execute(null);
        Assert.False(group.IsExpanded);
    }
}
