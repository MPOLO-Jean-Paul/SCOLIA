using Scolia.Bureau.Api;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

/// <summary>Les choix des cartes ne se perdent pas lorsque la liste est filtrée ou rafraîchie.</summary>
public sealed class CardSelectionTests
{
    [Fact]
    public void Choisir_et_decocher_une_carte_notifie_une_seule_fois_par_changement()
    {
        var id = Guid.NewGuid();
        var calls = new List<(Guid Id, bool Value)>();
        var source = new CardCandidateDto(id, "INF293", "SHIMBA MAKITA ELIE", "INF3",
            "Actif", true, true, false);
        var row = new CardRowVm(source, string.Empty, (studentId, selected) =>
            calls.Add((studentId, selected)));

        row.Selected = true;
        row.Selected = true;
        row.Selected = false;

        Assert.Equal(2, calls.Count);
        Assert.Equal((id, true), calls[0]);
        Assert.Equal((id, false), calls[1]);
    }

    [Fact]
    public void Restauration_de_la_selection_avec_le_meme_identifiant()
    {
        var id = Guid.NewGuid();
        var selected = new HashSet<Guid>();
        var source = new CardCandidateDto(id, "INF293", "SHIMBA MAKITA ELIE", "INF3",
            "Actif", true, true, false);

        var oldRow = new CardRowVm(source, string.Empty, (studentId, value) =>
        {
            if (value) selected.Add(studentId);
            else selected.Remove(studentId);
        });
        oldRow.Selected = true;
        var rebuiltRow = new CardRowVm(source, string.Empty, (studentId, value) =>
        {
            if (value) selected.Add(studentId);
            else selected.Remove(studentId);
        }) { Selected = selected.Contains(id) };

        Assert.True(rebuiltRow.Selected);
        Assert.Single(selected);
        rebuiltRow.Selected = false;
        Assert.Empty(selected);
    }
}
