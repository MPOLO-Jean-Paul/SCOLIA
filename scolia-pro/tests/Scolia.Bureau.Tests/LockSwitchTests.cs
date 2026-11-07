using Scolia.Bureau.Api;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Page de connexion ouverte depuis l'écran de veille : elle propose de revenir à la session verrouillée.</summary>
public sealed class LockSwitchTests
{
    [Fact]
    public void La_connexion_propose_le_retour_puis_le_retire()
    {
        using var api = new ScoliaClient(new Uri("http://127.0.0.1:1/"));
        var login = new LoginViewModel(api);
        Assert.False(login.CanResume);
        Assert.Equal("Connectez-vous à votre compte.", login.IntroText);

        login.UserName = "ancien";
        login.OfferResume("Secrétariat", "secretariat");
        Assert.True(login.CanResume);
        Assert.Equal("Revenir à la session de Secrétariat", login.ResumeText);
        Assert.Contains("secretariat", login.ResumeHint);
        Assert.Contains("verrouillée", login.IntroText);
        Assert.Equal("secretariat", login.UserName);
        Assert.True(login.HasRememberedAccount);

        var asked = 0;
        login.ResumeRequested += () => asked++;
        login.Password = "en cours";
        login.Resume.Execute(null);
        Assert.Equal(1, asked);
        Assert.Equal(string.Empty, login.Password);

        login.OfferResume(null, null);
        Assert.False(login.CanResume);
        login.Resume.Execute(null);
        Assert.Equal(1, asked);
    }
}
