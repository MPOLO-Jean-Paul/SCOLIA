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

/// <summary>
/// Lecture d'une liste de codes officiels existants (export d'ITMCD, feuille Excel copiée, fichier CSV) :
/// une ligne par apprenant, « identifiant ; code » ou « identifiant ; promotion ; code ».
/// L'identifiant est le matricule SCOLIA ou le nom complet (nom, post-nom, prénom). Séparateurs : point-virgule,
/// tabulation ou virgule. Une première ligne d'en-tête (« Nom ; Code »…) est ignorée.
/// </summary>
public static class CodeImportParser
{
    public const int MaxLines = 5000;

    public sealed record Line(int Number, string Identifier, string? Promotion, string Code);

    public static IReadOnlyList<Line> Parse(string? text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Count(l => !string.IsNullOrWhiteSpace(l)) > MaxLines)
            throw new DomainException("import", $"Liste trop longue : {MaxLines} lignes au plus par import.");
        var result = new List<Line>();
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim().TrimStart('﻿');
            if (raw.Length == 0) continue;
            var separator = raw.Contains(';', StringComparison.Ordinal) ? ';' : raw.Contains('\t', StringComparison.Ordinal) ? '\t' : ',';
            var cells = raw.Split(separator).Select(c => c.Trim().Trim('"').Trim()).Where(c => c.Length > 0).ToList();
            if (cells.Count < 2)
            {
                result.Add(new Line(i + 1, raw, null, string.Empty));
                continue;
            }
            // En-tête : la dernière colonne s'appelle « code » (code, code unique, code anonyme…).
            if (result.Count == 0 && TextKey.Normalize(cells[^1]).StartsWith("CODE", StringComparison.Ordinal)) continue;
            result.Add(new Line(i + 1, cells[0], cells.Count >= 3 ? cells[^2] : null, cells[^1]));
        }
        return result;
    }

    /// <summary>Clé de comparaison d'un nom : majuscules, sans accents, espaces simples.</summary>
    public static string NameKey(string? value) => TextKey.Normalize(value);
}
