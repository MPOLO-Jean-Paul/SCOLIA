namespace Scolia.Application.Dtos;

/// <summary>Branche EPST ou élément constitutif LMD, avec son enseignant actuel.</summary>
public sealed record CurriculumTeachingItemDto(
    Guid ItemId, Guid PromotionId, string PromotionName, string Kind, string Code, string Name, string Semester,
    string? UeCode, Guid? TeacherId, string? TeacherName);

public sealed record CurriculumTeacherDto(Guid Id, string FullName);
public sealed record CurriculumAssignInput(Guid TeacherId, bool ConfirmReplace = false);
public sealed record CurriculumUnassignInput(string? Reason);

/// <summary>Feuille de notes d'une branche EPST pour une période et une évaluation.</summary>
public sealed record EpstMarkSheetLineDto(Guid EnrollmentId, string Matricule, string FullName, decimal? Score);
public sealed record EpstMarkSheetDto(
    Guid SubjectId, string SubjectName, string PromotionName, Guid PeriodId, string PeriodCode, bool IsExam,
    string EvaluationCode, string Title, decimal MaxScore, bool Editable, string? LockReason,
    IReadOnlyList<string> ExistingCodes, IReadOnlyList<EpstMarkSheetLineDto> Lines);
public sealed record EpstMarkLineInput(Guid EnrollmentId, decimal? Score);
public sealed record EpstMarkSheetInput(Guid PeriodId, string EvaluationCode, string Title, decimal MaxScore,
    IReadOnlyList<EpstMarkLineInput> Lines);
