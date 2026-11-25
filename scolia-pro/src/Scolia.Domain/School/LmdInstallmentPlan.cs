namespace Scolia.Domain.School;

/// <summary>Frais annuels par promotion. Pondération n, n-1, ..., 1 ; arrondi dans la monnaie et solde exact.</summary>
public static class LmdInstallmentPlan
{
    public const int MaxCount = 6;
    public static string Name(int index) => $"Frais académiques — {(index == 1 ? "1re" : $"{index}e")} tranche";
    public static bool IsTranche(string? label) => Enumerable.Range(1, MaxCount).Any(i =>
        string.Equals(Name(i), label, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<decimal> Split(decimal total, int count, bool wholeAmounts = false)
    {
        var unit = wholeAmounts ? 1m : 0.01m;
        if (count is < 1 or > MaxCount || total <= 0 || total > 1_000_000_000m ||
            total != decimal.Round(total, wholeAmounts ? 0 : 2))
            throw new ArgumentException("Indiquez un montant annuel positif et de 1 à 6 tranches (deux décimales au plus).");
        var weights = count * (count + 1) / 2;
        var result = new decimal[count];
        decimal assigned = 0;
        for (var i = 0; i < count; i++)
        {
            result[i] = i == count - 1 ? total - assigned :
                decimal.Floor(total / unit * (count - i) / weights) * unit;
            assigned += result[i];
        }
        if (result.Any(v => v <= 0) || result.Zip(result.Skip(1)).Any(p => p.First <= p.Second))
            throw new ArgumentException("Montant trop faible pour des tranches strictement décroissantes : réduisez leur nombre.");
        return result;
    }

    public static IReadOnlyList<DateOnly> DueDates(DateOnly start, DateOnly end, int count)
    {
        if (count is < 1 or > MaxCount || end.DayNumber - start.DayNumber < count - 1)
            throw new ArgumentException("Calendrier insuffisant pour les tranches.");
        if (count == 1) return [start];
        var span = end.DayNumber - start.DayNumber;
        return Enumerable.Range(0, count).Select(i => start.AddDays(span * i / (count - 1))).ToArray();
    }
}
