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
