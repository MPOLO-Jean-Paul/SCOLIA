using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Le style iOS, commun au téléphone et à l'ordinateur, garde toutes les commandes de sécurité ; l'ancienne page reste en réserve.</summary>
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
        node.Attributes().Any(a => (a.Name.LocalName == key || a.Name.LocalName.EndsWith("." + key, StringComparison.Ordinal)) && a.Value == value);

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
        Assert.Contains("LoginScroll.IsVisible = !ios", code);
        Assert.Contains("IosLoginScroll.IsVisible = ios", code);
        Assert.Contains("AuthLayout.Track(IosLoginScroll)", code);
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
        Assert.Contains("ClassicLockScreen.IsVisible = !ios", code);
        Assert.Contains("IosLockScroll.IsVisible = ios", code);
        Assert.Contains("IosLockCode", code);
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
        Assert.Contains("ClassicConnectionScreen.IsVisible = !ios", code);
        Assert.Contains("IosConnectionScreen.IsVisible = ios", code);
    }

    [Fact]
    public void Le_style_iOS_est_aussi_celui_de_l_ordinateur()
    {
        var layout = File.ReadAllText(Source("Infrastructure/AuthLayout.cs"));
        Assert.Contains("UseIosStyle { get; } = true", layout);
        // L'ordinateur garde la possibilité de devenir serveur depuis le premier écran iOS.
        var nodes = Elements("ConnectionView.axaml");
        Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attribute(e, "Command", "{Binding UseThisComputer}")
            && Attribute(e, "IsVisible", "{Binding DesktopCanBeServer}") && Attribute(e, "Classes", "iosconnectsecondary"));
    }

    [Theory]
    [InlineData("LoginView.axaml")]
    [InlineData("ConnectionView.axaml")]
    [InlineData("LockScreenView.axaml")]
    public void Les_boutons_iOS_replient_leur_texte_au_lieu_de_deborder(string view)
    {
        // Un StackPanel horizontal donne une largeur infinie au texte : il sortirait de l'écran d'un petit téléphone.
        var nodes = Elements(view);
        var ios = nodes.Where(e => e.Name.LocalName == "Button" && e.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value.StartsWith("ios", StringComparison.Ordinal)));
        Assert.NotEmpty(ios);
        foreach (var button in ios)
            Assert.DoesNotContain(button.Elements(), c => c.Name.LocalName == "StackPanel" && Attribute(c, "Orientation", "Horizontal"));
    }
}
