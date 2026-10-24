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
            identity = new { lastName = "Mukendi", middleName = "Kabeya", firstName = "Grâce", gender = "F", birthDate = today.AddYears(-19), guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "Ilunga", middleName = "Kasongo", firstName = "Paul", gender = "M", birthDate = today.AddDays(-1).AddYears(-18), guardianPhone = "0812345679", guardianName = "Parent" },
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
