using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Vérifie que les nouvelles tables existent après migration, sans modifier
/// les mois de paiement ou le type d'une installation ITM existante.
/// </summary>
public sealed class CurriculumPersistenceSafetyTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task New_curriculum_tables_exist_and_legacy_itm_data_remain_usable()
    {
        var admin = await app.InstalledAdmin();
        var session = await admin.GetAsync("/api/session").Ok();
        Assert.Equal(InstitutionProfiles.ItmIm, session.GetProperty("institutionType").GetString());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var school = await db.Schools.SingleAsync();
        Assert.Equal(InstitutionProfiles.ItmIm, school.InstitutionType);

        Assert.Equal(10, await db.Periods.CountAsync());
        Assert.Empty(await db.PedagogicalPeriods.ToArrayAsync());
        Assert.Empty(await db.AcademicStructureUnits.ToArrayAsync());
        Assert.Empty(await db.CurriculumItems.ToArrayAsync());
    }
}
