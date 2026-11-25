using System.Text.RegularExpressions;
using System.Xml.Linq;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Pages d'accès (connexion, session protégée, récupération, premier serveur) au modèle sobre des comptes
/// Microsoft et Google : fond clair du thème, champs blancs à filet fin, un seul bouton bleu plein, liens bleus,
/// peu de texte. Les couleurs suivent le thème (aucune teinte cobalt figée) et les commandes de sécurité restent.
/// </summary>
public sealed class ProfessionalAccessPagesTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau", relative);
    }

    private static XElement[] Elements(string path) => XDocument.Load(Source(path)).Descendants().ToArray();

    private static string? Attr(XElement node, string key) =>
        node.Attributes().FirstOrDefault(a => a.Name.LocalName == key)?.Value;

    private static XElement Named(XElement[] nodes, string name) =>
        Assert.Single(nodes, e => Attr(e, "Name") == name);

    private static XElement StyleOf(XElement[] nodes, string selector) =>
        Assert.Single(nodes, e => e.Name.LocalName == "Style" && Attr(e, "Selector") == selector);

    private static string? Setting(XElement style, string property) =>
        style.Elements().FirstOrDefault(s => s.Name.LocalName == "Setter" && Attr(s, "Property") == property)
            ?.Attribute("Value")?.Value;

    /// <summary>Partie des pages actuellement rendue (le style commun au téléphone et à l'ordinateur).</summary>
    private static XElement[] Rendered(string view, string root) =>
        Named(Elements("Views/" + view), root).DescendantsAndSelf().ToArray();

    [Theory]
    [InlineData("LoginView.axaml", "IosLoginThemeScope")]
    [InlineData("LockScreenView.axaml", "IosLockScroll")]
    [InlineData("ConnectionView.axaml", "IosConnectionThemeScope")]
    public void Les_pages_d_acces_n_ont_plus_de_couleur_cobalt_figee(string view, string root)
    {
        var xml = string.Concat(Rendered(view, root).Select(e => e.ToString(SaveOptions.DisableFormatting)));
        // Plus de fond dégradé bleu, de champ #1845B1 ni de texte blanc imposé : seul le thème décide.
        foreach (var color in new[] { "#1845B1", "#5682EA", "#315DC6", "#E4EEFF", "#E1EAFF" })
            Assert.DoesNotContain(color, xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LinearGradientBrush", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_style_commun_suit_le_theme_et_place_une_carte_sur_grand_ecran()
    {
        var nodes = Elements("Themes/Auth.axaml");
        var field = StyleOf(nodes, "TextBox.authfield");
        Assert.Equal("{DynamicResource FieldBrush}", Setting(field, "Background"));
        Assert.Equal("{DynamicResource TextBrush}", Setting(field, "Foreground"));
        Assert.Equal("{DynamicResource FieldLineBrush}", Setting(field, "BorderBrush"));
        Assert.Equal("{DynamicResource AccentBrush}", Setting(field, "CaretBrush"));
        Assert.Equal("48", Setting(field, "MinHeight"));
        // Focus bleu : le filet devient l'accent, la hauteur ne change pas.
        var focus = StyleOf(nodes, "TextBox.authfield:focus /template/ Border#PART_BorderElement");
        Assert.Equal("{DynamicResource AccentBrush}", Setting(focus, "BorderBrush"));

        var primary = StyleOf(nodes, "Button.authprimary");
        Assert.Equal("{DynamicResource AccentBrush}", Setting(primary, "Background"));
        Assert.Equal("{DynamicResource OnAccentBrush}", Setting(primary, "Foreground"));
        // Désactivé : surface neutre et texte atténué, jamais blanc sur bleu pâle illisible.
        var disabled = StyleOf(nodes, "Button.authprimary:disabled /template/ ContentPresenter");
        Assert.Equal("{DynamicResource NeutralSoftBrush}", Setting(disabled, "Background"));
        Assert.Equal("{DynamicResource MutedBrush}", Setting(disabled, "Foreground"));
        Assert.Equal("{DynamicResource AccentBrush}", Setting(StyleOf(nodes, "Button.authlink"), "Foreground"));

        // Téléphone : aucun cadre ; grand écran : carte blanche à filet fin.
        Assert.Equal("Transparent", Setting(StyleOf(nodes, "Border.authcard"), "Background"));
        var wide = StyleOf(nodes, "ScrollViewer.wide Border.authcard");
        Assert.Equal("{DynamicResource SurfaceBrush}", Setting(wide, "Background"));
        Assert.Equal("{DynamicResource LineBrush}", Setting(wide, "BorderBrush"));
        Assert.Equal("440", Setting(StyleOf(nodes, "StackPanel.authbody"), "MaxWidth"));

        // Petites hauteurs (clavier ouvert) : l'ornement disparaît, les champs restent tactiles.
        Assert.Equal("False", Setting(StyleOf(nodes, "ScrollViewer.micro Border.authlogo"), "IsVisible"));
        Assert.Equal("False", Setting(StyleOf(nodes, "ScrollViewer.micro Grid.authavatar"), "IsVisible"));
        Assert.Equal("44", Setting(StyleOf(nodes, "ScrollViewer.micro TextBox.authfield"), "MinHeight"));

        var app = File.ReadAllText(Source("App.axaml"));
        Assert.Contains("avares://SCOLIA/Themes/Auth.axaml", app, StringComparison.Ordinal);
    }

    [Fact]
    public void La_connexion_est_courte_et_garde_ses_commandes()
    {
        var nodes = Rendered("LoginView.axaml", "IosLoginThemeScope");
        foreach (var name in new[] { "IosLoginScroll", "IosLoginCard", "IosSchoolHeading", "IosAccountAvatar",
                     "IosUserNameBox", "IosPasswordBox", "IosLoginSubmitButton", "IosRecoveryBiometricRow",
                     "IosForgotPasswordLink", "IosFingerprintSignIn", "IosNewPasswordBox", "IosConfirmPasswordBox",
                     "IosRecoveryCodeBox", "IosMobileActions", "IosLoginMore", "IosLoginMoreContent" })
            Named(nodes, name);
        foreach (var box in new[] { "IosPasswordBox", "IosNewPasswordBox", "IosConfirmPasswordBox", "IosRecoveryCodeBox" })
        {
            var field = Named(nodes, box);
            Assert.Equal("•", Attr(field, "PasswordChar"));
            Assert.Contains(field.Attributes(), a => a.Name.LocalName.EndsWith("IsSensitive", StringComparison.Ordinal) && a.Value == "True");
            Assert.Contains("authfield", Attr(field, "Classes"), StringComparison.Ordinal);
        }
        // Un titre court, aucun paragraphe d'introduction, aucun libellé en capitales.
        Assert.Contains(nodes, e => Attr(e, "Text") == "{Binding PageTitle}");
        Assert.DoesNotContain(nodes, e => Attr(e, "Text") is { Length: > 3 } t && !t.StartsWith('{') && t == t.ToUpperInvariant() && t.Any(char.IsLetter));
        // Pas de « Revenir à la session » empilé sur la récupération ; le retour est un lien sous le formulaire.
        var resume = Assert.Single(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Command") == "{Binding Resume}");
        Assert.Equal("{Binding !RecoveryOpen}", Attr(resume.Parent!, "IsVisible"));
        Assert.DoesNotContain(nodes, e => Attr(e, "Name") == "IosRecoveryAvatar");
        foreach (var command in new[] { "{Binding Submit}", "{Binding ToggleRecoveryHelp}", "{Binding SignInWithBiometrics}",
                     "{Binding RecoverWithCode}", "{Binding RecoverWithBiometrics}", "{Binding ChangeAccount}",
                     "{Binding ChangeServer}", "{Binding ForgetSelectedAccount}", "{Binding ForgetLinkedServer}",
                     "{Binding DisableBiometrics}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Command") == command);
        // Les options rares sont repliées : un seul lien direct sous le formulaire.
        var more = Named(nodes, "IosLoginMoreContent");
        Assert.Equal("{Binding IsChecked, ElementName=IosLoginMore}", Attr(more, "IsVisible"));
        Assert.Contains(more.Descendants(), e => Attr(e, "Command") == "{Binding ChangeServer}");

        var code = File.ReadAllText(Source("Views/LoginView.axaml.cs"));
        Assert.Contains("AuthLayout.Track(IosLoginScroll)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void La_session_protegee_montre_le_compte_puis_le_champ()
    {
        var nodes = Rendered("LockScreenView.axaml", "IosLockScroll");
        var code = Named(nodes, "IosLockCode");
        Assert.Equal("•", Attr(code, "PasswordChar"));
        Assert.Contains("authfield", Attr(code, "Classes"), StringComparison.Ordinal);
        Assert.Contains(nodes, e => e.Name.LocalName == "Grid" && Attr(e, "Classes") == "authavatar");
        Assert.Contains(nodes, e => Attr(e, "Classes") == "badge locked");
        foreach (var command in new[] { "{Binding Unlock}", "{Binding UnlockWithBiometrics}", "{Binding UnlockWithDevice}",
                     "{Binding ForgotCode}", "{Binding SwitchAccount}", "{Binding ToggleCode}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Command") == command);
        // Ni grand titre « Session protégée » ni horloge : l'avatar et le nom suffisent ; la marque est en bas.
        Assert.DoesNotContain(nodes, e => Attr(e, "Text") == "Session protégée");
        Assert.DoesNotContain(nodes, e => Attr(e, "Text") == "{Binding Time}");
        Assert.Contains(nodes, e => Attr(e, "Classes") == "authbrand");
    }

    [Fact]
    public void Le_premier_ecran_met_le_QR_en_avant_et_replie_le_reste()
    {
        var nodes = Rendered("ConnectionView.axaml", "IosConnectionThemeScope");
        var primary = Assert.Single(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Classes") == "authprimary");
        Assert.Equal("{Binding ScanCode}", Attr(primary, "Command"));
        foreach (var name in new[] { "IosQrOptions", "IosNearbyServers", "IosWifiOptions" })
            Assert.Equal("False", Attr(Named(nodes, name), "IsExpanded"));
        foreach (var command in new[] { "{Binding ImportQrImage}", "{Binding JoinInternet}", "{Binding Search}",
                     "{Binding ConnectAddress}", "{Binding UseThisComputer}", "{Binding Back}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Command") == command);
    }

    [Fact]
    public void Le_jumelage_Android_suit_le_theme_clair()
    {
        var nodes = Elements("Views/Phone/ConnectionCompactView.axaml");
        var xml = File.ReadAllText(Source("Views/Phone/ConnectionCompactView.axaml"));
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), xml);
        Assert.Equal("{DynamicResource BgBrush}", Attr(Named(nodes, "CompactRoot"), "Background"));
        var field = StyleOf(nodes, "TextBox.connectfield");
        Assert.Equal("{DynamicResource FieldBrush}", Setting(field, "Background"));
        Assert.Equal("{DynamicResource TextBrush}", Setting(field, "Foreground"));
        Assert.Equal("{DynamicResource AccentBrush}", Setting(StyleOf(nodes, "Button.connectmain"), "Background"));
        // Les commandes de jumelage restent toutes présentes, sur des pages courtes sans défilement.
        Assert.DoesNotContain(nodes, e => e.Name.LocalName == "ScrollViewer");
        foreach (var command in new[] { "{Binding ScanCode}", "{Binding ImportQrImage}", "{Binding JoinInternet}",
                     "{Binding Search}", "{Binding ConnectAddress}", "{Binding UseThisComputer}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Attr(e, "Command") == command);
    }

    [Fact]
    public void La_recuperation_tient_en_une_ligne_et_change_de_titre()
    {
        using var api = new Scolia.Bureau.Api.ScoliaClient(new Uri("http://localhost:5080/"));
        var login = new LoginViewModel(api);
        Assert.Equal("Connexion", login.PageTitle);
        Assert.DoesNotContain(". ", login.RecoveryInstructions, StringComparison.Ordinal);
        Assert.Contains("empreinte", login.RecoveryInstructions, StringComparison.Ordinal);
        Assert.True(login.RecoveryInstructions.Length < 80);
        login.ToggleRecoveryHelp.Execute(null);
        Assert.True(login.RecoveryOpen);
        Assert.Equal("Nouveau mot de passe", login.PageTitle);
    }

    [Fact]
    public void Le_telephone_navigue_sans_attendre_les_animations()
    {
        // Une seule page animée, 150 ms : la nouvelle page répond au toucher aussitôt.
        var frame = File.ReadAllText(Source("Views/PhoneFrame.axaml.cs"));
        Assert.Contains("lightweight: true", frame, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(150 * shell.Prefs.SpeedFactor, 110, 220)", frame, StringComparison.Ordinal);
        var desktop = File.ReadAllText(Source("Views/MainView.axaml.cs"));
        Assert.Contains("160 * prefs.SpeedFactor, 110, 240", desktop, StringComparison.Ordinal);
        Assert.Contains("8, lightweight: true", desktop, StringComparison.Ordinal);
        Assert.Contains("if (resizeQueued) return;", desktop, StringComparison.Ordinal);
        var startPhone = File.ReadAllText(Source("Views/Phone/StartView.axaml"));
        Assert.DoesNotContain("<ScrollViewer", startPhone, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenHome}\"", startPhone, StringComparison.Ordinal);
        var axis = File.ReadAllText(Source("Infrastructure/SharedAxisTransition.cs"));
        Assert.Contains("if (from is not null) from.IsVisible = false;", axis, StringComparison.Ordinal);
        // Pas d'apparition en cascade ni d'ombre floue sur la carte d'accueil du téléphone.
        var reveal = File.ReadAllText(Source("Infrastructure/Reveal.cs"));
        Assert.Contains("if (Platform.IsMobile || PhoneLayout.Active) return;", reveal, StringComparison.Ordinal);
        var home = File.ReadAllText(Source("Views/Phone/HomeView.axaml"));
        Assert.DoesNotContain("BoxShadow", home, StringComparison.Ordinal);
        // L'accueil Fluent privilégie les pictogrammes légers et les commandes réelles.
        Assert.Contains("Classes=\"workrow\"", home, StringComparison.Ordinal);
        Assert.Contains("Kind=\"{Binding Icon}\"", home, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding QuickActions}\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Classes=\"rowillustration\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("featureavatar mini\"><Ellipse Classes=\"halo\" Fill=\"{Binding Brush}", home, StringComparison.Ordinal);
        // La vue d'un écran n'est cherchée par réflexion qu'une fois.
        Assert.Contains("ConcurrentDictionary", File.ReadAllText(Source("Views/ViewLocator.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Demarrer_windows_separe_les_commandes_du_contenu_visuel()
    {
        var shell = File.ReadAllText(Source("ViewModels/Shell.cs"));
        Assert.Contains("public bool IsStartPage => current is StartPage;", shell, StringComparison.Ordinal);
        Assert.Contains("Raise(nameof(StartNavigation));", shell, StringComparison.Ordinal);
        Assert.Contains("IsStartPage && !Phone", shell, StringComparison.Ordinal);
        var frame = File.ReadAllText(Source("Views/MainView.axaml"));
        Assert.Contains("StartNavigation.OpenHome", frame, StringComparison.Ordinal);
        Assert.Contains("StartNavigation.NewActions", frame, StringComparison.Ordinal);
        Assert.Contains("LayoutTransformControl.anim Border#StartHero", frame, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !IsStartPage}\"", frame, StringComparison.Ordinal);
        var start = File.ReadAllText(Source("Views/StartView.axaml"));
        Assert.DoesNotContain("<Button", start, StringComparison.Ordinal);
        Assert.Contains("Binding Greeting", start, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartHero\"", start, StringComparison.Ordinal);
    }

    [Fact]
    public void La_typographie_est_homogene_et_pleine()
    {
        // Roboto en vraies graisses fixes : Medium et SemiBold ne retombent plus sur un trait fin sur Android.
        foreach (var weight in new[] { "Regular", "Medium", "SemiBold", "Bold" })
            Assert.True(File.Exists(Source($"Assets/Fonts/Roboto-{weight}.ttf")), weight);
        Assert.False(File.Exists(Source("Assets/Fonts/Roboto-Variable.ttf")));
        var nodes = Elements("Themes/Controls.axaml");
        // Graisse du texte courant : Medium sur le téléphone, Regular sur l'ordinateur comme dans Windows 11.
        Assert.Equal("{DynamicResource BodyWeight}", Setting(StyleOf(nodes, "TextBlock"), "FontWeight"));
        Assert.Equal("{DynamicResource BodyWeight}", Setting(StyleOf(nodes, "TemplatedControl"), "FontWeight"));
        var palette = File.ReadAllText(Source("Themes/Palette.axaml"));
        Assert.Contains("<FontWeight x:Key=\"BodyWeight\">Medium</FontWeight>", palette, StringComparison.Ordinal);
        var windows = File.ReadAllText(Source("Themes/Windows11.axaml"));
        Assert.Contains("<FontWeight x:Key=\"BodyWeight\">Normal</FontWeight>", windows, StringComparison.Ordinal);
        Assert.Equal("Normal", Setting(StyleOf(nodes, "TextBlock.muted"), "FontWeight"));
        Assert.Equal("Normal", Setting(StyleOf(nodes, "DataGridCell TextBlock"), "FontWeight"));
        var main = File.ReadAllText(Source("Views/MainView.axaml.cs"));
        Assert.Contains("TextRenderingMode.Antialias", main, StringComparison.Ordinal);
        Assert.Contains("TextHintingMode.Light", main, StringComparison.Ordinal);
    }
}
