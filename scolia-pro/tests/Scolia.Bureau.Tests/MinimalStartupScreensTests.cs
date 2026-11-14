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
    public void Premier_splash_Android_conserve_le_fond_blanc_et_uniquement_le_symbole_Scolia()
    {
        var colors = XDocument.Load(Source("src/Scolia.Mobile/Resources/values/colors.xml"));
        Assert.Contains(colors.Descendants(), e => e.Name.LocalName == "color" &&
            (string?)e.Attribute("name") == "fond_lancement" && e.Value.Trim() == "#FFFFFF");

        var splash = XDocument.Load(Source("src/Scolia.Mobile/Resources/values-v31/styles.xml"));
        var launchTheme = Assert.Single(splash.Descendants(), e => e.Name.LocalName == "style" &&
            (string?)e.Attribute("name") == "ScoliaLaunchTheme");
        var items = launchTheme.Elements().Where(e => e.Name.LocalName == "item").ToArray();
        Assert.Contains(items, e => (string?)e.Attribute("name") == "android:windowSplashScreenBackground" &&
            e.Value.Trim() == "@color/fond_lancement");
        Assert.Contains(items, e => (string?)e.Attribute("name") == "android:windowSplashScreenAnimatedIcon" &&
            e.Value.Trim() == "@drawable/scolia_mark");
        Assert.DoesNotContain(items, e => ((string?)e.Attribute("name"))?.Contains("Branding", StringComparison.OrdinalIgnoreCase) == true);

        var fallback = XDocument.Load(Source("src/Scolia.Mobile/Resources/drawable/lancement.xml"));
        Assert.Contains(fallback.Descendants(), e =>
            (string?)e.Attribute(XName.Get("drawable", "http://schemas.android.com/apk/res/android")) == "@drawable/scolia_mark");
        Assert.DoesNotContain(fallback.Descendants(), e => e.Name.LocalName is "text" or "TextView");
    }

    [Fact]
    public void Deuxieme_splash_conserve_son_fond_clair_et_une_animation_discrete()
    {
        var startup = File.ReadAllText(Source("src/Scolia.Mobile/StartupScreen.cs"));
        var assembly = File.ReadAllText(Source("src/Scolia.Mobile/LogoAssemblyView.cs"));
        Assert.Contains("SetBackgroundColor(Color.Rgb(246, 248, 252))", startup);
        Assert.Contains("new LinearLayout.LayoutParams(Dp(118), Dp(118))", startup);
        Assert.Contains("BottomMargin = Dp(19)", startup);
        Assert.Contains("title.LetterSpacing = 0.012f", startup);
        Assert.Contains("Text = \"SCOLIA Pro\"", startup);
        Assert.Contains("intro.Add(dots)", startup);
        Assert.Contains("for (var i = 0; i < 3; i++)", startup);
        Assert.Contains("pulse.SetDuration(960)", startup);
        Assert.Contains("pulse.SetInterpolator(new Android.Views.Animations.AccelerateDecelerateInterpolator())", startup);
        Assert.Contains("status.Visibility = ViewStates.Gone", startup);
        Assert.Contains("retry.Visibility = ViewStates.Gone", startup);
        Assert.Contains("canvas.Rotate(-16f", assembly);
        Assert.Contains("PaintFlags.AntiAlias", assembly);
    }

    [Fact]
    public void Connexion_conserve_des_champs_tactiles_et_des_espacements_calibres()
    {
        var source = File.ReadAllText(Source("src/Scolia.Bureau/Views/LoginView.axaml"));
        Assert.Contains("StackPanel.iosloginfields\"><Setter Property=\"Spacing\" Value=\"10\"", source);
        Assert.Contains("TextBox.iosauthfield", source);
        Assert.Contains("Padding=\"14,9,52,9\"", source);
        Assert.DoesNotContain("Classes=\"iosloginfields\" Spacing=\"15\"", source);
    }
}
