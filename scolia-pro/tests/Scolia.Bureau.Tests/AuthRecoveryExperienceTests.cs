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
        Assert.Contains("Réinitialiser le mot de passe", vm.RecoveryInstructions);

        vm.ShowRecoveryHelp("secretariat");
        Assert.Equal("secretariat", vm.UserName);
        Assert.True(vm.RecoveryOpen);
        Assert.Empty(vm.Password);
        vm.Reset();
        Assert.False(vm.RecoveryOpen);
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
