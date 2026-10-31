using Scolia.Domain.Academics;
using Scolia.Domain.Finance;
using Scolia.Domain.Students;

namespace Scolia.Application.Dtos;

// ---------- Établissement et configuration ----------
public sealed record SchoolDto(string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone,
    string? Email, string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, bool HasLogo, string PhoneCountryCode,
    string? CardMinistry = null, string? CardDirection = null, string? CardDivision = null, string? CardZone = null, string? CardInstitute = null, string? CardAddress = null);

public sealed record SchoolUpdate(string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone,
    string? Email, string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, string PhoneCountryCode,
    string? CardMinistry = null, string? CardDirection = null, string? CardDivision = null, string? CardZone = null, string? CardInstitute = null, string? CardAddress = null);

public sealed record SetupRequest(string SchoolName, string ShortName, string? Motto, string? City, string? Phone, string CurrencyCode, string CurrencySymbol,
    string YearLabel, int StartMonth, int MonthCount, string AdminUserName, string AdminDisplayName, string AdminPassword, bool LoadDemoData,
    IReadOnlyList<SetupAccount>? Accounts = null, int LastOnTimeDay = 5, int? EndMonth = null);

/// <summary>Compte métier créé pendant l'installation. Rôle : Secretariat ou Comptabilite.</summary>
public sealed record SetupAccount(string Role, string UserName, string DisplayName, string Password);

public sealed record SetupStatusDto(bool IsConfigured, string? SchoolName);

public sealed record PeriodDto(Guid Id, int Order, string Name, int Month, int CalendarYear, DateOnly DueDate);

public sealed record AcademicYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, string Status, bool IsCurrent, IReadOnlyList<PeriodDto> Periods);

/// <summary>LastOnTimeDay = dernier jour où payer sans retard, du mois suivant (1..28).
/// EndMonth permet de choisir explicitement la fin, même à cheval sur décembre.</summary>
public sealed record AcademicYearCreate(string Label, int StartMonth, int MonthCount, int DueDay,
    bool CopyPromotionsFromCurrent, bool MakeCurrent, int? EndMonth = null, int? LastOnTimeDay = null);

/// <summary>Modification du calendrier existant, bloquée si une mensualité a été affectée ou prise en charge.</summary>
public sealed record AcademicYearScheduleUpdate(int StartMonth, int EndMonth, int LastOnTimeDay);

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
    decimal Remaining, string Status, bool IsOverdue, string Kind);

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

/// <summary>Lecture financière du Promoteur : uniquement des totaux sans élèves, contacts ni reçus nominatifs.</summary>
public sealed record PromoterPeriodDto(Guid PeriodId, string Label, decimal CollectedOnDates,
    decimal DueNet, decimal PaidAgainstDue, decimal RemainingDue);

public sealed record PromoterPromotionDto(Guid Id, string Code, string Name, int ActiveStudents,
    int StudentsWithBalance, int StudentsOverdue, decimal CollectedToday, decimal CollectedThisMonth,
    decimal CollectedTotal, decimal Outstanding, decimal Overdue, IReadOnlyList<PromoterPeriodDto> Periods);

public sealed record PromoterDashboardDto(string AcademicYear, DateOnly AsOf, int ActiveStudents,
    int StudentsWithBalance, int StudentsOverdue, decimal CollectedToday, decimal CollectedThisMonth,
    decimal CollectedTotal, decimal Outstanding, decimal Overdue, IReadOnlyList<PromoterPromotionDto> Promotions);

// ---------- Suivi des paiements par mois ----------
/// <summary>Filtres du suivi mensuel : le mois, la promotion (toutes si absente), l'état et une recherche.</summary>
public sealed record MonthlyQuery(Guid? PeriodeId, Guid? PromotionId, string? Etat, string? Q);

public sealed record PeriodChoiceDto(Guid Id, string Name, bool IsCurrent);

/// <summary>Un apprenant face au mois choisi : montant du mois, payé, reste, état et dernier reçu qui l'a réglé.</summary>
public sealed record MonthlyRowDto(Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode,
    decimal Amount, decimal Paid, decimal Remaining, string Status, string? ReceiptNumber, DateOnly? PaidOn, string? GuardianPhone,
    string? WhatsAppLink, string? SmsLink);

