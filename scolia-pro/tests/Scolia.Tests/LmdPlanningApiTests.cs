using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Parcours LMD : structure hiérarchique, calendrier S1/S2 et UE/EC.</summary>
public sealed class LmdPlanningApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> InstallAsync(string profile = "SuperieurLmd")
    {
        var client = app.Client();
        await client.Post("/api/installation", new
        {
            institutionType = profile, governance = "Prive",
            schoolName = "Institut supérieur de test", shortName = "IST",
            currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Administration",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return client;
    }

    [Fact]
    public async Task Lmd_can_define_faculty_department_pathway_semesters_and_ue_ec()
    {
        var client = await InstallAsync();
        var faculty = await client.Post("/api/etablissement/lmd/structures", new
        {
            kind = "Faculty", parentId = (Guid?)null,
            code = "FSCI", name = "Faculté des sciences",
        }).Ok();
        var department = await client.Post("/api/etablissement/lmd/structures", new
        {
            kind = "Department", parentId = faculty.GetProperty("id").GetGuid(),
            code = "INFO", name = "Informatique",
        }).Ok();
        var pathway = await client.Post("/api/etablissement/lmd/structures", new
        {
            kind = "Filiere", parentId = department.GetProperty("id").GetGuid(),
            code = "MSI", name = "Management des systèmes d'information",
        }).Ok();

        var (badStatus, _) = await client.Post("/api/etablissement/lmd/structures", new
        {
            kind = "Filiere", parentId = faculty.GetProperty("id").GetGuid(),
            code = "INV", name = "Filière mal rattachée",
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, badStatus);

        var promotion = await client.Post("/api/etablissement/promotions", new
        {
            code = "LMD1", name = "Première Licence",
            sectionId = (Guid?)null, schoolCycleId = (Guid?)null,
            level = 1, capacity = (int?)null, isActive = true,
            nextPromotionCode = (string?)null,
            monthlyFee = 0m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetGuid();
        DateOnly firstStart, firstEnd, secondStart, secondEnd;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            firstStart = year.StartDate;
            firstEnd = firstStart.AddDays(80);
            secondStart = firstEnd.AddDays(1);
            secondEnd = year.EndDate;
        }
        var semesters = await client.Put("/api/etablissement/lmd/semestres", new
        {
            firstStart, firstEnd, secondStart, secondEnd,
        }).Ok();
        Assert.Equal(2, semesters.GetArrayLength());
        Assert.Equal("S1", semesters[0].GetProperty("code").GetString());
        Assert.Equal("S2", semesters[1].GetProperty("code").GetString());

        var courses = await client.Post($"/api/etablissement/lmd/promotions/{promotionId}/ues", new
        {
            academicUnitId = pathway.GetProperty("id").GetGuid(), semester = "S1",
            code = "ALGO", name = "Algorithmique", credits = 6m, hours = 48m,
            elementCode = "AL01", elementName = "Introduction aux algorithmes",
            elementHours = 24m,
        }).Ok();
        Assert.Equal(2, courses.GetArrayLength());
        var ueId = courses.EnumerateArray().Single(c =>
            c.GetProperty("kind").GetString() == "TeachingUnit").GetProperty("id").GetGuid();

        await client.Post($"/api/etablissement/lmd/promotions/{promotionId}/ecs", new
        {
            parentItemId = ueId, code = "AL02",
            name = "Travaux pratiques", hours = 24m,
        }).Ok();
        var program = await client.GetAsync(
            $"/api/etablissement/lmd/promotions/{promotionId}/programme").Ok();
        Assert.Equal(3, program.GetArrayLength());
        Assert.Single(program.EnumerateArray(), c =>
            c.GetProperty("kind").GetString() == "TeachingUnit");
        Assert.Equal(2, program.EnumerateArray().Count(c =>
            c.GetProperty("kind").GetString() == "TeachingElement"));

        using var verify = app.Services.CreateScope();
        var persisted = verify.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Equal(3, await persisted.CurriculumItems.CountAsync());
        Assert.Equal(3, await persisted.AcademicStructureUnits.CountAsync());
        Assert.Equal(2, await persisted.PedagogicalPeriods.CountAsync());
        Assert.Empty(await persisted.Grades.ToListAsync());
    }

    [Fact]
    public async Task Lmd_endpoints_reject_itm_without_modifying_legacy_schools()
    {
        var admin = await InstallAsync("ItmIm");
        var (status, _) = await admin.Post("/api/etablissement/lmd/structures", new
        {
            kind = "Faculty", parentId = (Guid?)null,
            code = "FSCI", name = "Sciences",
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Empty(await db.AcademicStructureUnits.ToListAsync());
    }
}
