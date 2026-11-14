using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Contrôles anti-régression du parcours mobile compact.</summary>
public sealed class AndroidAuthViewportTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau", relative);
    }

    [Theory]
    [InlineData("LoginView.axaml", "ScrollViewer.micro")]
    [InlineData("ConnectionView.axaml", "Grid.micro")]
    [InlineData("LockScreenView.axaml", "ScrollViewer.micro")]
    [InlineData("Phone/SetupView.axaml", "Grid.micro")]
    public void Les_ecrans_d_acces_possedent_un_layout_pour_les_petites_hauteurs(string path, string selector)
    {
        var nodes = XDocument.Load(Source("Views/" + path)).Descendants().ToArray();
        Assert.Contains(nodes, n => n.Name.LocalName == "Style" &&
            n.Attributes().Any(a => a.Name.LocalName == "Selector"
                && a.Value.Contains(selector, StringComparison.Ordinal)));
        Assert.Contains(nodes, n => n.Name.LocalName == "ScrollViewer" &&
            n.Attributes().Any(a => a.Name.LocalName == "HorizontalScrollBarVisibility" && a.Value == "Disabled"));
    }

    [Fact]
    public void Les_options_de_connexion_ne_disparaissent_pas_en_mode_compact()
    {
        var nodes = XDocument.Load(Source("Views/LoginView.axaml")).Descendants().ToArray();
        // Les actions restent accessibles via l'ouverture repliable intégrée au panneau cobalt.
        Assert.Contains(nodes, n => n.Name.LocalName == "ToggleButton" &&
            n.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosLoginMore") &&
            n.Attributes().Any(a => a.Name.LocalName == "IsChecked" && a.Value == "False"));
        Assert.Contains(nodes, n => n.Name.LocalName == "StackPanel" &&
            n.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "IosLoginMoreContent") &&
            n.Attributes().Any(a => a.Name.LocalName == "IsVisible" &&
                a.Value == "{Binding IsChecked, ElementName=IosLoginMore}") &&
            n.Descendants().Any(d => d.Attributes().Any(a =>
                a.Name.LocalName == "Command" && a.Value == "{Binding ChangeServer}")));
        Assert.Contains(nodes, n => n.Name.LocalName == "Button" &&
            n.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value == "{Binding RecoverWithCode}"));
    }

    [Fact]
    public void Le_jumelage_garde_ses_trois_sections_repliables()
    {
        var nodes = XDocument.Load(Source("Views/ConnectionView.axaml")).Descendants().ToArray();
        foreach (var name in new[] { "IosQrOptions", "IosWifiOptions", "IosNearbyServers" })
            Assert.Contains(nodes, n => n.Name.LocalName == "Expander" &&
                n.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == name) &&
                n.Attributes().Any(a => a.Name.LocalName == "IsExpanded" && a.Value == "False"));
        Assert.Contains("Platform.IsMobile ||", File.ReadAllText(Source("Views/ConnectionView.axaml.cs")));
    }

    [Fact]
    public void Jumelage_compact_reprend_les_couleurs_cobalt_de_la_connexion_et_preserve_les_actions()
    {
        var nodes = XDocument.Load(Source("Views/Phone/ConnectionCompactView.axaml")).Descendants().ToArray();
        static bool Has(XElement e, string name, string value) =>
            e.Attributes().Any(a => a.Name.LocalName == name && a.Value == value);
        static XElement Style(XElement[] xs, string selector) => Assert.Single(xs,
            e => e.Name.LocalName == "Style" && Has(e, "Selector", selector));
        static string? Set(XElement style, string property) => style.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Setter" && Has(e, "Property", property))
            ?.Attribute("Value")?.Value;

        var page = Assert.Single(nodes, e => e.Name.LocalName == "Grid" && Has(e, "Name", "CompactRoot"));
        var gradient = Assert.Single(page.Elements(), e => e.Name.LocalName == "Grid.Background")
            .Elements().Single();
        Assert.Equal("LinearGradientBrush", gradient.Name.LocalName);
        var stops = gradient.Elements().Select(e => e.Attribute("Color")?.Value).ToArray();
        Assert.Contains("#B5CEFF", stops);
        Assert.Contains("#6089DA", stops);

        Assert.Equal("#2354C8", Set(Style(nodes, "Border.connectcard"), "Background"));
        Assert.Equal("#FFFFFF", Set(Style(nodes, "TextBlock.connecttitle"), "Foreground"));
        Assert.Equal("#E4EDFF", Set(Style(nodes, "TextBlock.connecthint"), "Foreground"));
        Assert.Equal("#FFFFFF", Set(Style(nodes, "Button.connectmain"), "Background"));
        Assert.Equal("#1948AE", Set(Style(nodes, "Button.connectmain"), "Foreground"));
        Assert.Equal("#2E60D4", Set(Style(nodes, "Button.connectsecondary"), "Background"));

        foreach (var label in new[] { "Invitation", "Address" })
        {
            var field = Assert.Single(nodes, e => e.Name.LocalName == "TextBox" &&
                Has(e, "Text", "{Binding " + label + "}"));
            var classes = field.Attribute("Classes")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            Assert.Contains("onbrand", classes);
            Assert.Equal("#1845B1", field.Attribute("Background")?.Value);
            Assert.Equal("#FFFFFF", field.Attribute("Foreground")?.Value);
            Assert.Equal("#FFFFFF", field.Attribute("CaretBrush")?.Value);
            Assert.Equal("#E4EEFF", field.Attribute("PlaceholderForeground")?.Value);
        }
        foreach (var state in new[] { "", ":focus", ":pointerover", ":disabled" })
            Assert.Equal("#1845B1",
                Set(Style(nodes, "TextBox.connectfield.onbrand" + state +
                    " /template/ Border#PART_BorderElement"), "Background"));

        foreach (var name in new[] { "QrPage", "CodePage", "WifiPage", "ServerPage",
            "AddressPage", "DetailsPage" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Border" && Has(e, "Name", name) &&
                Has(e, "Classes", "connectcard"));
        Assert.DoesNotContain(nodes, e => e.Name.LocalName == "ScrollViewer");
        foreach (var binding in new[] { "{Binding ScanCode}", "{Binding JoinInternet}",
            "{Binding Search}", "{Binding ConnectAddress}", "{Binding UseThisComputer}" })
            Assert.Contains(nodes, e => e.Name.LocalName == "Button" && Has(e, "Command", binding));
        Assert.Contains(nodes, e => e.Name.LocalName == "Button" &&
            Has(e, "Name", "QrDetails") && Has(e, "Classes", "connectnav incard"));
        Assert.Contains(nodes, e => e.Name.LocalName == "Style" &&
            Has(e, "Selector", "Grid.micro Border.connectcard"));
    }

    [Fact]
    public void L_accueil_s_adapte_aux_ecrans_etroits()
    {
        Assert.Contains("AuthLayout.Track(Sections)", File.ReadAllText(Source("Views/Phone/HomeView.axaml.cs")));
    }
}
