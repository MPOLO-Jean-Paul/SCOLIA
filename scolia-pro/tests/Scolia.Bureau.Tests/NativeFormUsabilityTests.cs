using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Tests;

/// <summary>Comportements communs du formulaire natif Windows, Android et iOS.</summary>
public sealed class NativeFormUsabilityTests
{
    [Fact]
    public void Inscription_sans_promotion_explicitement_choisie_est_refusee()
    {
        var form = new FormViewModel("Inscription");
        var promotion = form.AddChoice("promotion", "Promotion",
            [new ChoiceItem("INF1", "Infirmière première année")], required: true);
        // Contrairement à un formulaire d'édition, l'inscription ne préremplit pas sa promotion.
        promotion.Choice = null;
        Assert.False(form.Validate());
        Assert.Equal("Champ obligatoire.", promotion.Error);
    }

    [Fact]
    public void Enseignant_ou_apprenant_n_est_jamais_choisi_automatiquement_parmi_plusieurs()
    {
        var form = new FormViewModel("Attribution");
        var selected = form.AddChoice("enseignant", "Enseignant",
        [
            new ChoiceItem("id1", "KABONGO Anne"),
            new ChoiceItem("id2", "MUKENDI Paul"),
        ], required: true);
        Assert.Null(selected.Choice);
        Assert.False(form.Validate());
        Assert.Equal("Champ obligatoire.", selected.Error);

        selected.Choice = selected.Options[1];
        Assert.True(form.Validate());
        Assert.Equal("id2", form.ChoiceValue("enseignant"));
    }

    [Fact]
    public void Une_valeur_existante_ou_un_choix_unique_reste_intuitif()
    {
        var options = new[]
        {
            new ChoiceItem("INF1", "Première année"),
            new ChoiceItem("INF2", "Deuxième année"),
        };
        var existing = new FormViewModel("Modifier");
        Assert.Equal("INF2", existing.AddChoice("promotion", "Promotion", options, "INF2", required: true).Choice?.Value);
        var only = new FormViewModel("Inscrire");
        Assert.Equal("INF1", only.AddChoice("promotion", "Promotion", [options[0]], required: true).Choice?.Value);
    }

    [Fact]
    public void Erreur_du_champ_obligatoire_disparait_quand_le_champ_est_corrige()
    {
        var form = new FormViewModel("Inscription");
        var name = form.Add("nom", "Nom", required: true);
        Assert.False(form.Validate());
        name.Text = "BANZA";
        Assert.Null(name.Error);
        Assert.True(form.Validate());
    }

    [Fact]
    public async Task Formulaire_occupe_ignore_double_validation_et_annulation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var form = new FormViewModel("Paiement", onSubmit: async _ =>
        {
            count++;
            started.TrySetResult();
            await release.Task;
            return true;
        });
        form.Submit.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(form.Busy);
        form.Submit.Execute(null);
        form.Cancel.Execute(null);
        Assert.Equal(1, count);
        Assert.False(form.Completion.IsCompleted);
        release.TrySetResult();
        Assert.True(await form.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, count);
    }
}
