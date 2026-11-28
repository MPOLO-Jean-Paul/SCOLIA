extern alias bureau;
using ClosedXML.Excel;
using Scolia.Domain.Finance;
using ClientFormat = bureau::Scolia.Bureau.Infrastructure.Format;

namespace Scolia.Tests;

public sealed class FinancialPrecisionTests
{
    [Theory]
    [InlineData("36.75", "36,75 $")]
    [InlineData("0.01", "0,01 $")]
    [InlineData("-36.50", "-36,50 $")]
    [InlineData("55000", "55 000 $")]
    public void Client_and_documents_keep_the_same_exact_amount(string input, string expected)
    {
        var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        static string Spaces(string value) => value.Replace('\u202f', ' ').Replace('\u00a0', ' ');
        Assert.Equal(expected, Spaces(Money.Display(amount, "$")));
        Assert.Equal(expected, Spaces(ClientFormat.Money(amount, "$")));
    }

    [Fact]
    public async Task Dollar_payments_keep_their_cents_in_accounts_exports_and_cash_salary_payments()
    {
        using var app = new ScoliaApp();
        using var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            schoolName = "École précision", shortName = "PREC", city = "Lubumbashi", phone = "0810000000",
            currencyCode = "USD", currencySymbol = "$", yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)),
            startMonth = 9, monthCount = 10, adminUserName = "admin", adminDisplayName = "Secrétariat",
            adminPassword = "Scolia2026!", loadDemoData = false
        }).Ok();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "PRC1", name = "Première année", level = 1, isActive = true,
            monthlyFee = 36.75m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "MUKENDI", middleName = "TEST", firstName = "Précision", gender = "F",
                birthDate = new DateOnly(2008, 3, 14), birthPlace = "Lubumbashi", guardianPhone = "0812345678" },
            promotionId = promotion.GetProperty("id").GetString()
        }).Ok();
        var enrollment = student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
        var receipt = await admin.PayMonths(enrollment);
        var dashboard = await admin.GetAsync("/api/rapports/tableau-de-bord").Ok();
        Assert.Equal(36.75m, dashboard.GetProperty("collectedTotal").GetDecimal());
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(36.75m, account.GetProperty("totalPaid").GetDecimal());
        var key = dashboard.GetProperty("selectionKey").GetString();
        using var response = await admin.GetAsync("/api/rapports/tableau-de-bord/export?format=xlsx&expectedSelection=" + key);
        response.EnsureSuccessStatusCode();
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var payments = workbook.Worksheets.SelectMany(sheet => sheet.CellsUsed())
            .Where(cell => cell.DataType == XLDataType.Number && cell.GetDouble() == 36.75).ToArray();
        Assert.NotEmpty(payments);
        Assert.All(payments, cell => Assert.Equal("#,##0.00", cell.Style.NumberFormat.Format));

        using var pdf = await admin.GetAsync($"/api/paiements/{receipt.GetProperty("id").GetString()}/recu.pdf");
        pdf.EnsureSuccessStatusCode();
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes[..4]));
        // Optional evidence for visual/text inspection of the actual generated receipt.
        if (Environment.GetEnvironmentVariable("SCOLIA_AUDIT_ARTIFACTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, "recu-36-75-usd.pdf"), bytes);
        }
        await admin.Post("/api/personnel", new
        {
            lastName = "KALENGA", middleName = "TEST", firstName = "Salaire", gender = "F", function = "Enseignante",
            phone = "0812345678", hireDate = DateOnly.FromDateTime(DateTime.Today), baseSalary = 25.75m, isActive = true
        }).Ok();
        var period = DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await admin.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok();
        var payrollId = (await admin.GetAsync($"/api/personnel/paie/{period}").Ok())[0].GetProperty("id").GetString();
        await admin.Put($"/api/personnel/paie/bulletins/{payrollId}", new { allowances = 2.50m, deductions = 0.25m, advances = 0.50m }).Ok();
        var salary = await admin.Post($"/api/personnel/paie/bulletins/{payrollId}/paiement", new { }).Ok();
        Assert.Equal(27.50m, salary.GetProperty("net").GetDecimal());
        var cash = await admin.GetAsync("/api/caisse/situation").Ok();
        Assert.Equal(9.25m, cash.GetProperty("expectedCash").GetDecimal());
    }
}
