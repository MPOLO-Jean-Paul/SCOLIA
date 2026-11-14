namespace Scolia.Application.Dtos;

public sealed record NationalExamCandidateDto(
    Guid EnrollmentId, string ClassName, string Matricule, string FullName, string Gender,
    DateOnly? BirthDate, string? BirthPlace, bool? Passed, decimal? Percent, string? Reference);

public sealed record NationalExamDto(string Exam, string Label, IReadOnlyList<NationalExamCandidateDto> Candidates);

public sealed record NationalExamResultInput(bool? Passed, decimal? Percent, string? Reference);
