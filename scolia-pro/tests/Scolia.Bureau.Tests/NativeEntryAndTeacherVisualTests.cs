using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Écrans natifs sobres : la connexion adopte un panneau cobalt de référence,
/// tandis que les autres méthodes conservent leur disposition compacte.</summary>
public sealed class NativeEntryAndTeacherVisualTests
{
    private static XDocument Load(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        return XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", relative));
    }

    private static XElement Style(string view, string selector) =>
        Assert.Single(Load(view).Descendants(), x =>
            x.Name.LocalName == "Style" && (string?)x.Attribute("Selector") == selector);

    [Theory]
    [InlineData("Views/ConnectionView.axaml", "Border.iosconnectcard")]
    [InlineData("Views/Phone/ConnectionCompactView.axaml", "Border.connectcard")]
    public void Avant_accueil_aucune_carte_de_connexion_avec_fond_ombre_ou_bordure(
        string view, string selector)
    {
        var style = Style(view, selector);
        var setters = style.Elements().Where(x => x.Name.LocalName == "Setter").ToArray();
        Assert.Contains(setters, x =>
            (string?)x.Attribute("Property") == "Background" &&
            (string?)x.Attribute("Value") == "Transparent");
        Assert.Contains(setters, x =>
            (string?)x.Attribute("Property") == "BorderThickness" &&
            (string?)x.Attribute("Value") == "0");
        Assert.Contains(setters, x =>
            (string?)x.Attribute("Property") == "Padding" &&
            (string?)x.Attribute("Value") == "0");
        Assert.DoesNotContain(setters, x => (string?)x.Attribute("Property") == "BoxShadow");
    }

    [Fact]
    public void Nouvelle_connexion_emploie_un_panneau_bleu_et_des_icones_accessibles()
    {
        var view = Load("Views/LoginView.axaml");
        var panel = Style("Views/LoginView.axaml", "Border.iosauthcard");
        Assert.Contains(panel.Elements(), x => x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == "Background" &&
            (string?)x.Attribute("Value") == "#2354C8");
        var background = Assert.Single(view.Descendants(), x =>
            x.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosBackdrop"));
        Assert.Contains(background.Descendants(), x => x.Name.LocalName == "LinearGradientBrush");
        foreach (var command in new[] { "ChangeAccount", "ForgetSelectedAccount",
            "ToggleRecoveryHelp", "ChangeServer", "SignInWithBiometrics" })
            Assert.Contains(view.Descendants(), x => x.Name.LocalName == "Button" &&
                (string?)x.Attribute("Command") == "{Binding " + command + "}" &&
                (string?)x.Attribute("AutomationProperties.Name") is { Length: > 0 });
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "TextBox" &&
            x.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosUserNameBox"));
    }

    [Fact]
    public void Session_verrouillee_reprend_la_meme_palette_et_les_actions_par_icones()
    {
        var view = Load("Views/LockScreenView.axaml");
        var panel = Style("Views/LockScreenView.axaml", "Border.ioslockcard");
        Assert.Contains(panel.Elements(), x => x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == "Background" &&
            (string?)x.Attribute("Value") == "#2354C8");
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "ScrollViewer" &&
            x.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosLockScroll") &&
            x.Descendants().Any(y => y.Name.LocalName == "LinearGradientBrush"));
        foreach (var command in new[] { "ForgotCode", "SwitchAccount" })
            Assert.Contains(view.Descendants(), x => x.Name.LocalName == "Button" &&
                (string?)x.Attribute("Command") == "{Binding " + command + "}" &&
                (string?)x.Attribute("AutomationProperties.Name") is { Length: > 0 });
    }

    [Theory]
    [InlineData("Views/LoginView.axaml", "TextBox.iosauthfield", "Button.iosauthprimary")]
    [InlineData("Views/LockScreenView.axaml", "TextBox.ioslockfield", "Button.ioslockprimary")]
    [InlineData("Views/ConnectionView.axaml", "TextBox.iosconnectfield", "Button.iosconnectprimary")]
    [InlineData("Views/Phone/ConnectionCompactView.axaml", "TextBox.connectfield", "Button.connectmain")]
    public void Les_ecrans_avant_accueil_animent_uniquement_les_pinceaux_sans_bousculer_la_saisie(
        string view, string fieldSelector, string buttonSelector)
    {
        var field = Style(view, fieldSelector);
        var focus = Style(view, fieldSelector + ":focus");
        var button = Style(view, buttonSelector);
        static bool HasBrushTransition(XElement style, string property) =>
            style.Descendants().Any(x => x.Name.LocalName == "BrushTransition"
                && (string?)x.Attribute("Property") == property);
        Assert.True(HasBrushTransition(field, "BorderBrush"));
        Assert.True(HasBrushTransition(button, "Background"));
        Assert.Contains(focus.Elements(), x => x.Name.LocalName == "Setter"
            && (string?)x.Attribute("Property") == "BorderThickness"
            && (string?)x.Attribute("Value") == "1");
    }

    [Fact]
    public void Configuration_mobile_preserve_ses_etapes_et_ajoute_une_reponse_visuelle_sobre()
    {
        var doc = Load("Views/Phone/SetupView.axaml");
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "Style"
            && (string?)x.Attribute("Selector") == "Button.iossetupprimary"
            && x.Descendants().Any(t => t.Name.LocalName == "BrushTransition"
                && (string?)t.Attribute("Property") == "Background"));
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "Grid"
            && x.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "SetupRoot"));
    }

    [Theory]
    [InlineData("Views/LoginView.axaml")]
    [InlineData("Views/LockScreenView.axaml")]
    [InlineData("Views/ConnectionView.axaml")]
    public void Le_parcours_de_connexion_respecte_le_theme_systeme(string view)
    {
        var doc = Load(view);
        Assert.DoesNotContain(doc.Descendants(), x =>
            x.Name.LocalName == "ThemeVariantScope" &&
            (string?)x.Attribute("RequestedThemeVariant") == "Light");
    }

    [Fact]
    public void L_enseignant_partage_l_accueil_et_les_actions_du_secretariat()
    {
        var teacher = Load("Views/TeacherHomeView.axaml");
        var admin = Load("Views/HomeView.axaml");
        Assert.Contains(admin.Descendants(), x =>
            x.Name.LocalName == "Border" &&
            ((string?)x.Attribute("Classes"))?.Split(' ').Contains("desktopwelcome") == true);
        Assert.Contains(teacher.Descendants(), x =>
            x.Name.LocalName == "Border" &&
            ((string?)x.Attribute("Classes"))?.Split(' ').Contains("desktopwelcome") == true);
        Assert.True(teacher.Descendants().Count(x =>
            x.Name.LocalName == "Button" &&
            ((string?)x.Attribute("Classes"))?.Split(' ').Contains("heroaction") == true) >= 2);
        Assert.Contains(teacher.Descendants(), x => (string?)x.Attribute("Command") == "{Binding EncodeByCode}");
        Assert.Contains(teacher.Descendants(), x => (string?)x.Attribute("Command") == "{Binding ChooseCode}");
    }

    [Fact]
    public void Les_comptes_enseignants_utilisent_les_champs_natifs_Scolia()
    {
        var accounts = Load("Views/TeacherAccountsView.axaml");
        Assert.Contains(accounts.Descendants(), x =>
            x.Name.LocalName == "TextBox" &&
            ((string?)x.Attribute("Classes"))?.Split(' ').Contains("w11") == true &&
            x.Attribute("PlaceholderText") is not null);
    }
}
