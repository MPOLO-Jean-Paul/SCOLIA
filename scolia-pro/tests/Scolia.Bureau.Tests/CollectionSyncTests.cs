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
    public void Les_ajouts_en_fin_ne_recreent_pas_les_lignes_existantes()
    {
        var a = new Row(1, "Amina");
        var rows = new ObservableCollection<Row> { a };
        var events = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => events.Add(e.Action);
        CollectionSync.Apply(rows, [new Row(1, "Amina"), new Row(2, "Paul")]);
        Assert.Same(a, rows[0]);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add }, events);
    }

    [Fact]
    public void Les_doublons_et_les_valeurs_nulles_restent_dans_le_bon_ordre()
    {
        var rows = new ObservableCollection<string?> { "A", null, "B", "A" };
        CollectionSync.Apply(rows, [null, "A", "C", "A", "B", null]);
        Assert.Equal(new string?[] { null, "A", "C", "A", "B", null }, rows.ToArray());
    }

    [Fact]
    public void Le_comparateur_metier_conserve_ses_instances()
    {
        var original = new Row(1, "Amina");
        var rows = new ObservableCollection<Row> { original, new(2, "Paul") };
        CollectionSync.Apply(rows, [new Row(2, "Paul"), new Row(1, "Autre libellé")], (a, b) => a.Id == b.Id);
        Assert.Same(original, rows[1]);
    }

    [Fact]
    public void Une_liste_entierement_modifiee_ne_declenche_pas_de_reset()
    {
        var rows = new ObservableCollection<int>(Enumerable.Range(0, 1000));
        var reset = false;
        var replaced = 0;
        rows.CollectionChanged += (_, e) =>
        {
            reset |= e.Action == NotifyCollectionChangedAction.Reset;
            if (e.Action == NotifyCollectionChangedAction.Replace) replaced++;
        };
        CollectionSync.Apply(rows, Enumerable.Range(1000, 1000));
        Assert.False(reset);
        Assert.Equal(1000, replaced);
        Assert.Equal(Enumerable.Range(1000, 1000), rows);
    }

    [Fact]
    public void Des_photographies_successives_reproduisent_exactement_leur_ordre()
    {
        var random = new Random(136);
        var rows = new ObservableCollection<int?>();
        var reset = false;
        rows.CollectionChanged += (_, e) => reset |= e.Action == NotifyCollectionChangedAction.Reset;
        for (var i = 0; i < 200; i++)
        {
            var incoming = Enumerable.Range(0, random.Next(0, 80))
                .Select(_ => random.Next(0, 8) is var n && n == 0 ? (int?)null : n)
                .ToArray();
            CollectionSync.Apply(rows, incoming);
            Assert.Equal(incoming, rows.ToArray());
        }
        Assert.False(reset);
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
