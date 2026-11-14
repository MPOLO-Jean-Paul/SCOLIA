using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scolia.Bureau.Api;

/// <summary>État de l'installation : une configuration existe-t-elle sur ce serveur ?</summary>
public sealed record SetupStatusDto(bool IsConfigured, string? SchoolName);

/// <summary>Année scolaire proposée par le serveur pour l'assistant.</summary>
public sealed record YearProposalDto(string Label);

/// <summary>Révision des données du serveur : elle change à chaque écriture, d'un poste quelconque.</summary>
public sealed record RevisionDto(long Revision);

/// <summary>Version de l'API réellement utilisée pour exporter les documents, distincte de celle du téléphone.</summary>
public sealed record ServerHealthDto(string Statut, string? Version, string? CardGenerator = null);

/// <summary>Ligne du registre disciplinaire, telle que le serveur la renvoie.</summary>
public sealed record DisciplineRowDto(Guid Id, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Measure, string Reason,
    DateOnly DecisionDate, DateOnly? FirstDay, DateOnly? LastDay, int? Days, DateOnly? ReturnFrom, DateTime? AppointmentAt, bool IsAutomatic,
    string? ParentName, string? ParentPhone, string Reception, string? ReceptionNote, string State, string? CancelReason, string RecordedBy,
    string EffectiveStatus, string AcademicYear, bool CanReceive, bool CanAbsent, bool CanCancel);

// Contrats de l'API SCOLIA, tels que servis en JSON (noms en camelCase, dates ISO, montants décimaux).

public sealed record SessionDto(
    Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, string[] Permissions, bool MustChangePassword,
    string SchoolName, string? SchoolShortName, string? CurrentYear, string CurrencySymbol, string PhoneCountryCode,
    string InstitutionType = "ItmIm");

public sealed record SchoolDto(
    string Name, string ShortName, string? Motto, string? Address, string? City, string? Country, string? Phone, string? Email,
    string? Website, string CurrencyCode, string CurrencySymbol, bool WholeAmountsOnly, string? ReceiptFooter, bool HasLogo, string PhoneCountryCode,
    string? CardMinistry = null, string? CardDirection = null, string? CardDivision = null, string? CardZone = null,
    string? CardInstitute = null, string? CardAddress = null, string InstitutionType = "ItmIm",
    string Governance = "Prive", string? Province = null, string? Commune = null, string? OfficialCode = null);

/// <summary>Plan LMD : consultable et paramétrable dans les mêmes interfaces natives.</summary>
public sealed record LmdStructureDto(Guid Id, string Kind, Guid? ParentId, string Code, string Name, bool IsActive);
public sealed record LmdSemesterDto(Guid Id, string Code, string Name, int Order, DateOnly StartDate, DateOnly EndDate);
public sealed record LmdProgramItemDto(Guid Id, Guid PromotionId, Guid? AcademicUnitId,
    Guid? ParentItemId, string Kind, string Code, string Name, string Semester,
    decimal Credits, decimal Coefficient, decimal Hours);

public sealed record SchoolPeriodDto(Guid Id, string Code, string Name, string TermCode,
    int Order, string Mode, DateOnly StartDate, DateOnly EndDate, bool IsExam = false)
{
    public string Kind => IsExam ? "Examen" : "Période de travail";
}
public sealed record AnnualSubjectDto(Guid Id, Guid PromotionId, string Code, string Name,
    decimal Coefficient, decimal Hours, bool IsActive, decimal? PeriodMaxScore = null)
{
    public string MaxLabel => PeriodMaxScore is { } m
        ? $"Maximum par période : {m.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))}"
        : "Maximum par période à définir";
}

/// <summary>Configuration distincte de chaque cycle d'un complexe scolaire.</summary>
public sealed record SchoolCycleConfigurationItemDto(Guid Id, string Kind, string Name,
    string? AuthorizationNumber, bool IsActive, bool ChargeMonthlyTuition, decimal MonthlyAmount, int ClassCount);
public sealed record SchoolCycleConfigurationDto(string InstitutionType, string Governance,
    Guid? AcademicYearId, IReadOnlyList<SchoolCycleConfigurationItemDto> Cycles);

