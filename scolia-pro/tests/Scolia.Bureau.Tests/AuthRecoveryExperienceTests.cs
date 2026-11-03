using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>La récupération ne doit pas ouvrir une session et les formulaires restent prévisibles.</summary>
public class AuthRecoveryExperienceTests
{
    [Fact]
    public void Connexion_exige_des_identifiants_avant_d_activer_la_fleche()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api);
        Assert.False(vm.CanSubmitCredentials);
        vm.UserName = "secretariat";
        Assert.False(vm.CanSubmitCredentials);
        vm.Password = "un-code-de-test";
        Assert.True(vm.CanSubmitCredentials);
        vm.Password = string.Empty;
        Assert.False(vm.CanSubmitCredentials);
    }

    [Fact]
    public void Aide_recuperation_ne_revele_pas_le_code_et_ne_connecte_pas_le_compte()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api)
        {
            UserName = "comptabilite",
            Password = "code-provisoire",
        };
        vm.ToggleRecoveryHelp.Execute(null);
        Assert.True(vm.RecoveryOpen);
        Assert.Empty(vm.Password);
        Assert.False(vm.CanSubmitCredentials);
        Assert.Contains("empreinte", vm.RecoveryInstructions);

        vm.ShowRecoveryHelp("secretariat");
        Assert.Equal("secretariat", vm.UserName);
        Assert.True(vm.RecoveryOpen);
        Assert.Empty(vm.Password);
        vm.Reset();
        Assert.False(vm.RecoveryOpen);
    }

    [Fact]
    public void Recuperation_et_connexion_sont_des_ecrans_exclusifs_avec_retour()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api);
        vm.RememberAccount("secretariat");
        Assert.True(vm.ShowSignInPassword);
        Assert.Equal("Mot de passe oublié ?", vm.RecoveryActionLabel);

        vm.ToggleRecoveryHelp.Execute(null);
        Assert.True(vm.RecoveryOpen);
        Assert.False(vm.ShowSignInPassword);
        Assert.Equal("Revenir à la connexion", vm.RecoveryActionLabel);

        vm.NewPassword = "code-fiable-2026";
        vm.ConfirmPassword = "code-fiable-2026";
        vm.RecoveryCode = "SC-CODE-DE-TEST";
        Assert.True(vm.CanRecoverWithCode);

        vm.ToggleRecoveryHelp.Execute(null);
        Assert.False(vm.RecoveryOpen);
        Assert.True(vm.ShowSignInPassword);
        Assert.Empty(vm.NewPassword);
        Assert.Empty(vm.ConfirmPassword);
        Assert.Empty(vm.RecoveryCode);
        Assert.False(vm.CanRecoverWithCode);
    }

    [Fact]
    public void Restauration_controle_la_qualite_et_la_confirmation_avant_d_activer_le_bouton()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api);
        vm.RememberAccount("secretariat");
        vm.ToggleRecoveryHelp.Execute(null);
        vm.RecoveryCode = "SC-EXEMPLE";
        vm.NewPassword = "123456";
        vm.ConfirmPassword = "123456";
        Assert.False(vm.CanRecoverWithCode);
        Assert.Contains("facile", vm.RecoveryPasswordHint);
        vm.NewPassword = "bon-mot-de-passe-2026";
        Assert.False(vm.CanRecoverWithCode);
        Assert.Contains("correspondent pas", vm.RecoveryConfirmationHint);
        vm.ConfirmPassword = vm.NewPassword;
        Assert.True(vm.CanRecoverWithCode);
        Assert.Equal("Les deux mots de passe correspondent.", vm.RecoveryConfirmationHint);
        Assert.False(vm.CanRecoverWithBiometrics);
    }

    [Fact]
    public void Trois_champs_de_recuperation_ont_des_yeux_independants_masques_initialement()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api);
        vm.ToggleRecoveryHelp.Execute(null);
        Assert.False(vm.ShowNewPassword);
        Assert.False(vm.ShowConfirmPassword);
        Assert.False(vm.ShowRecoveryCode);
        vm.ToggleNewPasswordEye.Execute(null);
        Assert.True(vm.ShowNewPassword);
        Assert.False(vm.ShowConfirmPassword);
        Assert.False(vm.ShowRecoveryCode);
        vm.ToggleConfirmPasswordEye.Execute(null);
        vm.ToggleRecoveryCodeEye.Execute(null);
        Assert.True(vm.ShowConfirmPassword);
        Assert.True(vm.ShowRecoveryCode);
        vm.ToggleRecoveryHelp.Execute(null);
        Assert.False(vm.ShowNewPassword);
        Assert.False(vm.ShowConfirmPassword);
        Assert.False(vm.ShowRecoveryCode);
    }

    [Fact]
    public void Mot_de_passe_ouvert_depuis_le_verrou_s_affiche_aussi_sur_un_poste_invite()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var vm = new LoginViewModel(api);
        vm.ConfigureClientMode(true);
        Assert.False(vm.ShowManualCredentials);
        vm.ShowRecoveryHelp("secretariat");
        Assert.True(vm.ShowManualCredentials);
        Assert.True(vm.RecoveryOpen);
        Assert.Equal("secretariat", vm.UserName);
    }

    [Fact]
    public void Champs_secrets_sont_masques_par_defaut_et_revelables_a_la_demande()
    {
        var field = new FormField("motDePasse", "Nouveau code", FieldKind.Secret, true, null);
        Assert.False(field.ShowSecret);
        field.ToggleSecret.Execute(null);
        Assert.True(field.ShowSecret);
        field.ToggleSecret.Execute(null);
        Assert.False(field.ShowSecret);
    }

    [Fact]
    public void Code_provisoire_rejette_les_codes_triviaux()
    {
        Assert.NotNull(AccessCode.Problem("123456"));
        Assert.NotNull(AccessCode.Problem("111111"));
        Assert.Null(AccessCode.Problem("nouvellesecret-2026"));
    }
}
