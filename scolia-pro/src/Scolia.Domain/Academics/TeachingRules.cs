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

    public static decimal Coefficient(decimal value)
    {
        if (value is <= 0 or > 100) throw new DomainException("cours", "Le coefficient doit être compris entre 0 et 100.");
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
