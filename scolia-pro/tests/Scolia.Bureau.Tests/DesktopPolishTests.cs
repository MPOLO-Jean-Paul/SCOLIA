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
            Platform.BackgroundAllowed = () => false;
            var poste = new PosteViewModel(shell);
            Assert.True(poste.BackgroundBlocked);
            Assert.Equal("Android peut endormir SCOLIA", poste.BackgroundTitle);

            // Retour des réglages : l'autorisation donnée se lit aussitôt.
            Platform.BackgroundAllowed = () => true;
            Assert.True(poste.BackgroundOk);
            Assert.Equal("Actif même écran éteint", poste.BackgroundTitle);
        }
        finally
        {
            Platform.BackgroundAllowed = null;
            Platform.BackgroundHint = string.Empty;
        }
    }
}
