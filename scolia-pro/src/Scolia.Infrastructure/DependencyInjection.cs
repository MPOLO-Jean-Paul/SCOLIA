using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Scolia.Application.Abstractions;
using Scolia.Application.Services;
using Scolia.Domain.Common;
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
        services.AddHostedService<AutomaticBackupWorker>();
        services.AddHostedService<Network.LanAnnouncer>();
        services.AddScoped<LegacyImportService>();

        services.AddScoped<SetupService>();
        services.AddScoped<SchoolService>();
        services.AddScoped<AccountService>();
        services.AddScoped<StudentService>();
        services.AddScoped<SponsorshipService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentBatchService>();
        services.AddScoped<DataErasureService>();
        services.AddScoped<CashService>();
        services.AddScoped<ReportService>();
        services.AddScoped<UserService>();
        services.AddSingleton<UnknownLoginTracker>();
        services.AddScoped<AuditService>();
        services.AddScoped<SearchService>();
        services.AddScoped<AcademicService>();
        services.AddScoped<DisciplineService>();
        services.AddScoped<TeachingService>();
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
    }

    /// <summary>Deux comptes seulement : l'ancien administrateur et la direction deviennent le secrétariat, qui administre SCOLIA.
    /// Un ancien compte enseignant est désactivé ; le secrétariat peut le réactiver en lui donnant l'un des deux rôles.</summary>
    private static async Task ConvertLegacyRolesAsync(ScoliaDbContext db, CancellationToken ct)
    {
        await db.Users.Where(u => u.Role == Role.Administrateur || u.Role == Role.Direction)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Role.Secretariat), ct);
        await db.Users.Where(u => u.Role == Role.Enseignant && u.IsActive)
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
