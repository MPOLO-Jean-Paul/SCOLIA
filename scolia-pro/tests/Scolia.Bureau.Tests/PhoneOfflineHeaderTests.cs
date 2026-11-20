using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Prévenir le retour des bandeaux réseau répétitifs sur Android.</summary>
public sealed class PhoneOfflineHeaderTests
{
    private static string Source(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Scolia.sln")))
            root = root.Parent;
        return Path.Combine(root?.FullName ??
            throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau", relative);
    }

    [Fact]
    public void Pas_de_bandeau_hors_ligne_redondant_dans_l_entete()
    {
        var xml = XDocument.Load(Source("Views/PhoneFrame.axaml"));
        var elements = xml.Descendants().ToArray();
        Assert.DoesNotContain(elements, e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Classes") is { } classes &&
            classes.Split(' ').Contains("livestrip") && classes.Split(' ').Contains("offline"));
        Assert.Contains(elements, e => e.Name.LocalName == "Ellipse" &&
            e.Attributes().Any(a => a.Name.LocalName == "IsVisible" &&
                a.Value.Contains("Sync.Offline", StringComparison.Ordinal)));
        // Les conflits réels restent signalés. Pas de suppression d'une alerte importante.
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Classes") is { } classes &&
            classes.Split(' ').Contains("conflict"));
        Assert.DoesNotContain(elements, e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Text" &&
                a.Value.Contains("envoi au retour de l'ordinateur principal", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void La_carte_d_accueil_conserve_le_statut_et_ouvre_la_page_reseau()
    {
        var xml = XDocument.Load(Source("Views/Phone/HomeView.axaml"));
        var elements = xml.Descendants().ToArray();
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" &&
                a.Value.Contains("Network", StringComparison.Ordinal)) &&
            e.Descendants().Any(d => d.Attributes().Any(a => a.Name.LocalName == "Text" &&
                a.Value.Contains("Sync.StatusText", StringComparison.Ordinal))));
        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "WelcomeCard"));
        var network = XDocument.Load(Source("Views/NetworkView.axaml"));
        Assert.Contains(network.Descendants(), e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" &&
                a.Value.Contains("Sync.PrepareOffline", StringComparison.Ordinal)));
    }
}
