using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Contrôle les défauts constatés sur Android : texte technique dans les boutons,
/// grande carte d'accueil enseignant, onglets inégaux et alerte hors ligne trop longue.
/// </summary>
public sealed class TeacherPhoneAndOfflineUxTests
{
    private static string Source(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(), "src", "Scolia.Bureau", relative);
    }

    private static XDocument View(string relative) => XDocument.Load(Source("Views/" + relative));

    private static bool Attr(XElement element, string attribute, string value) =>
        element.Attributes().Any(a => a.Name.LocalName == attribute && a.Value == value);

    [Fact]
    public void Les_actions_enseignant_affichent_de_vrais_textes_et_pas_un_objet_TextBlock()
    {
        var doc = View("TeacherHomeView.axaml");
        var actions = doc.Descendants().Where(x =>
            x.Name.LocalName == "Button" && Attr(x, "Classes", "heroaction")).ToArray();
        Assert.Equal(2, actions.Length);
        Assert.Contains(actions, x => Attr(x, "Content", "Encoder mes cotes") &&
            Attr(x, "Command", "{Binding EncodeByCode}"));
        Assert.Contains(actions, x => Attr(x, "Content", "Mon code d'accès") &&
            Attr(x, "Command", "{Binding ChooseCode}"));
        Assert.All(actions, x => Assert.DoesNotContain(x.Elements(), child =>
            child.Name.LocalName == "TextBlock"));
    }

    [Fact]
    public void Accueil_enseignant_android_utilise_les_groupes_du_secretariat()
    {
        var doc = View("TeacherHomeView.axaml");
        var root = Assert.Single(doc.Descendants(), x =>
            Attr(x, "Name", "TeacherPhoneSections"));
        Assert.True(Attr(root, "IsVisible", "{Binding Phone}"));
        Assert.True(root.Descendants().Count(x =>
            x.Name.LocalName == "Border" && Attr(x, "Classes", "phonegroup")) >= 3);
        Assert.True(root.Descendants().Count(x =>
            x.Name.LocalName == "Button" && Attr(x, "Classes", "settingsentry")) >= 3);
        Assert.Contains(root.Descendants(), x =>
            x.Name.LocalName == "ItemsControl" && Attr(x, "ItemsSource", "{Binding Stats}"));
        Assert.Contains(root.Descendants(), x =>
            x.Name.LocalName == "ItemsControl" && Attr(x, "ItemsSource", "{Binding Courses}"));
        Assert.Contains(root.Descendants(), x =>
            x.Name.LocalName == "Button" && Attr(x, "Command", "{Binding ChooseCode}"));
        Assert.Contains(root.Descendants(), x =>
            x.Name.LocalName == "Button" && Attr(x, "Command", "{Binding EncodeByCode}"));

        var desktop = Assert.Single(doc.Descendants(), x =>
            x.Name.LocalName == "ScrollViewer");
        Assert.True(Attr(desktop, "IsVisible", "{Binding NotPhone}"));
    }


    [Fact]
    public void Apercu_enseignant_conserve_des_icones_colorees_en_clair_et_en_sombre()
    {
        var source = File.ReadAllText(Source("ViewModels/TeacherSpace.cs"));
        var homeStart = source.IndexOf("public sealed class TeacherHomePage", StringComparison.Ordinal);
        Assert.True(homeStart >= 0);
        var home = source[homeStart..];
        foreach (var tone in new[] { "TonePurple", "ToneBlue", "ToneOrange", "ToneTeal" })
            Assert.Contains($"IconPalette.Get(\"{tone}\")", home, StringComparison.Ordinal);
        var stats = home[home.IndexOf("Stats.Add(new KpiVm", StringComparison.Ordinal)..];
        Assert.DoesNotContain("ThemeBrush.Get(", stats, StringComparison.Ordinal);

        var doc = View("TeacherHomeView.axaml");
        var phone = Assert.Single(doc.Descendants(), x =>
            Attr(x, "Name", "TeacherPhoneSections"));
        var overview = Assert.Single(phone.Descendants(), x =>
            x.Name.LocalName == "ItemsControl" && Attr(x, "ItemsSource", "{Binding Stats}"));
        Assert.Contains(overview.Descendants(), x =>
            x.Name.LocalName == "Border"
            && Attr(x, "Classes", "appicon large teacheroverviewicon")
            && Attr(x, "Background", "{Binding Brush}"));
        Assert.Contains(overview.Descendants(), x =>
            x.Name.LocalName == "MaterialIcon" && Attr(x, "Kind", "{Binding Icon}"));
    }

    [Fact]
    public void Progression_et_regles_enseignant_restent_lisibles_et_accessibles()
    {
        var doc = View("TeacherHomeView.axaml");
        Assert.Contains(doc.Descendants(), x =>
            x.Name.LocalName == "ProgressBar" && Attr(x, "Classes", "slim teacherprogress")
            && Attr(x, "Value", "{Binding Percent, Mode=OneWay}"));
        var rule = Assert.Single(doc.Descendants(), x =>
            x.Name.LocalName == "Expander" && Attr(x, "Classes", "teacherinfo"));
        Assert.True(Attr(rule, "IsExpanded", "False"));
        Assert.True(Attr(rule, "HorizontalAlignment", "Stretch"));
        Assert.Contains(rule.Descendants(), x =>
            x.Name.LocalName == "TextBlock" && Attr(x, "Text", "{Binding Rules}"));
    }

    [Fact]
    public void Bas_ecran_trois_onglets_equilibres_et_menu_avec_indicateur()
    {
        var phone = View("PhoneFrame.axaml");
        var menu = Assert.Single(phone.Descendants(), x => Attr(x, "Name", "MenuTab"));
        Assert.Contains(menu.Descendants(), x =>
            x.Name.LocalName == "Border" && Attr(x, "Classes", "tabicon") &&
            Attr(x, "Classes.selected", "{Binding PhoneMenuSelected}"));
        var source = File.ReadAllText(Source("Views/PhoneFrame.axaml.cs"));
        Assert.Contains("TabGrid.Bounds.Width / count", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ShellViewModel.PhoneTabs)", source, StringComparison.Ordinal);
        var nav = File.ReadAllText(Source("ViewModels/Sidebar.cs"));
        Assert.Contains("\"Mes cours et cotes\" => \"Cours & cotes\"", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void Notification_hors_ligne_tient_sur_une_courte_ligne_et_les_erreurs_restent()
    {
        var source = File.ReadAllText(Source("ViewModels/Shell.cs"));
        Assert.Contains("Hors ligne · Connectez-vous localement", source, StringComparison.Ordinal);
        Assert.DoesNotContain("L'ordinateur principal ne répond pas. Connectez-vous avec le dernier compte", source,
            StringComparison.Ordinal);
        var login = View("LoginView.axaml");
        Assert.Contains(login.Descendants(), x => x.Name.LocalName == "Border" &&
            Attr(x, "IsVisible", "{Binding HasNotice}") &&
            Attr(x, "Padding", "10,7"));
        Assert.Contains(login.Descendants(), x =>
            x.Name.LocalName == "Border" && Attr(x, "IsVisible", "{Binding HasError}"));
    }
}
