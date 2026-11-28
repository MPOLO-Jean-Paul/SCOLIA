using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

public sealed class DashboardSelectionTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private sealed record Sample(HttpClient Admin, string Section, string Promotion, string OtherPromotion, string Payer, string Boy);
    private async Task<Sample> Seed()
    {
        var admin = await app.InstalledAdmin();
        var section = (await admin.Post("/api/etablissement/sections", new { code = "INF", name = "Informatique", isActive = true }).Ok()).GetProperty("id").GetString()!;
        async Task<string> Promotion(string code, string name, decimal fee, string? sectionId) =>
            (await admin.Post("/api/etablissement/promotions", new { code, name, sectionId, level = 1, isActive = true, monthlyFee = fee, oneTimeFees = Array.Empty<object>() }).Ok()).GetProperty("id").GetString()!;
        var first = await Promotion("A001", "Première année", 100m, section);
        var other = await Promotion("B002", "Commerce", 200m, null);
        async Task<string> Enroll(string name, string promotionId, string gender)
        {
            await admin.Post("/api/apprenants", new
            {
                identity = new { lastName = name, middleName = "Confidentiel", firstName = "Test", birthPlace = "Lubumbashi", birthDate = new DateOnly(2008, 3, 14), gender, guardianPhone = "0812345678", guardianName = "Parent privé" },
                promotionId, confirmDuplicate = false
            }).Ok();
            return (await admin.GetAsync("/api/apprenants?q=" + name).Ok()).GetProperty("items")[0].GetProperty("enrollmentId").GetString()!;
        }
        var payer = await Enroll("AliceNominative", first, "F");
        var boy = await Enroll("BobNominatif", first, "M");
        var second = await Enroll("ChloeNominative", other, "F");
        var sponsored = await Enroll("DinaNominative", first, "F");
        var account = await admin.GetAsync($"/api/inscriptions/{sponsored}/compte").Ok();
        await admin.Post($"/api/inscriptions/{sponsored}/prises-en-charge", new
        {
            sponsorName = "Bourse", reason = "Excellence", effectiveFrom = DateOnly.FromDateTime(DateTime.Today),
            lines = account.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("kind").GetString() == "Mensualite")
                .Select(l => new { feeItemId = l.GetProperty("feeItemId").GetString(), percent = 100m }).ToArray()
        }).Ok();
        await admin.PayMonths(payer);
        await admin.PayMonths(second);
        return new Sample(admin, section, first, other, payer, boy);
    }

    private static void TotalsMatch(JsonElement data)
    {
        var rows = data.GetProperty("promotions").EnumerateArray().ToArray();
        Assert.Equal(data.GetProperty("activeStudents").GetInt32(), rows.Sum(r => r.GetProperty("students").GetInt32()));
        foreach (var pair in new[] { ("collectedTotal", "collected"), ("outstanding", "outstanding"), ("overdue", "overdue"), ("expectedToDate", "expected") })
            Assert.Equal(data.GetProperty(pair.Item1).GetDecimal(), rows.Sum(r => r.GetProperty(pair.Item2).GetDecimal()));
    }

    [Fact]
    public async Task Tous_les_filtres_et_les_tris_gardent_des_totaux_et_des_recus_coherents()
    {
        var s = await Seed();
        var options = await s.Admin.GetAsync("/api/rapports/tableau-de-bord/filtres").Ok();
        Assert.Equal(2, options.GetProperty("promotions").GetArrayLength());
        var scope = $"sectionId={s.Section}&promotionId={s.Promotion}&gender=F";
        foreach (var sort in new[] { "code", "code-desc", "students", "collected", "outstanding", "overdue" })
        {
            var filtered = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?" + scope + "&sort=" + sort).Ok();
            TotalsMatch(filtered);
            Assert.Equal(2, filtered.GetProperty("activeStudents").GetInt32());
            Assert.Equal(1, filtered.GetProperty("payingStudents").GetInt32());
            Assert.Equal(1, filtered.GetProperty("sponsoredStudents").GetInt32());
            Assert.Equal(100m, filtered.GetProperty("collectedTotal").GetDecimal());
            var receipt = Assert.Single(filtered.GetProperty("latestReceipts").EnumerateArray());
            Assert.Equal(100m, receipt.GetProperty("amount").GetDecimal());
            var all = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?sort=" + sort).Ok();
            TotalsMatch(all);
            var rows = all.GetProperty("promotions").EnumerateArray().ToArray();
            if (sort == "code-desc") Assert.Equal("B002", rows[0].GetProperty("code").GetString());
            if (sort == "code") Assert.Equal("A001", rows[0].GetProperty("code").GetString());
            var metric = sort switch { "students" => "students", "collected" => "collected", "outstanding" => "outstanding", "overdue" => "overdue", _ => null };
            if (metric is not null) Assert.True(rows[0].GetProperty(metric).GetDecimal() >= rows[1].GetProperty(metric).GetDecimal());
        }
        var girls = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?gender=F&search=premiere").Ok();
        Assert.Equal(2, girls.GetProperty("activeStudents").GetInt32());
        var boys = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?gender=M").Ok();
        Assert.Equal(1, boys.GetProperty("activeStudents").GetInt32());
        Assert.Empty(boys.GetProperty("latestReceipts").EnumerateArray());
        foreach (var state in new[] { "paying", "sponsored", "partial", "uptodate", "late", "balance" })
        {
            var filtered = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?state=" + state).Ok();
            TotalsMatch(filtered);
            if (state == "sponsored") Assert.Equal(1, filtered.GetProperty("activeStudents").GetInt32());
            if (state == "paying") Assert.Equal(3, filtered.GetProperty("activeStudents").GetInt32());
            if (state == "partial") Assert.Equal(0, filtered.GetProperty("activeStudents").GetInt32());
        }
    }

    [Fact]
    public async Task Les_exports_suivent_la_selection_et_refusent_les_chiffres_devenus_obsoletes()
    {
        var s = await Seed();
        var query = $"promotionId={s.Promotion}&gender=F&sort=collected";
        var data = await s.Admin.GetAsync("/api/rapports/tableau-de-bord?" + query).Ok();
        var key = data.GetProperty("selectionKey").GetString();
        var url = "/api/rapports/tableau-de-bord/export?" + query + "&expectedSelection=" + key;
        var excel = await s.Admin.GetAsync(url + "&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await excel.Content.ReadAsByteArrayAsync()));
        Assert.Equal(4, zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
        var texts = string.Join("\n", zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)).Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
        Assert.Contains("A001", texts);
        Assert.DoesNotContain("B002", texts);
        Assert.Contains("Total sélection", texts);
        var pdf = await s.Admin.GetAsync(url + "&format=pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.GetAsync(url.Replace("gender=F", "gender=M", StringComparison.Ordinal) + "&format=xlsx")).StatusCode);
        await s.Admin.PayMonths(s.Payer);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.GetAsync(url + "&format=xlsx")).StatusCode);
    }

    [Fact]
    public async Task Le_promoteur_filtre_et_exporte_uniquement_des_agregats()
    {
        var s = await Seed();
        await s.Admin.Post("/api/utilisateurs", new { userName = "pilotage-tri", displayName = "Direction", role = "Promoteur", password = "Provisoire1" }).Ok();
        var promoter = await app.FirstLogin("pilotage-tri", "Provisoire1", "Promoteur2026!");
        Assert.Equal(HttpStatusCode.OK, (await promoter.GetAsync("/api/pilotage/filtres")).StatusCode);
        var query = $"sectionId={s.Section}&gender=F&sort=outstanding";
        var filtered = await promoter.GetAsync("/api/pilotage/synthese?" + query).Ok();
        Assert.Equal(2, filtered.GetProperty("activeStudents").GetInt32());
        Assert.Equal(100m, filtered.GetProperty("collectedTotal").GetDecimal());
        var rows = filtered.GetProperty("promotions").EnumerateArray().ToArray();
        Assert.Equal(filtered.GetProperty("outstanding").GetDecimal(), rows.Sum(r => r.GetProperty("outstanding").GetDecimal()));
        var excel = await promoter.GetAsync("/api/pilotage/export?" + query + "&format=xlsx&expectedSelection=" + filtered.GetProperty("selectionKey").GetString());
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await excel.Content.ReadAsByteArrayAsync()));
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)));
        var xml = string.Join("\n", zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)).Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
        foreach (var secret in new[] { "AliceNominative", "Confidentiel", "Parent privé", "0812345678", "Derniers reçus" }) Assert.DoesNotContain(secret, xml, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await promoter.GetAsync("/api/rapports/tableau-de-bord/filtres")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await promoter.GetAsync("/api/rapports/tableau-de-bord/export?format=xlsx&expectedSelection=x")).StatusCode);
    }

    [Theory]
    [InlineData("gender=X")]
    [InlineData("state=inconnue")]
    [InlineData("sort=inconnu")]
    public async Task Une_selection_invalide_est_expliquee(string query)
    {
        var admin = await app.InstalledAdmin();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/rapports/tableau-de-bord?" + query)).StatusCode);
    }
}
