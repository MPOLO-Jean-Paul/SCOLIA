using System.Net;

namespace Scolia.Tests;

/// <summary>Réinscription sans préparation manuelle des promotions de la nouvelle année.</summary>
public sealed class ReEnrollmentAutoPromotionTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static async Task<(string TargetYearId, string StudentId)> PrepareAsync(
        HttpClient admin, string sourceCode = "INF1", string? nextCode = "INF2")
    {
        var source = await admin.Post("/api/etablissement/promotions", new
        {
            code = sourceCode,
            name = "Première année",
            level = 1,
            isActive = true,
            nextPromotionCode = nextCode,
            monthlyFee = 55000m,
            oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new
            {
                lastName = "MUKENDI", middleName = "KABEYA", firstName = "Grace",
                birthDate = new DateOnly(2007, 3, 14), birthPlace = "Lubumbashi",
                gender = "F", guardianPhone = "0812345678",
            },
            promotionId = source.GetProperty("id").GetString(),
            confirmDuplicate = false,
        }).Ok();
        var first = int.Parse(Http.Year(DateOnly.FromDateTime(DateTime.Today))[..4],
            System.Globalization.CultureInfo.InvariantCulture);
        var future = await admin.Post("/api/etablissement/annees", new
        {
            label = $"{first + 1}-{first + 2}",
            startMonth = 9, monthCount = 10, dueDay = 6,
            copyPromotionsFromCurrent = false, makeCurrent = false,
        }).Ok();
        return (future.GetProperty("id").GetString()!, student.GetProperty("id").GetString()!);
    }

    private static Task<System.Text.Json.JsonElement> ReEnroll(HttpClient admin, string year, string student, string code) =>
        admin.Post("/api/reinscriptions", new
        {
            targetYearId = year,
            choices = new[] { new { studentId = student, promotionId = (Guid?)null, promotionCode = code } },
        }).Ok();

    [Fact]
    public async Task Missing_next_promotion_is_created_and_student_is_enrolled_without_duplicate()
    {
        var admin = await app.InstalledAdmin();
        var (year, student) = await PrepareAsync(admin);
        var plan = (await admin.GetAsync($"/api/reinscriptions/plan?anneeId={year}").Ok()).EnumerateArray().ToList();
        var entry = Assert.Single(plan);
        Assert.Equal("INF2", entry.GetProperty("toPromotion").GetString());
        Assert.True(entry.GetProperty("selected").GetBoolean());

        var first = await ReEnroll(admin, year, student, "INF2");
        Assert.Equal(1, first.GetInt32());
        var repeated = await ReEnroll(admin, year, student, "INF2");
        Assert.Equal(0, repeated.GetInt32());

        var promotions = (await admin.GetAsync($"/api/etablissement/promotions?anneeId={year}").Ok())
            .EnumerateArray().ToList();
        var created = Assert.Single(promotions);
        Assert.Equal("INF2", created.GetProperty("code").GetString());
        Assert.Equal(10, created.GetProperty("fees").GetArrayLength());
        Assert.Equal(55000m, created.GetProperty("monthlyFee").GetDecimal());

        var profile = await admin.GetAsync($"/api/apprenants/{student}").Ok();
        Assert.Single(profile.GetProperty("enrollments").EnumerateArray(),
            e => e.GetProperty("academicYearId").GetString() == year);
    }

    [Fact]
    public async Task Without_configured_next_level_the_same_promotion_is_recreated_for_new_year()
    {
        var admin = await app.InstalledAdmin();
        var (year, student) = await PrepareAsync(admin, "ACC1", null);
        var plan = (await admin.GetAsync($"/api/reinscriptions/plan?anneeId={year}").Ok())
            .EnumerateArray().ToList();
        Assert.Equal("ACC1", Assert.Single(plan).GetProperty("toPromotion").GetString());

        Assert.Equal(1, (await ReEnroll(admin, year, student, "ACC1")).GetInt32());
        var promotions = (await admin.GetAsync($"/api/etablissement/promotions?anneeId={year}").Ok())
            .EnumerateArray().ToList();
        Assert.Equal("ACC1", Assert.Single(promotions).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unconfigured_promotion_code_is_rejected_without_creating_a_class()
    {
        var admin = await app.InstalledAdmin();
        var (year, student) = await PrepareAsync(admin);
        var result = await admin.Post("/api/reinscriptions", new
        {
            targetYearId = year,
            choices = new[] { new { studentId = student, promotionId = (Guid?)null, promotionCode = "ACC9" } },
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        var promotions = (await admin.GetAsync($"/api/etablissement/promotions?anneeId={year}").Ok())
            .EnumerateArray().ToList();
        Assert.Empty(promotions);
    }
}