/// <summary>Suivi d'un mois : les mois de l'année, les totaux par état et la liste filtrée.</summary>
public sealed record MonthlyFollowUpDto(Guid PeriodId, string Period, IReadOnlyList<PeriodChoiceDto> Periods,
    int PaidCount, int UnpaidCount, int LateCount, int CoveredCount, decimal Expected, decimal Collected, decimal Remaining,
    IReadOnlyList<MonthlyRowDto> Rows);

/// <summary>Journée en cours : caisse, appel, rendez-vous et anniversaires, pour l'accueil.</summary>
public sealed record TodayDto(DateOnly Date, int ReceiptsToday, decimal CollectedToday, decimal ExpensesToday, bool CashClosed,
    int AttendanceMarked, int SuspendedToday, IReadOnlyList<TodayPersonDto> Appointments, IReadOnlyList<TodayPersonDto> Birthdays);

public sealed record TodayPersonDto(Guid StudentId, string Name, string Promotion, string Detail);

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
public sealed record CourseDto(Guid Id, Guid PromotionId, string PromotionCode, int Number, string Name, decimal Coefficient, decimal Hours,
    decimal MaxScore, Guid? TeacherId, string? TeacherName, bool IsActive, int AssessmentCount);
public sealed record CourseUpsert(Guid PromotionId, int Number, string Name, decimal Coefficient, decimal Hours, decimal MaxScore);

/// <summary>Attribution d'un cours. La réattribution d'un cours déjà attribué demande une confirmation.</summary>
public sealed record AssignmentRequest(Guid TeacherId, bool ConfirmReplace);

public sealed record CourseArchiveRequest(string? Reason);

public sealed record AssignmentHistoryDto(Guid Id, string TeacherName, DateOnly StartedOn, DateOnly? EndedOn, string? EndReason, string AssignedBy, bool IsCurrent);

/// <summary>Charge pédagogique annuelle prévue d'un enseignant : cours actifs et attributions en cours uniquement.</summary>
public sealed record TeacherChargeDto(Guid TeacherId, string Name, int Courses, int Promotions, decimal Hours);

public sealed record TeacherChoiceDto(Guid Id, string Nom, string Function);

/// <summary>Signature d'entrée ou de sortie : l'heure est celle du poste, et une entrée à 08:00:00 ou après est en retard.</summary>
public sealed record StaffMarkDto(Guid Id, string Kind, string Time, bool IsLate);

public sealed record StaffJustifyRequest(DateOnly Date, string Reason);

public sealed record StaffCancelRequest(string Reason);

public sealed record StaffJustificationDto(Guid Id, DateOnly Date, string Reason, string RecordedBy, string? CancelReason);

/// <summary>Une journée du pointage : statut, heures signées, et si elle sert de conversion de retards.</summary>
public sealed record StaffDayDto(DateOnly Date, string Status, string? Entry, string? Exit, bool Late, bool Justified, bool Conversion, bool ConversionJustified);

/// <summary>Bilan d'un membre sur une période. Les jours déductibles comptent une fois par date.</summary>
public sealed record StaffBilanDto(Guid StaffMemberId, string Name, int Present, int Late, int Absent, int Justified, int Conversions, int Deductible);

public sealed record TeacherCourseLineDto(Guid CourseId, string PromotionCode, int Number, string Name, decimal Hours, decimal MaxScore);

public sealed record AssessmentDto(Guid Id, Guid CourseId, string Title, string Kind, int Term, DateOnly Date, decimal MaxScore, decimal Weight, int GradedCount);

public sealed record AssessmentUpsert(Guid CourseId, string Title, string Kind, int Term, DateOnly Date, decimal MaxScore, decimal Weight);

public sealed record GradeSheetDto(AssessmentDto Assessment, IReadOnlyList<GradeRow> Rows);

public sealed record GradeRow(Guid EnrollmentId, string Matricule, string FullName, decimal? Score, string? Comment);

public sealed record GradeInput(Guid EnrollmentId, decimal? Score, string? Comment);

