using Scolia.Domain.Common;

namespace Scolia.Domain.Academics;

/// <summary>Cours dispensé dans une promotion.</summary>
public sealed class Course : Entity
{
    public Guid PromotionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Pondération dans la moyenne générale (crédits ou coefficient).</summary>
    public decimal Coefficient { get; set; } = 1;
    public int Hours { get; set; }
    public Guid? TeacherId { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum AssessmentKind { Interrogation = 0, Devoir = 1, TravauxPratiques = 2, Examen = 3 }

/// <summary>Évaluation d'un cours (interrogation, examen…).</summary>
public sealed class Assessment : Entity
{
    public Guid CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public AssessmentKind Kind { get; set; }
    public int Term { get; set; } = 1;
    public DateOnly Date { get; set; }
    public decimal MaxScore { get; set; } = 20;
    /// <summary>Poids de l'évaluation dans la moyenne du cours.</summary>
    public decimal Weight { get; set; } = 1;
}

public sealed class Grade : Entity
{
    public Guid AssessmentId { get; set; }
    public Guid EnrollmentId { get; set; }
    /// <summary>Note obtenue ; vide si l'apprenant était absent.</summary>
    public decimal? Score { get; set; }
    public string? Comment { get; set; }
}

public enum AttendanceStatus { Present = 0, Absent = 1, Retard = 2, AbsenceJustifiee = 3 }

public sealed class AttendanceRecord : Entity
{
    public Guid EnrollmentId { get; set; }
    public DateOnly Date { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
}

public enum IncidentSeverity { Mineur = 0, Moyen = 1, Grave = 2 }

public sealed class DisciplineIncident : Entity
{
    public Guid EnrollmentId { get; set; }
    public DateOnly Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Sanction { get; set; }
    public IncidentSeverity Severity { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
}

/// <summary>Calcul des moyennes : moyenne pondérée des évaluations d'un cours, ramenée sur 20,
/// puis moyenne générale pondérée par les coefficients des cours. Les absences ne comptent pas.</summary>
public static class GradeCalculator
{
    public sealed record ScoredAssessment(decimal? Score, decimal MaxScore, decimal Weight);

    public static decimal? CourseAverage(IEnumerable<ScoredAssessment> items, decimal scale = 20)
    {
        decimal weighted = 0, weights = 0;
        foreach (var item in items)
        {
            if (item.Score is null || item.MaxScore <= 0 || item.Weight <= 0) continue;
            weighted += item.Score.Value / item.MaxScore * scale * item.Weight;
            weights += item.Weight;
        }
        return weights == 0 ? null : decimal.Round(weighted / weights, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal? GeneralAverage(IEnumerable<(decimal? Average, decimal Coefficient)> courses)
    {
        decimal total = 0, coefficients = 0;
        foreach (var (average, coefficient) in courses)
        {
            if (average is null || coefficient <= 0) continue;
            total += average.Value * coefficient;
            coefficients += coefficient;
        }
        return coefficients == 0 ? null : decimal.Round(total / coefficients, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Mention selon la moyenne sur 20.</summary>
    public static string Decision(decimal? average) => average switch
    {
        null => "Non évalué",
        >= 16 => "Très bien",
        >= 14 => "Bien",
        >= 12 => "Assez bien",
        >= 10 => "Admis",
        _ => "Ajourné",
    };
}
