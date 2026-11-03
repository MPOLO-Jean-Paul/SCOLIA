using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Finitions de l'ordinateur : libellés lisibles, statuts colorés, liste des polices stable.</summary>
[Collection("Plateforme")]
public sealed class DesktopPolishTests
{
    [Theory]
    [InlineData("connexion.reussie", "Connexion réussie")]
    [InlineData("paiement.encaisse", "Encaissement")]
    [InlineData("section.creee", "Section créée")]
    [InlineData("inconnu.code", "Inconnu code")]
    public void Le_journal_d_audit_montre_des_actions_lisibles(string code, string label) =>
        Assert.Equal(label, Labels.AuditAction(code));

    [Theory]
    [InlineData("Payé", "green")]
    [InlineData("Pris en charge", "green")]
    [InlineData("Impayé", "red")]
    [InlineData("Paye", "green")]
    public void Les_statuts_deja_traduits_gardent_leur_couleur(string status, string tone) =>
        Assert.Equal(tone, Labels.Tone(status));

    [Fact]
    public void La_liste_des_polices_est_la_meme_a_chaque_lecture()
    {
        // Une liste déroulante relit sa source : une nouvelle liste à chaque fois lui faisait perdre la police choisie.
        Assert.Same(Preferences.Fonts, Preferences.Fonts);
        Assert.Contains(Preferences.Fonts, f => f.Value == new Preferences().Font);
    }

    [Fact]
    public void Le_poste_dit_si_android_peut_endormir_le_serveur()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        try
        {
            Platform.IsHostForegroundActive = () => true;
            Platform.IsHostApiServing = () => true;
            Platform.BackgroundAllowed = () => false;
            var poste = new PosteViewModel(shell);
            Assert.True(poste.BackgroundBlocked);
            Assert.Equal("Serveur actif · autorisation batterie nécessaire", poste.BackgroundTitle);

            // Retour des réglages : l'autorisation batterie et les deux états
            // du serveur sont suivis indépendamment de la fenêtre Android.
            Platform.BackgroundAllowed = () => true;
            Assert.True(poste.BackgroundOk);
            Assert.True(poste.BackgroundHealthy);
            Assert.Equal("Serveur actif en arrière-plan", poste.BackgroundTitle);

            // La présence de la notification ne suffit pas si l'API a échoué.
            Platform.IsHostApiServing = () => false;
            Assert.False(poste.BackgroundHealthy);
            Assert.Equal("Démarrage du serveur scolaire…", poste.BackgroundTitle);
            Platform.IsHostForegroundActive = () => false;
            Assert.Equal("Le service serveur doit être réactivé", poste.BackgroundTitle);
        }
        finally
        {
            Platform.BackgroundAllowed = null;
            Platform.IsHostForegroundActive = null;
            Platform.IsHostApiServing = null;
            Platform.EnsureHostAvailable = null;
            Platform.BackgroundHint = string.Empty;
        }
    }

    [Fact]
    public void Rechercher_sur_l_ordinateur_reste_dans_la_barre_du_haut()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());

        // Sans barre affichée (aucune vue), la fenêtre de recherche reste le recours.
        shell.FindInHeader();
        Assert.IsType<SearchViewModel>(shell.Dialog);
        shell.CloseDialog();

        // Avec la barre de l'ordinateur : le curseur y va, aucune autre fenêtre.
        var focused = 0;
        shell.HeaderFinderRequested += () => focused++;
        shell.FindInHeader();
        Assert.Equal(1, focused);
        Assert.True(shell.HeaderFinderOpen);
        Assert.False(shell.HasDialog);

        // Une saisie garde la liste ouverte ; un résultat ouvert la referme et vide la barre.
        shell.HeaderFinder.Query = "Kabeya";
        Assert.True(shell.HeaderFinderOpen);
        shell.CloseHeaderFinder();
        Assert.False(shell.HeaderFinderOpen);
        Assert.Equal(string.Empty, shell.HeaderFinder.Query);
    }
}
