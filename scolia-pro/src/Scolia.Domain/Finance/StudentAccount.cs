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

    /// <param name="coverage">Pourcentage couvert par rubrique (somme des prises en charge actives).</param>
    /// <param name="payments">Montants affectés par rubrique (reçus valides uniquement).</param>
    public static StudentAccount Compute(
        IEnumerable<FeeLineInput> fees,
        IReadOnlyDictionary<Guid, decimal> coverage,
        IReadOnlyDictionary<Guid, decimal> payments,
        DateOnly asOf,
        bool wholeAmounts = true)
    {
        var lines = new List<AccountLine>();
        foreach (var fee in fees.OrderBy(f => f.DueDate).ThenBy(f => f.Order))
        {
            var percent = Math.Clamp(coverage.TryGetValue(fee.FeeItemId, out var p) ? p : 0m, 0m, 100m);
            var covered = fee.Amount * percent / 100m;
            covered = wholeAmounts ? decimal.Round(covered, 0, MidpointRounding.AwayFromZero) : decimal.Round(covered, 2, MidpointRounding.AwayFromZero);
            var net = fee.Amount - covered;
            var paid = payments.TryGetValue(fee.FeeItemId, out var paidAmount) ? paidAmount : 0m;
            var remaining = Math.Max(0m, net - paid);
            var status = net == 0 ? LineStatus.Couvert : remaining == 0 ? LineStatus.Paye : paid > 0 ? LineStatus.Partiel : LineStatus.Impaye;
            lines.Add(new AccountLine(fee.FeeItemId, fee.Label, fee.DueDate, fee.Order, fee.PeriodId, fee.Amount, covered, net, paid, remaining, status, remaining > 0 && fee.DueDate <= asOf, fee.Kind));
        }
        return new StudentAccount(lines);
    }
}

/// <summary>Règlement d'un paiement : les mois choisis, chacun soldé en entier (aucun règlement partiel).</summary>
public static class PaymentAllocator
{
    /// <summary>Règlement par mois : chaque rubrique choisie est soldée en entier, à son montant dû.
    /// Aucun règlement partiel n'est enregistré : le montant du versement est la somme des mois choisis.</summary>
    public static IReadOnlyList<(Guid FeeItemId, decimal Amount)> Months(StudentAccount account, IReadOnlyCollection<Guid> selected)
    {
        if (selected.Count == 0) throw new DomainException("rubriques", "Choisissez au moins un mois à payer.");
        var notTuition = account.Lines.Where(l => selected.Contains(l.FeeItemId) && l.Kind != FeeKind.Mensualite).ToList();
        if (notTuition.Count > 0) throw new DomainException("rubriques", "Seuls les mois de scolarité se règlent par mois.");
        var unknown = selected.Where(id => account.Lines.All(l => l.FeeItemId != id)).ToList();
        if (unknown.Count > 0) throw new DomainException("rubriques", "Une rubrique choisie n'appartient pas à cette inscription.");
        var plan = new List<(Guid, decimal)>();
        foreach (var line in account.Lines.Where(l => selected.Contains(l.FeeItemId)))
        {
            if (line.Remaining <= 0) throw new DomainException("rubriques", $"« {line.Label} » est déjà réglé : actualisez la liste des mois.");
            plan.Add((line.FeeItemId, line.Remaining));
        }
        return plan;
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
