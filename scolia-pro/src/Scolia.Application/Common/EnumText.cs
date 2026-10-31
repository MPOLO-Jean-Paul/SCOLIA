using Scolia.Domain.Common;

namespace Scolia.Application.Common;

public static class EnumText
{
    /// <summary>Convertit un libellé (avec ou sans accents, quelle que soit la casse) en valeur d'énumération.</summary>
    public static T Parse<T>(string? value, string field, IEnumerable<(T Value, string Label)>? labels = null) where T : struct, Enum
    {
        var key = TextKey.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (key.Length > 0)
        {
            foreach (var name in Enum.GetNames<T>())
                if (TextKey.Normalize(name) == key) return Enum.Parse<T>(name);
            if (labels is not null)
                foreach (var (v, label) in labels)
                    if (TextKey.Normalize(label).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal) == key) return v;
        }
        throw new DomainException(field, $"Valeur non reconnue : {value}.");
    }
}

/// <summary>Montants en toutes lettres (français) pour les reçus.</summary>
public static class FrenchNumbers
{
    private static readonly string[] Units =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
    ];
    private static readonly string[] Tens = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    public static string ToWords(long number)
    {
        if (number == 0) return "zéro";
        if (number < 0) return "moins " + ToWords(-number);
        var parts = new List<string>();
        var milliards = number / 1_000_000_000; number %= 1_000_000_000;
        var millions = number / 1_000_000; number %= 1_000_000;
        var milliers = number / 1000; number %= 1000;
        if (milliards > 0) parts.Add(Below1000(milliards, false) + (milliards > 1 ? " milliards" : " milliard"));
        if (millions > 0) parts.Add(Below1000(millions, false) + (millions > 1 ? " millions" : " million"));
        if (milliers > 0) parts.Add(milliers == 1 ? "mille" : Below1000(milliers, true) + " mille");
        if (number > 0) parts.Add(Below1000(number, false));
        return string.Join(' ', parts);
    }

    private static string Below1000(long n, bool beforeMille)
    {
        var hundreds = n / 100;
        var rest = n % 100;
        var words = new List<string>();
        if (hundreds > 0)
        {
            var cent = hundreds == 1 ? "cent" : Units[hundreds] + " cent";
            if (hundreds > 1 && rest == 0 && !beforeMille) cent += "s";
            words.Add(cent);
        }
        if (rest > 0) words.Add(Below100(rest, beforeMille));
        return string.Join(' ', words);
    }

    private static string Below100(long n, bool beforeMille)
    {
        if (n < 20) return Units[n];
        var ten = n / 10;
        var unit = n % 10;
        if (ten is 7 or 9)
        {
            var baseWord = Tens[ten];
            var tail = Units[10 + unit];
            return baseWord + (ten == 7 && unit == 1 ? " et " : "-") + tail;
        }
        if (unit == 0) return ten == 8 && !beforeMille ? "quatre-vingts" : Tens[ten];
        if (unit == 1 && ten != 8) return Tens[ten] + " et un";
        return Tens[ten] + "-" + Units[unit];
    }

    public static string Amount(decimal amount, string currencyName)
    {
        var whole = (long)decimal.Truncate(amount);
        var cents = (int)decimal.Round((amount - whole) * 100);
        var text = ToWords(whole) + " " + currencyName;
        if (cents > 0) text += " et " + ToWords(cents) + " centimes";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    public static string CurrencyName(string code) => code.ToUpperInvariant() switch
    {
        "CDF" => "francs congolais",
        "USD" => "dollars américains",
        "EUR" => "euros",
        "XAF" or "XOF" => "francs CFA",
        _ => code,
    };
}
