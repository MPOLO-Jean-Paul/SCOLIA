using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>En l'absence d'accueil, les formulaires se fondent dans l'écran natif
/// et l'espace enseignant partage les composants de l'administration.</summary>
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
    [InlineData("Views/LoginView.axaml", "Border.iosauthcard")]
    [InlineData("Views/LockScreenView.axaml", "Border.ioslockcard")]
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
