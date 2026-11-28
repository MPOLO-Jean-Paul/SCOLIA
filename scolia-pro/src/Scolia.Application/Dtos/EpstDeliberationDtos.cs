namespace Scolia.Application.Dtos;

public sealed record EpstRepechageLineDto(Guid SubjectId, string Name, decimal? SubjectPercent,
    decimal? Score, decimal? MaxScore);

public sealed record EpstDecisionDto(
    Guid EnrollmentId, string Matricule, string FullName,
    decimal? Percent, int? Rank, int FailedSubjects, int MissingCells,
    string Computed, string? Jury, string? JuryReason, string? Final, string Effective, string EffectiveLabel,
    IReadOnlyList<EpstRepechageLineDto> Repechage);

public sealed record EpstDeliberationDto(
    Guid PromotionId, string ClassName, string YearLabel, string Status,
    string? FirstSessionClosedBy, DateTime? FirstSessionClosedAtUtc, string? ClosedBy, DateTime? ClosedAtUtc,
    decimal PassPercent, decimal SubjectPassPercent, int MaxRepechageSubjects,
    IReadOnlyList<EpstDecisionDto> Pupils);

/// <summary>Décision du jury : Passe, Repechage, Redouble, Reoriente ; null pour revenir au calcul.</summary>
public sealed record EpstJuryInput(string? Outcome, string? Reason);

public sealed record EpstRepechageMarkInput(Guid SubjectId, decimal? Score, decimal MaxScore);
public sealed record EpstRepechageInput(IReadOnlyList<EpstRepechageMarkInput> Marks);

public sealed record EpstPalmaresPrintDto(
    string SchoolName, string? SchoolAddress, string? SchoolCity, string? Ministry, string? Division,
    byte[]? SchoolLogo, string CycleName, EpstDeliberationDto Deliberation);
