using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Domain.Finance;

public enum LineStatus { Impaye = 0, Partiel = 1, Paye = 2, Couvert = 3 }

/// <summary>Donnée d'entrée : une rubrique de frais de la promotion.</summary>
public sealed record FeeLineInput(Guid FeeItemId, string Label, decimal Amount, DateOnly DueDate, int Order, Guid? PeriodId = null, FeeKind Kind = FeeKind.Mensualite);

/// <summary>État d'une rubrique pour un apprenant.</summary>
public sealed record AccountLine(
    Guid FeeItemId, string Label, DateOnly DueDate, int Order, Guid? PeriodId,
    decimal Amount, decimal Covered, decimal Net, decimal Paid, decimal Remaining,
    LineStatus Status, bool IsOverdue, FeeKind Kind = FeeKind.Mensualite);

/// <summary>Compte d'un apprenant pour une année : calcul pur, sans base de données.</summary>
public sealed class StudentAccount
{
    public IReadOnlyList<AccountLine> Lines { get; }
    public decimal TotalFees { get; }
    public decimal TotalCovered { get; }
    public decimal TotalNet { get; }
    public decimal TotalPaid { get; }
    public decimal Balance { get; }
    public decimal Overdue { get; }
    public int OverdueCount { get; }
    public DateOnly? OldestOverdueDate { get; }

    private StudentAccount(IReadOnlyList<AccountLine> lines)
    {
        Lines = lines;
        TotalFees = lines.Sum(l => l.Amount);
        TotalCovered = lines.Sum(l => l.Covered);
        TotalNet = lines.Sum(l => l.Net);
        TotalPaid = lines.Sum(l => l.Paid);
        Balance = lines.Sum(l => l.Remaining);
        Overdue = lines.Where(l => l.IsOverdue).Sum(l => l.Remaining);
        OverdueCount = lines.Count(l => l.IsOverdue);
        OldestOverdueDate = lines.Where(l => l.IsOverdue).Select(l => (DateOnly?)l.DueDate).Min();
    }

    /// <summary>Compatibilité des anciens calculs de couverture en pourcentage.</summary>
    /// <param name="coverage">Pourcentage couvert par rubrique.</param>
    /// <param name="payments">Montant déjà encaissé par rubrique.</param>
    public static StudentAccount Compute(
        IEnumerable<FeeLineInput> fees,
        IReadOnlyDictionary<Guid, decimal> coverage,
        IReadOnlyDictionary<Guid, decimal> payments,
        DateOnly asOf,
        bool wholeAmounts = true)
    {
        var feeList = fees.ToList();
        var covered = feeList.ToDictionary(f => f.FeeItemId, f =>
        {
            var percent = Math.Clamp(coverage.GetValueOrDefault(f.FeeItemId), 0m, 100m);
            return decimal.Round(f.Amount * percent / 100m, wholeAmounts ? 0 : 2, MidpointRounding.AwayFromZero);
        });
        return ComputeAmounts(feeList, covered, payments, asOf, wholeAmounts);
    }

    /// <summary>Nouveau calcul au montant exact couvert, sans conversion en pourcentages.</summary>
    public static StudentAccount ComputeAmounts(
        IEnumerable<FeeLineInput> fees,
        IReadOnlyDictionary<Guid, decimal> coveredAmounts,
        IReadOnlyDictionary<Guid, decimal> payments,
        DateOnly asOf,
        bool wholeAmounts = true)
    {
        var lines = new List<AccountLine>();
        foreach (var fee in fees.OrderBy(f => f.DueDate).ThenBy(f => f.Order))
        {
            var covered = Math.Clamp(coveredAmounts.GetValueOrDefault(fee.FeeItemId), 0m, fee.Amount);
            covered = decimal.Round(covered, wholeAmounts ? 0 : 2, MidpointRounding.AwayFromZero);
            var net = fee.Amount - covered;
            var paid = payments.TryGetValue(fee.FeeItemId, out var paidAmount) ? paidAmount : 0m;
            var remaining = Math.Max(0m, net - paid);
            var status = net == 0 ? LineStatus.Couvert : remaining == 0 ? LineStatus.Paye : paid > 0 ? LineStatus.Partiel : LineStatus.Impaye;
            lines.Add(new AccountLine(fee.FeeItemId, fee.Label, fee.DueDate, fee.Order, fee.PeriodId, fee.Amount,
                covered, net, paid, remaining, status, remaining > 0 && fee.DueDate <= asOf, fee.Kind));
        }
        return new StudentAccount(lines);
    }

}

/// <summary>Répartit un versement total ou partiel sur les rubriques choisies (mois et autres frais), par ancienneté.</summary>
public static class PaymentAllocator
{
    /// <summary>
    /// Le montant demandé doit être strictement positif et ne jamais dépasser les restes dus.
    /// Un mois déjà payé ou étranger à l'inscription est refusé. L'absence de montant signifie
    /// que chaque mois sélectionné est soldé (compatibilité avec les anciens clients).
    /// </summary>
    /// <param name="monthsOnly">Vrai pour les encaissements par lot, limités aux mensualités.</param>
    public static IReadOnlyList<(Guid FeeItemId, decimal Amount)> Months(
        StudentAccount account, IReadOnlyCollection<Guid> selected, decimal? amount = null, bool monthsOnly = false)
    {
        if (selected.Count == 0) throw new DomainException("rubriques", "Choisissez au moins un mois ou un frais à payer.");
        if (selected.Distinct().Count() != selected.Count)
            throw new DomainException("rubriques", "Un même mois a été choisi plusieurs fois.");
        var chosen = account.Lines.Where(l => selected.Contains(l.FeeItemId))
            .OrderBy(l => l.DueDate).ThenBy(l => l.Order).ToList();
        if (chosen.Count != selected.Count)
            throw new DomainException("rubriques", "Une rubrique choisie n'appartient pas à cette inscription.");
        if (monthsOnly && chosen.Any(l => l.Kind != FeeKind.Mensualite))
            throw new DomainException("rubriques", "Seuls les mois de scolarité se règlent par lot : encaissez les autres frais un par un.");
        var settled = chosen.FirstOrDefault(l => l.Remaining <= 0);
        if (settled is not null)
            throw new DomainException("rubriques", $"« {settled.Label} » est déjà réglé : actualisez la liste des mois.");

        var maximum = chosen.Sum(l => l.Remaining);
        var toAllocate = amount ?? maximum;
        if (toAllocate <= 0 || toAllocate > maximum)
            throw new DomainException("montant", $"Indiquez un montant positif ne dépassant pas le solde des rubriques choisies ({maximum}).");

        var allocations = new List<(Guid FeeItemId, decimal Amount)>();
        foreach (var line in chosen)
        {
            if (toAllocate <= 0) break;
            var allocated = Math.Min(line.Remaining, toAllocate);
            allocations.Add((line.FeeItemId, allocated));
            toAllocate -= allocated;
        }
        return allocations;
    }
}

/// <summary>Balance âgée des créances (norme de suivi du recouvrement).</summary>
public static class Aging
{
    public static string Bucket(DateOnly? oldestOverdue, DateOnly asOf)
    {
        if (oldestOverdue is null) return "À jour";
        var days = asOf.DayNumber - oldestOverdue.Value.DayNumber;
        return days switch
        {
            <= 30 => "0-30 jours",
            <= 60 => "31-60 jours",
            <= 90 => "61-90 jours",
            _ => "Plus de 90 jours",
        };
    }
}
