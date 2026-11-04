using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Tests;

/// <summary>Les nouvelles bourses ne cassent ni les anciennes proportions ni les recettes déjà encaissées.</summary>
public sealed class SponsorshipModesTests
{
    private static FeeItem Fee(string label, decimal amount, FeeKind kind = FeeKind.Mensualite)
        => new() { Id = Guid.NewGuid(), Kind = kind, Label = label, Amount = amount,
            DueDate = new DateOnly(2026, 12, 6) };

    [Fact]
    public void Complete_couvre_tous_les_mois_et_tous_les_types_de_frais()
    {
        var sponsor = new Sponsorship { Mode = SponsorshipMode.Complete };
        var fees = new[]
        {
            Fee("Septembre", 45000), Fee("Octobre", 45000),
            Fee("Inscription", 25000, FeeKind.Inscription),
            Fee("Examen", 15000, FeeKind.Examen),
            Fee("Autre", 5000, FeeKind.Autre)
        };
        Assert.All(fees, f => Assert.Equal(f.Amount, SponsorshipCoverage.TotalFor(f, [sponsor], 0m)));
        // Une mensualité ajoutée après l'accord est automatiquement couverte.
        var nouveauMois = Fee("Novembre", 47000);
        Assert.Equal(47000m, SponsorshipCoverage.TotalFor(nouveauMois, [sponsor], 0m));
    }

    [Fact]
    public void Complete_ne_supprime_pas_un_paiement_deja_recu()
    {
        var fee = Fee("Octobre", 50000);
        var sponsor = new Sponsorship { Mode = SponsorshipMode.Complete };
        var covered = SponsorshipCoverage.TotalFor(fee, [sponsor], 15000m);
        Assert.Equal(35000m, covered);
        var account = StudentAccount.ComputeAmounts(
            [new FeeLineInput(fee.Id, fee.Label, fee.Amount, fee.DueDate, 1)],
            new Dictionary<Guid, decimal> { [fee.Id] = covered },
            new Dictionary<Guid, decimal> { [fee.Id] = 15000m },
            new DateOnly(2026, 11, 7));
        Assert.Equal(0m, account.Balance);
        Assert.Equal(15000m, account.TotalPaid);
        Assert.Equal(35000m, account.TotalCovered);
    }

    [Fact]
    public void Partielle_couvre_un_montant_fixe_par_mois_uniquement()
    {
        var months = new[] { Fee("Septembre", 50000), Fee("Octobre", 55000), Fee("Novembre", 52000) };
        var partial = new Sponsorship { Mode = SponsorshipMode.PartielleMensuelle };
        partial.Lines.Add(new SponsorshipLine { FeeItemId = months[0].Id, Amount = 18000m });
        partial.Lines.Add(new SponsorshipLine { FeeItemId = months[1].Id, Amount = 12000m });
        // Le troisième mois reste intégralement à charge.
        Assert.Equal(18000m, SponsorshipCoverage.TotalFor(months[0], [partial], 0m));
        Assert.Equal(12000m, SponsorshipCoverage.TotalFor(months[1], [partial], 0m));
        Assert.Equal(0m, SponsorshipCoverage.TotalFor(months[2], [partial], 0m));
        Assert.Equal(0m, SponsorshipCoverage.TotalFor(Fee("Examen", 25000, FeeKind.Examen), [partial], 0m));
    }

    [Fact]
    public void Ancienne_prise_en_charge_a_pourcentage_est_inchangee()
    {
        var fee = Fee("Octobre", 50001);
        var a = new Sponsorship { Mode = SponsorshipMode.Historique };
        a.Lines.Add(new SponsorshipLine { FeeItemId = fee.Id, Percent = 10m });
        var b = new Sponsorship { Mode = SponsorshipMode.Historique };
        b.Lines.Add(new SponsorshipLine { FeeItemId = fee.Id, Percent = 10m });
        // 20 % de 50001 -> 10000,20, arrondi une seule fois à 10000.
        Assert.Equal(10000m, SponsorshipCoverage.TotalFor(fee, [a, b], 0m));
    }

    [Fact]
    public void Une_prise_en_charge_terminee_ne_reduit_plus_le_solde()
    {
        var fee = Fee("Septembre", 60000);
        var ended = new Sponsorship { Mode = SponsorshipMode.PartielleMensuelle, MonthlyAmount = 20000, IsActive = false };
        Assert.Equal(0m, SponsorshipCoverage.TotalFor(fee, [ended], 0m));
        // Les règles historiques de la prise en charge restent visibles dans les archives.
        Assert.Equal(20000m, SponsorshipCoverage.AmountFor(fee, ended));
    }
}
