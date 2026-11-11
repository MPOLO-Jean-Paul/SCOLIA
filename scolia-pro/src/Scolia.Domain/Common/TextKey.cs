using System.Globalization;
using System.Text;

namespace Scolia.Domain.Common;

/// <summary>Normalisation des textes pour la recherche et la détection des doublons :
/// majuscules, sans accents, espaces uniques (même règle que SCOLIA V79).</summary>
public static class TextKey
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = false;
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            var isSpace = char.IsWhiteSpace(c) || c == ' ' || c == ' ';
            if (isSpace)
            {
                if (!previousSpace) builder.Append(' ');
                previousSpace = true;
                continue;
            }
            previousSpace = false;
            builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    /// <summary>Met en forme un nom propre : « jean-paul mPOLO » devient « Jean-Paul Mpolo ».</summary>
    public static string ProperName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var chars = text.ToLower(culture).ToCharArray();
        var upperNext = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (upperNext && char.IsLetter(chars[i])) { chars[i] = char.ToUpper(chars[i], culture); upperNext = false; }
            else if (chars[i] is ' ' or '-' or '\'' or '’') upperNext = true;
        }
        return new string(chars);
    }
}
