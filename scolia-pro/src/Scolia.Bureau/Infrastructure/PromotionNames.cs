using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Prédiction des promotions, comme le clavier d'un téléphone : « INF1 » propose « Première Infirmière », puis la deuxième,
/// la troisième et la quatrième ; « ACC » propose les accoucheuses. Les métiers viennent des sections et des promotions
/// déjà créées par l'établissement, puis d'une petite liste des filières courantes. Les propositions se resserrent à
/// chaque lettre tapée dans la désignation.
/// </summary>
public static partial class PromotionNames
{
    public const int DefaultLevels = 4;

    private static readonly string[] Ordinals =
        ["Première", "Deuxième", "Troisième", "Quatrième", "Cinquième", "Sixième", "Septième", "Huitième"];

    /// <summary>Filières courantes, connues même avant la première section : code habituel et métier.</summary>
    private static readonly (string Code, string Name)[] Known =
    [
        ("INFO", "Informatique"),
        ("INF", "Infirmière"),
        ("ACC", "Accoucheuse"),
        ("SF", "Sage-femme"),
        ("SAG", "Sage-femme"),
        ("LAB", "Laborantin"),
        ("PHA", "Pharmacie"),
        ("NUT", "Nutrition"),
        ("KIN", "Kinésithérapie"),
        ("RAD", "Radiologie"),
        ("SP", "Santé publique"),
        ("PED", "Pédagogie"),
        ("COM", "Commerciale"),
        ("MEC", "Mécanique"),
        ("ELE", "Électricité"),
        ("ELEC", "Électricité"),
        ("CON", "Construction"),
        ("COU", "Coupe et couture"),
        ("HOT", "Hôtellerie"),
        ("AGR", "Agronomie"),
        ("VET", "Vétérinaire"),
        ("SEC", "Secrétariat"),
    ];

    /// <summary>Ce que l'établissement a déjà : sections (code, nom) et promotions (code, désignation).</summary>
    public sealed record Context(IReadOnlyList<(string Code, string Name)> Sections, IReadOnlyList<(string Code, string Name)> Promotions)
    {
        public static Context Empty { get; } = new([], []);
    }

    /// <summary>Lettres et numéro d'un code tapé librement : « inf 1 », « INF-1 » et « Inf1 » donnent INF et 1.</summary>
    public static (string Prefix, int? Level) ParseCode(string? text)
    {
        var compact = Normalize(text ?? string.Empty).Replace(" ", "").Replace("-", "").Replace(".", "").ToUpperInvariant();
        var match = CodePattern().Match(compact);
        if (!match.Success) return (string.Empty, null);
        int? level = int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 8 ? n : null;
        return (match.Groups[1].Value, level);
    }

