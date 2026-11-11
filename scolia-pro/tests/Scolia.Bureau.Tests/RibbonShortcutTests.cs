using Avalonia.Input;
using Scolia.Bureau.Ribbon;
using Xunit;

namespace Scolia.Bureau.Tests;

public class RibbonShortcutTests
{
    [Fact]
    public void Chaque_raccourci_se_lit_comme_un_raccourci_avalonia()
    {
        foreach (var shortcut in RibbonCatalog.Shortcuts.Values)
            Assert.NotNull(KeyGesture.Parse(shortcut));
    }

    [Fact]
    public void Deux_commandes_ne_partagent_jamais_le_meme_raccourci()
    {
        var values = RibbonCatalog.Shortcuts.Values.ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    [Fact]
    public void Aucun_raccourci_n_utilise_ctrl_alt_pour_eviter_les_conflits_avec_altgr()
    {
        Assert.All(RibbonCatalog.Shortcuts.Values, shortcut => Assert.DoesNotContain("Ctrl+Alt", shortcut));
    }
}
