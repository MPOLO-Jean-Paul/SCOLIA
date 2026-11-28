namespace Scolia.Application.Dtos;

public sealed record SchoolPeriodDto(Guid Id, string Code, string Name, string TermCode,
    int Order, string Mode, DateOnly StartDate, DateOnly EndDate, bool IsExam = false);
/// <summary><paramref name="IsExam"/> : examen de fin de trimestre ou de semestre.</summary>
public sealed record SchoolPeriodCreate(string Code, string Name, string TermCode, DateOnly StartDate, DateOnly EndDate,
    bool IsExam = false);
public sealed record AnnualSubjectDto(Guid Id, Guid PromotionId, string Code, string Name, decimal Coefficient, decimal Hours,
    bool IsActive, decimal? PeriodMaxScore = null);
/// <summary><paramref name="PeriodMaxScore"/> : maximum de la branche pour une période (primaire, secondaire).</summary>
public sealed record AnnualSubjectCreate(string Code, string Name, decimal Coefficient, decimal Hours,
    decimal? PeriodMaxScore = null);
public sealed record AnnualSubjectMaxUpdate(decimal? PeriodMaxScore);
