namespace Scolia.Application.Dtos;

// ---------- Délibération (fusion d'ITMCD) ----------

public sealed record DeliberationSettingsDto(bool EncodingS1Open, bool EncodingS2Open, bool SupportEnabled, decimal PassPercent);

/// <summary>Code anonyme d'un apprenant inscrit. <c>Code</c> vide : aucun code encore attribué.</summary>
public sealed record AnonymousCodeRowDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string? Code, bool IsManual, DateTime? AssignedAtUtc);

public sealed record CodeAssignmentRequest(Guid? PromotionId);
public sealed record CodeAssignmentResult(int Assigned, int AlreadyCoded, int Total);

/// <summary>Code officiel saisi à la main, ou tirage d'un nouveau code (<c>Code</c> vide). Remplacer un code exige un motif.</summary>
public sealed record CodeChangeRequest(string? Code, string? Reason);

/// <summary>Résolution d'un code pour l'encodage anonyme : la promotion, jamais le nom.</summary>
public sealed record CodeLookupDto(string Code, Guid EnrollmentId, Guid PromotionId, string PromotionCode);

/// <summary>Cours vu par la délibération : organisation, maxima et avancement de l'encodage.</summary>
public sealed record DeliberationCourseDto(Guid Id, int Number, string Name, string Semester, decimal MaxScore, string? TeacherName,
    int Students, int GradedS1, int GradedS2, int Supports, bool LockedS1, bool LockedS2);

public sealed record MarkRowDto(Guid EnrollmentId, string? Code, string? Matricule, string? FullName, decimal? Value, decimal? Base, decimal? Retained, bool Failed);

/// <summary>Feuille de cotes d'un cours pour une nature (S1, S2 ou Appui). En mode anonyme, noms et matricules sont masqués.</summary>
public sealed record MarkSheetDto(Guid CourseId, string Course, string Semester, decimal MaxScore, string Kind, bool Anonymous, bool Locked, bool EncodingOpen,
    IReadOnlyList<MarkRowDto> Rows);

/// <summary>Cote saisie dans la feuille. <c>Value</c> vide efface la cote.</summary>
public sealed record MarkInput(Guid EnrollmentId, decimal? Value);

public sealed record MarkByCodeRequest(string Code, Guid CourseId, string Kind, decimal Value);
public sealed record MarkByCodeResult(string Code, string Course, string Kind, decimal Value, decimal? Previous, int Remaining);

public sealed record CourseOutcomeDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, decimal? Base, decimal Bonus, decimal Points,
    bool Graded, bool Failed);

public sealed record DeliberationRowDto(Guid EnrollmentId, string? Code, string Matricule, string FullName, decimal Points, decimal Max, decimal Percent,
    int Missing, int Failed, string Computed, string Decision, string? JuryOutcome, string? JuryReason, string Mention, int? Rank, bool NearThreshold);

public sealed record CourseStatDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, int Passed, int Failed, int Missing, decimal? Average);

/// <summary>Tableau de délibération d'une promotion pour une période : statistiques, cours et apprenants.</summary>
public sealed record DeliberationBoardDto(Guid PromotionId, string Promotion, string Period, string PeriodLabel, string AcademicYear, string Status,
    DateTime? ClosedAtUtc, string? ClosedBy, string? President, string? Members, int ReopenCount, string? LastReopenReason,
    int Students, int Admitted, int Failed, int Incomplete, decimal SuccessRate, decimal PassPercent,
    IReadOnlyList<CourseStatDto> Courses, IReadOnlyList<DeliberationRowDto> Rows);

public sealed record StudentDeliberationDto(DeliberationRowDto Result, string Promotion, string Period, IReadOnlyList<CourseOutcomeDto> Courses);

/// <summary>Décision motivée du jury. <c>Outcome</c> vide retire la décision et rend le résultat calculé.</summary>
public sealed record JuryDecisionRequest(Guid PromotionId, string Period, Guid EnrollmentId, string? Outcome, string? Reason);

public sealed record SessionCloseRequest(Guid PromotionId, string Period, string? Password, string? President, string? Members, string? Notes);
public sealed record SessionReopenRequest(Guid PromotionId, string Period, string? Password, string? Reason);
