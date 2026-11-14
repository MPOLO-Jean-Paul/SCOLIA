using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Validation des nouveaux calendriers et matières annuelles par cycle.</summary>
public sealed class SchoolPlanningApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> InstallAsync(string type)
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            institutionType = type, governance = "Prive",
            schoolName = "École de test", shortName = "ECO",
            currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat",
            adminPassword = "Scolia2026!", loadDemoData = false,
        }).Ok();
        return admin;
    }

    [Theory]
    [InlineData("Maternelle", "Observation")]
    [InlineData("Primaire", "Evaluation")]
    [InlineData("Secondaire", "Evaluation")]
    public async Task School_period_and_annual_subject_are_isolated_and_not_billed(
        string profile, string mode)
    {
        var admin = await InstallAsync(profile);
        var configuration = await admin.GetAsync("/api/etablissement/cycles").Ok();
        var cycleId = Assert.Single(configuration.GetProperty("cycles").EnumerateArray())
            .GetProperty("id").GetGuid();
        DateOnly start, end;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
            var year = await db.AcademicYears.SingleAsync(y => y.IsCurrent);
            start = year.StartDate;
            end = start.AddDays(20);
        }

        var period = await admin.Post($"/api/etablissement/cycles/{cycleId}/periodes", new
        {
            code = "P1", name = "Période 1", termCode = "T1",
            startDate = start, endDate = end,
        }).Ok();
        Assert.Equal(mode, period.GetProperty("mode").GetString());

        var (duplicate, _) = await admin.Post($"/api/etablissement/cycles/{cycleId}/periodes", new
        {
            code = "P1", name = "Période 1 bis", termCode = "T1",
            startDate = start, endDate = end,
        }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, duplicate);

        var classroom = await admin.Post("/api/etablissement/promotions", new
        {
            code = "CLS1", name = "Classe première",
            schoolCycleId = cycleId, sectionId = (Guid?)null,
            level = 1, capacity = (int?)null, isActive = true,
            nextPromotionCode = (string?)null, monthlyFee = 0m,
            oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = classroom.GetProperty("id").GetGuid();
        var subject = await admin.Post(
            $"/api/etablissement/cycles/promotions/{promotionId}/matieres", new
            {
                code = "COMM", name = "Communication",
                coefficient = 1m, hours = 30m,
            }).Ok();
        Assert.Equal("COMM", subject.GetProperty("code").GetString());

        var (sameSubject, _) = await admin.Post(
            $"/api/etablissement/cycles/promotions/{promotionId}/matieres", new
            {
                code = "COMM", name = "Autre matière",
                coefficient = 1m, hours = 30m,
            }).Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, sameSubject);

        var listed = await admin.GetAsync(
            $"/api/etablissement/cycles/promotions/{promotionId}/matieres").Ok();
        Assert.Single(listed.EnumerateArray());
        using var verify = app.Services.CreateScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        Assert.Single(await dbVerify.CurriculumItems.ToListAsync());
        Assert.Single(await dbVerify.SchoolCyclePeriods.ToListAsync());
        Assert.Empty(await dbVerify.Grades.ToListAsync());
        Assert.Empty(await dbVerify.Receipts.ToListAsync());
    }

    [Fact]
    public async Task Legacy_itm_does_not_accept_school_cycle_periods()
    {
        var admin = await InstallAsync("ItmIm");
        var (status, _) = await admin.GetAsync(
            $"/api/etablissement/cycles/{Guid.NewGuid()}/periodes").Fail();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, status);
    }
}
