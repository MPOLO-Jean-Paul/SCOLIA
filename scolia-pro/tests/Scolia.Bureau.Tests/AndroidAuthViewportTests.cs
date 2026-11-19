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
    public void L_accueil_s_adapte_aux_ecrans_etroits()
    {
        Assert.Contains("AuthLayout.Track(Sections)", File.ReadAllText(Source("Views/Phone/HomeView.axaml.cs")));
    }
}
