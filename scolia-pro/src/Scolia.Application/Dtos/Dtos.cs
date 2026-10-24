using Scolia.Domain.Academics;
using Scolia.Domain.Finance;
using Scolia.Domain.Students;

namespace Scolia.Application.Dtos;

// ---------- Établissement et configuration ----------
public sealed record SchoolDto(string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone,
    string? Email, string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, bool HasLogo, string PhoneCountryCode);

public sealed record SchoolUpdate(string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone,
    string? Email, string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, string PhoneCountryCode);

public sealed record SetupRequest(string SchoolName, string ShortName, string? Motto, string? City, string? Phone, string CurrencyCode, string CurrencySymbol,
    string YearLabel, int StartMonth, int MonthCount, string AdminUserName, string AdminDisplayName, string AdminPassword, bool LoadDemoData,
    IReadOnlyList<SetupAccount>? Accounts = null);

/// <summary>Compte métier créé pendant l'installation. Rôle : Secretariat ou Comptabilite.</summary>
public sealed record SetupAccount(string Role, string UserName, string DisplayName, string Password);

public sealed record SetupStatusDto(bool IsConfigured, string? SchoolName);

public sealed record PeriodDto(Guid Id, int Order, string Name, int Month, int CalendarYear, DateOnly DueDate);

public sealed record AcademicYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, string Status, bool IsCurrent, IReadOnlyList<PeriodDto> Periods);

public sealed record AcademicYearCreate(string Label, int StartMonth, int MonthCount, int DueDay, bool CopyPromotionsFromCurrent, bool MakeCurrent);

public sealed record SectionDto(Guid Id, string Code, string Name, bool IsActive);

public sealed record SectionUpsert(string Code, string Name, bool IsActive);

public sealed record FeeItemDto(Guid Id, string Kind, string Label, decimal Amount, DateOnly DueDate, int Order, Guid? PeriodId);

public sealed record PromotionDto(Guid Id, Guid AcademicYearId, string Code, string Name, Guid? SectionId, string? SectionName, int Level, int? Capacity,
    bool IsActive, string? NextPromotionCode, decimal MonthlyFee, decimal AnnualTotal, int StudentCount, IReadOnlyList<FeeItemDto> Fees);

/// <summary>Création ou modification d'une promotion : la mensualité génère une rubrique par mois,
/// les frais ponctuels (inscription, examen…) sont ajoutés en plus.</summary>
public sealed record PromotionUpsert(string Code, string Name, Guid? SectionId, int Level, int? Capacity, bool IsActive, string? NextPromotionCode,
    decimal MonthlyFee, IReadOnlyList<OneTimeFee>? OneTimeFees);

public sealed record OneTimeFee(string Kind, string Label, decimal Amount, DateOnly DueDate);

// ---------- Apprenants ----------
public sealed record StudentListItem(Guid Id, Guid? EnrollmentId, string Matricule, string LastName, string MiddleName, string FirstName, string Gender,
    string? PromotionCode, string? Status, string? GuardianPhone, decimal Balance, decimal Overdue);

public sealed record StudentDto(Guid Id, string Matricule, string LastName, string MiddleName, string FirstName, string Gender, DateOnly? BirthDate,
    string? BirthPlace, string? Nationality, string? Address, string? Phone, string? Email, string? GuardianName, string? GuardianRelation,
    string? GuardianPhone, string? GuardianPhone2, string? Notes, bool HasPhoto, IReadOnlyList<EnrollmentDto> Enrollments, long Version);

public sealed record EnrollmentDto(Guid Id, Guid AcademicYearId, string AcademicYear, Guid PromotionId, string PromotionCode, string PromotionName,
    DateOnly EnrolledOn, string Status, DateOnly? ExitDate, string? ExitReason, bool IsReEnrollment);

public sealed record StudentUpsert(string LastName, string MiddleName, string FirstName, string Gender, DateOnly? BirthDate, string? BirthPlace,
    string? Nationality, string? Address, string? Phone, string? Email, string? GuardianName, string? GuardianRelation, string? GuardianPhone,
    string? GuardianPhone2, string? Notes, long? Version);

