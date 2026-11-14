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
    public void Meme_largeur_de_formulaire_et_hauteur_tactile(
        string path, string body, string field, string primary)
    {
        Assert.Equal("420", Setter(Style(path, body), "MaxWidth"));
        Assert.Equal("52", Setter(Style(path, field), "MinHeight"));
        Assert.Equal("52", Setter(Style(path, primary), "MinHeight"));
        Assert.Equal("{DynamicResource TextBrush}", Setter(Style(path, field), "Foreground"));
        Assert.Equal("{DynamicResource OnAccentBrush}", Setter(Style(path, primary), "Foreground"));
    }

    [Theory]
    [InlineData("Views/LoginView.axaml", "StackPanel.iosloginbody", "TextBox.iosauthfield", "Button.iosauthprimary")]
    [InlineData("Views/LockScreenView.axaml", "StackPanel.ioslockbody", "TextBox.ioslockfield", "Button.ioslockprimary")]
    public void Le_nouveau_panneau_bleu_ciel_conserve_un_formulaire_tactile_et_lisible(
        string path, string body, string field, string primary)
    {
        Assert.Equal("640", Setter(Style(path, body), "MaxWidth"));
        Assert.Equal("50", Setter(Style(path, field), "MinHeight"));
        Assert.Equal("50", Setter(Style(path, primary), "MinHeight"));
        Assert.Equal("White", Setter(Style(path, field), "Foreground"));
        Assert.Equal("#1948AE", Setter(Style(path, primary), "Foreground"));
    }

    [Fact]
    public void Jumelage_android_lisible_en_sombre_et_sans_defilement()
    {
        var path = "Views/Phone/ConnectionCompactView.axaml";
        Assert.Equal("#FFFFFF", Setter(Style(path, "TextBox.connectfield"), "Foreground"));
        Assert.Equal("52", Setter(Style(path, "Button.connectmain"), "MinHeight"));
        Assert.Equal("52", Setter(Style(path, "TextBox.connectfield"), "MinHeight"));
        Assert.DoesNotContain(View(path).Descendants(), x => x.Name.LocalName == "ScrollViewer");
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

    [Theory]
    [InlineData("Views/ConnectionView.axaml", "TextBox.iosconnectfield")]
    [InlineData("Views/Phone/ConnectionCompactView.axaml", "TextBox.connectfield")]
    [InlineData("Views/Phone/SetupView.axaml", "TextBox.w11")]
    public void Champs_avant_accueil_meme_curseur_et_selection_colores(string path, string selector)
    {
        var style = Style(path, selector);
        var mobileCobalt = path.Contains("Phone/ConnectionCompactView", StringComparison.Ordinal);
        Assert.Equal(mobileCobalt ? "#FFFFFF" : "{DynamicResource AccentBrush}", Setter(style, "CaretBrush"));
        Assert.Equal(mobileCobalt ? "#6EA4FF" : "{DynamicResource AccentSoftBrush}", Setter(style, "SelectionBrush"));
    }

    [Theory]
    [InlineData("Views/LoginView.axaml", "TextBox.iosauthfield", "#6EA4FF")]
    [InlineData("Views/LockScreenView.axaml", "TextBox.ioslockfield", "#72A4FF")]
    public void Le_curseur_blanc_reste_visible_sur_les_champs_bleus(
        string path, string selector, string selection)
    {
        var style = Style(path, selector);
        Assert.Equal("White", Setter(style, "CaretBrush"));
        Assert.Equal(selection, Setter(style, "SelectionBrush"));
    }

    [Fact]
    public void Enseignant_et_secretariat_partagent_les_memes_sections_et_actions()
    {
        var teacher = View("Views/TeacherHomeView.axaml");
        var home = View("Views/HomeView.axaml");
        Assert.Contains(home.Descendants(), x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "desktopwelcome");
        Assert.Contains(teacher.Descendants(), x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "desktopwelcome");
        Assert.True(teacher.Descendants().Count(x =>
            x.Name.LocalName == "Border" && (string?)x.Attribute("Classes") == "desktopgroup") >= 3);
        Assert.True(teacher.Descendants().Count(x =>
            x.Name.LocalName == "Button" && (string?)x.Attribute("Classes") == "heroaction") >= 2);
        Assert.NotNull(Setter(Style("Themes/Controls.axaml", "Border.desktopgroup"), "CornerRadius"));
    }
}
