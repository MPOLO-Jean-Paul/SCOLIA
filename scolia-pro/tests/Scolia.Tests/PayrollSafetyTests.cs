using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

public sealed class PayrollSafetyTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private async Task<HttpClient> Install(string currency = "USD")
    {
        var admin = app.Client();
        await admin.Post("/api/installation", new
        {
            schoolName = "École paie", shortName = "PAIE", city = "Lubumbashi", phone = "0810000000",
            currencyCode = currency, currencySymbol = currency == "USD" ? "$" : "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)), startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat", adminPassword = "Scolia2026!", loadDemoData = false
        }).Ok();
        return admin;
    }

    private static object Staff(decimal salary) => new
    {
        lastName = "MUKENDI", middleName = "TEST", firstName = "Paie", gender = "F", function = "Enseignante",
        phone = "0812345678", hireDate = DateOnly.FromDateTime(DateTime.Today), baseSalary = salary, isActive = true
    };

    private static string Period => DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string> Prepare(HttpClient admin)
    {
        await admin.Post($"/api/personnel/paie/{Period}/preparation", new { }).Ok();
        return (await admin.GetAsync($"/api/personnel/paie/{Period}").Ok())[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Insufficient_cash_refuses_salary_atomically_and_bank_payment_is_recorded_once()
    {
        using var admin = await Install();
        await admin.Post("/api/personnel", Staff(36.75m)).Ok();
        var id = await Prepare(admin);
        await admin.Put($"/api/personnel/paie/bulletins/{id}", new { allowances = 2.50m, deductions = 0.25m, advances = 0.50m }).Ok();
        var refused = await admin.Post($"/api/personnel/paie/bulletins/{id}/paiement", new { }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("Espèces disponibles insuffisantes", refused.Body.GetProperty("title").GetString(), StringComparison.Ordinal);
        var unchanged = (await admin.GetAsync($"/api/personnel/paie/{Period}").Ok())[0];
        Assert.Equal("Valide", unchanged.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("paidOn").ValueKind);
        Assert.Equal(0, (await admin.GetAsync("/api/depenses?categorie=Salaires").Ok()).GetProperty("total").GetInt32());

        var paid = await admin.Post($"/api/personnel/paie/bulletins/{id}/paiement?mode=Banque", new { }).Ok();
        Assert.Equal(38.50m, paid.GetProperty("net").GetDecimal());
        Assert.Equal("Paye", paid.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/personnel/paie/bulletins/{id}/paiement?mode=Banque", new { }).Fail()).Status);
        var expenses = await admin.GetAsync("/api/depenses?categorie=Salaires").Ok();
        Assert.Equal(1, expenses.GetProperty("total").GetInt32());
        Assert.Equal(38.50m, expenses.GetProperty("items")[0].GetProperty("amount").GetDecimal());
        foreach (var action in new[] { "paie.modifiee", "paie.payee" })
        {
            var trail = await admin.GetAsync("/api/audit?q=" + action).Ok();
            Assert.Contains("38,50 $", trail.GetProperty("items")[0].GetProperty("summary").GetString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("USD", "100.001")]
    [InlineData("CDF", "100.50")]
    [InlineData("USD", "1000000000001")]
    public async Task Salary_obeys_the_same_currency_rules_as_other_amounts(string currency, string salary)
    {
        using var admin = await Install(currency);
        var invalid = await admin.Post("/api/personnel", Staff(decimal.Parse(salary, System.Globalization.CultureInfo.InvariantCulture))).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, invalid.Status);
        Assert.Empty((await admin.GetAsync("/api/personnel").Ok()).EnumerateArray());
    }

    [Theory]
    [InlineData("USD", "allowances", "0.001")]
    [InlineData("USD", "deductions", "0.001")]
    [InlineData("USD", "advances", "0.001")]
    [InlineData("CDF", "allowances", "0.50")]
    [InlineData("USD", "allowances", "-1")]
    [InlineData("USD", "deductions", "101")]
    [InlineData("USD", "advances", "101")]
    public async Task Invalid_payroll_amounts_do_not_modify_the_bulletin(string currency, string field, string value)
    {
        using var admin = await Install(currency);
        await admin.Post("/api/personnel", Staff(100m)).Ok();
        var id = await Prepare(admin);
        var input = new Dictionary<string, decimal> { ["allowances"] = 0, ["deductions"] = 0, ["advances"] = 0 };
        input[field] = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put($"/api/personnel/paie/bulletins/{id}", input).Fail()).Status);
        var unchanged = (await admin.GetAsync($"/api/personnel/paie/{Period}").Ok())[0];
        Assert.Equal(100m, unchanged.GetProperty("net").GetDecimal());
        Assert.Equal(0m, unchanged.GetProperty(field).GetDecimal());
    }

    [Fact]
    public async Task Individually_valid_amounts_cannot_produce_a_net_above_the_monetary_limit()
    {
        using var admin = await Install();
        await admin.Post("/api/personnel", Staff(1_000_000_000_000m)).Ok();
        var id = await Prepare(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put($"/api/personnel/paie/bulletins/{id}",
            new { allowances = 0.01m, deductions = 0m, advances = 0m }).Fail()).Status);
        var unchanged = (await admin.GetAsync($"/api/personnel/paie/{Period}").Ok())[0];
        Assert.Equal(1_000_000_000_000m, unchanged.GetProperty("net").GetDecimal());
        Assert.Equal(0m, unchanged.GetProperty("allowances").GetDecimal());
    }
}
