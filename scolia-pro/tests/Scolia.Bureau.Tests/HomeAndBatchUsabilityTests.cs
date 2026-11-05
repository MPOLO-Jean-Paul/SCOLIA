using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Scénarios de non-régression : bureau étroit, catalogue, recherche et encaissement par lot.</summary>
public sealed class HomeAndBatchUsabilityTests
{
    private static string ViewFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau", relative);
    }

    [Fact]
    public void L_accueil_garde_des_indicateurs_et_actions_adaptatifs()
    {
        var doc = XDocument.Load(ViewFile("Views/HomeView.axaml"));
        var nodes = doc.Descendants().ToArray();
        var kpiGrid = nodes.FirstOrDefault(x => x.Name.LocalName == "UniformGrid"
            && x.Attributes().Any(a => a.Name.LocalName == "MinItemWidth" && a.Value == "260"));
        Assert.NotNull(kpiGrid);
        Assert.Contains(nodes, x => x.Name.LocalName == "ItemsControl"
            && x.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("QuickActions")));
        Assert.Contains(nodes, x => x.Name.LocalName == "ItemsControl"
            && x.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("AllToolGroups")));
        Assert.Contains(nodes, x => x.Name.LocalName == "Grid"
            && x.Attributes().Any(a => a.Name.LocalName == "StackBelow" && a.Value == "760"));
    }

    [Fact]
    public void Afficher_tous_les_outils_est_une_vraie_action_interactive()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        var home = new HomePage(shell);
        Assert.False(home.AllToolsVisible);
        home.ShowAllTools.Execute(null);
        Assert.True(home.AllToolsVisible);
        Assert.Equal("Réduire les outils", home.AllToolsLabel);
        home.ShowAllTools.Execute(null);
        Assert.False(home.AllToolsVisible);
    }

    [Fact]
    public void Recherche_rapide_desktop_adapte_les_tuiles_a_sa_largeur()
    {
        var doc = XDocument.Load(ViewFile("Views/SearchResultsView.axaml"));
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "UniformGrid"
            && x.Attributes().Any(a => a.Name.LocalName == "MinItemWidth" && a.Value == "230"));
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "Button"
            && x.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "searchtile"));
    }

    [Fact]
    public void Une_erreur_de_paiement_ne_recoche_pas_un_dossier_retiré()
    {
        var eid = Guid.NewGuid();
        var line = new BatchRowVm(
            new BatchCandidateDto(eid, "INF293", "SHIMBA MAKITA ELIE", "INF3", 100, 100,
                [new BatchMonthDto(Guid.NewGuid(), "Novembre", new DateOnly(2026, 11, 5), 100, true)]),
            "100", "100", _ => { });
        line.ApplyMonths(line.Candidate.Months, x => x.ToString("0"));
        line.Selected = false;
        line.ShowResult(new BatchPaymentLineDto(eid, "INF293", "SHIMBA MAKITA ELIE",
            false, null, null, 0, "Le mois est déjà réglé"));
        Assert.False(line.Selected);
        line.Selected = true;
        line.ShowResult(new BatchPaymentLineDto(eid, "INF293", "SHIMBA MAKITA ELIE",
            true, Guid.NewGuid(), "REC-2026-01", 100, null));
        Assert.False(line.Selected);
        Assert.Contains("REC-2026-01", line.Result);
    }
}
