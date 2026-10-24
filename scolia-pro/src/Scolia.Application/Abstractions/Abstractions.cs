using Microsoft.EntityFrameworkCore;
using Scolia.Domain.Academics;
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
    DbSet<AcademicYear> AcademicYears { get; }
    DbSet<AcademicPeriod> Periods { get; }
    DbSet<Section> Sections { get; }
    DbSet<Promotion> Promotions { get; }
    DbSet<FeeItem> FeeItems { get; }
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
    DbSet<Assessment> Assessments { get; }
    DbSet<Grade> Grades { get; }
    DbSet<AttendanceRecord> Attendance { get; }
    DbSet<DisciplineIncident> Incidents { get; }
    DbSet<DisciplineDecision> DisciplineDecisions { get; }
    DbSet<StaffMember> Staff { get; }
    DbSet<PayrollEntry> Payroll { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Utilisateur connecté pour la requête courante.</summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    string Name { get; }
    bool HasPermission(string permission);
    string? IpAddress { get; }
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
    byte[] ReportCard(ReportCardDocument document);
}

public interface ISpreadsheetService
{
    byte[] Export(TableDocument document);
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

public sealed record TableDocument(SchoolHeader School, string Title, string? Subtitle, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? Totals = null, bool Landscape = false);

public sealed record ReportCardDocument(SchoolHeader School, string StudentName, string Matricule, string Promotion, string AcademicYear,
    IReadOnlyList<(string Course, decimal Coefficient, decimal? Average, decimal Max)> Lines, decimal? Average, int? Rank, int ClassSize, string Decision);
