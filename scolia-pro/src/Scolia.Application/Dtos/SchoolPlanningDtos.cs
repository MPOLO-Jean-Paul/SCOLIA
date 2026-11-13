namespace Scolia.Application.Dtos;

public sealed record SchoolPeriodDto(Guid Id, string Code, string Name, string TermCode,
    int Order, string Mode, DateOnly StartDate, DateOnly EndDate);
public sealed record SchoolPeriodCreate(string Code, string Name, string TermCode, DateOnly StartDate, DateOnly EndDate);
public sealed record AnnualSubjectDto(Guid Id, Guid PromotionId, string Code, string Name, decimal Coefficient, decimal Hours, bool IsActive);
public sealed record AnnualSubjectCreate(string Code, string Name, decimal Coefficient, decimal Hours);
