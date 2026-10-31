using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Clic droit et double-clic : quelles actions la page propose pour une ligne.</summary>
public class RowMenuTests
{
    private static ShellViewModel NewShell() => new(new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());

    [Fact]
    public void Le_double_clic_du_suivi_mensuel_ouvre_la_fiche()
    {
        var page = new MonthlyPage(NewShell());
        Assert.Equal("Ouvrir la fiche", page.DefaultRowAction?.Label);
        var row = page.RowActions.Select(a => a.Label).ToList();
        Assert.Contains("Relancer par WhatsApp", row);
        Assert.Contains("Aperçu rapide", row);
        // Les exports concernent toute la page, pas une ligne.
        Assert.DoesNotContain("Excel", row);
        Assert.Contains("Excel", page.PageActions.Select(a => a.Label));
    }

    [Fact]
    public void Les_actions_reservees_au_clic_droit_n_encombrent_pas_l_en_tete()
    {
        var page = new StudentsPage(NewShell());
        Assert.Contains("Aperçu rapide", page.RowActions.Select(a => a.Label));
        Assert.DoesNotContain("Aperçu rapide", page.HeaderActions.Select(a => a.Label));
        Assert.DoesNotContain("Aperçu rapide", page.VisibleActions.Concat(page.MoreActions).Select(a => a.Label));
        Assert.Equal("Ouvrir la fiche", page.DefaultRowAction?.Label);
    }

    [Fact]
    public void Une_page_sans_ligne_n_a_pas_d_action_par_defaut()
    {
        var page = new ReportsPage(NewShell());
        Assert.Null(page.DefaultRowAction);
        Assert.Empty(page.RowActions);
    }
}
