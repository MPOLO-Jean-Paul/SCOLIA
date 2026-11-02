using Scolia.Bureau.Infrastructure;
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
}
