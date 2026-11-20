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
}
