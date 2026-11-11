using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Garantit que le compte enseignant Android réutilise les composants Avalonia
/// existants, sans changer les vues administrateur/secrétariat ni l'éditeur Windows.
/// </summary>
public sealed class TeacherAvaloniaConsistencyTests
{
    private static string View(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Scolia.Bureau", "Views", name));
    }

    [Fact]
    public void Teacher_home_uses_the_same_phone_components_as_the_secretariat()
    {
        var teacher = View("TeacherHomeView.axaml");
        var home = View(Path.Combine("Phone", "HomeView.axaml"));

        foreach (var css in new[] { "phonegroup", "groupcaps", "settingsentry", "appicon medium" })
        {
            Assert.Contains(css, home, StringComparison.Ordinal);
            Assert.Contains(css, teacher, StringComparison.Ordinal);
        }

        Assert.Contains("IsVisible=\"{Binding Phone}\"", teacher, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding NotPhone}\"", teacher, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding Percent, Mode=OneWay}\"", teacher, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding EncodeByCode}\"", teacher, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ChooseCode}\"", teacher, StringComparison.Ordinal);
    }

    [Fact]
    public void Teacher_mobile_editor_is_anonymous_and_classic_windows_editor_remains()
    {
        var marks = View("MarksView.axaml");
        Assert.Contains("IsVisible=\"{Binding UseClassicMarksLayout}\"", marks, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsTeacherPhone}\"", marks, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding Course}\"", marks, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding Promotion}\"", marks, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding Kind}\"", marks, StringComparison.Ordinal);

        var mobile = marks.Split("<!-- Android enseignants", StringSplitOptions.None)[1];
        Assert.Contains("Header=\"Code\"", mobile, StringComparison.Ordinal);
        Assert.Contains("Header=\"Cote\"", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Nom complet\"", mobile, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Matricule\"", mobile, StringComparison.Ordinal);
    }
}
