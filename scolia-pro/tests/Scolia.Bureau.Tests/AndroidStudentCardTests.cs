using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class AndroidStudentCardTests
{
    [Fact]
    public void La_liste_android_affiche_des_fiches_cliquables_sans_grille_horizontale()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        var xml = XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", "Views", "StudentsView.axaml"));
        Assert.Contains(xml.Descendants(), element => element.Name.LocalName == "Button"
            && element.Attributes().Any(a => a.Name.LocalName == "Command"
                && a.Value.Contains("OpenStudentOnPhone", StringComparison.Ordinal))
            && element.Attributes().Any(a => a.Name.LocalName == "CommandParameter"));
        Assert.Contains(xml.Descendants(), element => element.Name.LocalName == "DataGrid"
            && element.Attributes().Any(a => a.Name.LocalName == "IsVisible"
                && a.Value.Contains("NotPhone", StringComparison.Ordinal)));
        Assert.Contains(xml.Descendants(), element => element.Name.LocalName == "ItemsControl"
            && element.Attributes().Any(a => a.Name.LocalName == "IsVisible"
                && a.Value.Contains("Phone", StringComparison.Ordinal)));
    }

    [Fact]
    public void Le_dossier_peut_s_ouvrir_au_toucher()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences()) { Phone = true };
        var page = new StudentsPage(shell);
        Assert.NotNull(page.OpenStudentOnPhone);
    }
}
