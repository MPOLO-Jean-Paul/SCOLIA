using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scolia.Application.Abstractions;
using Scolia.Application.Services;
using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Security;
using Scolia.Infrastructure.Backup;
using Scolia.Infrastructure.Documents;
using Scolia.Infrastructure.Import;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScolia(this IServiceCollection services, IConfiguration configuration, string defaultDataDirectory)
    {
        services.AddOptions<ScoliaStorageOptions>()
            .Bind(configuration.GetSection("Scolia:Stockage"))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.DataDirectory)) o.DataDirectory = defaultDataDirectory;
                o.DataDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(o.DataDirectory));
                Directory.CreateDirectory(o.DataDirectory);
            });
        services.AddDbContext<ScoliaDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<IOptions<ScoliaStorageOptions>>().Value.ConnectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ScoliaDbContext>());
        services.AddScoped<IWriteGate, WriteGate>();
        services.AddScoped<AuditBuffer>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<ISpreadsheetService, SpreadsheetService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<DataRevision>();
        services.AddSingleton<Live.LiveHub>();
        services.AddHostedService<AutomaticBackupWorker>();
        // Rapport journalier : PDF du jour à l'heure choisie, envoyé par WhatsApp si l'établissement l'a configuré.
        services.AddHttpClient("whatsapp", c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<Reports.DailyReportStore>();
        services.AddSingleton<Reports.WhatsAppCloudSender>();
        services.AddSingleton<Reports.EmailReportSender>();
        services.AddSingleton<Reports.DailyReportRunner>();
        services.AddHostedService<Reports.DailyReportWorker>();
        services.AddHostedService<Network.LanAnnouncer>();
        services.AddScoped<LegacyImportService>();

        services.AddScoped<SetupService>();
        services.AddScoped<SchoolService>();
        services.AddScoped<SchoolCycleConfigurationService>();
        services.AddScoped<LmdPlanningService>();
        services.AddScoped<SchoolCycleAssessmentService>();
        services.AddScoped<FeeCategoryService>();
        services.AddScoped<AccountService>();
        services.AddScoped<StudentService>();
        services.AddScoped<SponsorshipService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentBatchService>();
        services.AddScoped<DataErasureService>();
        services.AddScoped<CashService>();
        services.AddScoped<ReportService>();
        services.AddScoped<DailyReportService>();
        services.AddScoped<UserService>();
        services.AddSingleton<UnknownLoginTracker>();
        services.AddScoped<AuditService>();
        services.AddScoped<SearchService>();
        services.AddScoped<AcademicService>();
        services.AddScoped<DisciplineService>();
        services.AddScoped<TeachingService>();
        services.AddScoped<DeliberationService>();
        services.AddScoped<TeacherAccountService>();
        services.AddScoped<StaffAttendanceService>();
        services.AddScoped<CardService>();
        services.AddScoped<StaffService>();
        return services;
    }

    /// <summary>Crée ou met à jour le schéma, active le journal WAL (lectures pendant les écritures) et vérifie l'intégrité.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync(ct);
        }
        // Mise à jour du logiciel sur une base existante : sauvegarde vérifiée avant de modifier le schéma.
        var hasData = (await db.Database.GetAppliedMigrationsAsync(ct)).Any();
        var hasPending = (await db.Database.GetPendingMigrationsAsync(ct)).Any();
        if (hasData && hasPending)
            await scope.ServiceProvider.GetRequiredService<BackupService>().CreateAsync("avantmiseajour", ct);
        await db.Database.MigrateAsync(ct);
        await ConvertLegacyRolesAsync(db, ct);
        await AttachLegacyFeesToCategoriesAsync(db, scope.ServiceProvider.GetRequiredService<IAuditLog>(), ct);
    }

    /// <summary>
    /// Les frais saisis avant le catalogue (inscription, examen…) reçoivent leur catégorie, créée d'après leur libellé :
    /// rien n'est supprimé ni recalculé, et une catégorie n'apparaît que si l'établissement avait déjà saisi ces frais.
    /// </summary>
    public static async Task AttachLegacyFeesToCategoriesAsync(ScoliaDbContext db, IAuditLog audit, CancellationToken ct = default)
    {
        var orphans = await db.FeeItems.Where(f => f.Kind != FeeKind.Mensualite && f.CategoryId == null).ToListAsync(ct);
        if (orphans.Count == 0) return;
        var categories = await db.FeeCategories.ToDictionaryAsync(c => c.NameKey, ct);
        var order = categories.Count;
        foreach (var group in orphans.GroupBy(f => TextKey.Normalize(f.Label)))
        {
            if (group.Key.Length == 0) continue;
            if (!categories.TryGetValue(group.Key, out var category))
            {
                var first = group.OrderBy(f => f.Order).First();
                var name = first.Label.Trim();
                category = new FeeCategory
                {
                    Name = name.Length > FeeCategory.MaxNameLength ? name[..FeeCategory.MaxNameLength] : name, NameKey = group.Key,
                    // Montant le plus fréquent parmi les promotions, la promotion gardant son propre montant.
                    Amount = group.GroupBy(f => f.Amount).OrderByDescending(g => g.Count()).First().Key,
                    IsActive = true, Order = ++order,
                };
                db.FeeCategories.Add(category);
                categories[group.Key] = category;
            }
            foreach (var item in group) item.CategoryId = category.Id;
        }
        // Toute écriture est tracée dans le journal chaîné, y compris cette reprise au démarrage.
        audit.Record("frais.categories.reprise", "CategorieFrais", null,
            $"Mise à jour : {orphans.Count} frais existant(s) rattaché(s) au catalogue des catégories ({string.Join(", ", categories.Values.Select(c => c.Name).Distinct())}).");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deux comptes seulement : l'ancien administrateur et la direction deviennent le secrétariat, qui administre SCOLIA.
    /// Un ancien compte enseignant est désactivé ; le secrétariat peut le réactiver en lui donnant l'un des deux rôles.</summary>
    private static async Task ConvertLegacyRolesAsync(ScoliaDbContext db, CancellationToken ct)
    {
        await db.Users.Where(u => u.Role == Role.Administrateur || u.Role == Role.Direction)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Role.Secretariat), ct);
        // Les anciens comptes enseignants sans fiche du personnel restent désactivés ; un compte enseignant relié
        // à sa fiche (encodage des cotes de ses cours) est le rôle actuel.
        await db.Users.Where(u => u.Role == Role.Enseignant && u.IsActive && u.StaffMemberId == null)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false).SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString("N")), ct);
    }
}

/// <summary>Utilisé uniquement par l'outil « dotnet ef » pour générer les migrations.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ScoliaDbContext>
{
    public ScoliaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ScoliaDbContext>().UseSqlite("Data Source=design.db").Options);
}

public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}
