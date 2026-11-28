namespace Scolia.Application.Dtos;

/// <summary>
/// Cycle d'une même école ou d'un complexe scolaire. Le montant est une
/// proposition annuelle : il ne crée jamais de rubrique d'encaissement.
/// </summary>
public sealed record SchoolCycleConfigurationItemDto(
    Guid Id, string Kind, string Name, string? AuthorizationNumber,
    bool IsActive, bool ChargeMonthlyTuition, decimal MonthlyAmount, int ClassCount);

public sealed record SchoolCycleConfigurationDto(
    string InstitutionType, string Governance, Guid? AcademicYearId,
    IReadOnlyList<SchoolCycleConfigurationItemDto> Cycles);

/// <summary>
/// Configuration de l'offre de cycles et grille de référence de l'année
/// courante. Les écoles ITM/IM ne passent jamais par ce contrat.
/// </summary>
public sealed record SchoolCycleConfigurationInput(
    IReadOnlyList<SchoolCycleConfigurationDraft> Cycles);

public sealed record SchoolCycleConfigurationDraft(
    string Kind, string Name, string? AuthorizationNumber,
    bool ChargeMonthlyTuition, decimal MonthlyAmount);
