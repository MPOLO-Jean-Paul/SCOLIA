using System.Net;

namespace Scolia.Tests;

/// <summary>Vérifie une modification réelle du calendrier dans la base scolaire.</summary>
public sealed class SchoolCalendarApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static async Task<string> YearId(HttpClient admin) =>
        Assert.Single((await admin.GetAsync("/api/etablissement/annees").Ok()).EnumerateArray())
            .GetProperty("id").GetString()!;

    private static async Task<string> PromotionId(HttpClient admin) =>
        (await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Promotion de test", level = 1, isActive = true,
            monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok()).GetProperty("id").GetString()!;

    [Fact]
    public async Task Updating_existing_year_reconfigures_all_promotion_months()
    {
        var admin = await app.InstalledAdmin();
        var id = await YearId(admin);
        var promotion = await PromotionId(admin);
        var before = (await admin.GetAsync("/api/etablissement/promotions").Ok())
            .EnumerateArray().Single(p => p.GetProperty("id").GetString() == promotion);
        var originalFees = before.GetProperty("fees").EnumerateArray().ToArray();
        Assert.Equal(10, originalFees.Length);

        var year = await admin.Put($"/api/etablissement/annees/{id}/calendrier",
            new { startMonth = 10, endMonth = 4, lastOnTimeDay = 5 }).Ok();
        var periods = year.GetProperty("periods").EnumerateArray().ToArray();
        Assert.Equal(7, periods.Length);
        Assert.Equal("Octobre", periods[0].GetProperty("name").GetString());
        Assert.Equal("Avril", periods[^1].GetProperty("name").GetString());
        Assert.Equal(6, DateOnly.Parse(periods[0].GetProperty("dueDate").GetString()!).Day);

        var after = (await admin.GetAsync("/api/etablissement/promotions").Ok())
            .EnumerateArray().Single(p => p.GetProperty("id").GetString() == promotion);
        var changed = after.GetProperty("fees").EnumerateArray().ToArray();
        Assert.Equal(7, changed.Length);
        Assert.All(changed, fee => Assert.Equal(55000m, fee.GetProperty("amount").GetDecimal()));
        Assert.Equal(originalFees[0].GetProperty("id").GetString(),
            changed[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Non_secretariat_cannot_change_the_school_calendar()
    {
        var admin = await app.InstalledAdmin();
        var id = await YearId(admin);
        await admin.Post("/api/utilisateurs", new
        {
            userName = "compta", displayName = "Comptabilité",
            role = "Comptabilite", password = "Provisoire1",
        }).Ok();
        var accounting = await app.Login("compta", "Provisoire1");
        var response = await accounting.Put($"/api/etablissement/annees/{id}/calendrier",
            new { startMonth = 10, endMonth = 4, lastOnTimeDay = 5 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
