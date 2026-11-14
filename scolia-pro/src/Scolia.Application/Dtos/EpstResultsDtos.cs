namespace Scolia.Application.Dtos;

/// <summary>Case d'un bulletin : <c>Points</c> absent = aucune évaluation saisie (jamais zéro).</summary>
public sealed record EpstCellDto(decimal? Points, decimal Max);

public sealed record EpstPeriodColumnDto(Guid Id, string Code, string TermCode, bool IsExam);
public sealed record EpstTermColumnDto(string Code, IReadOnlyList<string> PeriodCodes);
public sealed record EpstSubjectColumnDto(Guid Id, string Code, string Name, decimal PeriodMax);

public sealed record EpstSubjectLineDto(
    Guid SubjectId, string Code, string Name,
    IReadOnlyList<EpstCellDto> Periods, IReadOnlyList<EpstCellDto> Terms,
    decimal? Total, decimal Max, decimal? Percent, bool Failed);

public sealed record EpstPupilResultDto(
    Guid EnrollmentId, Guid StudentId, string Matricule, string FullName,
    IReadOnlyList<EpstSubjectLineDto> Subjects,
    IReadOnlyList<EpstCellDto> PeriodTotals, IReadOnlyList<EpstCellDto> TermTotals,
    decimal? Total, decimal Max, decimal? Percent,
    int MissingCells, int FailedSubjects, int? Rank, IReadOnlyList<int?> TermRanks,
    IReadOnlyList<EpstConductCellDto> Conduct, string? Decision = null);

/// <summary>Application et conduite d'une période de travail (codes E, TB, B, AB, M, MA).</summary>
public sealed record EpstConductCellDto(Guid PeriodId, string PeriodCode, string? Application, string? Conduct);

public sealed record EpstConductLineInput(Guid EnrollmentId, string? Application, string? Conduct);
public sealed record EpstConductInput(Guid PeriodId, IReadOnlyList<EpstConductLineInput> Lines);

/// <summary>Résultats d'une classe du primaire ou du secondaire selon le règlement validé.</summary>
public sealed record EpstClassResultsDto(
    Guid PromotionId, string ClassName, string CycleName, string Scope, string YearLabel,
    bool ShowRank, decimal PassPercent, decimal SubjectPassPercent, int Headcount,
    IReadOnlyList<EpstTermColumnDto> Terms, IReadOnlyList<EpstPeriodColumnDto> Periods,
    IReadOnlyList<EpstSubjectColumnDto> Subjects, IReadOnlyList<EpstPupilResultDto> Pupils,
    string? RegulationValidatedBy, DateTime? RegulationValidatedAtUtc,
    string DeliberationStatus = "Ouverte");

/// <summary>Bulletins officiels de l'établissement, établis selon son règlement validé.</summary>
public sealed record EpstBulletinPrintDto(
    string SchoolName, string? SchoolAddress, string? SchoolCity,
    string? Ministry, string? Division, byte[]? SchoolLogo,
    EpstClassResultsDto Class, IReadOnlyList<EpstPupilResultDto> Pupils);
