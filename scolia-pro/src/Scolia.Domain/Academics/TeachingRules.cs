using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Staff;

namespace Scolia.Domain.Academics;

/// <summary>Règles pédagogiques de la version 79 : numéros de cours, volumes, cotations et enseignants disponibles.</summary>
public static class TeachingRules
{
    public const int MaxCourseNumber = 9999;
    public const int MaxNameLength = 160;

    public static int Number(int value)
    {
        if (value is < 1 or > MaxCourseNumber)
            throw new DomainException("cours", $"Le numéro du cours doit être compris entre 1 et {MaxCourseNumber}.");
        return value;
    }

    public static string Name(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxNameLength)
            throw new DomainException("cours", $"L'intitulé du cours est obligatoire et compte au plus {MaxNameLength} caractères.");
        return trimmed;
    }

    /// <summary>Volume horaire ou cotation : positif, avec deux décimales au maximum.</summary>
    public static decimal Positive(decimal value, string label)
    {
        if (value <= 0) throw new DomainException("cours", $"{label} doit être positif.");
        if (decimal.Round(value, 2) != value) throw new DomainException("cours", $"{label} accepte au maximum deux décimales.");
        return value;
    }

    /// <summary>Maxima d'un cours, comme dans ITMCD : de 1 à 2000 points.</summary>
    public const decimal MaxMaxScore = 2000;

    /// <summary>
    /// Pondération (coefficient) : poids du cours dans la moyenne sur 20 des bulletins de « Cours et notes ».
    /// La délibération ne l'utilise pas : elle additionne les points obtenus sur le maxima de chaque cours.
    /// </summary>
    public static decimal Coefficient(decimal value)
    {
        if (value is <= 0 or > 100) throw new DomainException("cours", "La pondération (coefficient) doit être comprise entre 0 et 100.");
        if (decimal.Round(value, 2) != value) throw new DomainException("cours", "La pondération accepte au maximum deux décimales.");
        return value;
    }

    /// <summary>
    /// Maxima (cotation maximale) : total de points du cours. Les cotes de la délibération (S1, S2, jury) vont de 0 à ce
    /// maxima et le cours est en échec sous sa moitié. Convention ITM : maxima = pondération × 10.
    /// </summary>
    public static decimal MaxScore(decimal value)
    {
        Positive(value, "Le maxima du cours");
        if (value < 1 || value > MaxMaxScore) throw new DomainException("cours", $"Le maxima du cours doit être compris entre 1 et {MaxMaxScore:0} points.");
        return value;
    }

    /// <summary>Un enseignant est proposé si sa période d'activité recoupe l'année scolaire.</summary>
    public static bool IsAvailableDuring(StaffMember member, AcademicYear year) =>
        member.IsActive && member.HireDate <= year.EndDate && (member.ExitDate is null || member.ExitDate >= year.StartDate);

    /// <summary>Premier numéro libre de la promotion, archives comprises.</summary>
    public static int NextFreeNumber(IEnumerable<int> used)
    {
        var taken = used.ToHashSet();
        var candidate = 1;
        while (taken.Contains(candidate) && candidate < MaxCourseNumber) candidate++;
        return candidate;
    }
}
