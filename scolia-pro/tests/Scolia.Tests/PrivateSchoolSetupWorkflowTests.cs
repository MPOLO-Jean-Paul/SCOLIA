using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Parcours complet du premier démarrage privé et attribution réelle des classes aux cycles.</summary>
public sealed class PrivateSchoolSetupWorkflowTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> InstallAsync(string type)
    {
        var client = app.Client();
        await client.Post("/api/installation", new
        {
            institutionType = type, schoolName = "École privée de test",
            shortName = "ECO", city = "Lubumbashi", motto = (string?)null,
            phone = "0810000000", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Now)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Administrateur",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return client;
    }

    [Theory]
    [InlineData("Maternelle")]
    [InlineData("Primaire")]
    [InlineData("Secondaire")]
    public async Task Single_cycle_school_is_initialized_and_its_class_is_bound_to_the_cycle(string type)
    {
        var admin = await InstallAsync(type);
        var configuration = await admin.GetAsync("/api/etablissement/cycles").Ok();
        Assert.Equal(type, configuration.GetProperty("institutionType").GetString());
        var cycle = Assert.Single(configuration.GetProperty("cycles").EnumerateArray());
        Assert.Equal(type, cycle.GetProperty("kind").GetString());
        Assert.True(cycle.GetProperty("isActive").GetBoolean());
        Assert.Equal(0m, cycle.GetProperty("monthlyAmount").GetDecimal());
        Assert.False(cycle.GetProperty("chargeMonthlyTuition").GetBoolean());

        var cycleId = cycle.GetProperty("id").GetGuid();
        await admin.Post("/api/etablissement/promotions", new
        {
            code = "CLS1", name = "Classe un", sectionId = (Guid?)null,
            schoolCycleId = cycleId, level = 1, capacity = (int?)null,
            isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(),
        }).Ok();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var created = await db.Promotions.SingleAsync(p => p.Code == "CLS1");
        Assert.Equal(cycleId, created.SchoolCycleId);
        Assert.Equal(10, await db.Periods.CountAsync());
        Assert.Equal(SchoolGovernanceKind.Prive, (await db.Schools.SingleAsync()).Governance);
        Assert.Empty(await db.SchoolCycleAssessments.ToListAsync());
    }

    [Fact]
    public async Task Complex_school_selects_two_cycles_without_creating_charges_automatically()
    {
        var admin = await InstallAsync("ComplexeScolaire");
        var empty = await admin.GetAsync("/api/etablissement/cycles").Ok();
        Assert.Empty(empty.GetProperty("cycles").EnumerateArray());
        var configured = await admin.Put("/api/etablissement/cycles", new
        {
            cycles = new[]
            {
                new { kind = "Maternelle", name = "Maternelle", authorizationNumber = (string?)null,
                    chargeMonthlyTuition = true, monthlyAmount = 70000m },
                new { kind = "Primaire", name = "Primaire", authorizationNumber = (string?)null,
                    chargeMonthlyTuition = true, monthlyAmount = 90000m },
            },
        }).Ok();
        var cycles = configured.GetProperty("cycles").EnumerateArray().ToArray();
        Assert.Equal(2, cycles.Length);
        Assert.NotEqual(cycles[0].GetProperty("id").GetGuid(), cycles[1].GetProperty("id").GetGuid());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Equal(2, await db.SchoolCycleFeePolicies.CountAsync());
        Assert.Empty(await db.Promotions.ToListAsync());
        Assert.Empty(await db.Receipts.ToListAsync());
    }

    [Fact]
    public async Task Lmd_institution_can_open_without_ITM_school_cycles_or_fees()
    {
        var admin = await InstallAsync("SuperieurLmd");
        var school = await admin.GetAsync("/api/etablissement").Ok();
        Assert.Equal("SuperieurLmd", school.GetProperty("institutionType").GetString());
        await admin.Post("/api/etablissement/promotions", new
        {
            code = "LMD1", name = "Licence 1", sectionId = (Guid?)null,
            schoolCycleId = (Guid?)null, level = 1, capacity = (int?)null,
            isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Empty(await db.SchoolCycles.ToListAsync());
        Assert.Equal("SuperieurLmd", (await db.Schools.SingleAsync()).InstitutionType);
        Assert.Empty(await db.FeeItems.ToListAsync());
    }
}
