using System.Net;

namespace Scolia.Tests;

/// <summary>Validation métier des inscriptions et changements d'identité.</summary>
public sealed class StudentBusinessRulesTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static object Identity(string lastName) => new
    {
        lastName, middleName = "KABUYA", firstName = "Grace",
        gender = "F", birthDate = new DateOnly(2007, 3, 14),
        birthPlace = "Lubumbashi", guardianPhone = "0812345678"
    };

    [Fact]
    public async Task Future_date_or_disabled_promotion_cannot_create_an_enrollment()
    {
        var admin = await app.InstalledAdmin();
        var active = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première infirmière", level = 1, isActive = true,
            monthlyFee = 55000m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        var disabled = await admin.Post("/api/etablissement/promotions", new
        {
            code = "ACC1", name = "Première accoucheuse", level = 1, isActive = false,
            monthlyFee = 55000m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        var activeId = active.GetProperty("id").GetString();
        var disabledId = disabled.GetProperty("id").GetString();

        var (futureStatus, _) = await admin.Post("/api/apprenants", new
        {
            identity = Identity("MUKENDI"), promotionId = activeId,
            enrolledOn = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, futureStatus);

        var (disabledStatus, _) = await admin.Post("/api/apprenants", new
        {
            identity = Identity("MUKENDI"), promotionId = disabledId
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, disabledStatus);

        var student = await admin.Post("/api/apprenants", new
        {
            identity = Identity("MUKENDI"), promotionId = activeId
        }).Ok();
        // Sans attribution officielle du code anonyme, aucun matricule factice n'est affiché.
        Assert.Equal(string.Empty, student.GetProperty("matricule").GetString());
        var lastPage = await admin.GetAsync("/api/apprenants?taille=1&page=999").Ok();
        Assert.Equal(1, lastPage.GetProperty("page").GetInt32());
        Assert.Single(lastPage.GetProperty("items").EnumerateArray());
        Assert.Equal(string.Empty, lastPage.GetProperty("items")[0].GetProperty("matricule").GetString());

        var enrollmentId = student.GetProperty("enrollments")[0].GetProperty("id").GetString();
        var (futureStatusDate, _) = await admin.Put($"/api/inscriptions/{enrollmentId}/statut", new
        {
            status = "Sorti", reason = "Transfert",
            date = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, futureStatusDate);
        var (changeStatus, _) = await admin.Put($"/api/inscriptions/{enrollmentId}/promotion",
            new { promotionId = disabledId }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, changeStatus);
    }

    [Fact]
    public async Task Renaming_a_student_cannot_silently_duplicate_an_existing_identity()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première infirmière", level = 1, isActive = true,
            monthlyFee = 55000m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString();
        await admin.Post("/api/apprenants", new
        {
            identity = Identity("MUKENDI"), promotionId
        }).Ok();
        var second = await admin.Post("/api/apprenants", new
        {
            identity = Identity("NGOY"), promotionId
        }).Ok();

        var secondId = second.GetProperty("id").GetString();
        var (status, _) = await admin.Put($"/api/apprenants/{secondId}", Identity("MUKENDI")).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);

        var after = await admin.GetAsync($"/api/apprenants/{secondId}").Ok();
        Assert.Equal("NGOY", after.GetProperty("lastName").GetString());
    }
}
