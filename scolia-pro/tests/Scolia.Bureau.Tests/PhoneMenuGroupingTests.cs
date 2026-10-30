using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class PhoneMenuGroupingTests
{
    [Fact]
    public void Affichage_est_dans_le_seul_accordeon_parametres()
    {
        var settings = new NavGroupVm("Paramètres", []);
        var appearance = new RibbonCommandVm(
            "Thème et couleurs", Material.Icons.MaterialIconKind.Cog, string.Empty,
            new Scolia.Bureau.Infrastructure.RelayCommand(() => { }), false, "TonePurple");
        var tools = new[]
        {
            new PhoneToolGroupVm("Paramètres", [appearance]),
        };

        var blocks = PhoneMenuBlockVm.Build([settings], tools);

        Assert.Single(blocks);
        Assert.Equal("ADMINISTRATION", blocks[0].Title);
        Assert.DoesNotContain(blocks, b => b.Title == "AFFICHAGE");
        Assert.Empty(blocks[0].ToolGroups);
        Assert.Single(settings.SupplementalCommands);
        Assert.Same(appearance, settings.SupplementalCommands[0]);
    }

    [Theory]
    [InlineData("Thème et couleurs", "Paramètres", "Paramètres")]
    [InlineData("Sons", "Administration", "Paramètres")]
    [InlineData("Texte et polices", "Paramètres", "Paramètres")]
    [InlineData("Aide et gestes", "Affichage", "Aide")]
    [InlineData("Gestion des utilisateurs", "Paramètres", "Administration")]
    [InlineData("Encaisser", "Finances", "Finances")]
    [InlineData("Option d'affichage", "Affichage", "Paramètres")]
    public void Une_commande_va_dans_son_unique_bloc(string label, string source, string destination)
    {
        Assert.Equal(destination, ShellViewModel.CanonicalPhoneGroup(label, source));
    }
}
