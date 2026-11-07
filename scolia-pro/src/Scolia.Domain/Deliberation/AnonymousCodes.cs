using System.Security.Cryptography;
using Scolia.Domain.Common;

namespace Scolia.Domain.Deliberation;

/// <summary>
/// Codes anonymes officiels. Le format généré est celui d'ITMCD : lettre, chiffre, chiffre, lettre, chiffre, lettre
/// (par exemple P13W5R), avec des lettres sans I ni O pour éviter la confusion avec 1 et 0.
/// Un code officiel existant peut aussi être saisi tel quel (par exemple ITM-26-AB12), après normalisation.
/// </summary>
public static class AnonymousCodeRules
{
    public const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Digits = "0123456789";
    public const int MinLength = 4;
    public const int MaxLength = 16;
    /// <summary>Gabarit du code généré : L = lettre, D = chiffre.</summary>
    public const string Pattern = "LDDLDL";

    /// <summary>Majuscules, sans accents ni espaces ; le tiret est conservé (format ITM-AA-XXXX).</summary>
    public static string Normalize(string? value) =>
        TextKey.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);

    public static bool IsValid(string? value)
    {
        var code = Normalize(value);
        return code.Length is >= MinLength and <= MaxLength
            && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-')
            && code.Any(char.IsAsciiLetterOrDigit) && code[0] != '-' && code[^1] != '-';
    }

    /// <summary>Code officiel saisi à la main : normalisé, ou refusé avec un message clair.</summary>
    public static string Official(string? value)
    {
        var code = Normalize(value);
        if (!IsValid(code))
            throw new DomainException("code", $"Code anonyme invalide : {MinLength} à {MaxLength} lettres ou chiffres (le tiret est admis), par exemple P13W5R ou ITM-26-AB12.");
        return code;
    }

    /// <summary>Tire un code libre, au hasard cryptographique, absent de <paramref name="taken"/>.</summary>
    public static string Generate(ISet<string> taken, Func<int, int>? random = null)
    {
        random ??= RandomNumberGenerator.GetInt32;
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var chars = Pattern.Select(p => p == 'L' ? Letters[random(Letters.Length)] : Digits[random(Digits.Length)]).ToArray();
            var code = new string(chars);
            if (!taken.Contains(code)) return code;
        }
        throw new DomainException("code", "Impossible de tirer un code anonyme libre : réessayez.");
    }
}
