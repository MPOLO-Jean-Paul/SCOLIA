using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public class CollectionSyncTests
{
    private sealed record Row(int Id, string Label);

    [Fact]
    public void Une_photographie_identique_garde_les_instances_sans_notification()
    {
        var old = new Row(1, "Amina");
        var rows = new ObservableCollection<Row> { old };
        var events = 0;
        rows.CollectionChanged += (_, _) => events++;
        CollectionSync.Apply(rows, [new Row(1, "Amina")]);
        Assert.Same(old, rows[0]);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Une_insertion_et_un_reclassement_gardent_les_lignes_existantes()
    {
        var a = new Row(1, "Amina");
        var b = new Row(2, "Paul");
        var rows = new ObservableCollection<Row> { a, b };
        CollectionSync.Apply(rows, [new Row(3, "Rose"), new Row(2, "Paul"), new Row(1, "Amina")]);
        Assert.Equal(new[] { 3, 2, 1 }, rows.Select(r => r.Id));
        Assert.Same(b, rows[1]);
        Assert.Same(a, rows[2]);
    }

    [Fact]
    public void Changer_une_valeur_et_supprimer_une_ligne_ne_vide_jamais_la_liste()
    {
        var a = new Row(1, "Amina");
        var rows = new ObservableCollection<Row> { a, new(2, "Paul"), new(3, "Rose") };
        var reset = false;
        rows.CollectionChanged += (_, e) => reset |= e.Action == NotifyCollectionChangedAction.Reset;
        CollectionSync.Apply(rows, [new Row(1, "Amina"), new Row(2, "Paul · payé")]);
        Assert.False(reset);
        Assert.Same(a, rows[0]);
        Assert.Equal("Paul · payé", rows[1].Label);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Une_commande_en_cours_ignore_un_second_appui()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var command = new AsyncCommand(async () => { count++; await pending.Task; });
        command.CanExecuteChanged += (_, _) => { if (command.CanExecute(null)) finished.TrySetResult(); };
        command.Execute(null);
        command.Execute(null);
        Assert.Equal(1, count);
        Assert.False(command.CanExecute(null));
        pending.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(command.CanExecute(null));
    }
}
