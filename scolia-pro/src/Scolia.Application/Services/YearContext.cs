using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Domain.Common;
using Scolia.Domain.School;

namespace Scolia.Application.Services;

public static class YearContext
{
    public static async Task<AcademicYear> CurrentAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var year = await db.AcademicYears.Include(y => y.Periods).FirstOrDefaultAsync(y => y.IsCurrent, ct)
            ?? throw new DomainException("annee", "Aucune année scolaire courante n'est définie. Configurez-la dans Paramètres.");
        year.Periods = [.. year.Periods.OrderBy(p => p.Order)];
        return year;
    }

    public static async Task<AcademicYear> GetAsync(IAppDbContext db, Guid? yearId, CancellationToken ct = default)
    {
        if (yearId is null) return await CurrentAsync(db, ct);
        var year = await db.AcademicYears.Include(y => y.Periods).FirstOrDefaultAsync(y => y.Id == yearId, ct)
            ?? throw new NotFoundException("Année scolaire introuvable.");
        year.Periods = [.. year.Periods.OrderBy(p => p.Order)];
        return year;
    }

    public static async Task<SchoolProfile> SchoolAsync(IAppDbContext db, CancellationToken ct = default) =>
        await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct) ?? throw new DomainException("configuration", "L'établissement n'est pas encore configuré.");
}
