using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scolia.Bureau.Api;

/// <summary>État de l'installation : une configuration existe-t-elle sur ce serveur ?</summary>
public sealed record SetupStatusDto(bool IsConfigured, string? SchoolName);

/// <summary>Année scolaire proposée par le serveur pour l'assistant.</summary>
public sealed record YearProposalDto(string Label);

/// <summary>Révision des données du serveur : elle change à chaque écriture, d'un poste quelconque.</summary>
public sealed record RevisionDto(long Revision);

/// <summary>Ligne du registre disciplinaire, telle que le serveur la renvoie.</summary>
public sealed record DisciplineRowDto(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Measure, string Reason,
    DateOnly DecisionDate, DateOnly? FirstDay, DateOnly? LastDay, int? Days, DateOnly? ReturnFrom, DateTime? AppointmentAt, bool IsAutomatic,
    string? ParentName, string? ParentPhone, string Reception, string? ReceptionNote, string State, string? CancelReason, string RecordedBy,
    string EffectiveStatus, string AcademicYear, bool CanReceive, bool CanAbsent, bool CanCancel);

// Contrats de l'API SCOLIA, tels que servis en JSON (noms en camelCase, dates ISO, montants décimaux).

public sealed record SessionDto(
    Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, string[] Permissions, bool MustChangePassword,
    string SchoolName, string? SchoolShortName, string? CurrentYear, string CurrencySymbol, string PhoneCountryCode);

public sealed record SchoolDto(
    string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone, string? Email,
    string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, bool HasLogo, string PhoneCountryCode,
    string? CardMinistry = null, string? CardDirection = null, string? CardDivision = null, string? CardZone = null,
    string? CardInstitute = null, string? CardAddress = null);

/// <summary>Apprenant proposé pour les cartes d'accès, avec son diagnostic.</summary>
public sealed record CardCandidateDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Status, bool Eligible,
    bool IdentityComplete, bool DuplicateMatricule);

public sealed record CardBatchDto(Guid Id, DateTime CreatedAtUtc, string Session, string PromotionLabel, int StudentCount, int CardCount, int PageCount,
    string Format, string FileName, string CreatedBy);

public sealed record PeriodDto(Guid Id, int Order, string Name, int Month, int CalendarYear, DateOnly DueDate);

public sealed record AcademicYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, string Status, bool IsCurrent, IReadOnlyList<PeriodDto> Periods);

public sealed record SectionDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record FeeItemDto(Guid Id, string Kind, string Label, decimal Amount, DateOnly DueDate, int Order, Guid? PeriodId);

public sealed record PromotionDto(
    Guid Id, Guid AcademicYearId, string Code, string Name, Guid? SectionId, string? SectionName, int Level, int? Capacity, bool IsActive,
    string? NextPromotionCode, decimal MonthlyFee, decimal AnnualTotal, int StudentCount, IReadOnlyList<FeeItemDto> Fees);

public sealed record DashboardDto(
    string AcademicYear, int ActiveStudents, int NewThisMonth, decimal CollectedTotal, decimal CollectedThisMonth, decimal CollectedToday,
    decimal ExpectedToDate, decimal Outstanding, decimal Overdue, decimal RecoveryRate, int StudentsUpToDate, int StudentsLate,
    decimal ExpensesThisMonth, IReadOnlyList<SeriesPoint> MonthlyCollections, IReadOnlyList<PromotionStat> Promotions,
    IReadOnlyList<SeriesPoint> Aging, IReadOnlyList<ReceiptSummaryDto> LatestReceipts, int GenderMale, int GenderFemale);

public sealed record SeriesPoint(string Label, decimal Value, decimal? Secondary);

public sealed record TodayDto(DateOnly Date, int ReceiptsToday, decimal CollectedToday, decimal ExpensesToday, bool CashClosed,
    int AttendanceMarked, int SuspendedToday, IReadOnlyList<TodayPersonDto> Appointments, IReadOnlyList<TodayPersonDto> Birthdays);

public sealed record TodayPersonDto(Guid StudentId, string Name, string Promotion, string Detail);

public sealed record PromotionStat(Guid Id, string Code, string Name, int Students, decimal Expected, decimal Collected, decimal Outstanding, decimal Overdue, decimal RecoveryRate);

public sealed record StudentListItem(
    Guid Id, Guid? EnrollmentId, string Matricule, string LastName, string MiddleName, string FirstName, string Gender,
    string? PromotionCode, string? Status, string? GuardianPhone, decimal Balance, decimal Overdue);

