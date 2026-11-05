using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Contrôles de non-régression de l'assistant Windows et du nouvel accueil compact.</summary>
public sealed class InstallerAndHomePolishTests
{
    private static string Source(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln"),
            path.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void L_assistance_de_l_installeur_est_un_lien_mailto_cliquable_sur_toutes_les_pages()
    {
        var iss = File.ReadAllText(Source("scripts/windows/SCOLIA-Pro.iss"));
        Assert.Contains("#define AppContactEmail \"jeanpaulmpolo242@gmail.com\"", iss);
        Assert.Contains("#define AppSupportLink \"mailto:jeanpaulmpolo242@gmail.com\"", iss);
        Assert.Contains("AppSupportURL={#AppSupportLink}", iss);
        Assert.Contains("LienAssistance.Parent := WizardForm", iss);
        Assert.Contains("LienAssistance.OnClick := @OuvrirAssistance", iss);
        Assert.Contains("LienAssistance.Cursor := crHand", iss);
        Assert.Contains("ShellExecAsOriginalUser('open', '{#AppSupportLink}'", iss);
        Assert.Contains("procedure InitializeWizard", iss);
        Assert.DoesNotContain("AppSupportURL=https://github.com", iss);
    }

    [Fact]
    public void Accueil_desktop_conserve_des_cartes_claires_et_responsives()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var elements = doc.Descendants().ToArray();

        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "desktopwelcome"));
        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Height" && a.Value == "3") &&
            e.Attributes().Any(a => a.Name.LocalName == "Background" && a.Value.Contains("AccentBrush")));
        Assert.Contains(elements, e => e.Name.LocalName == "UniformGrid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal) && a.Value == "170"));
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "410"));
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "760"));
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("ShowAllTools")));
    }
}
