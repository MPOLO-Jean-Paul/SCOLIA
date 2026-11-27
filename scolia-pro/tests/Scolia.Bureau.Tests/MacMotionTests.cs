using Avalonia;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Rythme macOS : ressort amorti sans rebond visible, défilement à la molette amorti et ciblé.</summary>
public sealed class MacMotionTests
{
    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(), "src", "Scolia.Bureau", relative);
    }

    [Fact]
    public void Le_ressort_part_vite_se_pose_sans_rebond_visible_et_finit_exactement()
    {
        var spring = new SpringEasing();
        Assert.Equal(0, spring.Ease(0));
        Assert.Equal(1, spring.Ease(1));
        // Départ franc : plus de 80 % du chemin au premier tiers, comme les animations d'Apple.
        Assert.True(spring.Ease(1 / 3d) > 0.8);
        var peak = Enumerable.Range(0, 1001).Select(i => spring.Ease(i / 1000d)).Max();
        Assert.True(peak < 1.01, $"Dépassement trop visible : {peak}");
    }

    [Fact]
    public void Seuls_les_crans_de_molette_sont_amortis()
    {
        Assert.True(SmoothScroll.IsWheelNotch(new Vector(0, -1)));
        Assert.True(SmoothScroll.IsWheelNotch(new Vector(0, 2)));
        // Pavé tactile (pas fractionnaires) et défilement horizontal : comportement natif.
        Assert.False(SmoothScroll.IsWheelNotch(new Vector(0, 0.35)));
        Assert.False(SmoothScroll.IsWheelNotch(new Vector(1, 0)));
    }

    [Fact]
    public void Le_mouvement_suit_partout_la_meme_courbe()
    {
        foreach (var file in new[] { "Themes/Controls.axaml", "Themes/Phone.axaml", "Views/MainView.axaml", "Views/ConnectionView.axaml", "Views/Phone/SetupView.axaml" })
        {
            var xaml = File.ReadAllText(Source(file));
            Assert.DoesNotContain("Easing=\"CubicEaseOut\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Easing=\"QuadraticEaseOut\"", xaml, StringComparison.Ordinal);
        }
        Assert.Contains("Motion.Spring", File.ReadAllText(Source("Infrastructure/Reveal.cs")), StringComparison.Ordinal);
        Assert.Contains("Motion.Spring", File.ReadAllText(Source("Infrastructure/SharedAxisTransition.cs")), StringComparison.Ordinal);
        Assert.Contains("SmoothScroll.Install()", File.ReadAllText(Source("App.axaml.cs")), StringComparison.Ordinal);
        var scroll = File.ReadAllText(Source("Infrastructure/SmoothScroll.cs"));
        // Le téléphone, Ctrl+molette, les tableaux et le réglage « Animations » gardent le comportement natif.
        Assert.Contains("Platform.IsMobile || PhoneLayout.Active || e.KeyModifiers != KeyModifiers.None", scroll, StringComparison.Ordinal);
        Assert.Contains("Reveal.MotionAllowed(viewer)", scroll, StringComparison.Ordinal);
        Assert.Contains("visual is DataGrid", scroll, StringComparison.Ordinal);
    }

    [Fact]
    public void L_ordinateur_reprend_les_controles_de_Windows_11()
    {
        var windows = File.ReadAllText(Source("Themes/Windows11.axaml"));
        // Boutons et champs de 32 px, coins de 4 px, filet d'élévation plus sombre en bas.
        Assert.Contains("<x:Double x:Key=\"FieldHeight\">32</x:Double>", windows, StringComparison.Ordinal);
        Assert.Contains("<Thickness x:Key=\"ButtonStroke\">1</Thickness>", windows, StringComparison.Ordinal);
        Assert.Contains("<CornerRadius x:Key=\"RadiusInput\">4</CornerRadius>", windows, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ButtonStrokeBrush\"", windows, StringComparison.Ordinal);
        var controls = File.ReadAllText(Source("Themes/Controls.axaml"));
        Assert.Contains("Value=\"{DynamicResource ButtonStroke}\"", controls, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"1,1,1,2\" />", controls, StringComparison.Ordinal);
        Assert.Contains("Button.navitem.selected Border.navmark", controls, StringComparison.Ordinal);
        Assert.Contains("Classes=\"navmark\"", File.ReadAllText(Source("Views/MainView.axaml")), StringComparison.Ordinal);
        // Échelle typographique de Windows 11 sur l'ordinateur.
        var prefs = File.ReadAllText(Source("Infrastructure/Preferences.cs"));
        // Le setter conditionnel préserve la taille de 14 points tout en évitant
        // les invalidations de ressources lorsque le zoom n'a pas changé.
        Assert.Contains("Resource(\"FsBase\", 14.0 * k);", prefs, StringComparison.Ordinal);
        Assert.Contains("Resource(\"FsBase\", 15.0 * k);", prefs, StringComparison.Ordinal);
        Assert.Contains("!Equals(current, value)", prefs, StringComparison.Ordinal);
        Assert.Contains("NavSelectedBrush", prefs, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_code_absent_n_encombre_jamais_un_sous_titre()
    {
        Assert.Equal("KABILA Josué", Format.NameAndCode("KABILA Josué", "À ATTRIBUER"));
        Assert.Equal("KABILA Josué · ITM-26-ZZ99", Format.NameAndCode("KABILA Josué", "ITM-26-ZZ99"));
        Assert.Equal("ITM-26-ZZ99 · KABILA Josué", Format.CodeAndName("ITM-26-ZZ99", "KABILA Josué"));
        Assert.Equal("KABILA Josué", Format.CodeAndName("—", "KABILA Josué"));
        Assert.Null(Format.CodeLabel("À ATTRIBUER"));
        Assert.Equal("ACC1 · Actif", Format.Line(Format.CodeLabel("À ATTRIBUER"), "ACC1", "Actif"));
        Assert.Equal("Matricule ITM-26-ZZ99 · ACC1", Format.Line(Format.CodeLabel("ITM-26-ZZ99"), "ACC1", null));
    }

    [Fact]
    public void Les_sauvegardes_se_lisent_comme_un_historique()
    {
        Assert.Equal("Sauvegarde manuelle", Scolia.Bureau.ViewModels.BackupRowVm.KindOf("scolia-20261122-205410-manuelle.db"));
        Assert.Equal("Sauvegarde automatique", Scolia.Bureau.ViewModels.BackupRowVm.KindOf("scolia-20261122-164610-automatique.db"));
        Assert.Equal("Avant une restauration", Scolia.Bureau.ViewModels.BackupRowVm.KindOf("scolia-20261122-164610-avantrestauration.db"));
        Assert.Equal("Sauvegarde importée", Scolia.Bureau.ViewModels.BackupRowVm.KindOf("scolia-20261122-164610-importee-0a1b.db"));
        var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("il y a 5 min", Scolia.Bureau.ViewModels.BackupText.Age(now.AddMinutes(-5), now));
        Assert.Equal("il y a 3 h", Scolia.Bureau.ViewModels.BackupText.Age(now.AddHours(-3), now));
        Assert.Equal("hier", Scolia.Bureau.ViewModels.BackupText.Age(now.AddHours(-30), now));
        Assert.Equal("il y a 4 jours", Scolia.Bureau.ViewModels.BackupText.Age(now.AddDays(-4), now));
        // Protection à jour : une copie de moins de 26 heures ; sinon, l'état passe à l'orange.
        Assert.True(Scolia.Bureau.ViewModels.BackupText.IsFresh(now.AddHours(-20), now));
        Assert.False(Scolia.Bureau.ViewModels.BackupText.IsFresh(now.AddHours(-30), now));
        Assert.False(Scolia.Bureau.ViewModels.BackupText.IsFresh(null, now));
    }
}
