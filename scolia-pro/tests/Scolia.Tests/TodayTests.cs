using System.Text.Json;
namespace Scolia.Tests;

/// <summary>Panneau « Aujourd'hui » de l'accueil : caisse du jour, convocations et anniversaires.</summary>
public sealed class TodayTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Today_lists_receipts_convocations_and_birthdays_of_the_day()
    {
        var admin = await app.InstalledAdmin();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var empty = await admin.GetAsync("/api/rapports/aujourdhui").Ok();
        Assert.Equal(0, empty.GetProperty("receiptsToday").GetInt32());
        Assert.False(empty.GetProperty("cashClosed").GetBoolean());
        Assert.Equal(0, empty.GetProperty("birthdays").GetArrayLength());

        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var birthday = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "Mukendi", middleName = "Kabeya", firstName = "Grâce", birthPlace = "Lubumbashi", gender = "F", birthDate = today.AddYears(-19), guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "Ilunga", middleName = "Kasongo", firstName = "Paul", birthPlace = "Lubumbashi", gender = "M", birthDate = today.AddDays(-1).AddYears(-18), guardianPhone = "0812345679", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        var enrollmentId = birthday.GetProperty("enrollments")[0].GetProperty("id").GetString()!;

        await admin.PayMonths(enrollmentId, 2);
        await admin.Post("/api/scolarite/discipline/registre", new
        {
            enrollmentId, measure = "Convocation", reason = "Comportement en classe", decisionDate = today, appointmentAt = DateTime.Today.AddHours(15),
        }).Ok();

        var day = await admin.GetAsync("/api/rapports/aujourdhui").Ok();
        Assert.Equal(1, day.GetProperty("receiptsToday").GetInt32());
        Assert.Equal(100000m, day.GetProperty("collectedToday").GetDecimal());
        var birthdays = day.GetProperty("birthdays");
        Assert.Equal(1, birthdays.GetArrayLength());
        Assert.Equal("MUKENDI KABEYA Grâce", birthdays[0].GetProperty("name").GetString());
        Assert.Equal("19 ans", birthdays[0].GetProperty("detail").GetString());
        var appointments = day.GetProperty("appointments");
        Assert.Equal(1, appointments.GetArrayLength());
        Assert.Equal("15:00 · en attente", appointments[0].GetProperty("detail").GetString());
        Assert.Equal("INF1", appointments[0].GetProperty("promotion").GetString());
    }
}

/// <summary>Rapports du groupe Données : chaque export sort en PDF et en Excel, totaux compris.</summary>
public sealed class DataReportsTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    [Theory]
    [InlineData("/api/rapports/promotions/export", "Situation financière par promotion")]
    [InlineData("/api/rapports/apprenants/export", "Liste générale des apprenants")]
    [InlineData("/api/depenses/export", "Registre des dépenses")]
    public async Task Data_reports_export_to_pdf_and_excel(string url, string title)
    {
        var admin = await app.InstalledAdmin(demo: true);

        var pdf = await admin.GetAsync(url);
        Assert.True(pdf.IsSuccessStatusCode, await pdf.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.True((await pdf.Content.ReadAsByteArrayAsync()).Length > 1000);

        var xlsx = await admin.GetAsync(url + "?format=xlsx");
        Assert.True(xlsx.IsSuccessStatusCode);
        using var book = new ClosedXML.Excel.XLWorkbook(await xlsx.Content.ReadAsStreamAsync());
        var sheet = book.Worksheet(1);
        Assert.Contains(sheet.CellsUsed(), c => c.GetString() == title);
        Assert.True(sheet.LastRowUsed()!.RowNumber() > 6);
    }
}

