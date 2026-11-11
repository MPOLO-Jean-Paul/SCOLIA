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
/// Une période d'un cycle et les évaluations/observations correspondantes.
/// Les périodes sans cote doivent rester visibles dans les futurs bulletins :
/// une absence de note ne vaut jamais zéro.
/// </summary>
public sealed record SchoolCycleBulletinPeriodDto(
    Guid Id, string Code, string Name, string TermCode,
    DateOnly StartDate, DateOnly EndDate, string Mode,
    IReadOnlyList<SchoolCycleAssessmentItemDto> Entries);

/// <summary>
/// Regroupement de périodes défini par le calendrier du cycle.
/// Les intitulés sont indicatifs : aucune décision ministérielle ni
/// calcul de réussite n'est déduit du terme.
/// </summary>
public sealed record SchoolCycleBulletinTermDto(
    string Code, string Label, IReadOnlyList<SchoolCycleBulletinPeriodDto> Periods);

/// <summary>
/// Aperçu non officiel. Aucune moyenne, décision de réussite ou classement
/// n'est induit tant que le règlement pédagogique propre au cycle n'est pas validé.
/// </summary>
/// <summary>
/// Grille annuelle de matières pour un relevé provisoire primaire/secondaire.
/// Une cellule peut contenir plusieurs évaluations distinctes : aucun total
/// ou examen n'est calculé sans barème validé. Une matière non cotée reste visible.
/// </summary>
public sealed record SchoolCycleBulletinSubjectPeriodDto(
    Guid SchoolCyclePeriodId, string PeriodCode, string PeriodName,
    IReadOnlyList<SchoolCycleAssessmentItemDto> Entries);

public sealed record SchoolCycleBulletinSubjectDto(
    Guid CurriculumItemId, string SubjectCode, string SubjectName,
    decimal Coefficient, bool IsActive,
    IReadOnlyList<SchoolCycleBulletinSubjectPeriodDto> Periods);

public sealed record SchoolCycleBulletinPreviewDto(
    Guid EnrollmentId, Guid SchoolCycleId, string Cycle, string Promotion,
    string AcademicYear, IReadOnlyList<SchoolCycleAssessmentItemDto> Entries,
    bool IsOfficial = false,
    string Notice = "Aperçu pédagogique provisoire : ni bulletin officiel ni décision de réussite.",
    IReadOnlyList<SchoolCycleBulletinTermDto>? Terms = null,
    string TemplateCode = "CYCLE_PROVISOIRE",
    string DocumentTitle = "Aperçu pédagogique",
    string EntryHeading = "Activités ou branches",
    string PeriodHeading = "Période",
    int ClassLevel = 0,
    string GradeBand = "Cycle non précisé",
    string? OptionLabel = null,
    IReadOnlyList<SchoolCycleBulletinSubjectDto>? SubjectRows = null);