public sealed record StudentDto(
    Guid Id, string Matricule, string LastName, string MiddleName, string FirstName, string Gender, DateOnly? BirthDate, string? BirthPlace,
    string? Nationality, string? Address, string? Phone, string? Email, string? GuardianName, string? GuardianRelation, string? GuardianPhone,
    string? GuardianPhone2, string? Notes, bool HasPhoto, IReadOnlyList<EnrollmentDto> Enrollments, long Version);

public sealed record EnrollmentDto(
    Guid Id, Guid AcademicYearId, string AcademicYear, Guid PromotionId, string PromotionCode, string PromotionName, DateOnly EnrolledOn,
    string Status, DateOnly? ExitDate, string? ExitReason, bool IsReEnrollment);

public sealed record DuplicateCandidateDto(Guid Id, string Matricule, string FullName, string? PromotionCode);

public sealed record AccountLineDto(
    Guid FeeItemId, string Label, DateOnly DueDate, decimal Amount, decimal Covered, decimal Net, decimal Paid, decimal Remaining,
    string Status, bool IsOverdue, string Kind);

public sealed record ReceiptSummaryDto(Guid Id, string Number, DateOnly Date, decimal Amount, string Method, string Status, string Cashier, string? Allocations);

public sealed record AccountDto(
    Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string AcademicYear, string EnrollmentStatus,
    IReadOnlyList<AccountLineDto> Lines, decimal TotalFees, decimal TotalCovered, decimal TotalNet, decimal TotalPaid, decimal Balance,
    decimal Overdue, int OverdueCount, string AgingBucket, IReadOnlyList<ReceiptSummaryDto> Receipts, string? GuardianPhone, string? WhatsAppLink);

public sealed record AllocationDto(Guid FeeItemId, string Label, decimal Amount, decimal RemainingAfter);

public sealed record PaymentPreviewDto(IReadOnlyList<AllocationDto> Allocations, decimal BalanceAfter);

public sealed record ReceiptDto(
    Guid Id, string Number, string VerificationCode, DateOnly Date, Guid StudentId, string Matricule, string StudentName, string PromotionCode,
    string AcademicYear, decimal Amount, string Method, string? Reference, string? PayerName, string? PayerPhone, string? Note, string Cashier,
    string Status, DateTime? CancelledAtUtc, string? CancelledBy, string? CancellationReason, IReadOnlyList<AllocationDto> Allocations, DateTime CreatedAtUtc);

public sealed record ReceiptRowDto(
    Guid Id, string Number, DateOnly Date, string Matricule, string StudentName, string PromotionCode, decimal Amount, string Method,
    string Status, string Cashier, string? Reference);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, decimal? Sum);

public sealed record SponsorshipLineDto(Guid FeeItemId, string Label, decimal Percent, decimal Amount);

public sealed record SponsorshipDto(Guid Id, Guid EnrollmentId, string SponsorName, string Reason, DateOnly EffectiveFrom, bool IsActive, IReadOnlyList<SponsorshipLineDto> Lines, decimal CoveredTotal);

public sealed record SponsorshipListItem(
    Guid Id, Guid EnrollmentId, string Matricule, string FullName, string Promotion, string SponsorName, string Reason, DateOnly EffectiveFrom,
    bool IsActive, decimal Covered);

public sealed record DebtorDto(
    Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, decimal Balance, decimal Overdue, int OverdueCount,
    string Periods, string AgingBucket, string? GuardianName, string? GuardianPhone, string? WhatsAppLink, string? SmsLink);

public sealed record ExpenseDto(
    Guid Id, string Number, DateOnly Date, string Category, string Beneficiary, string Description, decimal Amount, string Method,
    string? Reference, string RecordedBy, string Status, string? CancellationReason);

public sealed record CashMovementDto(DateOnly Date, string Type, string Number, string Label, string Method, decimal In, decimal Out);

public sealed record CashSituationDto(
    DateOnly PeriodStart, DateOnly Date, decimal OpeningBalance, decimal CashReceipts, decimal CashExpenses, decimal ExpectedCash,
    decimal OtherReceipts, DateOnly? LastClosingDate, IReadOnlyList<CashMovementDto> Movements);

public sealed record CashClosingDto(
    Guid Id, DateOnly Date, DateOnly PeriodStart, decimal OpeningBalance, decimal CashReceipts, decimal CashExpenses, decimal ExpectedCash,
    decimal CountedCash, decimal Difference, string? Note, string ClosedBy, DateTime ClosedAtUtc);

