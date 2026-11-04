using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Domain.Finance;

/// <summary>Un seul calcul pour les comptes, les listes et la validation des prises en charge.</summary>
public static class SponsorshipCoverage
{
    public static decimal AmountFor(FeeItem fee, Sponsorship sponsorship, bool wholeAmounts = true)
    {
        if (!sponsorship.IsActive) return 0m;
        var amount = sponsorship.Mode switch
        {
            SponsorshipMode.Complete => fee.Amount,
            SponsorshipMode.PartielleMensuelle when fee.Kind == FeeKind.Mensualite => sponsorship.MonthlyAmount,
            SponsorshipMode.Historique => fee.Amount * (sponsorship.Lines
                .FirstOrDefault(l => l.FeeItemId == fee.Id)?.Percent ?? 0m) / 100m,
            _ => 0m
        };
        return decimal.Round(Math.Max(0m, amount), wholeAmounts ? 0 : 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Les encaissements existants restent acquis : une couverture complète couvre leur reste
    /// sans créer de solde négatif ou annuler un reçu. Les autres modes sont cumulatifs.
    /// </summary>
    public static decimal TotalFor(FeeItem fee, IEnumerable<Sponsorship> sponsorships,
        decimal alreadyPaid, bool wholeAmounts = true)
    {
        var active = sponsorships.Where(s => s.IsActive).ToList();
        if (active.Any(s => s.Mode == SponsorshipMode.Complete))
            return Math.Max(0m, fee.Amount - alreadyPaid);
        return Math.Clamp(active.Sum(s => AmountFor(fee, s, wholeAmounts)), 0m, Math.Max(0m, fee.Amount));
    }
}
