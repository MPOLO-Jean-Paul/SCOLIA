using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le style iOS du téléphone ne doit ni masquer les commandes de sécurité ni remplacer les vues Windows.</summary>
public sealed class MobileIosAuthenticationViewsTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau", relative);
    }

    private static XElement[] Elements(string path) =>
        XDocument.Load(Source("Views/" + path)).Descendants().ToArray();

    private static bool Attribute(XElement node, string key, string value) =>
        node.Attributes().Any(a => a.Name.LocalName == key && a.Value == value);

    [Fact]
    public void La_connexion_iOS_est_distincte_de_Windows_et_exige_le_mot_de_passe()
    {
        var nodes = Elements("LoginView.axaml");
        foreach (var name in new[] { "LoginScroll", "IosLoginScroll", "IosBackdrop",
            "IosPasswordBox", "IosUserNameBox", "IosNewPasswordBox",
            "IosConfirmPasswordBox", "IosRecoveryCodeBox" })
            Assert.Contains(nodes, e => Attribute(e, "Name", name));

        Assert.Contains(nodes, e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding Submit}") &&
            Attribute(e, "IsEnabled", "{Binding CanSubmitCredentials}"));
        Assert.Contains(nodes, e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding ToggleRecoveryHelp}"));
        Assert.Contains(nodes, e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding RecoverWithCode}") &&
            Attribute(e, "IsEnabled", "{Binding CanRecoverWithCode}"));
        Assert.Contains(nodes, e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding RecoverWithBiometrics}") &&
            Attribute(e, "IsEnabled", "{Binding CanRecoverWithBiometrics}"));

        foreach (var name in new[] { "IosPasswordBox", "IosNewPasswordBox",
            "IosConfirmPasswordBox", "IosRecoveryCodeBox" })
        {
            var field = Assert.Single(nodes, e => Attribute(e, "Name", name));
            Assert.True(Attribute(field, "PasswordChar", "•"));
            Assert.True(Attribute(field, "IsSensitive", "True"));
        }
        var code = File.ReadAllText(Source("Views/LoginView.axaml.cs"));
        Assert.Contains("LoginScroll.IsVisible = !mobile", code);
        Assert.Contains("IosLoginScroll.IsVisible = mobile", code);
        Assert.Contains("login.CanSubmitCredentials", code);
    }

    [Fact]
    public void La_reprise_iOS_conserve_le_code_et_la_biometrie()
    {
        var nodes = Elements("LockScreenView.axaml");
        foreach (var name in new[] { "ClassicLockScreen", "IosLockScroll", "LockCode", "IosLockCode" })
            Assert.Contains(nodes, e => Attribute(e, "Name", name));
        var password = Assert.Single(nodes, e => Attribute(e, "Name", "IosLockCode"));
        Assert.True(Attribute(password, "PasswordChar", "•"));
        Assert.True(Attribute(password, "IsSensitive", "True"));
        foreach (var binding in new[] { "{Binding Unlock}", "{Binding UnlockWithBiometrics}",
            "{Binding ForgotCode}", "{Binding SwitchAccount}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attribute(e, "Command", binding));
        var code = File.ReadAllText(Source("Views/LockScreenView.axaml.cs"));
        Assert.Contains("ClassicLockScreen.IsVisible = !mobile", code);
        Assert.Contains("IosLockScroll.IsVisible = mobile", code);
    }

    [Fact]
    public void Premiere_ouverture_iOS_conserve_le_QR_et_la_verification_apres_jumelage()
    {
        var nodes = Elements("ConnectionView.axaml");
        Assert.Contains(nodes, e => Attribute(e, "Name", "ClassicConnectionScreen"));
        Assert.Contains(nodes, e => Attribute(e, "Name", "IosConnectionScreen"));
        foreach (var command in new[] { "{Binding ScanCode}", "{Binding ImportQrImage}",
            "{Binding Search}", "{Binding UseThisComputer}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attribute(e, "Command", command));
        Assert.Contains(nodes, e => e.Name.LocalName == "ScrollViewer" &&
            Attribute(e, "IsVisible", "{Binding PairingInProgress}") &&
            e.Descendants().Any(d => Attribute(d, "Text", "Vérification sécurisée")));
        var code = File.ReadAllText(Source("Views/ConnectionView.axaml.cs"));
        Assert.Contains("ClassicConnectionScreen.IsVisible = !mobile", code);
        Assert.Contains("IosConnectionScreen.IsVisible = mobile", code);
    }
}
