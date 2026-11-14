using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Scolia.Application.Abstractions;
using Scolia.Domain.Academics;
using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
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
    public DbSet<SchoolCycle> SchoolCycles => Set<SchoolCycle>();
    public DbSet<SchoolCycleFeePolicy> SchoolCycleFeePolicies => Set<SchoolCycleFeePolicy>();
    public DbSet<SchoolCyclePeriod> SchoolCyclePeriods => Set<SchoolCyclePeriod>();
    public DbSet<SchoolCycleAssessment> SchoolCycleAssessments => Set<SchoolCycleAssessment>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<AcademicPeriod> Periods => Set<AcademicPeriod>();
    public DbSet<PedagogicalPeriod> PedagogicalPeriods => Set<PedagogicalPeriod>();
    public DbSet<AcademicStructureUnit> AcademicStructureUnits => Set<AcademicStructureUnit>();
    public DbSet<CurriculumItem> CurriculumItems => Set<CurriculumItem>();
    public DbSet<AcademicRegulation> AcademicRegulations => Set<AcademicRegulation>();
    public DbSet<EpstConductMark> EpstConductMarks => Set<EpstConductMark>();
    public DbSet<EpstDeliberation> EpstDeliberations => Set<EpstDeliberation>();
    public DbSet<EpstDecision> EpstDecisions => Set<EpstDecision>();
    public DbSet<EpstRepechageMark> EpstRepechageMarks => Set<EpstRepechageMark>();
    public DbSet<NationalExamResult> NationalExamResults => Set<NationalExamResult>();
    public DbSet<LmdMark> LmdMarks => Set<LmdMark>();
    public DbSet<LmdDeliberation> LmdDeliberations => Set<LmdDeliberation>();
    public DbSet<LmdDecision> LmdDecisions => Set<LmdDecision>();
    public DbSet<LmdDebt> LmdDebts => Set<LmdDebt>();
    public DbSet<CurriculumAssignment> CurriculumAssignments => Set<CurriculumAssignment>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<FeeItem> FeeItems => Set<FeeItem>();
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
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
    public DbSet<AnonymousCode> AnonymousCodes => Set<AnonymousCode>();
    public DbSet<CourseMark> CourseMarks => Set<CourseMark>();
    public DbSet<DeliberationSettings> DeliberationSettings => Set<DeliberationSettings>();
    public DbSet<DeliberationSession> DeliberationSessions => Set<DeliberationSession>();
    public DbSet<JuryDecision> JuryDecisions => Set<JuryDecision>();
    public DbSet<DeliberationResult> DeliberationResults => Set<DeliberationResult>();
    public DbSet<MarkSubmission> MarkSubmissions => Set<MarkSubmission>();

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

        b.Entity<SchoolProfile>(e =>
        {
            e.ToTable("Etablissement");
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.InstitutionType).HasMaxLength(32).HasDefaultValue(InstitutionProfiles.ItmIm);
            e.Property(x => x.Governance).HasDefaultValue(SchoolGovernanceKind.Prive);
        });

        // Un seul établissement, mais jusqu'à trois cycles scolaires.
        // Ces tables sont optionnelles, entièrement absentes des installations ITM historiques.
        b.Entity<SchoolCycle>(e =>
        {
            e.ToTable("CyclesScolaires");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.AuthorizationNumber).HasMaxLength(100);
            e.HasIndex(x => new { x.SchoolId, x.Kind }).IsUnique();
            e.HasOne<SchoolProfile>().WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SchoolCycleFeePolicy>(e =>
        {
            e.ToTable("TarifsCyclesScolaires");
            e.Property(x => x.LegalBasis).HasMaxLength(500);
            e.HasIndex(x => new { x.SchoolCycleId, x.AcademicYearId }).IsUnique();
            e.HasOne<SchoolCycle>().WithMany().HasForeignKey(x => x.SchoolCycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SchoolCyclePeriod>(e =>
        {
            e.ToTable("PeriodesCyclesScolaires");
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.TermCode).HasMaxLength(16);
            e.HasIndex(x => new { x.SchoolCycleId, x.AcademicYearId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.SchoolCycleId, x.AcademicYearId, x.Order }).IsUnique();
            e.HasOne<SchoolCycle>().WithMany().HasForeignKey(x => x.SchoolCycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AcademicYear>(e =>
        {
            e.ToTable("AnneesScolaires");
            e.HasIndex(x => x.Label).IsUnique();
            e.HasMany(x => x.Periods).WithOne().HasForeignKey(p => p.AcademicYearId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(x => x.FirstCalendarYear);
        });
        b.Entity<AcademicPeriod>(e => { e.ToTable("Periodes"); e.HasIndex(x => new { x.AcademicYearId, x.Order }).IsUnique(); });
        // Les périodes pédagogiques ne sont PAS les échéances financières.
        b.Entity<PedagogicalPeriod>(e =>
        {
            e.ToTable("PeriodesPedagogiques");
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => new { x.AcademicYearId, x.Order }).IsUnique();
            e.HasIndex(x => new { x.AcademicYearId, x.Code }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        });
        // Registre séparé des évaluations scolaires : aucune réutilisation des
        // notes ITM/IM, aucune conversion automatique des observations en scores.
        b.Entity<AcademicRegulation>(e =>
        {
            // Règlement pédagogique EPST / LMD : une seule ligne par année et par portée.
            e.ToTable("ReglementsPedagogiques");
            e.Property(x => x.LegalReference).HasMaxLength(300);
            e.Property(x => x.ValidatedByName).HasMaxLength(120);
            e.HasIndex(x => new { x.AcademicYearId, x.Scope }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EpstConductMark>(e =>
        {
            // Application et conduite par période : une ligne par élève et par période.
            e.ToTable("ConduitesCyclesScolaires");
            e.Property(x => x.Application).HasMaxLength(4);
            e.Property(x => x.Conduct).HasMaxLength(4);
            e.HasIndex(x => new { x.EnrollmentId, x.SchoolCyclePeriodId }).IsUnique();
            e.HasIndex(x => new { x.PromotionId, x.AcademicYearId });
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolCyclePeriod>().WithMany().HasForeignKey(x => x.SchoolCyclePeriodId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EpstDeliberation>(e =>
        {
            // Délibération de fin d'année d'une classe EPST, distincte des sessions ITM.
            e.ToTable("DeliberationsCyclesScolaires");
            e.Property(x => x.FirstSessionClosedBy).HasMaxLength(120);
            e.Property(x => x.ClosedBy).HasMaxLength(120);
            e.HasIndex(x => new { x.PromotionId, x.AcademicYearId }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EpstDecision>(e =>
        {
            e.ToTable("DecisionsCyclesScolaires");
            e.Property(x => x.JuryReason).HasMaxLength(300);
            e.Property(x => x.RepechageSubjects).HasMaxLength(400);
            e.Ignore(x => x.RepechageSubjectIds);
            e.Ignore(x => x.Effective);
            e.HasIndex(x => new { x.DeliberationId, x.EnrollmentId }).IsUnique();
            e.HasOne<EpstDeliberation>().WithMany().HasForeignKey(x => x.DeliberationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EpstRepechageMark>(e =>
        {
            e.ToTable("RepechagesCyclesScolaires");
            e.HasIndex(x => new { x.DeliberationId, x.EnrollmentId, x.SubjectId }).IsUnique();
            e.HasOne<EpstDeliberation>().WithMany().HasForeignKey(x => x.DeliberationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CurriculumItem>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NationalExamResult>(e =>
        {
            e.ToTable("ExamensNationaux");
            e.Property(x => x.Reference).HasMaxLength(60);
            e.HasIndex(x => new { x.EnrollmentId, x.Exam }).IsUnique();
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LmdMark>(e =>
        {
            // Notes des EC par session, distinctes des cotes ITM (CourseMarks).
            e.ToTable("NotesLmd");
            e.HasIndex(x => new { x.EnrollmentId, x.ElementId, x.Session }).IsUnique();
            e.HasIndex(x => new { x.PromotionId, x.AcademicYearId });
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CurriculumItem>().WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LmdDeliberation>(e =>
        {
            e.ToTable("DeliberationsLmd");
            e.Property(x => x.S1PublishedBy).HasMaxLength(120);
            e.Property(x => x.FirstSessionClosedBy).HasMaxLength(120);
            e.Property(x => x.ClosedBy).HasMaxLength(120);
            e.HasIndex(x => new { x.PromotionId, x.AcademicYearId }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LmdDecision>(e =>
        {
            e.ToTable("DecisionsLmd");
            e.Property(x => x.JuryReason).HasMaxLength(300);
            e.Property(x => x.FinalJuryReason).HasMaxLength(300);
            e.Ignore(x => x.Effective);
            e.HasIndex(x => new { x.DeliberationId, x.EnrollmentId }).IsUnique();
            e.HasOne<LmdDeliberation>().WithMany().HasForeignKey(x => x.DeliberationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LmdDebt>(e =>
        {
            e.ToTable("DettesLmd");
            e.Property(x => x.UeCode).HasMaxLength(24);
            e.Property(x => x.UeName).HasMaxLength(160);
            e.Property(x => x.Semester).HasMaxLength(4);
            e.Property(x => x.ClearedBy).HasMaxLength(120);
            e.Ignore(x => x.IsCleared);
            e.HasIndex(x => new { x.OriginEnrollmentId, x.UeId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.OriginEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CurriculumAssignment>(e =>
        {
            // Attribution des branches EPST et des EC LMD, distincte des cours ITM.
            e.ToTable("AttributionsProgramme");
            e.Property(x => x.EndReason).HasMaxLength(200);
            e.Property(x => x.AssignedByName).HasMaxLength(120);
            e.Ignore(x => x.IsCurrent);
            e.HasIndex(x => x.CurriculumItemId).IsUnique().HasFilter("\"EndedOn\" IS NULL");
            e.HasIndex(x => x.TeacherId);
            e.HasOne<CurriculumItem>().WithMany().HasForeignKey(x => x.CurriculumItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SchoolCycleAssessment>(e =>
        {
            e.ToTable("EvaluationsCyclesScolaires");
            e.Property(x => x.EvaluationCode).HasMaxLength(24);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Observation).HasMaxLength(800);
            e.Property(x => x.Comment).HasMaxLength(800);
            // Les codes d'interrogation (ex. INTERRO1) peuvent se répéter entre matières.
            // Deux index partiels garantissent aussi l'unicité des activités sans matière en maternelle.
            e.HasIndex(x => new { x.EnrollmentId, x.SchoolCyclePeriodId, x.EvaluationCode })
                .IsUnique().HasFilter("\"CurriculumItemId\" IS NULL");
            e.HasIndex(x => new { x.EnrollmentId, x.SchoolCyclePeriodId, x.CurriculumItemId, x.EvaluationCode })
                .IsUnique().HasFilter("\"CurriculumItemId\" IS NOT NULL");
            e.HasIndex(x => new { x.SchoolCycleId, x.AcademicYearId, x.PromotionId });
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolCycle>().WithMany().HasForeignKey(x => x.SchoolCycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchoolCyclePeriod>().WithMany().HasForeignKey(x => x.SchoolCyclePeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CurriculumItem>().WithMany().HasForeignKey(x => x.CurriculumItemId).OnDelete(DeleteBehavior.Restrict);
        });
        // Référentiels nouveaux profils : isolés des promotions et des cours ITM existants.
        b.Entity<AcademicStructureUnit>(e =>
        {
            e.ToTable("StructuresAcademiques");
            e.Property(x => x.Code).HasMaxLength(24);
            e.Property(x => x.Name).HasMaxLength(160);
            e.HasIndex(x => new { x.ParentId, x.Kind, x.Code });
            e.HasOne<AcademicStructureUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CurriculumItem>(e =>
        {
            e.ToTable("ProgrammesPedagogiques");
            e.Property(x => x.Code).HasMaxLength(24);
            e.Property(x => x.Name).HasMaxLength(160);
            e.HasIndex(x => new { x.AcademicYearId, x.PromotionId, x.Semester });
            e.HasIndex(x => x.AcademicUnitId);
            e.HasIndex(x => x.ParentItemId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicStructureUnit>().WithMany().HasForeignKey(x => x.AcademicUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CurriculumItem>().WithMany().HasForeignKey(x => x.ParentItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Section>(e => { e.ToTable("Sections"); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<Promotion>(e =>
        {
            e.ToTable("Promotions");
            e.HasIndex(x => new { x.AcademicYearId, x.Code }).IsUnique();
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
            // SQLite ne peut pas ajouter une clé étrangère sur Promotions sans reconstruire la table.
            // Le rattachement est validé par le service et indexé ; pas de migration destructive.
            e.HasIndex(x => x.SchoolCycleId);
            e.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Fees).WithOne().HasForeignKey(f => f.PromotionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<FeeItem>(e =>
        {
            e.ToTable("RubriquesFrais");
            e.HasIndex(x => x.PromotionId);
            e.HasIndex(x => x.CategoryId);
            e.HasOne<FeeCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FeeCategory>(e => { e.ToTable("CategoriesFrais"); e.HasIndex(x => x.NameKey).IsUnique(); e.Property(x => x.Name).HasMaxLength(FeeCategory.MaxNameLength); });
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
        b.Entity<AppUser>(e =>
        {
            e.ToTable("Utilisateurs");
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
            e.HasIndex(x => x.StaffMemberId);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffMemberId).OnDelete(DeleteBehavior.SetNull);
        });
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
        // Délibération (fusion d'ITMCD) : codes anonymes, cotes par semestre, sessions du jury et résultats figés.
        b.Entity<AnonymousCode>(e =>
        {
            e.ToTable("CodesAnonymes");
            e.HasIndex(x => new { x.AcademicYearId, x.Code }).IsUnique();
            e.HasIndex(x => x.EnrollmentId).IsUnique().HasFilter("\"IsActive\" = 1");
            e.Property(x => x.Code).HasMaxLength(AnonymousCodeRules.MaxLength);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CourseMark>(e =>
        {
            e.ToTable("CotesDeliberation");
            e.HasIndex(x => new { x.EnrollmentId, x.CourseId, x.Kind }).IsUnique();
            e.HasIndex(x => x.CourseId);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MarkSubmission>(e =>
        {
            e.ToTable("RemisesCotes");
            e.HasIndex(x => new { x.CourseId, x.Kind }).IsUnique();
            e.Property(x => x.SubmittedByName).HasMaxLength(120);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DeliberationSettings>(e =>
        {
            e.ToTable("ReglagesDeliberation");
            e.HasIndex(x => x.AcademicYearId).IsUnique();
            e.Property(x => x.Prefect).HasMaxLength(120);
            e.Property(x => x.Principal).HasMaxLength(120);
            e.Property(x => x.JuryPresident).HasMaxLength(120);
            e.Property(x => x.JurySecretary).HasMaxLength(120);
            e.Property(x => x.JuryMembers).HasMaxLength(600);
        });
        b.Entity<DeliberationSession>(e =>
        {
            e.ToTable("SessionsDeliberation");
            e.Ignore(x => x.IsClosed);
            e.HasIndex(x => new { x.PromotionId, x.Period }).IsUnique();
            e.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<JuryDecision>(e =>
        {
            e.ToTable("DecisionsJury");
            e.HasIndex(x => new { x.SessionId, x.EnrollmentId }).IsUnique();
            e.HasOne<DeliberationSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DeliberationResult>(e =>
        {
            e.ToTable("ResultatsDeliberation");
            e.HasIndex(x => new { x.SessionId, x.EnrollmentId }).IsUnique();
            e.HasIndex(x => x.VerificationCode);
            e.Property(x => x.VerificationCode).HasMaxLength(16);
            e.HasOne<DeliberationSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
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
            auditBuffer!.Saved.AddRange(audits);
            auditBuffer.Pending.Clear();
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
