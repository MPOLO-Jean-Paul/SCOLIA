using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Non-régression visuelle : les transitions restent courtes et ne changent
/// ni les positions des commandes ni les parcours d'authentification.
/// </summary>
public sealed class PremiumMotionViewsTests
{
    private static XDocument Load(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var project = Path.Combine(dir?.FullName
            ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau");
        return XDocument.Load(Path.Combine(project, relative));
    }

    private static XElement Style(string file, string selector) =>
        Assert.Single(Load(file).Descendants().Where(node =>
            node.Name.LocalName == "Style" &&
            node.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Selector" &&
                attribute.Value == selector)));

    private static XElement Transition(XElement style, string property) =>
        Assert.Single(style.Descendants().Where(node =>
            node.Name.LocalName.EndsWith("Transition", StringComparison.Ordinal) &&
            node.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Property" &&
                attribute.Value == property)));

    [Fact]
    public void Les_boutons_et_champs_communs_animent_couleurs_sans_recalcul_de_taille()
    {
        var theme = "Themes/Controls.axaml";
        var button = Style(theme, "Button");
        var background = Transition(button, "Background");
        Assert.Equal("0:0:0.14", background.Attribute("Duration")?.Value);
        Assert.DoesNotContain(button.Descendants(), n =>
            n.Name.LocalName.EndsWith("Transition", StringComparison.Ordinal) &&
            n.Attribute("Property")?.Value is "Width" or "Height" or "Margin");
        var field = Transition(Style(theme, "TextBox"), "BorderBrush");
        Assert.Equal("0:0:0.12", field.Attribute("Duration")?.Value);
    }

    [Fact]
    public void La_barre_mobile_anime_sa_selection_sans_deplacer_les_onglets()
    {
        var phone = "Themes/Phone.axaml";
        Assert.Equal("0:0:0.16",
            Transition(Style(phone, "Border.tabicon"), "Background")
                .Attribute("Duration")?.Value);
        Assert.Equal("0:0:0.16",
            Transition(Style(phone, "Button.phonetab"), "Foreground")
                .Attribute("Duration")?.Value);
    }

    [Theory]
    [InlineData("Views/LoginView.axaml", "Button.iosauthprimary:pressed",
        "TextBox.iosauthfield:focus")]
    [InlineData("Views/ConnectionView.axaml", "Button.iosconnectprimary:pressed",
        "TextBox.iosconnectfield:focus")]
    [InlineData("Views/Phone/ConnectionCompactView.axaml", "Button.connectmain:pressed",
        "TextBox.connectfield:focus")]
    public void La_connexion_donne_un_retour_tactile_et_un_focus_identifiable(
        string file, string primary, string textField)
    {
        var button = Style(file, primary);
        Assert.Contains(button.Descendants(), x =>
            x.Name.LocalName == "Setter" &&
            x.Attribute("Property")?.Value == "Background");
        var field = Style(file, textField);
        Assert.Contains(field.Descendants(), x =>
            x.Name.LocalName == "Setter" &&
            x.Attribute("Property")?.Value == "BorderBrush" &&
            x.Attribute("Value")?.Value == "#1769E6");
    }
}
