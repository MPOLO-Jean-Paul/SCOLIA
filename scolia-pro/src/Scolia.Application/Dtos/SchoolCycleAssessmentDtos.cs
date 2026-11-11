namespace Scolia.Application.Dtos;

/// <summary>
/// Saisie par élève et par période. Le serveur déduit la classe, le cycle,
/// l'année et le mode d'évaluation à partir de l'inscription et de la période.
/// </summary>
public sealed record SchoolCycleAssessmentInput(
    Guid SchoolCyclePeriodId,
    string EvaluationCode,
    string Title,
    Guid? CurriculumItemId,
    string? Observation,
    decimal? Score,
    decimal? MaxScore,
    string? Comment,
    long? ExpectedVersion = null);

public sealed record SchoolCycleAssessmentItemDto(
    Guid Id, Guid SchoolCyclePeriodId, string PeriodCode,
    string EvaluationCode, string Title, string Mode, Guid? CurriculumItemId,
    string? Observation, decimal? Score, decimal? MaxScore,
    string? Comment, long Version);

/// <summary>
/// Aperçu non officiel. Aucune moyenne, décision de réussite ou classement
/// n'est induit tant que le règlement pédagogique propre au cycle n'est pas validé.
/// </summary>
public sealed record SchoolCycleBulletinPreviewDto(
    Guid EnrollmentId, Guid SchoolCycleId, string Cycle, string Promotion,
    string AcademicYear, IReadOnlyList<SchoolCycleAssessmentItemDto> Entries,
    bool IsOfficial = false,
    string Notice = "Aperçu pédagogique provisoire : ni bulletin officiel ni décision de réussite.");
