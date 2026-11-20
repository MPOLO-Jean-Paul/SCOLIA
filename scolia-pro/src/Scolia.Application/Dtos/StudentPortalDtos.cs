namespace Scolia.Application.Dtos;

public sealed record StudentAccountDto(
    Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionName,
    Guid? UserId, string? UserName, bool Active, bool MustChangePassword, DateTime? LastLoginUtc);

/// <summary>Code provisoire montré une seule fois au secrétariat, à remettre à l'étudiant.</summary>
public sealed record StudentCredentialsDto(Guid StudentId, Guid UserId, string FullName, string Matricule, string UserName, string ProvisionalCode);

public sealed record StudentAccountStatusInput(bool Active);

/// <summary>Résultats publiés d'une inscription : étape la plus avancée publiée par le jury.</summary>
public sealed record StudentPublishedResultDto(
    Guid EnrollmentId, string PromotionName, string Level, string? Filiere, string YearLabel,
    string Stage, string StageLabel, DateTime? PublishedAtUtc, decimal MarkScale, int Headcount,
    LmdStudentLineDto Result);

public sealed record StudentPortalDto(
    string FullName, string Matricule, IReadOnlyList<StudentPublishedResultDto> Results,
    IReadOnlyList<LmdDebtDto> Debts, string? Notice);