/// <summary>
/// Lecture seule des carnets / bulletins provisoires par cycle. Ces
/// contrats correspondent aux DTO servis par l'API et ne remplacent pas
/// les bulletins ITM ni les délibérations du jury.
/// </summary>
public sealed record SchoolBulletinEntryDto(
    Guid Id, Guid SchoolCyclePeriodId, string PeriodCode,
    string EvaluationCode, string Title, string Mode, Guid? CurriculumItemId,
    string? Observation, decimal? Score, decimal? MaxScore,
    string? Comment, long Version);

public sealed record SchoolBulletinPeriodDto(
    Guid Id, string Code, string Name, string TermCode, DateOnly StartDate,
    DateOnly EndDate, string Mode, IReadOnlyList<SchoolBulletinEntryDto> Entries);

public sealed record SchoolBulletinTermDto(
    string Code, string Label, IReadOnlyList<SchoolBulletinPeriodDto> Periods);

public sealed record SchoolBulletinSubjectPeriodDto(
    Guid SchoolCyclePeriodId, string PeriodCode, string PeriodName,
    IReadOnlyList<SchoolBulletinEntryDto> Entries);

public sealed record SchoolBulletinSubjectDto(
    Guid CurriculumItemId, string SubjectCode, string SubjectName,
    decimal Coefficient, bool IsActive,
    IReadOnlyList<SchoolBulletinSubjectPeriodDto> Periods);

public sealed record SchoolBulletinPreviewDto(
    Guid EnrollmentId, Guid SchoolCycleId, string Cycle, string Promotion,
    string AcademicYear, IReadOnlyList<SchoolBulletinEntryDto> Entries,
    bool IsOfficial, string Notice, IReadOnlyList<SchoolBulletinTermDto>? Terms,
    string TemplateCode = "CYCLE_PROVISOIRE",
    string DocumentTitle = "Aperçu pédagogique",
    string EntryHeading = "Activités ou branches",
    string PeriodHeading = "Période",
    int ClassLevel = 0,
    string GradeBand = "Cycle non précisé",
    string? OptionLabel = null,
    IReadOnlyList<SchoolBulletinSubjectDto>? SubjectRows = null,
    IReadOnlyList<SchoolBulletinEntryDto>? UnassignedEntries = null);

/// <summary>Apprenant proposé pour les cartes d'accès, avec son diagnostic.</summary>
public sealed record CardCandidateDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string Status, bool Eligible,
    bool IdentityComplete, bool DuplicateMatricule);

public sealed record CardBatchDto(Guid Id, DateTime CreatedAtUtc, string Session, string PromotionLabel, int StudentCount, int CardCount, int PageCount,
    string Format, string FileName, string CreatedBy);

public sealed record PeriodDto(Guid Id, int Order, string Name, int Month, int CalendarYear, DateOnly DueDate);

public sealed record AcademicYearDto(Guid Id, string Label, DateOnly StartDate, DateOnly EndDate, string Status, bool IsCurrent, IReadOnlyList<PeriodDto> Periods);

public sealed record SectionDto(Guid Id, string Code, string Name, bool IsActive, int PromotionCount = 0, int StudentCount = 0);

public sealed record FeeItemDto(Guid Id, string Kind, string Label, decimal Amount, DateOnly DueDate, int Order, Guid? PeriodId, Guid? CategoryId = null);

/// <summary>Catégorie de frais du catalogue de l'établissement (inscription, examen, tenue…), avec son montant.</summary>
public sealed record FeeCategoryDto(Guid Id, string Name, decimal Amount, DateOnly? DueDate, bool IsActive, int Order, int PromotionCount);

public sealed record PromotionDto(
    Guid Id, Guid AcademicYearId, string Code, string Name, Guid? SectionId, string? SectionName, int Level, int? Capacity, bool IsActive,
    string? NextPromotionCode, decimal MonthlyFee, decimal AnnualTotal, int StudentCount, IReadOnlyList<FeeItemDto> Fees,
    Guid? SchoolCycleId = null, Guid? AcademicUnitId = null);

