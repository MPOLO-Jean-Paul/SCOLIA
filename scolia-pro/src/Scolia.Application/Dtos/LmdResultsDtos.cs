namespace Scolia.Application.Dtos;

public sealed record LmdEcLineDto(Guid Id, string Code, string Name, decimal Weight, decimal? Mark, int? Session,
    string? Appreciation);
public sealed record LmdUeLineDto(Guid Id, string Code, string Name, string Semester, decimal Credits,
    decimal? Average, bool Acquired, bool ByCompensation, IReadOnlyList<LmdEcLineDto> Elements);
public sealed record LmdSemesterLineDto(string Semester, decimal? Average, decimal CreditsAcquired,
    decimal CreditsPlanned, bool Complete);

public sealed record LmdStudentLineDto(
    Guid EnrollmentId, Guid StudentId, string Matricule, string FullName,
    LmdSemesterLineDto S1, LmdSemesterLineDto S2, decimal? Average, decimal CreditsAcquired, decimal CreditsPlanned,
    bool Complete, int? Rank, string Computed, string? Jury, string? JuryReason, string? Decision,
    IReadOnlyList<LmdUeLineDto> Units);

/// <summary>Résultats d'une promotion : partiels du S1, première session ou résultats définitifs.</summary>
public sealed record LmdPromotionResultsDto(
    Guid PromotionId, string PromotionName, string Level, string? Filiere, string YearLabel, string Stage,
    string Status, decimal MarkScale, decimal UeValidationMark, decimal MaxDebtCredits,
    string? S1PublishedBy, DateTime? S1PublishedAtUtc, string? FirstSessionClosedBy, DateTime? FirstSessionClosedAtUtc,
    string? ClosedBy, DateTime? ClosedAtUtc, IReadOnlyList<LmdStudentLineDto> Students);

public sealed record LmdMarkLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal? Score, bool Editable);
public sealed record LmdMarkSheetDto(Guid ElementId, string Code, string Name, string UeCode, string Semester,
    int Session, decimal MarkScale, bool Editable, string? LockReason, IReadOnlyList<LmdMarkLineDto> Lines);
public sealed record LmdMarkInput(Guid EnrollmentId, decimal? Score);
public sealed record LmdMarkSheetInput(int Session, IReadOnlyList<LmdMarkInput> Lines);

/// <summary>Décision du jury : Admis, DeuxiemeSession, AdmisAvecDettes, Ajourne ; null pour revenir au calcul.</summary>
public sealed record LmdJuryInput(string? Outcome, string? Reason);

public sealed record LmdDebtDto(Guid Id, Guid StudentId, string Matricule, string FullName, string OriginYear,
    string UeCode, string UeName, string Semester, decimal Credits, decimal? ClearedMark, string? ClearedBy,
    DateTime? ClearedAtUtc);
public sealed record LmdDebtClearInput(decimal Mark);

public sealed record LmdInstallmentInput(decimal Amount, DateOnly DueDate);
public sealed record LmdInstallmentsInput(IReadOnlyList<LmdInstallmentInput> Installments);

public sealed record LmdTranscriptPrintDto(
    string SchoolName, string? SchoolAddress, string? SchoolCity, byte[]? SchoolLogo, string CurrencySymbol,
    LmdPromotionResultsDto Promotion, IReadOnlyList<LmdStudentLineDto> Students);
