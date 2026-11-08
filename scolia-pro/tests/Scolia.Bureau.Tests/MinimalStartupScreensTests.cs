using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Anti-régression : les deux écrans de transition montrent uniquement le logo,
/// le nom SCOLIA Pro et les trois points animés. Les messages restent disponibles
/// dans l'état interne et les erreurs Android restent diagnostiquables.
/// </summary>
public sealed class MinimalStartupScreensTests
{
    private static string Source(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(), relative);
    }

    private static XElement Named(XDocument doc, string name) =>
        Assert.Single(doc.Descendants(), el => el.Attributes()
            .Any(a => a.Name.LocalName == "Name" && a.Value == name));

    private static void AssertOnlyLogoTitleAndDots(XElement area, string logoName)
    {
        Assert.Contains(area.Descendants(), e => e.Attributes()
            .Any(a => a.Name.LocalName == "Name" && a.Value == logoName));
        var labels = area.Descendants().Where(e => e.Name.LocalName == "TextBlock")
            .Select(e => (string?)e.Attribute("Text")).ToArray();
        Assert.Equal(new[] { "SCOLIA Pro" }, labels);
        Assert.Equal(3, area.Descendants().Count(e => e.Name.LocalName == "Border"
            && ((string?)e.Attribute("Classes"))?.Contains("welcomedot", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(area.Descendants(),
            e => e.Name.LocalName == "ProgressBar" || e.Name.LocalName == "ScrollViewer");
    }

    [Fact]
    public void Démarrage_Avalonia_se_limite_au_logo_au_nom_et_aux_trois_points()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/MainView.axaml"));
        AssertOnlyLogoTitleAndDots(Named(doc, "StartupContent"), "StartupLogo");
    }

    [Fact]
    public void Après_connexion_aucune_étape_technique_ni_barre_de_progression_visible()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/MainView.axaml"));
        AssertOnlyLogoTitleAndDots(Named(doc, "OpeningHost"), "OpeningLogo");
        var source = File.ReadAllText(Source("src/Scolia.Bureau/Views/MainView.axaml"));
        foreach (var forbidden in new[] { "Text=\"{Binding OpeningTitle}\"",
            "Text=\"{Binding OpeningContext}\"", "Text=\"{Binding OpeningStep}\"",
            "Value=\"{Binding OpeningProgress" })
            Assert.DoesNotContain(forbidden, source);
    }

    [Fact]
    public void Démarrage_Android_ne_montre_pas_de_texte_supplémentaire_sauf_en_cas_erreur()
    {
        var source = File.ReadAllText(Source("src/Scolia.Mobile/StartupScreen.cs"));
        Assert.DoesNotContain("ESPACE SCOLAIRE", source);
        Assert.DoesNotContain("Votre établissement, en toute sécurité", source);
        Assert.DoesNotContain("intro.Add(status)", source);
        Assert.Contains("Text = \"SCOLIA Pro\"", source);
        Assert.Contains("intro.Add(dots)", source);
        Assert.Contains("status.Visibility = ViewStates.Visible;", source);
        Assert.Contains("retry.Visibility = ViewStates.Visible;", source);
    }

    [Fact]
    public void Connexion_garde_des_champs_tactiles_mais_moins_hauts_et_espacés()
    {
        var source = File.ReadAllText(Source("src/Scolia.Bureau/Views/LoginView.axaml"));
        Assert.Contains("StackPanel.iosloginfields\"><Setter Property=\"Spacing\" Value=\"10\"", source);
        Assert.Contains("TextBox.iosauthfield", source);
        Assert.Contains("Padding=\"14,9,52,9\"", source);
        Assert.DoesNotContain("Classes=\"iosloginfields\" Spacing=\"15\"", source);
    }
}