public sealed record DashboardDto(
    string AcademicYear, int ActiveStudents, int NewThisMonth, decimal CollectedTotal, decimal CollectedThisMonth, decimal CollectedToday,
    decimal ExpectedToDate, decimal Outstanding, decimal Overdue, decimal RecoveryRate, int StudentsUpToDate, int StudentsLate,
    decimal ExpensesThisMonth, IReadOnlyList<SeriesPoint> MonthlyCollections, IReadOnlyList<PromotionStat> Promotions,
    IReadOnlyList<SeriesPoint> Aging, IReadOnlyList<ReceiptSummaryDto> LatestReceipts, int GenderMale, int GenderFemale,
    int PayingStudents = 0, int SponsoredStudents = 0, int PartiallySponsoredStudents = 0, decimal SponsoredAmount = 0m);

public sealed record SeriesPoint(string Label, decimal Value, decimal? Secondary);

public sealed record PromoterPeriodDto(Guid PeriodId, string Label, decimal CollectedOnDates,
    decimal DueNet, decimal PaidAgainstDue, decimal RemainingDue);
public sealed record PromoterPromotionDto(Guid Id, string Code, string Name, int ActiveStudents,
    int StudentsWithBalance, int StudentsOverdue, decimal CollectedToday, decimal CollectedThisMonth,
    decimal CollectedTotal, decimal Outstanding, decimal Overdue, IReadOnlyList<PromoterPeriodDto> Periods,
    int PayingStudents = 0, int SponsoredStudents = 0, int PartiallySponsoredStudents = 0, decimal SponsoredAmount = 0m, int StudentsUpToDate = 0);
public sealed record PromoterDashboardDto(string AcademicYear, DateOnly AsOf, int ActiveStudents,
    int StudentsWithBalance, int StudentsOverdue, decimal CollectedToday, decimal CollectedThisMonth,
    decimal CollectedTotal, decimal Outstanding, decimal Overdue, IReadOnlyList<PromoterPromotionDto> Promotions,
    int PayingStudents = 0, int SponsoredStudents = 0, int PartiallySponsoredStudents = 0, decimal SponsoredAmount = 0m, int StudentsUpToDate = 0);

public sealed record PeriodChoiceDto(Guid Id, string Name, bool IsCurrent);

public sealed record MonthlyRowDto(Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode,
    decimal Amount, decimal Paid, decimal Remaining, string Status, string? ReceiptNumber, DateOnly? PaidOn, string? GuardianPhone,
    string? WhatsAppLink, string? SmsLink, string? Sponsorship = null)
{
    public bool HasSponsorship => !string.IsNullOrEmpty(Sponsorship);
}

public sealed record MonthlyFollowUpDto(Guid PeriodId, string Period, IReadOnlyList<PeriodChoiceDto> Periods,
    int PaidCount, int UnpaidCount, int LateCount, int CoveredCount, decimal Expected, decimal Collected, decimal Remaining,
    IReadOnlyList<MonthlyRowDto> Rows);

public sealed record TodayDto(DateOnly Date, int ReceiptsToday, decimal CollectedToday, decimal ExpensesToday, bool CashClosed,
    int AttendanceMarked, int SuspendedToday, IReadOnlyList<TodayPersonDto> Appointments, IReadOnlyList<TodayPersonDto> Birthdays);

public sealed record TodayPersonDto(Guid StudentId, string Name, string Promotion, string Detail);

public sealed record PromotionStat(Guid Id, string Code, string Name, int Students, decimal Expected, decimal Collected, decimal Outstanding, decimal Overdue, decimal RecoveryRate,
    int Paying = 0, int Sponsored = 0, int PartiallySponsored = 0, int UpToDate = 0, int Late = 0, decimal SponsoredAmount = 0m);

public sealed record StudentListItem(
    Guid Id, Guid? EnrollmentId, string Matricule, string LastName, string MiddleName, string FirstName, string Gender,
    string? PromotionCode, string? Status, string? GuardianPhone, decimal Balance, decimal Overdue, string? Sponsorship = null);

public sealed record StudentDto(
    Guid Id, string Matricule, string LastName, string MiddleName, string FirstName, string Gender, DateOnly? BirthDate, string? BirthPlace,
    string? Nationality, string? Address, string? Phone, string? Email, string? GuardianName, string? GuardianRelation, string? GuardianPhone,
    string? GuardianPhone2, string? Notes, bool HasPhoto, IReadOnlyList<EnrollmentDto> Enrollments, long Version,
    string? PermanentNumber = null);

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

