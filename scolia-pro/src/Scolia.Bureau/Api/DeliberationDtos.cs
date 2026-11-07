namespace Scolia.Bureau.Api;

// Délibération (fusion d'ITMCD) : mêmes formes que le serveur.

public sealed record DeliberationSettingsDto(bool EncodingS1Open, bool EncodingS2Open, bool SupportEnabled, decimal PassPercent);

public sealed record AnonymousCodeRowDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string? Code, bool IsManual, DateTime? AssignedAtUtc);

public sealed record CodeAssignmentResult(int Assigned, int AlreadyCoded, int Total);

public sealed record DeliberationCourseDto(Guid Id, int Number, string Name, string Semester, decimal MaxScore, string? TeacherName,
    int Students, int GradedS1, int GradedS2, int Supports, bool LockedS1, bool LockedS2);

public sealed record MarkRowDto(Guid EnrollmentId, string? Code, string? Matricule, string? FullName, decimal? Value, decimal? Base, decimal? Retained, bool Failed);

public sealed record MarkSheetDto(Guid CourseId, string Course, string Semester, decimal MaxScore, string Kind, bool Anonymous, bool Locked, bool EncodingOpen,
    IReadOnlyList<MarkRowDto> Rows);

public sealed record MarkByCodeResult(string Code, string Course, string Kind, decimal Value, decimal? Previous, int Remaining);

public sealed record CourseOutcomeDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, decimal? Base, decimal Bonus, decimal Points,
    bool Graded, bool Failed);

public sealed record DeliberationRowDto(Guid EnrollmentId, string? Code, string Matricule, string FullName, decimal Points, decimal Max, decimal Percent,
    int Missing, int Failed, string Computed, string Decision, string? JuryOutcome, string? JuryReason, string Mention, int? Rank, bool NearThreshold);

public sealed record CourseStatDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, int Passed, int Failed, int Missing, decimal? Average);

public sealed record DeliberationBoardDto(Guid PromotionId, string Promotion, string Period, string PeriodLabel, string AcademicYear, string Status,
    DateTime? ClosedAtUtc, string? ClosedBy, string? President, string? Members, int ReopenCount, string? LastReopenReason,
    int Students, int Admitted, int Failed, int Incomplete, decimal SuccessRate, decimal PassPercent,
    IReadOnlyList<CourseStatDto> Courses, IReadOnlyList<DeliberationRowDto> Rows);

public sealed record StudentDeliberationDto(DeliberationRowDto Result, string Promotion, string Period, IReadOnlyList<CourseOutcomeDto> Courses);
