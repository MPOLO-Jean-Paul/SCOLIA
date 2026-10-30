using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class PhoneMenuGroupingTests
{
    [Theory]
    [InlineData("Thème et couleurs", "Paramètres", "Affichage")]
    [InlineData("Sons", "Administration", "Affichage")]
    [InlineData("Texte et polices", "Paramètres", "Affichage")]
    [InlineData("Aide et gestes", "Affichage", "Aide")]
    [InlineData("Gestion des utilisateurs", "Paramètres", "Administration")]
    [InlineData("Encaisser", "Finances", "Finances")]
    public void Une_commande_va_dans_son_unique_bloc(string label, string source, string destination)
    {
        Assert.Equal(destination, ShellViewModel.CanonicalPhoneGroup(label, source));
    }
}
