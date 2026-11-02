using Scolia.Domain.Common;
using Scolia.Domain.Discipline;

namespace Scolia.Domain.Academics;

/// <summary>Règles de l'appel des apprenants (version 79) : absences non signées, discipline et alertes.</summary>
public static class AttendanceRules
{
    /// <summary>Absences non justifiées sur la période à partir desquelles une alerte est affichée.</summary>
    public const int AbsenceAlertThreshold = 3;

    /// <summary>Jours de cours consécutifs absents à partir desquels une alerte est affichée.</summary>
    public const int ConsecutiveAlertThreshold = 3;

    /// <summary>Vrai quand une décision du registre exclut l'apprenant ce jour-là : aucune absence automatique n'est créée.</summary>
    public static bool IsExcluded(IEnumerable<DisciplineDecision> decisions, DateOnly day) =>
        decisions.Any(d => DisciplineRules.IsDefinitive(d) && d.EffectiveFrom <= day) || decisions.Any(d => DisciplineRules.IsTemporaryOn(d, day));

    /// <summary>
    /// Absence non signée : un jour de cours confirmé, passé, sans appel, pendant l'inscription, hors exclusion.
    /// Le jour courant reste à signer ; les dates futures et les périodes ambiguës ne produisent rien.
    /// </summary>
    public static bool IsUnsignedAbsence(DateOnly day, DateOnly today, bool confirmedCourseDay, DateOnly enrolledOn, bool excluded, bool hasRecord) =>
        !hasRecord && confirmedCourseDay && day < today && day >= enrolledOn && !excluded;

    /// <summary>Une présence ne peut pas être saisie pendant une exclusion disciplinaire.</summary>
    public static void EnsureCanBePresent(bool excluded, DateOnly day)
    {
        if (excluded)
            throw new DomainException("presences", $"Cet apprenant est exclu par une décision disciplinaire le {day:dd/MM/yyyy} : la présence ne peut pas être saisie.");
    }

    /// <summary>Une absence justifiée exige un motif de 5 à 2000 caractères.</summary>
    public static string Justification(string? note)
    {
        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is < 5 or > 2000) throw new DomainException("presences", "Une absence justifiée exige un motif de 5 à 2000 caractères.");
        return trimmed;
    }

    /// <summary>
    /// Absences non justifiées consécutives les plus longues, sur les jours de cours closes d'une période,
    /// dans l'ordre des dates. Une présence, un retard ou une absence justifiée coupe la série.
    /// </summary>
    public static int LongestUnjustifiedRun(IEnumerable<(DateOnly Day, bool UnjustifiedAbsence)> days)
    {
        var longest = 0;
        var current = 0;
        foreach (var (_, absent) in days.OrderBy(d => d.Day))
        {
            current = absent ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return longest;
    }

    public static bool NeedsAlert(int unjustifiedAbsences, int longestRun) =>
        unjustifiedAbsences >= AbsenceAlertThreshold || longestRun >= ConsecutiveAlertThreshold;
}
