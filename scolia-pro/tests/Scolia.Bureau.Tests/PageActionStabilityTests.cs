using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class PageActionStabilityTests
{
    private sealed class Probe() : PageViewModel(null!)
    {
        public override string Title => "Test";
    }

    private static ActionVm Item(string label, bool primary = false, bool hidden = false)
        => new(label, MaterialIconKind.FileDocumentOutline, new RelayCommand(() => { }), Primary: primary, MenuOnly: hidden);

    [Fact]
    public void Les_lectures_reutilisent_les_listes_jusqu_a_un_changement()
    {
        var page = new Probe();
        page.Actions.Add(Item("A"));
        page.Actions.Add(Item("B", primary: true));
        page.Actions.Add(Item("C", hidden: true));
        var header = page.HeaderActions;
        var visible = page.VisibleActions;
        Assert.Same(header, page.HeaderActions);
        Assert.Same(visible, page.VisibleActions);
        Assert.Equal(new[] { "B", "A" }, header.Select(a => a.Label));
        page.FitActions(1);
        Assert.Same(header, page.HeaderActions);
        Assert.Equal("B", Assert.Single(page.VisibleActions).Label);
        Assert.Equal("A", Assert.Single(page.MoreActions).Label);
        page.Actions.RemoveAt(1);
        Assert.NotSame(header, page.HeaderActions);
        Assert.Equal("A", Assert.Single(page.VisibleActions).Label);
        Assert.Empty(page.MoreActions);
    }

    [Fact]
    public void Une_largeur_equivalente_ne_recree_pas_les_boutons()
    {
        var page = new Probe();
        page.Actions.Add(Item("A"));
        var visible = page.VisibleActions;
        var more = page.MoreActions;
        page.FitActions(3);
        page.FitActions(2);
        Assert.Same(visible, page.VisibleActions);
        Assert.Same(more, page.MoreActions);
        page.FitActions(0);
        Assert.Empty(page.VisibleActions);
        Assert.Single(page.MoreActions);
    }
}
