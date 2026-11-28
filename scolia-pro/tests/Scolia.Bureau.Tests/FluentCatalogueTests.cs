using System.Text.RegularExpressions;
using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

public sealed class FluentCatalogueTests
{
    [Fact]
    public void Toutes_les_actions_des_vues_et_des_menus_ont_un_trace_Fluent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scolia.sln")))
            directory = directory.Parent;
        var source = Path.Combine(directory!.FullName, "src", "Scolia.Bureau");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new[]
        {
            "MaterialIconKind\\.([A-Za-z0-9_]+)",
            "<mi:MaterialIcon[^>]*?\\bKind=\"([A-Za-z0-9_]+)\"",
            "\\bIcon=\"([A-Za-z0-9_]+)\"",
            "infra:Icons\\.Lead=\"([A-Za-z0-9_]+)\""
        };
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                     .Where(p => Path.GetExtension(p) is ".cs" or ".axaml")
                     .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                         && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(path);
            foreach (var pattern in patterns)
                foreach (Match match in Regex.Matches(text, pattern, RegexOptions.Singleline))
                    names.Add(match.Groups[1].Value);
        }
        // Les identités de services restent leurs logos officiels.
        names.ExceptWith(["MicrosoftExcel", "Whatsapp"]);
        Assert.True(names.Count > 250);
        var missing = names.Where(name => !Enum.TryParse<MaterialIconKind>(name, out var kind)
            || !FluentGlyphs.HasPath(kind)).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, "Actions sans pictogramme Fluent : " + string.Join(", ", missing));
    }

    [Fact]
    public void Les_metiers_gardent_des_reperes_couleur_distincts()
    {
        var families = new[] { "Accueil", "Données", "Secrétariat", "Finances", "Scolarité", "Administration", "Paramètres" };
        var keys = families.Select(FeatureVisuals.Icon).Select(FluentGlyphs.ArtworkKey).ToArray();
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(families.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Erreur_notification_et_selection_conservent_des_etats_distincts()
    {
        FluentGlyphs.Install();
        var provider = MaterialIconDataProvider.Instance;
        Assert.NotEqual(provider.ProvideData(MaterialIconKind.AlertCircleOutline),
            provider.ProvideData(MaterialIconKind.BellOutline));
        Assert.NotEqual(provider.ProvideData(MaterialIconKind.RadioboxMarked),
            provider.ProvideData(MaterialIconKind.RadioboxBlank));
        Assert.NotEqual(provider.ProvideData(MaterialIconKind.Circle),
            provider.ProvideData(MaterialIconKind.CircleOutline));
        Assert.NotEqual(FluentGlyphs.ArtworkKey(MaterialIconKind.AlertCircleOutline),
            FluentGlyphs.ArtworkKey(MaterialIconKind.BellOutline));
    }

    [Fact]
    public void Apparence_fenetres_et_reseau_ont_des_illustrations_distinctes_du_catalogue()
    {
        var kinds = new[] { MaterialIconKind.Palette, MaterialIconKind.WindowMaximize,
            MaterialIconKind.Wifi, MaterialIconKind.DotsGrid };
        var keys = kinds.Select(FluentGlyphs.ArtworkKey).ToArray();
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(kinds.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Chaque_ecran_principal_a_sa_propre_forme_et_sa_propre_illustration()
    {
        FluentGlyphs.Install();
        var screens = FeatureVisuals.PageIcons;
        Assert.True(screens.Count >= 50);
        foreach (var duplicates in screens.GroupBy(p => MaterialIconDataProvider.Instance.ProvideData(p.Value)).Where(g => g.Count() > 1))
            Assert.Fail("Écrans avec le même tracé : " + string.Join(", ", duplicates.Select(p => p.Key.Name)));
        foreach (var duplicates in screens.GroupBy(p => FluentGlyphs.ArtworkKey(p.Value)).Where(g => g.Count() > 1))
            Assert.Fail("Écrans avec la même illustration : " + string.Join(", ", duplicates.Select(p => p.Key.Name)));
        Assert.All(screens.Values, icon => Assert.NotNull(FluentGlyphs.ArtworkKey(icon)));
    }

    [Fact]
    public void Retour_suivant_chevron_et_reseau_gardent_des_formes_differentes()
    {
        FluentGlyphs.Install();
        var kinds = new[] { MaterialIconKind.ArrowLeft, MaterialIconKind.ArrowRight,
            MaterialIconKind.ChevronLeft, MaterialIconKind.LanConnect, MaterialIconKind.LanDisconnect,
            MaterialIconKind.Wifi, MaterialIconKind.AccountMultipleOutline, MaterialIconKind.CellphoneLink };
        var paths = kinds.Select(MaterialIconDataProvider.Instance.ProvideData).ToArray();
        Assert.Equal(kinds.Length, paths.Distinct(StringComparer.Ordinal).Count());
    }
}
