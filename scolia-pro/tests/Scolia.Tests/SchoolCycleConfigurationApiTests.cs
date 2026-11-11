using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Application.Abstractions;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Les futurs outils de configuration doivent être sûrs dès l'apparition
/// de leurs routes : secretariat seulement, aucun changement d'ITM/IM.
/// </summary>
public sealed class SchoolCycleConfigurationApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Itm_can_read_empty_configuration_but_cannot_change_its_school_cycles()
    {
        var admin = await app.InstalledAdmin();
        var available = await admin.GetAsync("/api/etablissement/cycles").Ok();
        Assert.Equal(InstitutionProfiles.ItmIm, available.GetProperty("institutionType").GetString());
        Assert.Equal("Prive", available.GetProperty("governance").GetString());
        Assert.Empty(available.GetProperty("cycles").EnumerateArray());

        var (status, problem) = await admin.Put("/api/etablissement/cycles", new
        {
            cycles = new object[]
            {
                new { kind = "Maternelle", name = "Maternelle", authorizationNumber = (string?)null,
                      chargeMonthlyTuition = true, monthlyAmount = 75000m },
                new { kind = "Primaire", name = "Primaire", authorizationNumber = (string?)null,
                      chargeMonthlyTuition = true, monthlyAmount = 95000m },
            },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("cycles", problem.GetProperty("code").GetString());

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Empty(await db.SchoolCycles.ToArrayAsync());
        Assert.Empty(await db.SchoolCycleFeePolicies.ToArrayAsync());
        Assert.Equal(10, await db.Periods.CountAsync());
    }

    [Fact]
    public async Task School_profile_can_assign_primary_class_without_monthly_fee_while_itm_rules_remain_intact()
    {
        var admin = await app.InstalledAdmin();

        // Une installation ITM garde strictement sa mensualité obligatoire.
        var (itmStatus, _) = await admin.Post("/api/etablissement/promotions", new
        {
            code = "IT01", name = "ITM", sectionId = (Guid?)null,
            level = 1, capacity = (int?)null, isActive = true,
            nextPromotionCode = (string?)null, monthlyFee = 0m,
            oneTimeFees = Array.Empty<object>(),
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, itmStatus);

        // Simulation isolée d'un futur profil scolaire : le parcours
        // d'installation reste verrouillé en production tant que non validé.
        Guid primaryId;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var school = await db.Schools.SingleAsync();
            school.InstitutionType = InstitutionProfiles.ComplexeScolaire;
            var nursery = new SchoolCycle
            {
                SchoolId = school.Id, Kind = SchoolCycleKind.Maternelle, Name = "Maternelle",
            };
            var primary = new SchoolCycle
            {
                SchoolId = school.Id, Kind = SchoolCycleKind.Primaire, Name = "Primaire",
            };
            primaryId = primary.Id;
            db.SchoolCycles.AddRange(nursery, primary);
            scope.ServiceProvider.GetRequiredService<IAuditLog>()
                .Record("test.profil.scolaire", "Etablissement", school.Id.ToString(),
                    "Profil de complexe scolaire simulé en base de test.");
            await db.SaveChangesAsync();
        }

        var created = await admin.Post("/api/etablissement/promotions", new
        {
            code = "PR01", name = "Première primaire", sectionId = (Guid?)null,
            level = 1, capacity = (int?)null, isActive = true,
            nextPromotionCode = (string?)null, monthlyFee = 0m,
            oneTimeFees = Array.Empty<object>(), schoolCycleId = primaryId,
        }).Ok();
        Assert.Equal(primaryId, created.GetProperty("schoolCycleId").GetGuid());
        Assert.Equal(0, created.GetProperty("monthlyFee").GetDecimal());

        using var checkScope = app.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Equal(primaryId, (await check.Promotions.SingleAsync(p => p.Code == "PR01")).SchoolCycleId);
        Assert.Empty(await check.FeeItems.Where(f => f.PromotionId ==
            check.Promotions.Single(p => p.Code == "PR01").Id).ToArrayAsync());
    }

    [Fact]
    public async Task Accounting_cannot_read_or_write_cycle_configurations()
    {
        var admin = await app.InstalledAdmin();
        await admin.Post("/api/utilisateurs", new
        {
            userName = "compta", displayName = "Comptabilite",
            role = "Comptabilite", password = "Provisoire1",
        }).Ok();
        var accounting = await app.Login("compta", "Provisoire1");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await accounting.GetAsync("/api/etablissement/cycles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await accounting.Put("/api/etablissement/cycles",
                new { cycles = Array.Empty<object>() })).StatusCode);
    }
}
