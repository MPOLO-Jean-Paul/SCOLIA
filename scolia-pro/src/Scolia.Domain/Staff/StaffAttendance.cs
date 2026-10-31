using Scolia.Domain.Common;

namespace Scolia.Domain.Staff;

public enum StaffMark { Entree = 0, Sortie = 1 }

/// <summary>Signature d'entrée ou de sortie d'un membre du personnel. Une signature enregistrée ne se modifie pas.</summary>
public sealed class StaffAttendanceMark : Entity
{
    public Guid StaffMemberId { get; set; }
    public DateOnly Date { get; set; }
    public StaffMark Kind { get; set; }
    /// <summary>Heure locale du poste au moment de la signature.</summary>
    public TimeOnly Time { get; set; }
    public int UtcOffsetMinutes { get; set; }
    public string AccountName { get; set; } = string.Empty;
    /// <summary>Vrai pour une entrée à 08:00:00 ou après.</summary>
    public bool IsLate { get; set; }
}

/// <summary>Justification d'une journée d'absence. Une annulation garde le motif initial et le motif d'annulation.</summary>
public sealed class StaffDayJustification : Entity
{
    public Guid StaffMemberId { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? CancelReason { get; set; }

    public bool IsActive => CancelReason is null;
}

public enum StaffDayStatus { EnAttente, Present, PresentEnRetard, Absent, Justifiee, SansObjet }

/// <summary>Une conversion : trois retards signés sur une même semaine donnent une absence, datée du troisième retard.</summary>
public sealed record LateConversion(DateOnly Date, DateOnly WeekStart);

/// <summary>Règles du pointage des enseignants (version 79), sans accès à la base.</summary>
public static class StaffAttendanceRules
{
    public static readonly TimeOnly LateFrom = new(8, 0, 0);
    public const int LatesPerAbsence = 3;

    public static bool IsLate(TimeOnly time) => time >= LateFrom;

    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Un membre est attendu entre son entrée en fonction et la fin de son activité, si elle est renseignée.</summary>
    public static bool InActivity(StaffMember member, DateOnly day) =>
        member.IsActive && day >= member.HireDate && (member.ExitDate is null || day <= member.ExitDate);

    /// <summary>
    /// Statut d'une journée. Deux signatures donnent présent, ou présent en retard. Sans deux signatures, la journée
    /// reste en attente le jour même, puis devient absente sur un jour ouvrable passé de la période d'activité.
    /// Les jours futurs, non ouvrables ou hors activité ne créent aucune absence.
    /// </summary>
    public static StaffDayStatus DayStatus(DateOnly day, DateOnly today, bool applicable, bool entry, bool exit, bool late, bool justified)
    {
        if (entry && exit) return late ? StaffDayStatus.PresentEnRetard : StaffDayStatus.Present;
        if (!applicable || day > today) return StaffDayStatus.SansObjet;
        if (day == today) return StaffDayStatus.EnAttente;
        return justified ? StaffDayStatus.Justifiee : StaffDayStatus.Absent;
    }

    /// <summary>
    /// Conversion hebdomadaire des retards : la semaine va du lundi au dimanche, chaque groupe de trois retards
    /// donne une absence datée du troisième. Les retards restants ne sont pas reportés à la semaine suivante.
    /// </summary>
    public static IReadOnlyList<LateConversion> Conversions(IEnumerable<DateOnly> lateDays)
    {
        var result = new List<LateConversion>();
        foreach (var week in lateDays.Distinct().GroupBy(WeekStart).OrderBy(g => g.Key))
        {
            var ordered = week.OrderBy(d => d).ToList();
            for (var i = LatesPerAbsence - 1; i < ordered.Count; i += LatesPerAbsence)
                result.Add(new LateConversion(ordered[i], week.Key));
        }
        return result;
    }

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Un motif d'absence est obligatoire et doit rester raisonnablement court.</summary>
    public static string Reason(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < 5 or > 2000) throw new DomainException("pointage", "Le motif doit compter entre 5 et 2000 caractères.");
        return trimmed;
    }
}
