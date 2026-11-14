using System.Xml.Linq;
using Avalonia.Media;
using Scolia.Bureau.Infrastructure;
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
    public void Champs_login_lisibles_sur_fond_cobalt_en_themes_clair_et_sombre()
    {
        var nodes = Elements("LoginView.axaml");
        var fields = new[] { "IosUserNameBox", "IosPasswordBox", "IosNewPasswordBox",
            "IosConfirmPasswordBox", "IosRecoveryCodeBox" };
        foreach (var name in fields)
        {
            var input = Assert.Single(nodes, e => e.Name.LocalName == "TextBox" && Attribute(e, "Name", name));
            var classes = input.Attribute("Classes")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            Assert.Contains("iosauthfield", classes);
            Assert.Contains("onbrand", classes); // évite les styles métiers fond clair.
            Assert.NotNull(input.Attribute("PlaceholderText"));
            // Contrôler aussi les valeurs LOCALES, prioritaires sur styles Avalonia/Android.
            Assert.Equal("#1845B1", input.Attribute("Background")?.Value);
            Assert.Equal("#FFFFFF", input.Attribute("Foreground")?.Value);
            Assert.Equal("#FFFFFF", input.Attribute("CaretBrush")?.Value);
            Assert.Equal("#E4EEFF", input.Attribute("PlaceholderForeground")?.Value);
            Assert.True(ThemeContrast.Ratio(
                Color.Parse(input.Attribute("Foreground")!.Value),
                Color.Parse(input.Attribute("Background")!.Value)) >= 7);
        }

        var loginStyle = Assert.Single(nodes, e => e.Name.LocalName == "Style" &&
            Attribute(e, "Selector", "TextBox.iosauthfield"));
        static string? Setting(XElement style, string property) =>
            style.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter" &&
                Attribute(e, "Property", property))?.Attribute("Value")?.Value;
        var bg = Color.Parse(Setting(loginStyle, "Background")!);
        var text = Color.Parse(Setting(loginStyle, "Foreground")!);
        var placeholder = Color.Parse(Setting(loginStyle, "PlaceholderForeground")!);
        Assert.True(ThemeContrast.Ratio(text, bg) >= 7); // texte réellement tapé + points du mot de passe
        Assert.True(ThemeContrast.Ratio(placeholder, bg) >= 4.5); // texte indicatif
        Assert.Equal("White", Setting(loginStyle, "CaretBrush"));
        Assert.Equal("White", Setting(loginStyle, "SelectionForegroundBrush"));
        Assert.Contains(nodes, e => e.Name.LocalName == "Style" &&
            Attribute(e, "Selector", "TextBox.iosauthfield /template/ TextBlock#PART_Watermark"));

        // Le bug Android était sur PART_BorderElement, pas sur le TextBox de surface.
        foreach (var state in new[] { "", ":pointerover", ":focus", ":disabled" })
        {
            var selector = "TextBox.iosauthfield" + state + " /template/ Border#PART_BorderElement";
            var border = Assert.Single(nodes, e => e.Name.LocalName == "Style" && Attribute(e, "Selector", selector));
            Assert.Equal("#1845B1", Setting(border, "Background"));
        }

        var lockNodes = Elements("LockScreenView.axaml");
        var lockCode = Assert.Single(lockNodes, e => e.Name.LocalName == "TextBox" && Attribute(e, "Name", "IosLockCode"));
        Assert.Equal("#1845B1", lockCode.Attribute("Background")?.Value);
        Assert.Equal("#FFFFFF", lockCode.Attribute("Foreground")?.Value);
        Assert.Equal("#E4EEFF", lockCode.Attribute("PlaceholderForeground")?.Value);
        var lockClasses = lockCode.Attribute("Classes")?.Value.Split(' ') ?? [];
        Assert.Contains("onbrand", lockClasses);
        foreach (var state in new[] { "", ":pointerover", ":focus", ":disabled" })
        {
            var selector = "TextBox.ioslockfield" + state + " /template/ Border#PART_BorderElement";
            var border = Assert.Single(lockNodes, e => e.Name.LocalName == "Style" && Attribute(e, "Selector", selector));
            Assert.Equal("#1845B1", Setting(border, "Background"));
        }

        var global = XDocument.Load(Source("Themes/Controls.axaml")).Descendants().ToArray();
        Assert.Contains(global, e => e.Name.LocalName == "Style" &&
            (e.Attribute("Selector")?.Value.Contains(":not(.ioslockfield)", StringComparison.Ordinal) ?? false));
        // Ne pas retomber sur les marges minuscules qui cachaient l'identifiant derrière l'icône.
        var tiny = Assert.Single(nodes, e => e.Name.LocalName == "Style" &&
            Attribute(e, "Selector", "ScrollViewer.tiny TextBox.iosauthfield"));
        Assert.Equal("42,10,52,10", Setting(tiny, "Padding"));
    }

    [Fact]
    public void Options_de_connexion_repliees_dans_le_panneau_cobalt_sans_en_tete_Fluent_blanc()
    {
        var nodes = Elements("LoginView.axaml");
        var toggle = Assert.Single(nodes, e => e.Name.LocalName == "ToggleButton" && Attribute(e, "Name", "IosLoginMore"));
        Assert.True(Attribute(toggle, "IsChecked", "False"));
        Assert.Contains(toggle.Ancestors(), e => e.Name.LocalName == "Border" && Attribute(e, "Name", "IosLoginCard"));
        var panel = Assert.Single(nodes, e => e.Name.LocalName == "StackPanel" && Attribute(e, "Name", "IosLoginMoreContent"));
        Assert.True(Attribute(panel, "IsVisible", "{Binding IsChecked, ElementName=IosLoginMore}"));
        Assert.Contains(panel.Descendants(), e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding ChangeServer}"));
        Assert.Contains(panel.Descendants(), e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding ForgetLinkedServer}"));
        Assert.Contains(panel.Descendants(), e => e.Name.LocalName == "Button" &&
            Attribute(e, "Command", "{Binding DisableBiometrics}"));
        Assert.DoesNotContain(nodes, e => e.Name.LocalName == "Expander" && Attribute(e, "Classes", "skyoptions"));
        var style = Assert.Single(nodes, e => e.Name.LocalName == "Style" && Attribute(e, "Selector", "ToggleButton.iosmoretoggle"));
        Assert.Contains(style.Elements(), e => e.Name.LocalName == "Setter" && Attribute(e, "Property", "MinHeight") &&
            Attribute(e, "Value", "42"));
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

    [Fact]
    public void Serveur_du_telephone_la_connexion_n_apparait_qu_une_fois_le_serveur_pret()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln"))) dir = dir.Parent;
        var shell = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Scolia.Bureau", "ViewModels", "Shell.cs"));
        var start = shell.IndexOf("public async Task UseThisComputerAsync()", StringComparison.Ordinal);
        var body = shell[start..shell.IndexOf("public async Task SetNetworkAsync", start, StringComparison.Ordinal)];
        // Le démarrage est suivi sur l'écran de choix ; l'échec y reste affiché avec son détail.
        Assert.True(body.IndexOf("Connection.BeginProgress", StringComparison.Ordinal)
            < body.IndexOf("ChoosingServer = false", StringComparison.Ordinal));
        Assert.Contains("localServer.Diagnostic", body);
        Assert.Contains("ChoosingServer = true", body);
    }
}
