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

    [Fact]
    public void Connexion_Windows_centre_le_formulaire_et_place_les_options_a_droite()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/LoginView.axaml"));
        var grid = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "Grid"
            && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "IosLoginLayout");
        Assert.Equal("Center", (string?)grid.Attribute("HorizontalAlignment"));
        Assert.Equal("*,Auto", (string?)grid.Attribute("ColumnDefinitions"));

        var actions = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "StackPanel"
            && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "IosDesktopActions");
        Assert.Equal("1", (string?)actions.Attribute("Grid.Column"));

        var code = File.ReadAllText(Source("src/Scolia.Bureau/Views/LoginView.axaml.cs"));
        Assert.Contains("!Platform.IsMobile && size.Width >= 850", code);
        Assert.Contains("IosDesktopActions.IsVisible = desktop;", code);
        Assert.Contains("IosMobileActions.IsVisible = !desktop;", code);
    }

    [Fact]
    public void Jumelage_desktop_pleine_page_conserve_un_contraste_de_lecture_eleves()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/ConnectionView.axaml"));
        var grid = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "Grid"
            && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "IosConnectionScreen");
        Assert.Contains("desktopconnect", (string?)grid.Attribute("Classes"));
        var colors = grid.Descendants().Where(e => e.Name.LocalName == "GradientStop")
            .Select(e => (string?)e.Attribute("Color")).ToArray();
        Assert.Contains("#315DC6", colors);
        Assert.Contains("#5682EA", colors);

        var styles = doc.Descendants().Where(e => e.Name.LocalName == "Style").ToArray();
        static XElement FindStyle(XElement[] all, string selector) =>
            Assert.Single(all, e => (string?)e.Attribute("Selector") == selector);
        static string? GetSetter(XElement element, string name) =>
            (string?)element.Elements().Single(e => (string?)e.Attribute("Property") == name)
                .Attribute("Value");

        Assert.Equal("820", GetSetter(FindStyle(styles, "Grid.wide StackPanel.iosconnectbody"), "MaxWidth"));
        Assert.Equal("#FFFFFF", GetSetter(FindStyle(styles, "Grid.desktopconnect TextBlock"), "Foreground"));
        Assert.Equal("#DDE9FF", GetSetter(FindStyle(styles, "Grid.desktopconnect TextBlock.muted"), "Foreground"));
        Assert.Equal("#FFFFFF", GetSetter(FindStyle(styles, "Button.iosconnectprimary"), "Background"));
        Assert.Equal("#1846A6", GetSetter(FindStyle(styles, "Button.iosconnectprimary"), "Foreground"));
        Assert.Equal("#254FAE", GetSetter(FindStyle(styles, "Button.iosconnectsecondary"), "Background"));
        Assert.Equal("#FFFFFF", GetSetter(FindStyle(styles, "Button.iosconnectsecondary"), "Foreground"));

        var code = File.ReadAllText(Source("src/Scolia.Bureau/Views/ConnectionView.axaml.cs"));
        Assert.Contains("if (Platform.IsMobile)", code);
        Assert.Contains("CompactConnectionThemeScope.IsVisible = true", code);
    }
}
