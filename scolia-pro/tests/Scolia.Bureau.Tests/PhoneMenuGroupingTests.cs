using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class PhoneMenuGroupingTests
{
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
