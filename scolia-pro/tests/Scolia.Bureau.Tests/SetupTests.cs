using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public class SetupTests
{
    private static SetupViewModel NewWizard() =>
        new(new ShellViewModel(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences()))
        {
            SchoolName = "Institut de test",
            ShortName = "IT",
            YearLabel = "2026 - 2027",
            AdminUserName = "admin",
            AdminDisplayName = "Administrateur",
            AdminPassword = "Scolia2026!",
            AdminConfirm = "Scolia2026!",
        };

    [Fact]
    public void Etape_un_demande_le_nom_et_le_sigle()
    {
        var wizard = NewWizard();
        wizard.SchoolName = "";
        Assert.Equal("Indiquez le nom de l'établissement.", wizard.ValidateStepOne());
        wizard.SchoolName = "Institut de test";
        wizard.ShortName = " ";
        Assert.Equal("Indiquez le sigle de l'établissement.", wizard.ValidateStepOne());
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026-2026")]
    [InlineData("2026-2028")]
    public void Annee_scolaire_couvre_deux_annees_consecutives(string year)
    {
        var wizard = NewWizard();
        wizard.YearLabel = year;
        Assert.NotNull(wizard.ValidateStepOne());
    }

    [Fact]
    public void Etape_un_accepte_une_annee_valide()
    {
        Assert.Null(NewWizard().ValidateStepOne());
    }

    [Fact]
    public void Administrateur_exige_un_mot_de_passe_fort_et_confirme()
    {
        var wizard = NewWizard();
        wizard.AdminPassword = "motdepasse";
        wizard.AdminConfirm = "motdepasse";
        Assert.Contains("lettres et des chiffres", wizard.ValidateStepTwo());
        wizard.AdminPassword = "Scolia2026!";
        wizard.AdminConfirm = "autre";
        Assert.Contains("ne correspondent pas", wizard.ValidateStepTwo());
    }

    [Fact]
    public void Deux_comptes_ne_peuvent_pas_partager_un_identifiant()
    {
        var wizard = NewWizard();
        wizard.UseComptabilite = false;
        wizard.SecretariatUserName = "admin";
        wizard.SecretariatPassword = "Secretariat1";
        wizard.SecretariatConfirm = "Secretariat1";
        Assert.Equal("Deux comptes ne peuvent pas porter le même identifiant.", wizard.ValidateStepTwo());
    }

    [Fact]
    public void Un_compte_decoche_n_est_pas_verifie()
    {
        var wizard = NewWizard();
        wizard.UseSecretariat = false;
        wizard.SecretariatUserName = "";
        wizard.UseComptabilite = false;
        wizard.ComptabiliteUserName = "";
        Assert.Null(wizard.ValidateStepTwo());
    }
}

/// <summary>Promotions saisies dans l'assistant : mêmes règles que la version 79.</summary>
public class SetupPromotionTests
{
    private static SetupViewModel NewWizard() =>
        new(new ShellViewModel(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences()));

    [Fact]
    public void Une_ligne_vide_est_ignoree()
    {
        var wizard = NewWizard();
        Assert.Single(wizard.Promotions);
        Assert.Null(wizard.ValidatePromotions());
    }

    [Fact]
    public void Le_code_compte_quatre_caracteres_uniques()
    {
        var wizard = NewWizard();
        var first = wizard.Promotions[0];
        first.Code = "inf1";
        first.Name = "Première année";
        first.Fee = "55000";
        Assert.Equal("INF1", first.Code);
        Assert.Null(wizard.ValidatePromotions());

        first.Code = "INFI1";
        Assert.Contains("4 caractères", wizard.ValidatePromotions(), StringComparison.Ordinal);
        first.Code = "INF1";

        wizard.AddPromotion.Execute(null);
        var second = wizard.Promotions[1];
        second.Code = "INF1";
        second.Name = "Autre";
        second.Fee = "50000";
        Assert.Contains("déjà utilisé", wizard.ValidatePromotions(), StringComparison.Ordinal);
        second.Code = "ACC1";
        second.Fee = "abc";
        Assert.Contains("mensualité", wizard.ValidatePromotions(), StringComparison.Ordinal);
        second.Remove.Execute(null);
        Assert.Single(wizard.Promotions);
        Assert.Null(wizard.ValidatePromotions());
    }
}
