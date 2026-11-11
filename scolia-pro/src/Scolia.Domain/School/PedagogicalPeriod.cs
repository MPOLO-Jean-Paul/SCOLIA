using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Découpage des évaluations et enseignements, indépendant des mensualités.</summary>
public enum PedagogicalPeriodKind
{
    Trimestre = 1,
    Semestre = 2,
    Sequence = 3,
    Autre = 4,
}

/// <summary>
/// Période pédagogique d'une année (trimestre scolaire, semestre LMD, etc.).
/// Aucune période n'est créée automatiquement dans les bases ITM/IEM :
/// leurs règles existantes de S1/S2 et de jury ne sont pas touchées.
/// </summary>
public sealed class PedagogicalPeriod : Entity
{
    public Guid AcademicYearId { get; set; }
    public PedagogicalPeriodKind Kind { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsClosed { get; set; }

    public static PedagogicalPeriod Define(
        Guid yearId, PedagogicalPeriodKind kind, int number, string name,
        DateOnly startDate, DateOnly endDate, DateOnly yearStart, DateOnly yearEnd)
    {
        if (yearId == Guid.Empty) throw new DomainException("annee", "Année académique obligatoire.");
        if (!Enum.IsDefined(kind)) throw new DomainException("periode", "Type de période pédagogique inconnu.");
        if (number is < 1 or > 12) throw new DomainException("periode", "Numéro de période invalide.");
        if (kind == PedagogicalPeriodKind.Semestre && number > 2)
            throw new DomainException("periode", "Le cycle LMD comporte deux semestres.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new DomainException("periode", "Le libellé de la période doit contenir au plus 80 caractères.");
        if (startDate > endDate || startDate < yearStart || endDate > yearEnd)
            throw new DomainException("periode", "La période pédagogique doit être comprise dans l'année scolaire ou académique.");
        return new PedagogicalPeriod
        {
            AcademicYearId = yearId, Kind = kind, Number = number, Name = name.Trim(),
            StartDate = startDate, EndDate = endDate
        };
    }

    public static bool Conflicts(PedagogicalPeriod left, PedagogicalPeriod right) =>
        left.AcademicYearId == right.AcademicYearId &&
        left.Kind == right.Kind &&
        left.StartDate <= right.EndDate && right.StartDate <= left.EndDate;
}
