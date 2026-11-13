namespace Scolia.Application.Dtos;

/// <summary>Organisation LMD enregistrée localement ; ne constitue pas un agrément ministériel.</summary>
public sealed record LmdStructureDto(Guid Id, string Kind, Guid? ParentId, string Code, string Name, bool IsActive);
public sealed record LmdStructureInput(string Kind, Guid? ParentId, string Code, string Name);

public sealed record LmdSemesterDto(Guid Id, string Code, string Name, int Order, DateOnly StartDate, DateOnly EndDate);
public sealed record LmdSemesterCalendarInput(DateOnly FirstStart, DateOnly FirstEnd, DateOnly SecondStart, DateOnly SecondEnd);

public sealed record LmdProgramItemDto(Guid Id, Guid PromotionId, Guid? AcademicUnitId,
    Guid? ParentItemId, string Kind, string Code, string Name, string Semester,
    decimal Credits, decimal Coefficient, decimal Hours);
public sealed record LmdTeachingUnitInput(Guid AcademicUnitId, string Semester,
    string Code, string Name, decimal Credits, decimal Hours,
    string ElementCode, string ElementName, decimal ElementHours);
public sealed record LmdTeachingElementInput(Guid ParentItemId, string Code, string Name,
    decimal Hours);
