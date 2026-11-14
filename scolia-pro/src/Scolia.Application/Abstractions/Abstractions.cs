using Microsoft.EntityFrameworkCore;
using Scolia.Domain.Academics;
using Scolia.Domain.Deliberation;
using Scolia.Domain.Discipline;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Staff;
using Scolia.Domain.Students;

namespace Scolia.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<SchoolProfile> Schools { get; }
    DbSet<SchoolCycle> SchoolCycles { get; }
    DbSet<SchoolCycleFeePolicy> SchoolCycleFeePolicies { get; }
    DbSet<SchoolCyclePeriod> SchoolCyclePeriods { get; }
    DbSet<SchoolCycleAssessment> SchoolCycleAssessments { get; }
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<AcademicPeriod> Periods { get; }
    DbSet<PedagogicalPeriod> PedagogicalPeriods { get; }
    DbSet<AcademicStructureUnit> AcademicStructureUnits { get; }
    DbSet<CurriculumItem> CurriculumItems { get; }
    DbSet<AcademicRegulation> AcademicRegulations { get; }
    DbSet<EpstConductMark> EpstConductMarks { get; }
    DbSet<EpstDeliberation> EpstDeliberations { get; }
    DbSet<EpstDecision> EpstDecisions { get; }
    DbSet<EpstRepechageMark> EpstRepechageMarks { get; }
    DbSet<NationalExamResult> NationalExamResults { get; }
    DbSet<LmdMark> LmdMarks { get; }
    DbSet<LmdDeliberation> LmdDeliberations { get; }
    DbSet<LmdDecision> LmdDecisions { get; }
    DbSet<LmdDebt> LmdDebts { get; }
    DbSet<CurriculumAssignment> CurriculumAssignments { get; }
    DbSet<Section> Sections { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<FeeItem> FeeItems { get; }
    DbSet<FeeCategory> FeeCategories { get; }
    DbSet<Student> Students { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<Sponsorship> Sponsorships { get; }
    DbSet<SponsorshipLine> SponsorshipLines { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<ReceiptAllocation> ReceiptAllocations { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<CashClosing> CashClosings { get; }
    DbSet<Counter> Counters { get; }
    DbSet<AppUser> Users { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Course> Courses { get; }
    DbSet<CourseAssignment> CourseAssignments { get; }
    DbSet<CourseDay> CourseDays { get; }
    DbSet<CardBatch> CardBatches { get; }
    DbSet<Assessment> Assessments { get; }
    DbSet<Grade> Grades { get; }
    DbSet<AttendanceRecord> Attendance { get; }
    DbSet<DisciplineIncident> Incidents { get; }
    DbSet<DisciplineDecision> DisciplineDecisions { get; }
    DbSet<StaffMember> Staff { get; }
    DbSet<PayrollEntry> Payroll { get; }
    DbSet<StaffAttendanceMark> StaffMarks { get; }
    DbSet<StaffDayJustification> StaffJustifications { get; }
    DbSet<AnonymousCode> AnonymousCodes { get; }
    DbSet<CourseMark> CourseMarks { get; }
    DbSet<DeliberationSettings> DeliberationSettings { get; }
    DbSet<DeliberationSession> DeliberationSessions { get; }
    DbSet<JuryDecision> JuryDecisions { get; }
    DbSet<DeliberationResult> DeliberationResults { get; }
    DbSet<MarkSubmission> MarkSubmissions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Utilisateur connecté pour la requête courante.</summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    string Name { get; }
    bool HasPermission(string permission);
    string? IpAddress { get; }
    /// <summary>Identifiant stable du poste ou du téléphone qui envoie la requête, s'il se présente.</summary>
    string? DeviceId => null;
    /// <summary>Nom lisible de ce poste : « Téléphone Samsung A15 », « PC-SECRETARIAT ».</summary>
    string? Device => null;
}

/// <summary>Sérialise les opérations d'écriture et les exécute dans une transaction unique.
/// Empêche par exemple deux caisses d'encaisser simultanément au-delà du reste dû.</summary>
public interface IWriteGate
{
    Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);
}

public interface IAuditLog
{
    void Record(string action, string entityType, string? entityId, string summary, object? data = null);

    /// <summary>Même enregistrement, attribué à un auteur précis : utile avant l'ouverture de sa session (connexion).</summary>
    void RecordAs(string actor, string action, string entityType, string? entityId, string summary) => Record(action, entityType, entityId, summary);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password, out bool needsRehash);
}

/// <summary>Production des documents imprimables (PDF).</summary>
public interface IDocumentService
{
    byte[] Receipt(ReceiptDocument document);
    byte[] Statement(StatementDocument document);
    byte[] Table(TableDocument document);
    /// <summary>Rapport à plusieurs parties : chiffres clés, puis un tableau par partie (rapport journalier).</summary>
    byte[] Report(ReportDocument document);
    /// <summary>Plusieurs rapports dans un seul PDF, chacun commençant sur une nouvelle page (bulletins d'une promotion).</summary>
    byte[] Reports(IReadOnlyList<ReportDocument> documents);
    byte[] ReportCard(ReportCardDocument document);
    byte[] StudentCards(CardDocument document);
    byte[] StudentCardsWord(CardDocument document);
}

public interface ISpreadsheetService
{
    byte[] Export(TableDocument document);

    /// <summary>Un seul classeur Excel avec une feuille par promotion, sans export intermédiaire de données scolaires.</summary>
    byte[] ExportGrids(IReadOnlyList<(string SheetName, TableDocument Document)> grids);
}

public sealed record SchoolHeader(string Name, string? Motto, string? Address, string? Phone, string? Email, byte[]? Logo, string CurrencySymbol, string? ReceiptFooter);

public sealed record ReceiptDocument(
    SchoolHeader School, string Number, DateOnly Date, string StudentName, string Matricule, string Promotion, string AcademicYear,
    decimal Amount, string AmountInWords, string Method, string? Reference, string? PayerName, string Cashier,
    IReadOnlyList<(string Label, decimal Amount)> Allocations, decimal BalanceAfter, string VerificationUrl, bool Cancelled, string? CancellationReason);

public sealed record StatementDocument(
    SchoolHeader School, string StudentName, string Matricule, string Promotion, string AcademicYear, DateOnly AsOf,
    IReadOnlyList<Dtos.AccountLineDto> Lines, IReadOnlyList<Dtos.ReceiptSummaryDto> Receipts,
    decimal TotalNet, decimal TotalPaid, decimal Balance, decimal Overdue);

/// <summary>En-tête des cartes d'accès : les mentions officielles de l'établissement.</summary>
public sealed record SchoolCardHeader(
    string Country, string? Ministry, string? Direction, string? Division, string? CityZone,
    string Institute, string? Address, byte[]? Logo,
    string? Phone = null, string? Email = null, string? Website = null, string? Motto = null);

/// <summary>Une carte à imprimer. La couleur est celle de la promotion ; le QR contient l'identité, la classe, l'établissement, la session et l'année.</summary>
public sealed record CardItem(string LastName, string MiddleName, string FirstName, string Classe, string Color, string QrText, string Matricule = "");

public sealed record CardDocument(SchoolCardHeader Header, string Session, string AcademicYear, IReadOnlyList<CardItem> Cards);

/// <summary>Partie d'un rapport : un titre, un tableau, et un texte quand il n'y a rien à montrer.</summary>
public sealed record ReportSection(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string>? Totals = null, string? Empty = null);

/// <summary>Rapport à plusieurs parties : en-tête de l'établissement, chiffres clés en tuiles, puis les parties.</summary>
/// <summary><c>OnePage</c> : le contenu est réduit pour tenir sur une seule page (bulletin), jamais coupé sur deux.</summary>
public sealed record ReportDocument(SchoolHeader School, string Title, string? Subtitle, IReadOnlyList<(string Label, string Value)> Figures,
    IReadOnlyList<ReportSection> Sections, string? Note = null, bool OnePage = false, string? VerificationUrl = null);

public sealed record TableDocument(SchoolHeader School, string Title, string? Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? Totals = null, bool Landscape = false);

public sealed record ReportCardDocument(SchoolHeader School, string StudentName, string Matricule, string Promotion, string AcademicYear,
    IReadOnlyList<(string Course, decimal Coefficient, decimal? Average, decimal Max)> Lines, decimal? Average, int? Rank, int ClassSize, string Decision);
