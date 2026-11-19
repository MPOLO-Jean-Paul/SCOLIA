using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Prévention du retour de la fenêtre de connexion Windows de 460 × 640 pixels,
/// et du jumelage trop pâle sur un grand écran.
/// </summary>
public sealed class DesktopMaximizedPreHomeTests
{
    private static string Source(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(), relative);
    }

    [Fact]
    public void Fenetre_Windows_demarre_maximisee_avec_barre_de_titre()
    {
        var window = XDocument.Load(Source("src/Scolia.Bureau/MainWindow.axaml")).Root!;
        Assert.Equal("Maximized", (string?)window.Attribute("WindowState"));
        Assert.Equal("1280", (string?)window.Attribute("Width"));
        Assert.Equal("820", (string?)window.Attribute("Height"));
        Assert.Equal("760", (string?)window.Attribute("MinWidth"));
        Assert.Equal("520", (string?)window.Attribute("MinHeight"));

        var code = File.ReadAllText(Source("src/Scolia.Bureau/MainWindow.axaml.cs"));
        Assert.Contains("var wanted = !OperatingSystem.IsWindows() && shell.CompactWindow;", code);
        Assert.Contains("private WindowState fullState = WindowState.Maximized;", code);
        Assert.Contains("ExtendClientAreaToDecorationsHint = false", code);
        Assert.Contains("CanResize = true", code);
        Assert.Contains("Prefs.Startup == \"pleinecran\"", code);
    }

}
