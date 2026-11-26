using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class FormSubscriptionTests
{
    [Fact]
    public void Un_champ_retire_ne_reveille_plus_le_formulaire()
    {
        var form = new FormViewModel("Test");
        var field = new FormField("nom", "Nom", FieldKind.Text, false, null);
        var changes = 0;
        form.Changed = _ => changes++;
        form.Fields.Add(field);
        field.Text = "A";
        form.Fields.Remove(field);
        field.Text = "B";
        form.Fields.Add(field);
        field.Text = "C";
        form.Fields.Clear();
        field.Text = "D";
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Une_reinsertion_ne_duplique_pas_les_notifications()
    {
        var form = new FormViewModel("Test");
        var field = new FormField("nom", "Nom", FieldKind.Text, false, null);
        var changes = 0;
        form.Changed = _ => changes++;
        form.Fields.Add(field);
        form.Fields.Add(field);
        field.Text = "A";
        form.Fields.Remove(field);
        field.Text = "B";
        form.Fields.Remove(field);
        field.Text = "C";
        Assert.Equal(2, changes);
    }
}
