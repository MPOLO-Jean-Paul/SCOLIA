namespace Scolia.Bureau.Api;

// Délibération (fusion d'ITMCD) : mêmes formes que le serveur.

public sealed record DeliberationSettingsDto(bool EncodingS1Open, bool EncodingS2Open, bool SupportEnabled, decimal PassPercent,
    bool TeacherUpdates = false, bool TeacherDownloads = true);

public sealed record AnonymousCodeRowDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string? Code, bool IsManual, DateTime? AssignedAtUtc);

public sealed record CodeAssignmentResult(int Assigned, int AlreadyCoded, int Total);

public sealed record DeliberationCourseDto(Guid Id, int Number, string Name, string Semester, decimal MaxScore, string? TeacherName,
    int Students, int GradedS1, int GradedS2, int Supports, bool LockedS1, bool LockedS2, bool SubmittedS1 = false, bool SubmittedS2 = false, int GradedJury = 0);

public sealed record MarkRowDto(Guid EnrollmentId, string? Code, string? Matricule, string? FullName, decimal? Value, decimal? Base, decimal? Retained, bool Failed);

public sealed record MarkSheetDto(Guid CourseId, string Course, string Semester, decimal MaxScore, string Kind, bool Anonymous, bool Locked, bool EncodingOpen,
    IReadOnlyList<MarkRowDto> Rows, DateTime? SubmittedAtUtc = null, string? SubmittedBy = null, int Failed = 0, decimal? Average = null);

public sealed record MarkByCodeResult(string Code, string Course, string Kind, decimal Value, decimal? Previous, int Remaining);

public sealed record CourseOutcomeDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, decimal? Base, decimal Bonus, decimal Points,
    bool Graded, bool Failed);

public sealed record DeliberationRowDto(Guid EnrollmentId, string? Code, string Matricule, string FullName, decimal Points, decimal Max, decimal Percent,
    int Missing, int Failed, string Computed, string Decision, string? JuryOutcome, string? JuryReason, string Mention, int? Rank, bool NearThreshold,
    string? Grade = null, JuryTotalsDto? Totals = null);

public sealed record JuryTotalsDto(decimal Total1, decimal Total1Max, decimal Subtotal1, decimal Subtotal1Max, decimal Subtotal2, decimal Subtotal2Max,
    decimal Total2, decimal Total2Max, decimal General, decimal GeneralMax);

public sealed record AuthoritiesDto(string? Prefect, string? Principal, string? JuryPresident, string? JurySecretary, IReadOnlyList<string>? JuryMembers);

public sealed record JuryEvaluationsResult(int Created, int Existing);

public sealed record CourseStatDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, int Passed, int Failed, int Missing, decimal? Average);

public sealed record DeliberationBoardDto(Guid PromotionId, string Promotion, string Period, string PeriodLabel, string AcademicYear, string Status,
    DateTime? ClosedAtUtc, string? ClosedBy, string? President, string? Members, int ReopenCount, string? LastReopenReason,
    int Students, int Admitted, int Failed, int Incomplete, decimal SuccessRate, decimal PassPercent,
    IReadOnlyList<CourseStatDto> Courses, IReadOnlyList<DeliberationRowDto> Rows);

public sealed record StudentDeliberationDto(DeliberationRowDto Result, string Promotion, string Period, IReadOnlyList<CourseOutcomeDto> Courses);

/// <summary>Cours attribué à l'enseignant connecté (portail enseignant).</summary>
public sealed record TeacherCourseDto(Guid PromotionId, string Promotion, DeliberationCourseDto Course);

public sealed record MarkImportLineDto(int Line, string Identifier, string Value, string Status, decimal? Previous);

public sealed record MarkImportResult(bool Applied, int Saved, int Ready, int Unchanged, int Problems, IReadOnlyList<MarkImportLineDto> Lines);

public sealed record AnnouncementDto(DateTime AtUtc, string Title, string Text, string By);

public sealed record MarkSubmissionDto(Guid CourseId, string Course, string Kind, DateTime SubmittedAtUtc, string SubmittedBy, int Graded, int Students);

public sealed record EncodingProgressRowDto(Guid CourseId, Guid PromotionId, string Promotion, int Number, string Course, string Semester, string Kind,
    string? TeacherName, int Students, int Graded, decimal Percent, int Failed, decimal? Average, DateTime? SubmittedAtUtc, string? SubmittedBy, bool Locked, string Status);

public sealed record EncodingProgressDto(string AcademicYear, string PeriodLabel, int Sheets, int Complete, int Submitted, int Locked, int ExpectedMarks, int GradedMarks,
    decimal Percent, IReadOnlyList<EncodingProgressRowDto> Rows);

public sealed record CodeImportLineDto(int Line, string Identifier, string Code, string Status, Guid? EnrollmentId, string? Matricule, string? FullName, string? PromotionCode);

public sealed record CodeImportResult(bool Applied, int Assigned, int Ready, int Unchanged, int Problems, IReadOnlyList<CodeImportLineDto> Lines);

/// <summary>Enseignant et état de son compte (page « Comptes enseignants » du secrétariat).</summary>
public sealed record TeacherAccountDto(Guid StaffMemberId, string Number, string FullName, string Function, string? Phone, bool StaffActive, int Courses,
    Guid? UserId, string? UserName, bool HasAccount, bool AccountActive, bool MustChangePassword, DateTime? LastLoginUtc, bool IsLocked, bool JuryAccess,
    bool Teaching = true);

/// <summary>Identifiants à remettre à l'enseignant ; le code provisoire n'est montré qu'une fois.</summary>
public sealed record TeacherCredentialsDto(Guid StaffMemberId, Guid UserId, string FullName, string UserName, string ProvisionalCode);

public sealed record NewTeacherResult(Guid StaffMemberId, string Number, string FullName, TeacherCredentialsDto? Account);