/// <summary>Suivi des paiements par mois : qui a payé le mois choisi, par promotion ou pour tout l'établissement.</summary>
public sealed class MonthlyFollowUpTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    private static async Task<string> Promotion(HttpClient admin, string code) =>
        (await admin.Post("/api/etablissement/promotions", new
        {
            code, name = $"Promotion {code}", level = 1, isActive = true, monthlyFee = 40000m, oneTimeFees = Array.Empty<object>(),
        }).Ok()).GetProperty("id").GetString()!;

    private static async Task<string> Student(HttpClient admin, string promotionId, string last, string phone) =>
        (await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = last, middleName = "Kabeya", firstName = "Grâce", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = phone, guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok()).GetProperty("enrollments")[0].GetProperty("id").GetString()!;

    [Fact]
    public async Task Monthly_follow_up_sorts_students_by_month_promotion_and_state()
    {
        var admin = await app.InstalledAdmin();
        var first = await Promotion(admin, "INF1");
        var second = await Promotion(admin, "INF2");
        var payer = await Student(admin, first, "Mukendi", "0811111111");
        await Student(admin, first, "Ilunga", "0822222222");
        await Student(admin, second, "Kasongo", "0833333333");
        var receipt = await admin.PayMonths(payer, 1);

        // Le premier mois de l'année : celui que le premier versement a réglé.
        var overview = await admin.GetAsync("/api/rapports/suivi-mensuel").Ok();
        var september = overview.GetProperty("periods")[0];
        var periodId = september.GetProperty("id").GetString()!;

        var all = await admin.GetAsync($"/api/rapports/suivi-mensuel?periodeId={periodId}").Ok();
        Assert.Equal(september.GetProperty("name").GetString(), all.GetProperty("period").GetString());
        Assert.Equal(3, all.GetProperty("rows").GetArrayLength());
        Assert.Equal(1, all.GetProperty("paidCount").GetInt32());
        Assert.Equal(2, all.GetProperty("unpaidCount").GetInt32());
        Assert.Equal(40000m, all.GetProperty("collected").GetDecimal());
        Assert.Equal(80000m, all.GetProperty("remaining").GetDecimal());

        var paid = await admin.GetAsync($"/api/rapports/suivi-mensuel?periodeId={periodId}&etat=paye").Ok();
        var row = Assert.Single(paid.GetProperty("rows").EnumerateArray());
        Assert.Equal("MUKENDI KABEYA Grâce", row.GetProperty("fullName").GetString());
        Assert.Equal("Payé", row.GetProperty("status").GetString());
        Assert.Equal(receipt.GetProperty("number").GetString(), row.GetProperty("receiptNumber").GetString());

        var unpaid = await admin.GetAsync($"/api/rapports/suivi-mensuel?periodeId={periodId}&etat=nonpaye").Ok();
        Assert.Equal(2, unpaid.GetProperty("rows").GetArrayLength());
        Assert.All(unpaid.GetProperty("rows").EnumerateArray(), r =>
        {
            Assert.NotEqual("Payé", r.GetProperty("status").GetString());
            Assert.StartsWith("https://wa.me/243", r.GetProperty("whatsAppLink").GetString(), StringComparison.Ordinal);
        });
        Assert.Equal(JsonValueKind.Null, row.GetProperty("whatsAppLink").ValueKind);

        // Par promotion : les totaux suivent la promotion choisie.
        var byPromotion = await admin.GetAsync($"/api/rapports/suivi-mensuel?periodeId={periodId}&promotionId={second}").Ok();
        var only = Assert.Single(byPromotion.GetProperty("rows").EnumerateArray());
        Assert.Equal("INF2", only.GetProperty("promotionCode").GetString());
        Assert.Equal(0, byPromotion.GetProperty("paidCount").GetInt32());

        // Le mois suivant n'est réglé par personne.
        var october = overview.GetProperty("periods")[1].GetProperty("id").GetString()!;
        var next = await admin.GetAsync($"/api/rapports/suivi-mensuel?periodeId={october}").Ok();
        Assert.Equal(0, next.GetProperty("paidCount").GetInt32());

        var xlsx = await admin.GetAsync($"/api/rapports/suivi-mensuel/export?periodeId={periodId}&etat=nonpaye&format=xlsx");
        Assert.True(xlsx.IsSuccessStatusCode);
        using var book = new ClosedXML.Excel.XLWorkbook(await xlsx.Content.ReadAsStreamAsync());
        Assert.Contains(book.Worksheet(1).CellsUsed(), c => c.GetString().StartsWith("Suivi des paiements", StringComparison.Ordinal));
        var pdf = await admin.GetAsync($"/api/rapports/suivi-mensuel/export?periodeId={periodId}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }
}
