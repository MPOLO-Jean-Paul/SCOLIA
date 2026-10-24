using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Scolia.Application.Abstractions;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Discipline;
using Scolia.Domain.Finance;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Domain.Staff;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Persistence;

public sealed class ScoliaDbContext(DbContextOptions<ScoliaDbContext> options, ICurrentUser? currentUser = null, IClock? clock = null, AuditBuffer? auditBuffer = null)
    : DbContext(options), IAppDbContext
{
    /// <summary>Protège la chaîne d'empreintes du journal d'audit entre requêtes concurrentes.</summary>
    private static readonly SemaphoreSlim AuditChainLock = new(1, 1);

    public DbSet<SchoolProfile> Schools => Set<SchoolProfile>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<AcademicPeriod> Periods => Set<AcademicPeriod>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<FeeItem> FeeItems => Set<FeeItem>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Sponsorship> Sponsorships => Set<Sponsorship>();
    public DbSet<SponsorshipLine> SponsorshipLines => Set<SponsorshipLine>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<CashClosing> CashClosings => Set<CashClosing>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseAssignment> CourseAssignments => Set<CourseAssignment>();
    public DbSet<CourseDay> CourseDays => Set<CourseDay>();
    public DbSet<CardBatch> CardBatches => Set<CardBatch>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<DisciplineIncident> Incidents => Set<DisciplineIncident>();
    public DbSet<DisciplineDecision> DisciplineDecisions => Set<DisciplineDecision>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<StaffAttendanceMark> StaffMarks => Set<StaffAttendanceMark>();
    public DbSet<StaffDayJustification> StaffJustifications => Set<StaffDayJustification>();
    public DbSet<PayrollEntry> Payroll => Set<PayrollEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        var builder = configurationBuilder;
        // Montants exacts : stockés en centièmes entiers (SQLite ne possède pas de type décimal).
        builder.Properties<decimal>().HaveConversion<DecimalToHundredthsConverter>();
        builder.Properties<decimal?>().HaveConversion<NullableDecimalToHundredthsConverter>();
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder;
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(type.ClrType).Property(nameof(Entity.Version)).IsConcurrencyToken();
            b.Entity(type.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
        }

        b.Entity<SchoolProfile>(e => { e.ToTable("Etablissement"); e.Property(x => x.Name).HasMaxLength(160); });
        b.Entity<AcademicYear>(e =>
        {
            e.ToTable("AnneesScolaires");
            e.HasIndex(x => x.Label).IsUnique();
            e.HasMany(x => x.Periods).WithOne().HasForeignKey(p => p.AcademicYearId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.FirstCalendarYear);
        });
        b.Entity<AcademicPeriod>(e => { e.ToTable("Periodes"); e.HasIndex(x => new { x.AcademicYearId, x.Order }).IsUnique(); });
        b.Entity<Section>(e => { e.ToTable("Sections"); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<Promotion>(e =>
        {
            e.ToTable("Promotions");
            e.HasIndex(x => new { x.AcademicYearId, x.Code }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Fees).WithOne().HasForeignKey(f => f.PromotionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FeeItem>(e => { e.ToTable("RubriquesFrais"); e.HasIndex(x => x.PromotionId); });
        b.Entity<Student>(e =>
        {
            e.ToTable("Apprenants");
            e.HasIndex(x => x.Matricule).IsUnique();
            e.HasIndex(x => x.IdentityKey);
            e.Ignore(x => x.FullName);
            e.HasMany(x => x.Enrollments).WithOne(x => x.Student).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Enrollment>(e =>
        {
            e.ToTable("Inscriptions");
            e.HasIndex(x => new { x.StudentId, x.AcademicYearId }).IsUnique();
            e.HasIndex(x => new { x.AcademicYearId, x.PromotionId });
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Sponsorship>(e =>
        {
            e.ToTable("PrisesEnCharge");
            e.HasIndex(x => x.EnrollmentId);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SponsorshipId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SponsorshipLine>(e =>
        {
            e.ToTable("PrisesEnChargeLignes");
            e.HasOne<FeeItem>().WithMany().HasForeignKey(x => x.FeeItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Receipt>(e =>
        {
            e.ToTable("Recus");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.VerificationCode).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("IdempotencyKey IS NOT NULL");
            e.HasIndex(x => x.EnrollmentId);
            e.HasIndex(x => x.Date);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Allocations).WithOne().HasForeignKey(a => a.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReceiptAllocation>(e =>
        {
            e.ToTable("RecusAffectations");
            e.HasIndex(x => x.FeeItemId);
            e.HasOne<FeeItem>().WithMany().HasForeignKey(x => x.FeeItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Expense>(e => { e.ToTable("Depenses"); e.HasIndex(x => x.Number).IsUnique(); e.HasIndex(x => x.Date); });
        b.Entity<CashClosing>(e => { e.ToTable("ArretesCaisse"); e.HasIndex(x => x.Date).IsUnique(); });
        b.Entity<Counter>(e => { e.ToTable("Compteurs"); e.HasKey(x => x.Name); e.Property(x => x.Value).IsConcurrencyToken(); });
        b.Entity<AppUser>(e => { e.ToTable("Utilisateurs"); e.HasIndex(x => x.NormalizedUserName).IsUnique(); });
        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("JournalAudit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.EntityType);
        });
        b.Entity<Course>(e =>
        {
            e.ToTable("Cours");
            e.HasIndex(x => new { x.PromotionId, x.Number }).IsUnique();
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourseDay>(e =>
        {
            e.ToTable("JoursDeCours");
            e.HasIndex(x => new { x.PromotionId, x.Date }).IsUnique();
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CardBatch>(e => e.ToTable("LotsCartes"));
        b.Entity<CourseAssignment>(e =>
        {
            e.ToTable("AffectationsCours");
            e.Ignore(x => x.IsCurrent);
            e.HasIndex(x => x.CourseId);
            e.HasIndex(x => x.TeacherId);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Assessment>(e =>
        {
            e.ToTable("Evaluations");
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Grade>(e =>
        {
            e.ToTable("Notes");
            e.HasIndex(x => new { x.AssessmentId, x.EnrollmentId }).IsUnique();
            e.HasOne<Assessment>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("Presences");
            e.HasIndex(x => new { x.EnrollmentId, x.Date }).IsUnique();
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DisciplineIncident>(e =>
        {
            e.ToTable("Discipline");
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DisciplineDecision>(e =>
        {
            e.ToTable("RegistreDiscipline");
            e.HasIndex(x => x.EnrollmentId);
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StaffAttendanceMark>(e =>
        {
            e.ToTable("PointageEnseignants");
            e.HasIndex(x => new { x.StaffMemberId, x.Date, x.Kind }).IsUnique();
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StaffDayJustification>(e =>
        {
            e.ToTable("JustificationsEnseignants");
            e.Ignore(x => x.IsActive);
            e.HasIndex(x => new { x.StaffMemberId, x.Date });
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StaffMember>(e => { e.ToTable("Personnel"); e.HasIndex(x => x.Number).IsUnique(); e.Ignore(x => x.FullName); });
        b.Entity<PayrollEntry>(e =>
        {
            e.ToTable("Paie");
            e.HasIndex(x => new { x.StaffMemberId, x.Period }).IsUnique();
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges() => throw new NotSupportedException("Utilisez SaveChangesAsync.");

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock?.UtcNow ?? DateTime.UtcNow;
        var user = currentUser?.Name ?? "système";
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.CreatedBy = user;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.UpdatedBy = user;
                entry.Entity.Version++;
            }
        }

        var audits = auditBuffer?.Pending.ToList() ?? [];
        // Garde-fou : aucune donnée ne change sans trace. Une écriture non tracée est refusée
        // au lieu d'être enregistrée silencieusement.
        if (audits.Count == 0 && auditBuffer is not null && ChangeTracker.Entries<Entity>().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Écriture refusée : cette opération n'a pas de trace d'audit.");
        if (audits.Count == 0) return await base.SaveChangesAsync(cancellationToken);

        await AuditChainLock.WaitAsync(cancellationToken);
        try
        {
            var last = await AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Select(a => new { a.Id, a.Hash }).FirstOrDefaultAsync(cancellationToken);
            var id = last?.Id ?? 0;
            var previous = last?.Hash ?? string.Empty;
            foreach (var audit in audits)
            {
                audit.Id = ++id;
                audit.AtUtc = DateTime.SpecifyKind(new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
                audit.PreviousHash = previous;
                audit.Hash = audit.ComputeHash();
                previous = audit.Hash;
            }
            AuditEntries.AddRange(audits);
            var result = await base.SaveChangesAsync(cancellationToken);
            auditBuffer!.Pending.Clear();
            return result;
        }
        finally
        {
            AuditChainLock.Release();
        }
    }
}

public sealed class DecimalToHundredthsConverter() : ValueConverter<decimal, long>(
    v => (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero), v => v / 100m);

public sealed class NullableDecimalToHundredthsConverter() : ValueConverter<decimal?, long?>(
    v => v == null ? null : (long)Math.Round(v.Value * 100m, MidpointRounding.AwayFromZero), v => v == null ? null : v.Value / 100m);

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? null : v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : v, v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
