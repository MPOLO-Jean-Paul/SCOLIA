using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Domain.Students;

/// <summary>
/// Cartes d'accès aux examens. Insert au format ISO ID-1/CR80 (85,60 x 53,98 mm),
/// huit cartes sur A4 en deux colonnes et quatre rangées, avec traits de découpe.
/// </summary>
public static class StudentCardRules
{
    public const int CardsPerPage = 8;
    public const float CardWidthMm = 85.6f;
    public const float CardHeightMm = 53.98f;
    public const float ColumnGapMm = 6;
    public const float RowGapMm = 7;
    public const float MarginMm = 16;
    public const float PageWidthMm = 210;
    public const float PageHeightMm = 297;
    public const int MinCopies = 1;
    public const int MaxCopies = 5;
    public const int MaxNameLength = 60;

    private static readonly string[] FrenchMonths =
    [
        "JANVIER", "FÉVRIER", "MARS", "AVRIL", "MAI", "JUIN", "JUILLET", "AOÛT", "SEPTEMBRE", "OCTOBRE", "NOVEMBRE", "DÉCEMBRE",
    ];

    public static int PagesFor(int cards) => cards <= 0 ? 0 : (cards + CardsPerPage - 1) / CardsPerPage;

    /// <summary>Année de la session affichée en grand ; le mois reste inscrit en dessous.</summary>
    public static string SessionYear(string session)
    {
        var last = session.LastIndexOf(' ');
        return last < 0 ? session.Trim() : session[(last + 1)..].Trim();
    }

    /// <summary>Nom complet sur une seule rubrique, sans nom d'apprenant codé en dur.</summary>
    public static string FullName(string lastName, string middleName, string firstName) =>
        string.Join(' ', new[] { lastName, middleName, firstName }
            .Where(v => !string.IsNullOrWhiteSpace(v))).ToUpperInvariant();



    /// <summary>La grille tient sur une page A4 : deux cartes et un espacement, quatre rangées et leurs espacements.</summary>
    public static bool FitsOnPage() =>
        2 * CardWidthMm + ColumnGapMm + 2 * MarginMm <= PageWidthMm && 4 * CardHeightMm + 3 * RowGapMm + 2 * MarginMm <= PageHeightMm;

    public static int Copies(int copies)
    {
        if (copies is < MinCopies or > MaxCopies) throw new DomainException("cartes", $"Le nombre d'exemplaires va de {MinCopies} à {MaxCopies}.");
        return copies;
    }

    /// <summary>Mois et année de la session, en majuscules : « SEPTEMBRE 2026 ».</summary>
    public static string SessionLabel(int month, int year)
    {
        if (month is < 1 or > 12) throw new DomainException("cartes", "Le mois de session est invalide.");
        if (year is < 2000 or > 2100) throw new DomainException("cartes", "L'année de session est invalide.");
        return $"{FrenchMonths[month - 1]} {year}";
    }

    /// <summary>
    /// Couleurs des bandes de filière, en bas des cartes : franches, distinctes entre elles et lisibles à l'impression.
    /// </summary>
    public static readonly IReadOnlyList<string> BandPalette =
    [
        "#1D4ED8", "#D97706", "#059669", "#7C3AED", "#DC2626", "#0891B2",
        "#DB2777", "#65A30D", "#B45309", "#4F46E5", "#0F766E", "#9F1239",
    ];

    /// <summary>
    /// Filière d'une promotion : sa section quand elle en a une, sinon son code sans les chiffres
    /// (ACC1, ACC2 et ACC3 forment la filière ACC). Une même filière a une même bande.
    /// </summary>
    public static string FiliereKey(string? sectionCode, string promotionCode)
    {
        if (!string.IsNullOrWhiteSpace(sectionCode)) return "S:" + sectionCode.Trim().ToUpperInvariant();
        var code = Promotion.NormalizeCode(promotionCode);
        var letters = new string(code.Where(c => !char.IsDigit(c)).ToArray());
        return "P:" + (letters.Length == 0 ? code : letters);
    }

    /// <summary>
    /// Couleur de bande de chaque filière de l'établissement : les filières sont rangées par ordre alphabétique et
    /// reçoivent chacune une couleur de la palette, si bien que deux filières n'ont jamais la même tant qu'il y en a
    /// douze au plus, et qu'une filière garde sa couleur d'une impression à l'autre.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BandColors(IEnumerable<string> filiereKeys) =>
        filiereKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select((key, index) => (key, color: BandPalette[index % BandPalette.Count]))
            .ToDictionary(x => x.key, x => x.color, StringComparer.Ordinal);

    /// <summary>Détecte les matricules doublés de l'année avant tout filtrage.</summary>
    public static HashSet<string> DuplicateMatricules(IEnumerable<string> matricules) =>
        matricules.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any()).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Une carte est imprimée avec une identité complète, sans matricule répété.</summary>
    public static bool IdentityComplete(string lastName, string firstName) =>
        lastName.Trim().Length > 0 && firstName.Trim().Length > 0;

    /// <summary>Un nom trop long pour la carte est refusé plutôt que tronqué ou écrasé.</summary>
    public static void EnsureFits(string text, string field)
    {
        if (text.Length > MaxNameLength)
            throw new DomainException("cartes", $"Le champ « {field} » dépasse {MaxNameLength} caractères : la carte ne peut pas être générée.");
    }

    /// <summary>Les sortis et les suspendus sont exclus par défaut ; une case permet de les inclure.</summary>
    public static bool Eligible(EnrollmentStatus effective, bool includeLeft) => includeLeft || effective == EnrollmentStatus.Actif;
}
