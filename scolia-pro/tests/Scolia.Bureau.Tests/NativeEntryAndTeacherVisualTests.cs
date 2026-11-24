using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Écrans natifs sobres : la connexion adopte un panneau cobalt de référence,
/// et le jumelage Android utilise désormais la même palette sans changer la navigation compacte.</summary>
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
    public void Connexion_regroupe_logo_et_avatar_dans_un_embleme_unique()
    {
        var view = Load("Views/LoginView.axaml");
        var emblem = Assert.Single(view.Descendants(), e =>
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "ScoliaAccountEmblem"));
        Assert.Contains(emblem.Descendants(), e =>
            e.Name.LocalName == "Image" && (string?)e.Attribute("Source") == "{DynamicResource ScoliaMark}");
        Assert.Contains(emblem.Descendants(), e =>
            e.Name.LocalName == "TextBlock" && ((string?)e.Attribute("Text"))?.Contains("TextCase.Initials", StringComparison.Ordinal) == true);
        Assert.Single(emblem.Descendants(), e =>
            e.Name.LocalName == "Border" && (string?)e.Attribute("Classes") == "badge");
        var original = Assert.Single(view.Descendants(), e =>
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosAccountAvatar"));
        Assert.DoesNotContain(original.Descendants(), e => e.Name.LocalName == "Grid");
        Assert.Contains(view.Descendants(), e =>
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "StandaloneBrand"));
    }

    [Fact]
    public void L_enseignant_partage_l_accueil_et_les_actions_du_secretariat()
    {
        var teacher = Load("Views/TeacherHomeView.axaml");
        var admin = Load("Views/HomeView.axaml");
        // La nouvelle vue du secrétariat reprend la surface Power BI ; l'enseignant
        // conserve son tableau de démarrage actuel, avec les mêmes droits métier.
        Assert.Contains(admin.Descendants(), x =>
            x.Name.LocalName == "Border" &&
            ((string?)x.Attribute("Classes"))?.Split(' ').Contains("pbiPanel") == true);
        Assert.Contains(admin.Descendants(), x =>
            x.Name.LocalName == "ItemsControl" &&
            (string?)x.Attribute("ItemsSource") == "{Binding QuickActions}");
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