    /// <summary>Niveau lu dans une désignation : « Deuxième… », « 2e… », « 3ème année ».</summary>
    public static int? LevelOf(string? name)
    {
        var words = Normalize(name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            for (var i = 0; i < Ordinals.Length; i++)
                if (word == Normalize(Ordinals[i]) || (i == 0 && word is "premier" or "1er" or "1ere")) return i + 1;
            if (OrdinalNumber().Match(word) is { Success: true } m) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        return null;
    }

    public static string Ordinal(int level) => level >= 1 && level <= Ordinals.Length ? Ordinals[level - 1] : $"{level}e";

    /// <summary>Désignation d'un niveau : « Première Infirmière », ou « Première année de pharmacie » pour une matière.</summary>
    public static string Name(int level, string trade)
    {
        if (string.IsNullOrWhiteSpace(trade)) return $"{Ordinal(level)} année";
        return IsPerson(trade) ? $"{Ordinal(level)} {trade}" : $"{Ordinal(level)} année de {LowerFirst(trade)}";
    }

    /// <summary>Code proposé pour un niveau : INF + 2 = INF2, quatre caractères au plus.</summary>
    public static string Code(string prefix, int level)
    {
        var digits = level.ToString(CultureInfo.InvariantCulture);
        var head = prefix.Length + digits.Length > 4 ? prefix[..Math.Max(1, 4 - digits.Length)] : prefix;
        return head + digits;
    }

    /// <summary>Métier reconnu pour des lettres : d'abord l'établissement, puis les filières courantes.</summary>
    public static string? TradeFor(string letters, Context context)
    {
        var key = Normalize(letters).Replace(" ", "").ToUpperInvariant();
        if (key.Length < 2) return null;
        // Promotions existantes au même préfixe : « ACC2 · Deuxième Accoucheuse » apprend « Accoucheuse ».
        foreach (var (code, name) in context.Promotions)
            if (ParseCode(code).Prefix == key && TradeOf(name) is { Length: > 0 } learned) return learned;
        // Le métier (« Infirmière ») passe avant le nom de la section (« Sciences infirmières »), qui reste proposé après.
        foreach (var (code, name) in Known)
            if (code == key) return name;
        if (SectionTrade(key, context) is { } section) return section;
        // Début d'un nom : « infirm », « accou ».
        var start = Normalize(letters);
        foreach (var (_, name) in context.Sections)
            if (Normalize(name).StartsWith(start, StringComparison.Ordinal)) return name.Trim();
        foreach (var (_, name) in Known)
            if (Normalize(name).StartsWith(start, StringComparison.Ordinal)) return name;
        foreach (var (code, name) in Known)
            if (code.StartsWith(key, StringComparison.Ordinal)) return name;
        return null;
    }

    /// <summary>Nom de la section dont le code est ces lettres : « INF » donne « Sciences infirmières ».</summary>
    private static string? SectionTrade(string key, Context context)
    {
        foreach (var (code, name) in context.Sections)
            if (Normalize(code).ToUpperInvariant() == key && !string.IsNullOrWhiteSpace(name)) return name.Trim();
        return null;
    }

    /// <summary>Préfixe de code d'un métier connu, pour proposer le code quand on commence par la désignation.</summary>
    public static string? PrefixFor(string trade, Context context)
    {
        var target = Normalize(trade);
        foreach (var (code, name) in context.Promotions)
            if (TradeOf(name) is { } learned && Normalize(learned) == target && ParseCode(code).Prefix is { Length: > 0 } p) return p;
        foreach (var (code, name) in context.Sections)
            if (Normalize(name) == target && code.Length is > 0 and <= 3) return code.ToUpperInvariant();
        foreach (var (code, name) in Known)
            if (Normalize(name) == target && code.Length <= 3) return code;
        return null;
    }

    /// <summary>
    /// Propositions de désignation d'après le code et ce qui est déjà tapé. Le niveau du code vient en tête, puis les
    /// autres niveaux ; une désignation déjà complète ne propose plus rien.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string? code, string? typed, Context context, int levels = DefaultLevels)
    {
        typed ??= string.Empty;
        var (prefix, level) = ParseCode(code);
        string? trade = prefix.Length > 0 ? TradeFor(prefix, context) : null;
        var filter = typed;

        // Désignation commencée comme un code (« inf 1 ») ou comme un métier (« accou », « deuxième inf »).
        if (typed.Trim().Length > 0)
        {
            var (typedPrefix, typedLevel) = ParseCode(typed);
            if (typedPrefix.Length >= 2 && (typedLevel is not null || !StartsOrdinal(typed)) && TradeFor(typedPrefix, context) is { } fromCode)
            {
                trade = fromCode;
                level = typedLevel ?? level;
                filter = string.Empty;
            }
            else
            {
                level = LevelOf(typed) ?? level;
                var words = Normalize(typed).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => LevelOf(w) is null && w is not "annee" and not "de" and not "d").ToArray();
                if (words.Length > 0 && !StartsOrdinal(words[^1]) && (trade is null || !Normalize(trade).Contains(words[^1], StringComparison.Ordinal))
                    && TradeFor(words[^1], context) is { } fromWord)
                {
                    trade = fromWord;
                    filter = LevelOf(typed) is { } l ? Ordinal(l) : string.Empty;
                }
            }
        }
        if (trade is null && level is null) return [];

        var max = Math.Max(levels, level ?? 1);
        var order = Enumerable.Range(1, max).OrderBy(l => l == level ? 0 : 1).ThenBy(l => l);
        // Même tournure que les promotions déjà créées : « Première Soignante » plutôt que « Première année de soignante ».
        var learned = trade is null ? null : context.Promotions.Select(p => p.Name)
            .FirstOrDefault(n => Normalize(TradeOf(n) ?? string.Empty) == Normalize(trade));
        var names = order.Select(l => learned is not null && !Normalize(learned).Contains("annee", StringComparison.Ordinal)
            ? $"{Ordinal(l)} {trade}" : Name(l, trade ?? string.Empty)).ToList();
        // Désignation d'après la section, en dernière proposition, pour l'établissement qui préfère cette tournure.
        if (prefix.Length >= 2 && SectionTrade(prefix, context) is { } sectionTrade && trade is not null
            && Normalize(sectionTrade) != Normalize(trade))
            names.Add(Name(level ?? 1, sectionTrade));
        var terms = Normalize(filter).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = names.Where(n => terms.All(t => Normalize(n).Split(' ').Any(w => w.StartsWith(t, StringComparison.Ordinal)))).ToList();
        if (matching.Any(n => Normalize(n) == Normalize(typed))) return [];
        return matching;
    }

    /// <summary>Métier d'une désignation, sans le rang : « Deuxième Accoucheuse » donne « Accoucheuse ».</summary>
    public static string? TradeOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0 && (LevelOf(words[0]) is not null || Normalize(words[0]) is "annee" or "de" or "d" or "du" or "des"))
            words.RemoveAt(0);
        if (words.Count == 0) return null;
        var trade = string.Join(' ', words);
        return char.ToUpperInvariant(trade[0]) + trade[1..];
    }

    /// <summary>Début d'un rang en toutes lettres (« deu », « troi ») : ce n'est pas un métier.</summary>
    private static bool StartsOrdinal(string text)
    {
        var word = Normalize(text);
        return word.Length > 0 && (Ordinals.Any(o => Normalize(o).StartsWith(word, StringComparison.Ordinal))
            || "annee".StartsWith(word, StringComparison.Ordinal) || "premier".StartsWith(word, StringComparison.Ordinal));
    }

    private static bool IsPerson(string trade)
    {
        var last = Normalize(trade).Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        return last.EndsWith("iere", StringComparison.Ordinal) || last.EndsWith("euse", StringComparison.Ordinal)
            || last.EndsWith("rice", StringComparison.Ordinal) || last.EndsWith("femme", StringComparison.Ordinal)
            || last.EndsWith("ier", StringComparison.Ordinal) || last.EndsWith("antin", StringComparison.Ordinal)
            || last.EndsWith("eur", StringComparison.Ordinal) || last.EndsWith("ien", StringComparison.Ordinal)
            || last.EndsWith("ienne", StringComparison.Ordinal) || last.EndsWith("ante", StringComparison.Ordinal)
            || last.EndsWith("ant", StringComparison.Ordinal) || last.EndsWith("iste", StringComparison.Ordinal);
    }

    private static string LowerFirst(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

    /// <summary>Minuscules sans accents : « Première » et « premiere » se valent.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c is '-' or '\'' or '’' ? ' ' : c);
        return MultipleSpaces().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex("^([A-Z]+)([0-9]*)$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^([1-8])(?:e|er|ere|eme|ieme)$")]
    private static partial Regex OrdinalNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();
}
