using Scolia.Domain.Common;

namespace Scolia.Domain.Students;

/// <summary>
/// Cartes d'accès aux examens (version 79). Format A4 portrait, cartes de 92 × 63 mm, deux colonnes et quatre rangées,
/// espacements de 6 mm entre colonnes et de 7 mm entre rangées, marges de 10 mm.
/// </summary>
public static class StudentCardRules
{
    public const int CardsPerPage = 8;
    public const float CardWidthMm = 92;
    public const float CardHeightMm = 63;
    public const float ColumnGapMm = 6;
    public const float RowGapMm = 7;
    public const float MarginMm = 10;
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

    /// <summary>Couleur de la bande de promotion. Une autre promotion utilise une bande neutre.</summary>
    public static string ColorOf(string promotionCode) => promotionCode.Trim().ToUpperInvariant() switch
    {
        "TLM1" or "TML1" => "#2563EB",
        "ACC1" => "#FACC15",
        "ACC2" => "#7C3AED",
        "ACC3" => "#86EFAC",
        "INF1" => "#7DD3FC",
        "INF2" => "#F9A8D4",
        "INF3" => "#22C55E",
        _ => "#CBD5E1",
    };

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
