namespace Scolia.Bureau.Api;

// Contrats des écrans EPST et LMD, alignés sur les DTO de l'API (champs utiles seulement).

public sealed record AcademicRegulationDto(
    Guid? Id, string Scope, string Label, string Status, bool Saved,
    int TermsCount, int PeriodsPerTerm, decimal ExamMaxFactor,
    decimal PassPercent, decimal SubjectPassPercent, int MaxRepechageSubjects, bool ShowRank,
    decimal MarkScale, decimal UeValidationMark, bool CompensateWithinUe, bool CompensateBetweenUe,
    decimal MaxDebtCredits, string? LegalReference, string? ValidatedByName, DateTime? ValidatedAtUtc);

public sealed record EpstCellDto(decimal? Points, decimal Max);
public sealed record EpstPeriodColumnDto(Guid Id, string Code, string TermCode, bool IsExam);
public sealed record EpstTermColumnDto(string Code, IReadOnlyList<string> PeriodCodes);
public sealed record EpstSubjectLineDto(Guid SubjectId, string Code, string Name,
    IReadOnlyList<EpstCellDto> Periods, IReadOnlyList<EpstCellDto> Terms,
    decimal? Total, decimal Max, decimal? Percent, bool Failed);
public sealed record EpstConductCellDto(Guid PeriodId, string PeriodCode, string? Application, string? Conduct);
public sealed record EpstPupilResultDto(
    Guid EnrollmentId, Guid StudentId, string Matricule, string FullName,
    IReadOnlyList<EpstSubjectLineDto> Subjects, IReadOnlyList<EpstCellDto> PeriodTotals, IReadOnlyList<EpstCellDto> TermTotals,
    decimal? Total, decimal Max, decimal? Percent, int MissingCells, int FailedSubjects, int? Rank,
    IReadOnlyList<int?> TermRanks, IReadOnlyList<EpstConductCellDto> Conduct, string? Decision);
public sealed record EpstClassResultsDto(
    Guid PromotionId, string ClassName, string CycleName, string Scope, string YearLabel,
    bool ShowRank, decimal PassPercent, decimal SubjectPassPercent, int Headcount,
    IReadOnlyList<EpstTermColumnDto> Terms, IReadOnlyList<EpstPeriodColumnDto> Periods,
    IReadOnlyList<EpstPupilResultDto> Pupils, string DeliberationStatus);

public sealed record EpstRepechageLineDto(Guid SubjectId, string Name, decimal? SubjectPercent, decimal? Score, decimal? MaxScore);
public sealed record EpstDecisionDto(
    Guid EnrollmentId, string Matricule, string FullName, decimal? Percent, int? Rank, int FailedSubjects, int MissingCells,
    string Computed, string? Jury, string? JuryReason, string? Final, string Effective, string EffectiveLabel,
    IReadOnlyList<EpstRepechageLineDto> Repechage);
public sealed record EpstDeliberationDto(
    Guid PromotionId, string ClassName, string YearLabel, string Status,
    string? FirstSessionClosedBy, DateTime? FirstSessionClosedAtUtc, string? ClosedBy, DateTime? ClosedAtUtc,
    decimal PassPercent, decimal SubjectPassPercent, int MaxRepechageSubjects, IReadOnlyList<EpstDecisionDto> Pupils);

public sealed record NationalExamCandidateDto(
    Guid EnrollmentId, string ClassName, string Matricule, string FullName, string Gender,
    DateOnly? BirthDate, string? BirthPlace, bool? Passed, decimal? Percent, string? Reference);
public sealed record NationalExamDto(string Exam, string Label, IReadOnlyList<NationalExamCandidateDto> Candidates);

public sealed record LmdSemesterLineDto(string Semester, decimal? Average, decimal CreditsAcquired, decimal CreditsPlanned, bool Complete);
public sealed record LmdUeLineDto(Guid Id, string Code, string Name, string Semester, decimal Credits,
    decimal? Average, bool Acquired, bool ByCompensation);
public sealed record LmdStudentLineDto(
    Guid EnrollmentId, Guid StudentId, string Matricule, string FullName,
    LmdSemesterLineDto S1, LmdSemesterLineDto S2, decimal? Average, decimal CreditsAcquired, decimal CreditsPlanned,
    bool Complete, int? Rank, string Computed, string? Jury, string? JuryReason, string? Decision,
    IReadOnlyList<LmdUeLineDto> Units);
public sealed record LmdPromotionResultsDto(
    Guid PromotionId, string PromotionName, string Level, string? Filiere, string YearLabel, string Stage,
    string Status, decimal MarkScale, decimal UeValidationMark, decimal MaxDebtCredits,
    IReadOnlyList<LmdStudentLineDto> Students);

public sealed record LmdMarkLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal? Score, bool Editable);
public sealed record LmdMarkSheetDto(Guid ElementId, string Code, string Name, string UeCode, string Semester,
    int Session, decimal MarkScale, bool Editable, string? LockReason, IReadOnlyList<LmdMarkLineDto> Lines);

public sealed record LmdDebtDto(Guid Id, Guid StudentId, string Matricule, string FullName, string OriginYear,
    string UeCode, string UeName, string Semester, decimal Credits, decimal? ClearedMark, string? ClearedBy, DateTime? ClearedAtUtc);

public sealed record CurriculumTeachingItemDto(
    Guid ItemId, Guid PromotionId, string PromotionName, string Kind, string Code, string Name, string Semester,
    string? UeCode, Guid? TeacherId, string? TeacherName);
public sealed record CurriculumTeacherDto(Guid Id, string FullName);
public sealed record EpstMarkSheetLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal? Score);
public sealed record EpstMarkSheetDto(
    Guid SubjectId, string SubjectName, string PromotionName, Guid PeriodId, string PeriodCode, bool IsExam,
    string EvaluationCode, string Title, decimal MaxScore, bool Editable, string? LockReason,
    IReadOnlyList<string> ExistingCodes, IReadOnlyList<EpstMarkSheetLineDto> Lines);