public sealed record StudentCreate(StudentUpsert Identity, Guid PromotionId, DateOnly? EnrolledOn, bool ConfirmDuplicate);

public sealed record DuplicateCandidate(Guid Id, string Matricule, string FullName, string? PromotionCode);

public sealed record EnrollRequest(Guid PromotionId, DateOnly? EnrolledOn);

public sealed record StatusChange(string Status, DateOnly? Date, string? Reason);

public sealed record ReEnrollmentPlanItem(Guid StudentId, string Matricule, string FullName, string FromPromotion, string? ToPromotion, decimal PreviousBalance, bool Selected);

public sealed record ReEnrollmentRequest(Guid TargetYearId, IReadOnlyList<ReEnrollmentChoice> Choices);

public sealed record ReEnrollmentChoice(Guid StudentId, Guid PromotionId);

// ---------- Prises en charge ----------
public sealed record SponsorshipDto(Guid Id, Guid EnrollmentId, string SponsorName, string Reason, DateOnly EffectiveFrom, bool IsActive,
    IReadOnlyList<SponsorshipLineDto> Lines, decimal CoveredTotal);

public sealed record SponsorshipListItem(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string Promotion, string SponsorName,
    string Reason, DateOnly EffectiveFrom, bool IsActive, decimal Covered);

public sealed record SponsorshipLineDto(Guid FeeItemId, string Label, decimal Percent, decimal Amount);

public sealed record SponsorshipUpsert(string SponsorName, string Reason, DateOnly EffectiveFrom, IReadOnlyList<SponsorshipLineInput> Lines);

public sealed record SponsorshipLineInput(Guid FeeItemId, decimal Percent);

// ---------- Compte et paiements ----------
public sealed record AccountLineDto(Guid FeeItemId, string Label, DateOnly DueDate, decimal Amount, decimal Covered, decimal Net, decimal Paid,
    decimal Remaining, string Status, bool IsOverdue);

public sealed record AccountDto(Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string AcademicYear,
    string EnrollmentStatus, IReadOnlyList<AccountLineDto> Lines, decimal TotalFees, decimal TotalCovered, decimal TotalNet, decimal TotalPaid,
    decimal Balance, decimal Overdue, int OverdueCount, string AgingBucket, IReadOnlyList<ReceiptSummaryDto> Receipts, string? GuardianPhone, string? WhatsAppLink);

public sealed record ReceiptSummaryDto(Guid Id, string Number, DateOnly Date, decimal Amount, string Method, string Status, string Cashier, string? Allocations);

public sealed record PaymentRequest(Guid EnrollmentId, decimal Amount, DateOnly? Date, string Method, string? Reference, string? PayerName,
    string? PayerPhone, string? Note, IReadOnlyList<Guid>? FeeItemIds, string? IdempotencyKey);

public sealed record PaymentPreview(IReadOnlyList<AllocationPreview> Allocations, decimal BalanceAfter);

public sealed record AllocationPreview(Guid FeeItemId, string Label, decimal Amount, decimal RemainingAfter);

public sealed record ReceiptDto(Guid Id, string Number, string VerificationCode, DateOnly Date, Guid StudentId, string Matricule, string StudentName,
    string PromotionCode, string AcademicYear, decimal Amount, string Method, string? Reference, string? PayerName, string? PayerPhone, string? Note,
    string Cashier, string Status, DateTime? CancelledAtUtc, string? CancelledBy, string? CancellationReason,
    IReadOnlyList<AllocationPreview> Allocations, DateTime CreatedAtUtc);

public sealed record CancelRequest(string Reason);

public sealed record ReceiptVerification(bool Found, string? Number, DateOnly? Date, decimal? Amount, string? Student, string? School, string? Status, string? Currency);

public sealed record ReceiptQuery(string? Search, DateOnly? From, DateOnly? To, string? Method, string? Status, Guid? PromotionId, int Page = 1, int PageSize = 50);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, decimal? Sum = null);

