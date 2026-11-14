namespace Scolia.Application.Dtos;

/// <summary>
/// Règlement pédagogique d'une portée (cycle EPST ou université) pour l'année courante.
/// <paramref name="Saved"/> vaut false tant que la proposition SCOLIA n'a pas été enregistrée.
/// </summary>
public sealed record AcademicRegulationDto(
    Guid? Id, string Scope, string Label, string Status, bool Saved,
    int TermsCount, int PeriodsPerTerm, decimal ExamMaxFactor,
    decimal PassPercent, decimal SubjectPassPercent, int MaxRepechageSubjects, bool ShowRank,
    decimal MarkScale, decimal UeValidationMark, bool CompensateWithinUe, bool CompensateBetweenUe,
    decimal MaxDebtCredits, string? LegalReference, string? ValidatedByName, DateTime? ValidatedAtUtc);

public sealed record AcademicRegulationInput(
    int TermsCount, int PeriodsPerTerm, decimal ExamMaxFactor,
    decimal PassPercent, decimal SubjectPassPercent, int MaxRepechageSubjects, bool ShowRank,
    decimal MarkScale, decimal UeValidationMark, bool CompensateWithinUe, bool CompensateBetweenUe,
    decimal MaxDebtCredits, string? LegalReference);

public sealed record AcademicRegulationReopen(string? Reason);
