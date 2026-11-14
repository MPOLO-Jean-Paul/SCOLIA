using System.Net;

namespace Scolia.Tests;

/// <summary>Sections (filières) : effectifs, suppression protégée, section désactivée, filtre des apprenants.</summary>
public sealed class SectionTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Sections_count_promotions_filter_students_and_protect_their_history()
    {
        var admin = await app.InstalledAdmin();
        var nursing = await admin.Post("/api/etablissement/sections", new { code = "INF", name = "Sciences infirmières", isActive = true }).Ok();
        var lab = await admin.Post("/api/etablissement/sections", new { code = "TLM", name = "Laboratoire", isActive = true }).Ok();
        var nursingId = nursing.GetProperty("id").GetString();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", sectionId = nursingId, level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        await admin.Post("/api/etablissement/promotions", new
        {
            code = "TLM1", name = "Laboratoire 1", sectionId = lab.GetProperty("id").GetString(), level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "Mukendi", middleName = "Kabeya", firstName = "Grâce", birthPlace = "Kinshasa", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId = promotion.GetProperty("id").GetString(), confirmDuplicate = false,
        }).Ok();

        var sections = await admin.GetAsync("/api/etablissement/sections").Ok();
        var row = sections.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "INF");
        Assert.Equal(1, row.GetProperty("promotionCount").GetInt32());
        Assert.Equal(1, row.GetProperty("studentCount").GetInt32());

        // Filtre de la liste des apprenants par section.
        var inNursing = await admin.GetAsync($"/api/apprenants?sectionId={nursingId}").Ok();
        Assert.Equal(1, inNursing.GetProperty("total").GetInt32());
        var inLab = await admin.GetAsync($"/api/apprenants?sectionId={lab.GetProperty("id").GetString()}").Ok();
        Assert.Equal(0, inLab.GetProperty("total").GetInt32());

        // Une section utilisée ne se supprime pas ; une section vide, si.
        var (status, _) = await admin.DeleteAsync($"/api/etablissement/sections/{nursingId}").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var empty = await admin.Post("/api/etablissement/sections", new { code = "ACC", name = "Accoucheuse", isActive = true }).Ok();
        await admin.DeleteAsync($"/api/etablissement/sections/{empty.GetProperty("id").GetString()}").Ok();

        // Une section désactivée ne reçoit plus de nouvelle promotion.
        await admin.Put($"/api/etablissement/sections/{lab.GetProperty("id").GetString()}", new { code = "TLM", name = "Laboratoire", isActive = false }).Ok();
        var (_, refused) = await admin.Post("/api/etablissement/promotions", new
        {
            code = "TLM2", name = "Laboratoire 2", sectionId = lab.GetProperty("id").GetString(), level = 2, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
        }).Fail();
        Assert.Contains("désactivée", refused.GetRawText(), StringComparison.Ordinal);
    }
}