public sealed record ResultRow(Guid EnrollmentId, string Matricule, string FullName, IReadOnlyList<decimal?> CourseAverages, decimal? Average, int? Rank, string Decision);

public sealed record ResultsDto(string Promotion, int? Term, IReadOnlyList<string> Courses, IReadOnlyList<decimal> Coefficients, IReadOnlyList<ResultRow> Rows, decimal? ClassAverage, int Passed);

public sealed record AttendanceSheetDto(Guid PromotionId, DateOnly Date, IReadOnlyList<AttendanceRow> Rows, bool CourseDay);

/// <summary>Apprenant proposé pour les cartes. Les identités incomplètes et les matricules répétés sont signalés et écartés.</summary>
public sealed record CardCandidateDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Status, bool Eligible,
    bool IdentityComplete, bool DuplicateMatricule);

/// <summary>Nettoyage manuel des sauvegardes : garder les plus récentes, et ne supprimer que celles qui ont plus de N jours.</summary>
public sealed record BackupCleanup(int Garder, int PlusAnciennesQue);

/// <summary>Export des cartes : format « pdf » (par défaut) ou « docx ».</summary>
public sealed record CardExportRequest(IReadOnlyList<Guid> EnrollmentIds, int Month, int Year, int Copies, bool IncludeLeft, string? Format = null);

public sealed record CardBatchDto(Guid Id, DateTime CreatedAtUtc, string Session, string PromotionLabel, int StudentCount, int CardCount, int PageCount,
    string Format, string FileName, string CreatedBy);

/// <summary>Apprenant à traiter : absences non justifiées ou jours consécutifs absents, au-delà des seuils.</summary>
public sealed record AttendanceAlertDto(Guid EnrollmentId, string Matricule, string FullName, int UnjustifiedAbsences, int LongestRun);

public sealed record AttendanceRow(Guid EnrollmentId, string Matricule, string FullName, string? Status, string? Note);

public sealed record AttendanceInput(Guid EnrollmentId, string Status, string? Note);

public sealed record AttendanceSummary(Guid EnrollmentId, string Matricule, string FullName, int Present, int Absent, int Late, int Justified, decimal Rate);

public sealed record IncidentDto(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, DateOnly Date, string Category,
    string Description, string? Sanction, string Severity, string RecordedBy);

public sealed record IncidentCreate(Guid EnrollmentId, DateOnly? Date, string Category, string Description, string? Sanction, string Severity);

/// <summary>Nouvelle décision : renvoi temporaire, renvoi définitif ou convocation. Les dates dépendent de la mesure.</summary>
public sealed record DisciplineRequest(Guid EnrollmentId, string Measure, string Reason, DateOnly? DecisionDate, DateOnly? FirstDay, DateOnly? LastDay,
    DateTime? AppointmentAt, bool ConfirmDefinitive, string? RequestKey);

public sealed record DisciplineReceptionInput(string Reception, string Note);

public sealed record DisciplineCancelInput(string Reason);

/// <summary>Ligne du registre. Les champs calculés (durée, retour, statut effectif) viennent du serveur.</summary>
public sealed record DisciplineRowDto(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Measure, string Reason,
    DateOnly DecisionDate, DateOnly? FirstDay, DateOnly? LastDay, int? Days, DateOnly? ReturnFrom, DateTime? AppointmentAt, bool IsAutomatic,
    string? ParentName, string? ParentPhone, string Reception, string? ReceptionNote, string State, string? CancelReason, string RecordedBy,
    string EffectiveStatus, string AcademicYear, bool CanReceive, bool CanAbsent, bool CanCancel);

// ---------- Personnel ----------
public sealed record StaffDto(Guid Id, string Number, string LastName, string MiddleName, string FirstName, string Gender, string Function,
    string? Phone, string? Email, DateOnly HireDate, DateOnly? ExitDate, decimal BaseSalary, bool IsActive);

public sealed record StaffUpsert(string LastName, string MiddleName, string FirstName, string Gender, string Function, string? Phone, string? Email,
    DateOnly HireDate, DateOnly? ExitDate, decimal BaseSalary, bool IsActive);

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