public sealed record SponsorshipDto(Guid Id, Guid EnrollmentId, string SponsorName, string Reason, DateOnly EffectiveFrom, bool IsActive, IReadOnlyList<SponsorshipLineDto> Lines, decimal CoveredTotal, string Type = "Historique", decimal MonthlyAmount = 0m);

public sealed record SponsorshipListItem(
    Guid Id, Guid EnrollmentId, string Matricule, string FullName, string Promotion, string SponsorName, string Reason, DateOnly EffectiveFrom,
    bool IsActive, decimal Covered, string Type = "Historique", decimal MonthlyAmount = 0m);

public sealed record DebtorDto(
    Guid StudentId, Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, decimal Balance, decimal Overdue, int OverdueCount,
    string Periods, string AgingBucket, string? GuardianName, string? GuardianPhone, string? WhatsAppLink, string? SmsLink, string? Sponsorship = null);

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

public sealed record ReEnrollmentItemDto(Guid StudentId, string Matricule, string FullName, string FromPromotion, string? ToPromotion, decimal PreviousBalance, bool Selected,
    string? JuryDecision = null);

public sealed record CourseDto(
    Guid Id, Guid PromotionId, string PromotionCode, int Number, string Name, decimal Coefficient, decimal Hours, decimal MaxScore,
    Guid? TeacherId, string? TeacherName, bool IsActive, int AssessmentCount, string Semester = "Annuel");

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

public sealed record UserDto(Guid Id, string UserName, string DisplayName, string Role, string RoleLabel, bool IsActive, bool MustChangePassword, DateTime? LastLoginUtc, bool IsLocked,
    Guid? StaffMemberId = null, bool JuryAccess = false);

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
public sealed record StaffTodayDto(Guid StaffId, StaffDayDto? Day);

/// <summary>Bilan d'un membre sur une période, pour la préparation de la paie.</summary>
public sealed record StaffBilanDto(Guid StaffMemberId, string Name, int Present, int Late, int Absent, int Justified, int Conversions, int Deductible);

/// <summary>Réglages du rapport journalier tels que l'ordinateur principal les montre : jamais le jeton lui-même.</summary>
public sealed record DailyReportViewDto(bool Enabled, string Time, List<string> Recipients, string? WhatsAppPhoneNumberId,
    string? WhatsAppTemplate, string WhatsAppLanguage, bool HasToken, bool WhatsAppReady, string? LastStatus,
    List<string>? EmailRecipients = null, string? SmtpHost = null, int SmtpPort = 587, bool SmtpSsl = true, string? SmtpUser = null,
    string? SmtpFrom = null, bool HasSmtpPassword = false, bool EmailReady = false, string? WhatsAppApiVersion = null);

/// <summary>Résultat d'un envoi demandé : le fichier, ce qui a été fait, et les chiffres du jour.</summary>
public sealed record DailyReportResultDto(string FileName, string Status, string Summary);

/// <summary>Appareil autorisé par code QR à ouvrir le compte du responsable, avec sa validité.</summary>
public sealed record AuthorizedDeviceDto(Guid Id, string Name, DateTime ExpiresUtc, bool Revoked);

// Sauvegarde et relève en ligne : mêmes formes que le serveur (jamais de clé).
public sealed record CloudRefusalDto(DateTimeOffset At, string Label, string Path, string User, string Reason);

public sealed record CloudStatusDto(bool Registered, bool Enabled, bool RecoveryConfirmed, string? KeyFingerprint, DateTimeOffset? ActivatedAt,
    DateTimeOffset? LastBackupAt, long LastBackupBytes, DateTimeOffset? LastDrainAt, int Applied, int Refused,
    string? LastError, DateTimeOffset? LastErrorAt, IReadOnlyList<CloudRefusalDto> Refusals, bool ActivatedByOwner = false);

public sealed record CloudBackupDto(Guid Id, DateTimeOffset CreatedAt, long SizeBytes, string KeyFingerprint, bool SameKey);

public sealed record CloudRecoveryCodeDto(string Code, string? KeyFingerprint);
