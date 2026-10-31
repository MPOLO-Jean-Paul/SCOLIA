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
    [InlineData("Nouvelle promotion", "Administration", "Paramètres")]
    [InlineData("Nouvelle année", "Administration", "Paramètres")]
    [InlineData("Nouvel utilisateur", "Administration", "Administration")]
    [InlineData("Nouvelle inscription", "Secrétariat", "Secrétariat")]
    [InlineData("Nouveau cours", "Scolarité", "Scolarité")]
    [InlineData("Nouvelle dépense", "Finances", "Finances")]
    [InlineData("Annuler le reçu", "Reçu", "Finances")]
    [InlineData("Modifier", "Apprenant", "Secrétariat")]
    [InlineData("À propos", "Aide", "Aide")]
    [InlineData("Relevé de compte", "Apprenant", "Finances")]

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
    [Theory]
    [InlineData("Situation (PDF)", "Données", "Exports rapides", "Exports")]
    [InlineData("Sauvegarder", "Données", "Sauvegardes", "Sauvegardes")]
    [InlineData("Importation du classeur", "Données", "Sauvegardes", "Sauvegardes")]
    [InlineData("Vérifier la base", "Données", "Contrôle", "Contrôle")]
    [InlineData("Effacer des dossiers", "Données", "Effacement", "Effacement")]
    [InlineData("Tableau de bord", "Données", "Pilotage", "Exports")]
    public void Groupe_du_ruban_est_classe_selon_sa_nature(string label, string onglet, string groupe, string attendu)
        => Assert.Equal(attendu, ShellViewModel.CanonicalPhoneGroup(label, onglet, groupe));


    [Theory]
    [InlineData("empreinte", "Sécurité du compte biométrie empreinte digitale", true)]
    [InlineData("securite", "Sécurité du compte biométrie empreinte digitale", true)]
    [InlineData("TAILSCALE vpn", "Poste et réseau tailscale vpn WiFi", true)]
    [InlineData("couleur accent", "Thème et couleurs couleur d'accent", true)]
    [InlineData("police", "Texte et polices taille de police", true)]
    [InlineData("apprenants", "Thème et couleurs couleur d'accent", false)]
    [InlineData("finance", "Sons notifications", false)]
    [InlineData("empreinte réseau", "Sécurité du compte biométrie empreinte digitale", false)]
    public void Recherche_des_reglages_ignore_accents_et_ne_parcourt_pas_les_donnees(
        string saisie, string reglage, bool attendu)
        => Assert.Equal(attendu, PhoneSettingsSearchVm.Matches(saisie, reglage));
}