// ---------- Dépenses et caisse ----------
public sealed record ExpenseDto(Guid Id, string Number, DateOnly Date, string Category, string Beneficiary, string Description, decimal Amount,
    string Method, string? Reference, string RecordedBy, string Status, string? CancellationReason);

public sealed record ExpenseCreate(DateOnly? Date, string Category, string Beneficiary, string Description, decimal Amount, string Method, string? Reference);

public sealed record CashSituationDto(DateOnly PeriodStart, DateOnly Date, decimal OpeningBalance, decimal CashReceipts, decimal CashExpenses,
    decimal ExpectedCash, decimal OtherReceipts, DateOnly? LastClosingDate, IReadOnlyList<CashMovementDto> Movements);

public sealed record CashMovementDto(DateOnly Date, string Type, string Number, string Label, string Method, decimal In, decimal Out);

public sealed record CashClosingRequest(DateOnly Date, decimal CountedCash, string? Note);

public sealed record CashClosingDto(Guid Id, DateOnly Date, DateOnly PeriodStart, decimal OpeningBalance, decimal CashReceipts, decimal CashExpenses,
    decimal ExpectedCash, decimal CountedCash, decimal Difference, string? Note, string ClosedBy, DateTime ClosedAtUtc);

// ---------- Tableau de bord et rapports ----------
public sealed record DashboardDto(string AcademicYear, int ActiveStudents, int NewThisMonth, decimal CollectedTotal, decimal CollectedThisMonth,
    decimal CollectedToday, decimal ExpectedToDate, decimal Outstanding, decimal Overdue, decimal RecoveryRate, int StudentsUpToDate,
    int StudentsLate, decimal ExpensesThisMonth, IReadOnlyList<SeriesPoint> MonthlyCollections, IReadOnlyList<PromotionStat> Promotions,
    IReadOnlyList<SeriesPoint> Aging, IReadOnlyList<ReceiptSummaryDto> LatestReceipts, int GenderMale, int GenderFemale);

public sealed record SeriesPoint(string Label, decimal Value, decimal? Secondary = null);

public sealed record PromotionStat(Guid Id, string Code, string Name, int Students, decimal Expected, decimal Collected, decimal Outstanding, decimal Overdue, decimal RecoveryRate);

public sealed record DebtorDto(Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, decimal Balance,
    decimal Overdue, int OverdueCount, string Periods, string AgingBucket, string? GuardianName, string? GuardianPhone, string? WhatsAppLink, string? SmsLink);

public sealed record DebtorQuery(Guid? PromotionId, int? MinOverdueMonths, string? Bucket, string? Search);

// ---------- Utilisateurs et audit ----------
public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, bool IsActive, bool MustChangePassword,
    DateTime? LastLoginUtc, bool IsLocked);

public sealed record UserCreate(string UserName, string DisplayName, string Role, string Password);

public sealed record UserUpdate(string DisplayName, string Role, bool IsActive);

public sealed record PasswordReset(string NewPassword);

public sealed record PasswordChange(string CurrentPassword, string NewPassword);

public sealed record SessionDto(Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, IReadOnlyCollection<string> Permissions,
    bool MustChangePassword, string SchoolName, string? SchoolShortName, string? CurrentYear, string CurrencySymbol, string PhoneCountryCode);

public sealed record LoginRequest(string UserName, string Password);

public sealed record AuditDto(long Id, DateTime AtUtc, string UserName, string Action, string EntityType, string? EntityId, string Summary, string? IpAddress);

public sealed record AuditVerification(bool IsIntact, long Checked, long? BrokenAtId, string Message);

// ---------- Recherche globale ----------
public sealed record SearchHit(string Type, string Title, string Subtitle, string Url);

// ---------- Scolarité ----------
public sealed record CourseDto(Guid Id, Guid PromotionId, string Code, string Name, decimal Coefficient, int Hours, Guid? TeacherId, string? TeacherName, bool IsActive, int AssessmentCount);

public sealed record CourseUpsert(Guid PromotionId, string Code, string Name, decimal Coefficient, int Hours, Guid? TeacherId, bool IsActive);

