using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Seuls les encaissements et les sauvegardes des autres postes deviennent une bannière, selon les réglages.</summary>
public sealed class AlertsTests
{
    private static LiveEventDto Change(string summary, params string[] topics) =>
        new("donnees", 12, "poste-b", "PC-ACCUEIL", "Secrétariat", summary, [.. topics], DateTime.UtcNow);

    private static Preferences Prefs() => new() { Alerts = true, AlertPayments = true, AlertBackups = true };

    [Fact]
    public void Un_encaissement_devient_une_banniere()
    {
        var alert = Alerts.From(Change("Reçu R-000045 : 50 000 FC pour ACC101 (Espèces).", "Recu"), Prefs());
        Assert.NotNull(alert);
        Assert.Equal(AlertKind.Payment, alert!.Kind);
        Assert.Equal("Nouvel encaissement", alert.Title);
        Assert.Equal("Secrétariat · PC-ACCUEIL", alert.Origin);
    }

    [Theory]
    [InlineData("Reçu R-000045 annulé.", AlertKind.PaymentCancelled)]
    [InlineData("Lot de 12 paiement(s) pour 600 000 FC, 0 ligne(s) en erreur.", AlertKind.PaymentBatch)]
    public void Annulation_et_lot_sont_reconnus(string summary, AlertKind kind) =>
        Assert.Equal(kind, Alerts.From(Change(summary, "Recu"), Prefs())?.Kind);

    [Fact]
    public void Une_sauvegarde_devient_une_banniere() =>
        Assert.Equal(AlertKind.Backup, Alerts.From(Change("Sauvegarde faite (scolia-20261101.db).", "Sauvegarde"), Prefs())?.Kind);

    [Fact]
    public void Le_reste_ne_derange_pas() =>
        Assert.Null(Alerts.From(Change("Apprenant ACC101 modifié.", "Apprenant"), Prefs()));

    [Fact]
    public void Les_reglages_coupent_chaque_sorte()
    {
        var prefs = Prefs();
        prefs.AlertPayments = false;
        Assert.Null(Alerts.From(Change("Reçu R-1 : 10 FC pour A (Espèces).", "Recu"), prefs));
        Assert.NotNull(Alerts.From(Change("Sauvegarde faite.", "Sauvegarde"), prefs));
        prefs.Alerts = false;
        Assert.Null(Alerts.From(Change("Sauvegarde faite.", "Sauvegarde"), prefs));
    }

    [Fact]
    public void La_deliberation_previent_les_enseignants_et_le_jury()
    {
        static bool Teacher(string p) => p == "cotes.encoder";
        static bool Jury(string p) => p == "deliberation.gerer";
        var opened = Change("Délibération 2026-2027 : encodage S1 ouvert, S2 fermé.", "ReglagesDeliberation");
        Assert.Equal(AlertKind.Deliberation, Alerts.From(opened, Prefs(), Teacher)?.Kind);
        Assert.Null(Alerts.From(opened, Prefs(), _ => false));

        var submitted = Change("Cotes S1 du cours 1 « Anatomie » remises : 30 cote(s).", "RemiseCotes");
        Assert.Equal("Cotes remises", Alerts.From(submitted, Prefs(), Jury)?.Title);
        Assert.Null(Alerts.From(submitted, Prefs(), Teacher));

        var returned = Change("Feuille S1 du cours 1 « Anatomie » renvoyée à l'enseignant.", "RemiseCotes");
        Assert.Equal("Feuille de cotes renvoyée", Alerts.From(returned, Prefs(), Teacher)?.Title);
        Assert.Null(Alerts.From(returned, Prefs(), Jury));
    }
}
