using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

public class FormatTests
{
    [Theory]
    [InlineData("1\u202F500", 1500)]
    [InlineData("1 500", 1500)]
    [InlineData("1 500,50", 1500.50)]
    [InlineData("1 500", 1500)]
    [InlineData("25000", 25000)]
    public void Montant_saisi_a_la_francaise_est_lu(string saisie, decimal attendu)
    {
        Assert.True(Format.TryAmount(saisie, out var valeur));
        Assert.Equal(attendu, valeur);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Montant_illisible_est_refuse(string? saisie)
    {
        Assert.False(Format.TryAmount(saisie, out _));
    }

    [Theory]
    [InlineData("24/10/2026", 2026, 10, 24)]
    [InlineData("4/9/2026", 2026, 9, 4)]
    [InlineData("2026-09-01", 2026, 9, 1)]
    public void Date_saisie_en_jour_mois_annee_est_lue(string saisie, int annee, int mois, int jour)
    {
        Assert.True(Format.TryDay(saisie, out var jourLu));
        Assert.Equal(new DateOnly(annee, mois, jour), jourLu);
    }

    [Fact]
    public void Date_inexistante_est_refusee()
    {
        Assert.False(Format.TryDay("31/02/2026", out _));
    }

    [Fact]
    public void Nom_complet_ignore_les_parties_vides()
    {
        Assert.Equal("BANZA KITENGE Merveille", Format.FullName("BANZA", "KITENGE", "Merveille"));
        Assert.Equal("BANZA Merveille", Format.FullName("BANZA", "", "Merveille"));
    }

    [Fact]
    public void Montant_est_affiche_avec_espaces_de_milliers()
    {
        // fr-FR sépare les milliers par une espace fine insécable : on la normalise pour comparer.
        var affiche = Format.Money(440000, "FC").Replace('\u202F', ' ').Replace('\u00A0', ' ');
        Assert.Equal("440 000 FC", affiche);
    }
}

public class LabelsTests
{
    [Theory]
    [InlineData("Especes", "Espèces")]
    [InlineData("MobileMoney", "Mobile Money")]
    [InlineData("Cheque", "Chèque")]
    public void Mode_de_paiement_est_traduit(string code, string libelle)
    {
        Assert.Equal(libelle, Labels.Method(code));
    }

    [Theory]
    [InlineData("Diplome", "Diplômé")]
    [InlineData("Transfere", "Transféré")]
    [InlineData("Annule", "Annulé")]
    public void Statut_est_traduit(string code, string libelle)
    {
        Assert.Equal(libelle, Labels.Status(code));
    }

    [Fact]
    public void Libelle_inconnu_est_rendu_tel_quel()
    {
        Assert.Equal("Autre", Labels.Method("Autre"));
        Assert.Equal("Code", Labels.Status("Code"));
    }

    [Fact]
    public void Tranches_d_anciennete_sont_celles_du_serveur()
    {
        Assert.Contains(Labels.Buckets, b => b.Value == "Plus de 90 jours");
        Assert.Contains(Labels.Buckets, b => b.Value == "0-30 jours");
    }
}

public class QueryTests
{
    [Fact]
    public void Valeurs_vides_sont_omises()
    {
        var query = ScoliaClient.Query(("q", ""), ("promotionId", null), ("page", 2));
        Assert.Equal("?page=2", query);
    }

    [Fact]
    public void Dates_sont_envoyees_au_format_iso()
    {
        var query = ScoliaClient.Query(("du", new DateOnly(2026, 9, 1)));
        Assert.Equal("?du=2026-09-01", query);
    }

    [Fact]
    public void Valeurs_sont_encodees()
    {
        var query = ScoliaClient.Query(("q", "Jean Pierre & fils"));
        Assert.Equal("?q=Jean%20Pierre%20%26%20fils", query);
    }

    [Fact]
    public void Aucun_parametre_donne_une_chaine_vide()
    {
        Assert.Equal(string.Empty, ScoliaClient.Query(("q", null)));
    }
}

public class FormViewModelTests
{
    [Fact]
    public void Champ_obligatoire_vide_bloque_la_validation()
    {
        var form = new FormViewModel("Test");
        form.Add("nom", "Nom", required: true);
        Assert.False(form.Validate());
        Assert.Equal("Champ obligatoire.", form.Find("nom").Error);
        Assert.True(form.HasError);
    }

    [Fact]
    public void Montant_invalide_est_signale()
    {
        var form = new FormViewModel("Test");
        form.Add("montant", "Montant", FieldKind.Amount, "abc");
        Assert.False(form.Validate());
        Assert.Equal("Montant invalide.", form.Find("montant").Error);
    }

    [Fact]
    public void Formulaire_valide_renvoie_ses_valeurs()
    {
        var form = new FormViewModel("Test");
        form.Add("nom", "Nom", value: "  BANZA  ", required: true);
        form.Add("montant", "Montant", FieldKind.Amount, "1 500");
        form.AddChoice("mode", "Mode", Labels.Methods, "Banque", required: true);
        form.AddDay("date", "Date", new DateOnly(2026, 10, 24), required: true);
        form.AddFlag("actif", "Actif", true);

        Assert.True(form.Validate());
        Assert.Equal("BANZA", form.Text("nom"));
        Assert.Equal(1500m, form.Amount("montant"));
        Assert.Equal("Banque", form.ChoiceValue("mode"));
        Assert.Equal(new DateOnly(2026, 10, 24), form.Day("date"));
        Assert.True(form.Flag("actif"));
    }

    [Fact]
    public void Date_absente_est_signalee_comme_vide()
    {
        var form = new FormViewModel("Test");
        form.AddDay("date", "Date", null, required: true);
        Assert.False(form.Validate());
    }

    [Fact]
    public void Texte_optionnel_vide_devient_null()
    {
        var form = new FormViewModel("Test");
        form.Add("note", "Note");
        Assert.Null(form.OptionalText("note"));
    }

    [Fact]
    public async Task Soumission_qui_echoue_garde_la_boite_ouverte_avec_le_message()
    {
        var form = new FormViewModel("Test", string.Empty, _ => throw new ApiException("Doublon détecté."));
        form.Add("nom", "Nom", value: "X");
        form.Submit.Execute(null);
        for (var i = 0; i < 200 && form.Busy; i++) await Task.Delay(5);
        Assert.Equal("Doublon détecté.", form.Error);
        Assert.False(form.Completion.IsCompleted);
    }

    [Fact]
    public async Task Soumission_reussie_ferme_la_boite()
    {
        var form = new FormViewModel("Test", string.Empty, _ => Task.FromResult(true));
        form.Add("nom", "Nom", value: "X", required: true);
        form.Submit.Execute(null);
        Assert.True(await form.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
