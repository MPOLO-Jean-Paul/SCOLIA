using Scolia.Domain.Common;
using Scolia.Domain.Finance;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

/// <summary>Situation de paiement retenue pour trier la liste des apprenants.</summary>
public enum PaymentSituation { EnOrdre, EnRetard, JamaisPaye, PrisEnCharge }

/// <summary>
/// Tri par paiement de la liste des apprenants. Les pris en charge ne sont jamais comptés parmi ceux
/// « en ordre » ou « en retard » : ils ont leur propre choix. Avec un mois précis, la situation porte sur
/// la mensualité de ce mois seulement.
/// </summary>
public static class PaymentFilter
{
    public static PaymentSituation? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "en_ordre" or "ordre" => PaymentSituation.EnOrdre,
        "en_retard" or "retard" => PaymentSituation.EnRetard,
        "jamais_paye" or "jamais" => PaymentSituation.JamaisPaye,
        "pris_en_charge" => PaymentSituation.PrisEnCharge,
        _ => throw new DomainException("payment", $"Situation de paiement inconnue : {value}.")
    };

    public static bool Matches(PaymentSituation filter, AccountService.EnrollmentAccount entry, Guid? periodId)
    {
        if (filter == PaymentSituation.PrisEnCharge) return entry.IsSponsored;
        if (entry.IsSponsored) return false;
        var account = entry.Account;
        if (periodId is { } pid)
        {
            // Mensualité du mois choisi ; un apprenant sans ce mois (inscrit après, promotion sans ce mois) n'est pas retenu.
            var lines = account.Lines.Where(l => l.PeriodId == pid && l.Kind == FeeKind.Mensualite && l.Net > 0).ToList();
            if (lines.Count == 0) return false;
            return filter switch
            {
                PaymentSituation.EnOrdre => lines.All(l => l.Remaining <= 0),
                PaymentSituation.EnRetard => lines.Any(l => l.Remaining > 0),
                PaymentSituation.JamaisPaye => lines.All(l => l.Paid <= 0),
                _ => false
            };
        }
        return filter switch
        {
            PaymentSituation.EnOrdre => account.Overdue <= 0,
            PaymentSituation.EnRetard => account.Overdue > 0,
            // Rien versé de toute l'année alors qu'il y a des frais à sa charge.
            PaymentSituation.JamaisPaye => account.TotalPaid <= 0 && account.TotalNet > 0,
            _ => false
        };
    }
}
