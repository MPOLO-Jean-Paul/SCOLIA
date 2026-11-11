using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Contrôle de migration avec la base de test ITM :
/// les cycles restent optionnels et les paiements historiques inchangés.
/// </summary>
public sealed class ComplexSchoolMigrationSafetyTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Existing_itm_setup_remains_private_by_default_with_no_school_cycles()
    {
        var admin = await app.InstalledAdmin();
        var response = await admin.GetAsync("/api/session").Ok();
        Assert.Equal(InstitutionProfiles.ItmIm, response.GetProperty("institutionType").GetString());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var school = await db.Schools.SingleAsync();
        Assert.Equal(SchoolGovernanceKind.Prive, school.Governance);
        Assert.Equal(InstitutionProfiles.ItmIm, school.InstitutionType);

        Assert.Equal(10, await db.Periods.CountAsync());
        Assert.Empty(await db.SchoolCycles.ToArrayAsync());
        Assert.Empty(await db.SchoolCycleFeePolicies.ToArrayAsync());
        Assert.Empty(await db.SchoolCyclePeriods.ToArrayAsync());
        Assert.All(await db.Promotions.ToArrayAsync(), p => Assert.Null(p.SchoolCycleId));
    }

    [Fact]
    public async Task Complex_school_tables_support_distinct_cycles_and_yearly_tariffs()
    {
        await app.InstalledAdmin();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var school = await db.Schools.SingleAsync();
        var year = await db.AcademicYears.SingleAsync(x => x.IsCurrent);

        var nursery = new SchoolCycle { SchoolId = school.Id, Kind = SchoolCycleKind.Maternelle, Name = "Maternelle" };
        var primary = new SchoolCycle { SchoolId = school.Id, Kind = SchoolCycleKind.Primaire, Name = "Primaire" };
        db.SchoolCycles.AddRange(nursery, primary);
        db.SchoolCycleFeePolicies.AddRange(
            new SchoolCycleFeePolicy { SchoolCycleId = nursery.Id, AcademicYearId = year.Id, MonthlyAmount = 70000m, ChargeMonthlyTuition = true },
            new SchoolCycleFeePolicy { SchoolCycleId = primary.Id, AcademicYearId = year.Id, MonthlyAmount = 90000m, ChargeMonthlyTuition = true });
        db.SchoolCyclePeriods.AddRange(
            new SchoolCyclePeriod
            {
                SchoolCycleId = nursery.Id, AcademicYearId = year.Id, Code = "P1", Name = "Observation 1",
                TermCode = "T1", Mode = SchoolCycleAssessmentMode.Observation, Order = 1,
                StartDate = year.StartDate, EndDate = year.StartDate.AddDays(8),
            },
            new SchoolCyclePeriod
            {
                SchoolCycleId = primary.Id, AcademicYearId = year.Id, Code = "P1", Name = "Période 1",
                TermCode = "T1", Mode = SchoolCycleAssessmentMode.Evaluation, Order = 1,
                StartDate = year.StartDate, EndDate = year.StartDate.AddDays(8),
            });
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.SchoolCycles.CountAsync());
        Assert.Equal(2, await db.SchoolCycleFeePolicies.CountAsync());
        Assert.Equal(2, await db.SchoolCyclePeriods.CountAsync(p => p.Code == "P1"));
        Assert.Equal(10, await db.Periods.CountAsync());
        Assert.Equal(InstitutionProfiles.ItmIm, (await db.Schools.SingleAsync()).InstitutionType);
    }
}
