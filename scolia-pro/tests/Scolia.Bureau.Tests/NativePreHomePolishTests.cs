using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// La connexion, le jumelage et le compte enseignant utilisent la même densité
/// native SCOLIA. Le jumelage Android reprend le panneau cobalt natif.
/// </summary>
public sealed class NativePreHomePolishTests
{
    private static XDocument View(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        return XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", path));
    }

    private static XElement Style(string path, string selector) =>
        Assert.Single(View(path).Descendants(), x =>
            x.Name.LocalName == "Style" &&
            (string?)x.Attribute("Selector") == selector);

    private static string? Setter(XElement style, string property) =>
        style.Elements().FirstOrDefault(x =>
            x.Name.LocalName == "Setter" &&
            (string?)x.Attribute("Property") == property)?.Attribute("Value")?.Value;

    [Theory]
    [InlineData("Views/ConnectionView.axaml", "StackPanel.iosconnectbody",
        "TextBox.iosconnectfield", "Button.iosconnectprimary")]
    public void Jumelage_desktop_plus_large_sans_perdre_la_taille_tactile(
        string path, string body, string field, string primary)
    {
        Assert.Equal("760", Setter(Style(path, body), "MaxWidth"));
        Assert.Equal("52", Setter(Style(path, field), "MinHeight"));
        Assert.Equal("52", Setter(Style(path, primary), "MinHeight"));
        Assert.Equal("{DynamicResource TextBrush}", Setter(Style(path, field), "Foreground"));
        Assert.Equal("#1846A6", Setter(Style(path, primary), "Foreground"));
    }

    [Fact]
    public void Configuration_initiale_sans_carte_ni_couleur_de_bouton_figee()
    {
        var path = "Views/Phone/SetupView.axaml";
        var card = Style(path, "Border.card");
        Assert.Equal("Transparent", Setter(card, "Background"));
        Assert.Equal("0", Setter(card, "BorderThickness"));
        Assert.Equal("0", Setter(card, "Padding"));
        Assert.Null(Setter(card, "BoxShadow"));
        Assert.Equal("{DynamicResource AccentBrush}",
            Setter(Style(path, "Button.iossetupprimary"), "Background"));
    }

    [Fact]
    public void Enseignant_et_secretariat_partagent_les_memes_sections_et_actions()
    {
        var teacher = View("Views/TeacherHomeView.axaml");
        var home = View("Views/HomeView.axaml");
        Assert.Contains(home.Descendants(), x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "pbiPanel");
        Assert.Contains(home.Descendants(), x =>
            x.Name.LocalName == "ItemsControl" &&
            (string?)x.Attribute("ItemsSource") == "{Binding MoreTools}");
        Assert.Contains(teacher.Descendants(), x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "desktopwelcome");
        Assert.True(teacher.Descendants().Count(x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "desktopgroup") >= 3);
        Assert.True(teacher.Descendants().Count(x =>
            x.Name.LocalName == "Button" && (string?)x.Attribute("Classes") == "heroaction") >= 2);
        Assert.NotNull(Setter(Style("Themes/Controls.axaml", "Border.desktopgroup"), "CornerRadius"));
    }
}
