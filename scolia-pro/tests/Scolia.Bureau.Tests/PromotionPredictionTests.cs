using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Prédiction des promotions et des mois : on tape le début, SCOLIA propose la suite.</summary>
public sealed class PromotionPredictionTests
{
    private static readonly PromotionNames.Context None = PromotionNames.Context.Empty;

    [Theory]
    [InlineData("inf 1", "INF", 1)]
    [InlineData("INF-2", "INF", 2)]
    [InlineData("Acc3", "ACC", 3)]
    [InlineData("ACC", "ACC", null)]
    [InlineData("", "", null)]
    public void Le_code_est_lu_comme_on_le_tape(string text, string prefix, int? level)
    {
        Assert.Equal((prefix, level), PromotionNames.ParseCode(text));
    }

    [Fact]
    public void INF1_propose_les_quatre_niveaux_d_infirmiere_le_premier_en_tete()
    {
        Assert.Equal(
            ["Première Infirmière", "Deuxième Infirmière", "Troisième Infirmière", "Quatrième Infirmière"],
            PromotionNames.Suggest("INF1", "", None));
    }

    [Fact]
    public void ACC3_met_la_troisieme_accoucheuse_en_tete()
    {
        var names = PromotionNames.Suggest("ACC3", "", None);
        Assert.Equal("Troisième Accoucheuse", names[0]);
        Assert.Contains("Première Accoucheuse", names);
        Assert.Equal(4, names.Count);
    }

    [Theory]
    [InlineData("inf 1")]
    [InlineData("INF")]
    [InlineData("infirm")]
    public void La_designation_commencee_comme_un_code_ou_un_metier_propose_aussi(string typed)
    {
        Assert.Contains("Première Infirmière", PromotionNames.Suggest("", typed, None));
    }

    [Fact]
    public void Chaque_lettre_resserre_les_propositions()
    {
        Assert.Equal(["Deuxième Infirmière"], PromotionNames.Suggest("INF1", "Deu", None));
        Assert.Equal(["Deuxième Accoucheuse"], PromotionNames.Suggest("", "deuxième acc", None));
        Assert.Empty(PromotionNames.Suggest("INF1", "Première Infirmière", None));
    }

    [Fact]
    public void Un_debut_de_rang_n_est_pas_pris_pour_un_metier()
    {
        // « co », « se » ou « de » ne doivent pas basculer vers Commerciale ou Secrétariat.
        Assert.All(PromotionNames.Suggest("INF1", "de", None), n => Assert.EndsWith("Infirmière", n));
    }

    [Fact]
    public void Les_noms_deja_utilises_par_l_etablissement_sont_appris()
    {
        var context = new PromotionNames.Context([("TLB", "Techniques de laboratoire")], [("SOI1", "Première Soignante")]);
        Assert.Equal("Deuxième Soignante", PromotionNames.Suggest("SOI2", "", context)[0]);
        Assert.Equal("Première année de techniques de laboratoire", PromotionNames.Suggest("TLB1", "", context)[0]);
        Assert.Equal("SOI", PromotionNames.PrefixFor("Soignante", context));
    }

    [Theory]
    [InlineData("Deuxième Infirmière", 2)]
    [InlineData("3e année", 3)]
    [InlineData("1ère Accoucheuse", 1)]
    [InlineData("Infirmière", null)]
    public void Le_niveau_est_lu_dans_la_designation(string name, int? level)
    {
        Assert.Equal(level, PromotionNames.LevelOf(name));
    }

    [Fact]
    public void L_assistant_prepare_la_ligne_suivante()
    {
        var setup = new PromotionDraftVm(_ => { }) { Code = "inf1" };
        Assert.Equal("INF1", setup.Code);
        Assert.Contains("Première Infirmière", setup.Suggestions);
        setup.Pick.Execute("Première Infirmière");
        Assert.Equal("Première Infirmière", setup.Name);
        Assert.False(setup.HasSuggestions);
        Assert.Equal(("INF2", "Deuxième Infirmière"), setup.Next());
    }

    [Fact]
    public void Choisir_une_designation_sans_code_remplit_le_code()
    {
        var row = new PromotionDraftVm(_ => { }) { Name = "acc" };
        row.Pick.Execute("Deuxième Accoucheuse");
        Assert.Equal("ACC2", row.Code);
    }

    [Theory]
    [InlineData("2026", "2026-2027")]
    [InlineData("26", "2026-2027")]
    [InlineData("2027-", "2027-2028")]
    public void L_annee_scolaire_se_complete(string typed, string expected)
    {
        Assert.Equal([expected], SettingsPage.YearLabels(typed));
    }

    [Fact]
    public void Une_annee_deja_complete_ne_propose_rien()
    {
        Assert.Empty(SettingsPage.YearLabels("2026-2027"));
        Assert.Empty(SettingsPage.YearLabels("abc"));
    }

    [Fact]
    public void Le_dernier_mois_suit_le_premier_jusqu_au_choix_a_la_main()
    {
        var form = new FormViewModel("Nouvelle année scolaire", "", _ => Task.FromResult(true));
        form.AddChoice("debut", "Premier mois", SettingsPage.CalendarMonths, "9", required: true);
        form.AddChoice("fin", "Dernier mois", SettingsPage.CalendarMonths, "6", required: true);
        form.AddChoice("limite", "Limite", SettingsPage.PaymentDeadlineDays, "5", required: true);
        var summary = form.AddHeader("Exemple");
        SettingsPage.PredictMonths(form, summary);
        Assert.Contains("10 mois", summary.Hint);

        form.Find("debut").Choice = SettingsPage.CalendarMonths[9];   // octobre
        Assert.Equal("7", form.ChoiceValue("fin"));                   // juillet
        Assert.Contains("de octobre à juillet", summary.Hint);

        form.Find("fin").Choice = SettingsPage.CalendarMonths[5];     // juin, choisi à la main
        form.Find("debut").Choice = SettingsPage.CalendarMonths[8];   // septembre
        Assert.Equal("6", form.ChoiceValue("fin"));
        Assert.Contains("retard dès le 6 octobre", summary.Hint);
    }
}
