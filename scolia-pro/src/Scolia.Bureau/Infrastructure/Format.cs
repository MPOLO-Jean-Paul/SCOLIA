using System.Globalization;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Mise en forme française des montants et des dates.</summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Money(decimal value, string symbol) => $"{value.ToString("N0", Fr)} {symbol}";

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Fr);

    public static string Percent(decimal value) => $"{value.ToString("0.0", Fr)} %";

    public static string FullName(string last, string middle, string first) =>
        string.Join(' ', new[] { last, middle, first }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
