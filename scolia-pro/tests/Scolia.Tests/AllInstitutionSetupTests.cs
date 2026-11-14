using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Domain.School;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Activation métier : public/privé, LMD, interdiction des frais publics non justifiés.</summary>
public sealed class AllInstitutionSetupTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static object Configuration(string type, string governance) => new
    {
        institutionType = type, governance,
        schoolName = "Établissement de test",
        shortName = "TEST", phone = "0810000000",
        city = "Lubumbashi", currencyCode = "CDF", currencySymbol = "FC",
        yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
        startMonth = 9, monthCount = 10,
        adminUserName = "admin", adminDisplayName = "Secrétariat",
        adminPassword = "Scolia2026!", loadDemoData = false,
    };

    [Theory]
    [InlineData("Primaire")]
    [InlineData("Secondaire")]
    [InlineData("ComplexeScolaire")]
    [InlineData("SuperieurLmd")]
    public async Task Public_institution_can_register_classes_without_mandatory_charges(string type)
    {
        var admin = app.Client();
        await admin.Post("/api/installation", Configuration(type, "Public")).Ok();
        var school = await admin.GetAsync("/api/etablissement").Ok();
        Assert.Equal("Public", school.GetProperty("governance").GetString());
        Assert.Equal(type, school.GetProperty("institutionType").GetString());

        Guid? cycleId = null;
        if (type != "SuperieurLmd")
        {
            var plan = await admin.GetAsync("/api/etablissement/cycles").Ok();
            if (type == "ComplexeScolaire")
            {
                var result = await admin.Put("/api/etablissement/cycles", new
                {
                    cycles = new[]
                    {
                        new { kind = "Maternelle", name = "Maternelle",
                            authorizationNumber = (string?)null, chargeMonthlyTuition = false, monthlyAmount = 0m },
                        new { kind = "Primaire", name = "Primaire",
                            authorizationNumber = (string?)null, chargeMonthlyTuition = false, monthlyAmount = 0m },
                    },
                }).Ok();
                cycleId = result.GetProperty("cycles")[0].GetProperty("id").GetGuid();
            }
            else
            {
                cycleId = Assert.Single(plan.GetProperty("cycles").EnumerateArray())
                    .GetProperty("id").GetGuid();
            }
        }
        await admin.Post("/api/etablissement/promotions", new
        {
            code = "CLS1", name = "Classe", sectionId = (Guid?)null,
            schoolCycleId = cycleId, level = 1, capacity = (int?)null,
            isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(),
        }).Ok();

        var (monthlyStatus, _) = await admin.Post("/api/etablissement/promotions", new
        {
            code = "CLS2", name = "Classe facturée", sectionId = (Guid?)null,
            schoolCycleId = cycleId, level = 2, capacity = (int?)null,
            isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 30000m, oneTimeFees = Array.Empty<object>(),
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, monthlyStatus);

        var (categoryStatus, _) = await admin.Post("/api/etablissement/categories-frais", new
        {
            name = "Frais d'inscription", amount = 5000m, dueDate = (DateOnly?)null,
            isActive = true, applyToPromotions = true,
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, categoryStatus);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Equal(SchoolGovernanceKind.Public, (await db.Schools.SingleAsync()).Governance);
        Assert.Single(await db.Promotions.ToListAsync());
        Assert.Empty(await db.FeeItems.ToListAsync());
    }

    [Fact]
    public async Task Lmd_private_can_create_semester_course_but_not_annual_itm_course()
    {
        var admin = app.Client();
        await admin.Post("/api/installation", Configuration("SuperieurLmd", "Prive")).Ok();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "LMD1", name = "Licence 1", sectionId = (Guid?)null,
            schoolCycleId = (Guid?)null, level = 1, capacity = (int?)null,
            isActive = true, nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetGuid();
        await admin.Post("/api/scolarite/cours", new
        {
            promotionId, number = 1, name = "Algèbre",
            coefficient = 2m, hours = 30m, maxScore = 20m,
            semester = "S1",
        }).Ok();
        var (status, _) = await admin.Post("/api/scolarite/cours", new
        {
            promotionId, number = 2, name = "Anatomie",
            coefficient = 2m, hours = 30m, maxScore = 20m,
            semester = "Annuel",
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Single(await db.Courses.ToListAsync());
        Assert.Empty(await db.SchoolCycles.ToListAsync());
    }

    [Theory]
    [InlineData("garanti")]
    [InlineData("privééé")]
    public async Task Unknown_governance_cannot_create_an_institution(string value)
    {
        var client = app.Client();
        var (status, _) = await client.Post("/api/installation",
            Configuration("Primaire", value)).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Empty(await db.Schools.ToListAsync());
    }
}
