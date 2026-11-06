using System.Xml.Linq;
using Scolia.Bureau.Infrastructure;
using Scolia.Domain.Students;

namespace Scolia.Bureau.Tests;

public sealed class CompactFieldsTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln");
        return File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Les_champs_metier_reprennent_la_hauteur_et_le_rayonnage_des_segments()
    {
        var xml = XDocument.Parse(Source("src/Scolia.Bureau/Themes/Controls.axaml"));
        XNamespace ax = "https://github.com/avaloniaui";
        var styles = xml.Descendants(ax + "Style").ToArray();
        var segments = styles.Single(s => s.Attribute("Selector")?.Value == "Border.segmented");
        Assert.Contains(segments.Descendants(ax + "Setter"), s =>
            s.Attribute("Property")?.Value == "CornerRadius");
        var controls = styles.Single(s =>
            s.Attribute("Selector")?.Value == "ComboBox, CalendarDatePicker, NumericUpDown");
        Assert.Contains(controls.Descendants(ax + "Setter"), s =>
            s.Attribute("Property")?.Value == "MinHeight" &&
            s.Attribute("Value")?.Value == "36");
        Assert.Contains(controls.Descendants(ax + "Setter"), s =>
            s.Attribute("Property")?.Value == "CornerRadius" &&
            s.Attribute("Value")?.Value == "{StaticResource RadiusSmall}");
    }

    [Fact]
    public void Authentification_recherche_et_mots_de_passe_gardent_leur_style()
    {
        var theme = Source("src/Scolia.Bureau/Themes/Controls.axaml");
        Assert.Contains("TextBox:not(.onbrand):not(.search):not(.searchfield):not(.code)", theme, StringComparison.Ordinal);
        Assert.Contains("TextBox.w11.onbrand", theme, StringComparison.Ordinal);
        Assert.Contains("RadioButton.segment", theme, StringComparison.Ordinal);
        var staff = Source("src/Scolia.Bureau/Views/StaffView.axaml");
        Assert.Contains("Dossiers du personnel", staff, StringComparison.Ordinal);
        Assert.Contains("Paie mensuelle", staff, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Periods}\"", staff, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_client_refuse_le_moteur_obsolete_meme_si_le_numero_de_serveur_est_recent()
    {
        Assert.True(CardServerCompatibility.SupportsCurrentGenerator(CardGeneratorManifest.Id));
        Assert.False(CardServerCompatibility.SupportsCurrentGenerator(null));
        Assert.False(CardServerCompatibility.SupportsCurrentGenerator("SCOLIA-CARTES-ID1-2026-11-V2"));
    }
}
