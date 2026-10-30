using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

/// <summary>Le menu mobile ne présente pas deux fois un bloc ni une rubrique.</summary>
public sealed class PhoneMenuBlocksTests
{
    [Fact]
    public void Chaque_rubrique_est_affectee_a_un_seul_bloc()
    {
        NavGroupVm[] navigation =
        [
            new("Données", []), new("Secrétariat", []), new("Scolarité", []),
            new("Finances", []), new("Administration", []), new("Paramètres", []),
        ];
        PhoneToolGroupVm[] commands =
        [
            new("Données", []), new("Finances", []), new("Affichage", []),
            new("Administration", []), new("Aide", []),
        ];

        var blocks = PhoneMenuBlockVm.Build(navigation, commands);
        Assert.Equal(navigation.Length, blocks.Sum(b => b.NavigationGroups.Count));
        Assert.Equal(commands.Length, blocks.Sum(b => b.ToolGroups.Count));
        Assert.All(navigation, group => Assert.Single(blocks, b => b.NavigationGroups.Contains(group)));
        Assert.All(commands, group => Assert.Single(blocks, b => b.ToolGroups.Contains(group)));
        Assert.Single(blocks, b => b.Title == "AFFICHAGE");
    }

    [Fact]
    public void Le_bloc_affichage_ne_contient_aucune_fonction_d_un_autre_metier()
    {
        var groups = new[]
        {
            new PhoneToolGroupVm("Affichage", []),
            new PhoneToolGroupVm("Finances", []),
            new PhoneToolGroupVm("Scolarité", []),
        };
        var blocks = PhoneMenuBlockVm.Build([], groups);
        var display = Assert.Single(blocks, b => b.Title == "AFFICHAGE");
        Assert.Equal("Affichage", Assert.Single(display.ToolGroups).Title);
        Assert.Empty(display.NavigationGroups);
        Assert.DoesNotContain(display.ToolGroups, x => x.Title == "Finances" || x.Title == "Scolarité");
    }
}