public sealed record ReEnrollmentItemDto(Guid StudentId, string Matricule, string FullName, string FromPromotion, string? ToPromotion, decimal PreviousBalance, bool Selected);

public sealed record CourseDto(
    Guid Id, Guid PromotionId, string PromotionCode, int Number, string Name, decimal Coefficient, decimal Hours, decimal MaxScore,
    Guid? TeacherId, string? TeacherName, bool IsActive, int AssessmentCount);

public sealed record AssignmentHistoryDto(Guid Id, string TeacherName, DateOnly StartedOn, DateOnly? EndedOn, string? EndReason, string AssignedBy, bool IsCurrent);

public sealed record NextNumberDto(int Numero);

public sealed record TeacherChargeDto(Guid TeacherId, string Name, int Courses, int Promotions, decimal Hours);

public sealed record TeacherCourseLineDto(Guid CourseId, string PromotionCode, int Number, string Name, decimal Hours, decimal MaxScore);

public sealed record AssessmentDto(Guid Id, Guid CourseId, string Title, string Kind, int Term, DateOnly Date, decimal MaxScore, decimal Weight, int GradedCount);

public sealed record GradeRowDto(Guid EnrollmentId, string Matricule, string FullName, decimal? Score, string? Comment);

public sealed record GradeSheetDto(AssessmentDto Assessment, IReadOnlyList<GradeRowDto> Rows);

public sealed record ResultRowDto(Guid EnrollmentId, string Matricule, string FullName, IReadOnlyList<decimal?> CourseAverages, decimal? Average, int? Rank, string Decision);

public sealed record ResultsDto(string Promotion, int? Term, IReadOnlyList<string> Courses, IReadOnlyList<decimal> Coefficients, IReadOnlyList<ResultRowDto> Rows, decimal? ClassAverage, int Passed);

public sealed record AttendanceRowDto(Guid EnrollmentId, string Matricule, string FullName, string? Status, string? Note);

public sealed record AttendanceSheetDto(IReadOnlyList<AttendanceRowDto> Rows, bool CourseDay);

/// <summary>Apprenant à traiter : absences non justifiées ou jours consécutifs absents, au-delà des seuils.</summary>
public sealed record AttendanceAlertDto(Guid EnrollmentId, string Matricule, string FullName, int UnjustifiedAbsences, int LongestRun);

public sealed record AttendanceSummaryDto(Guid EnrollmentId, string Matricule, string FullName, int Present, int Absent, int Late, int Justified, decimal Rate);

public sealed record IncidentDto(
    Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, DateOnly Date, string Category, string Description,
    string? Sanction, string Severity, string RecordedBy);

public sealed record TeacherDto(Guid Id, string Nom, string Function);

public sealed record StaffDto(
    Guid Id, string Number, string LastName, string MiddleName, string FirstName, string Gender, string Function, string? Phone, string? Email,
    DateOnly HireDate, DateOnly? ExitDate, decimal BaseSalary, bool IsActive);

public sealed record PayrollDto(
    Guid Id, Guid StaffMemberId, string Number, string FullName, string Function, string Period, decimal BaseSalary, decimal Allowances,
    decimal Deductions, decimal Advances, decimal Net, string Status, DateOnly? PaidOn, string? Note);

public sealed record BackupDto(string Name, DateTime CreatedUtc, long SizeBytes);

public sealed record ImportReportDto(bool Applied, int Promotions, int Students, int Receipts, int Allocations, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, bool IsActive, bool MustChangePassword, DateTime? LastLoginUtc, bool IsLocked);

public sealed record AuditDto(long Id, DateTime AtUtc, string UserName, string Action, string EntityType, string? EntityId, string Summary, string? IpAddress);

public sealed record SearchHitDto(string Type, string Title, string Subtitle, string Url);

public sealed record AuditVerificationDto(bool IsIntact);

public sealed record IntegrityDto(bool Intacte);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Signature d'entrée ou de sortie, telle que le serveur la confirme.</summary>
public sealed record StaffMarkDto(Guid Id, string Kind, string Time, bool IsLate);

/// <summary>Une journée du pointage d'un membre du personnel.</summary>
public sealed record StaffDayDto(DateOnly Date, string Status, string? Entry, string? Exit, bool Late, bool Justified, bool Conversion, bool ConversionJustified);

/// <summary>Bilan d'un membre sur une période, pour la préparation de la paie.</summary>
public sealed record StaffBilanDto(Guid StaffMemberId, string Name, int Present, int Late, int Absent, int Justified, int Conversions, int Deductible);
