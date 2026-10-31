using System.Net;

namespace Scolia.Tests;

/// <summary>Une mensualité réglée ne doit jamais changer rétroactivement de mois ou de date.</summary>
public sealed class PaidCalendarSafetyTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Any_receipt_allocation_locks_the_year_schedule()
    {
        var admin = await app.InstalledAdmin();
        var year = Assert.Single((await admin.GetAsync("/api/etablissement/annees").Ok()).EnumerateArray())
            .GetProperty("id").GetString()!;
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true,
            monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new
            {
                lastName = "TEST", middleName = "DUPONT", firstName = "Alex",
                gender = "M", guardianPhone = "0812345678", guardianName = "Parent",
            },
            promotionId = promotion.GetProperty("id").GetString()!,
            confirmDuplicate = false,
        }).Ok();
        var enrollment = student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
        await admin.PayMonths(enrollment, 1);
        var attempt = await admin.Put($"/api/etablissement/annees/{year}/calendrier",
            new { startMonth = 10, endMonth = 4, lastOnTimeDay = 8 }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, attempt.Status);
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(10, account.GetProperty("lines").GetArrayLength());
        Assert.Equal(55000m, account.GetProperty("totalPaid").GetDecimal());
    }
}