public sealed record AssessmentDto(Guid Id, Guid CourseId, string Title, string Kind, int Term, DateOnly Date, decimal MaxScore, decimal Weight, int GradedCount);

public sealed record AssessmentUpsert(Guid CourseId, string Title, string Kind, int Term, DateOnly Date, decimal MaxScore, decimal Weight);

public sealed record GradeSheetDto(AssessmentDto Assessment, IReadOnlyList<GradeRow> Rows);

public sealed record GradeRow(Guid EnrollmentId, string Matricule, string FullName, decimal? Score, string? Comment);

public sealed record GradeInput(Guid EnrollmentId, decimal? Score, string? Comment);

public sealed record ResultRow(Guid EnrollmentId, string Matricule, string FullName, IReadOnlyList<decimal?> CourseAverages, decimal? Average, int? Rank, string Decision);

public sealed record ResultsDto(string Promotion, int? Term, IReadOnlyList<string> Courses, IReadOnlyList<decimal> Coefficients, IReadOnlyList<ResultRow> Rows, decimal? ClassAverage, int Passed);

public sealed record AttendanceSheetDto(Guid PromotionId, DateOnly Date, IReadOnlyList<AttendanceRow> Rows);

public sealed record AttendanceRow(Guid EnrollmentId, string Matricule, string FullName, string? Status, string? Note);

public sealed record AttendanceInput(Guid EnrollmentId, string Status, string? Note);

public sealed record AttendanceSummary(Guid EnrollmentId, string Matricule, string FullName, int Present, int Absent, int Late, int Justified, decimal Rate);

public sealed record IncidentDto(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, DateOnly Date, string Category,
    string Description, string? Sanction, string Severity, string RecordedBy);

public sealed record IncidentCreate(Guid EnrollmentId, DateOnly? Date, string Category, string Description, string? Sanction, string Severity);

// ---------- Personnel ----------
public sealed record StaffDto(Guid Id, string Number, string LastName, string MiddleName, string FirstName, string Gender, string Function,
    string? Phone, string? Email, DateOnly HireDate, decimal BaseSalary, bool IsActive);

public sealed record StaffUpsert(string LastName, string MiddleName, string FirstName, string Gender, string Function, string? Phone, string? Email,
    DateOnly HireDate, decimal BaseSalary, bool IsActive);

public sealed record PayrollDto(Guid Id, Guid StaffMemberId, string Number, string FullName, string Function, string Period, decimal BaseSalary,
    decimal Allowances, decimal Deductions, decimal Advances, decimal Net, string Status, DateOnly? PaidOn, string? Note);

public sealed record PayrollUpdate(decimal Allowances, decimal Deductions, decimal Advances, string? Note);

// ---------- Import ----------
public sealed record ImportReport(bool Applied, int Promotions, int Students, int Receipts, int Allocations, IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors);

public static class Labels
{
    public static string Of(EnrollmentStatus status) => status switch
    {
        EnrollmentStatus.Actif => "Actif",
        EnrollmentStatus.Suspendu => "Suspendu",
        EnrollmentStatus.Sorti => "Sorti",
        EnrollmentStatus.Diplome => "Diplômé",
        EnrollmentStatus.Transfere => "Transféré",
        _ => status.ToString(),
    };

    public static string Of(PaymentMethod method) => method switch
    {
        PaymentMethod.Especes => "Espèces",
        PaymentMethod.MobileMoney => "Mobile Money",
        PaymentMethod.Banque => "Banque",
        PaymentMethod.Cheque => "Chèque",
        _ => "Autre",
    };

    public static string Of(LineStatus status) => status switch
    {
        LineStatus.Paye => "Payé",
        LineStatus.Partiel => "Partiel",
        LineStatus.Couvert => "Pris en charge",
        _ => "Impayé",
    };

    public static string Of(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "Présent",
        AttendanceStatus.Absent => "Absent",
        AttendanceStatus.Retard => "Retard",
        _ => "Absence justifiée",
    };
}
