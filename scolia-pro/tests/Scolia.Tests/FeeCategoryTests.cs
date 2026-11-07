using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Infrastructure;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Catégories de frais (seules celles créées existent, mensualité obligatoire) et apprenants pris en charge,
/// comptés à part des payants et des apprenants en ordre.
/// </summary>
public sealed class FeeCategoryTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    private static object Promotion(string code, decimal monthly, object[]? fees = null, bool manages = true) => new
    {
        code, name = "Promotion " + code, level = 1, isActive = true, monthlyFee = monthly,
        oneTimeFees = fees ?? [], managesCategories = manages,
    };

    private static async Task<string> Enroll(HttpClient admin, string promotionId, string lastName, string phone)
    {
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Kabeya", firstName = "Grâce", birthPlace = "Kinshasa", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = phone, guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        return student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Monthly_fee_is_mandatory_and_only_created_categories_exist()
    {
        var admin = await app.InstalledAdmin();
        var (_, noMonthly) = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 0m)).Fail();
        Assert.Contains("La mensualité est obligatoire", noMonthly.GetRawText(), StringComparison.Ordinal);

        // Un frais dont la catégorie n'a pas été créée est refusé (ancien client : nature + libellé).
        var (_, unknown) = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m,
            [new { kind = "Autre", label = "Tenue scolaire", amount = 15000m, dueDate = DateOnly.FromDateTime(DateTime.Today) }], manages: false)).Fail();
        Assert.Contains("Créez d'abord la catégorie", unknown.GetProperty("title").GetString(), StringComparison.Ordinal);

        // Sans catégorie : seules les mensualités sont facturées.
        var plain = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        Assert.All(plain.GetProperty("fees").EnumerateArray(), f => Assert.Equal("Mensualite", f.GetProperty("kind").GetString()));
        Assert.Equal(0, (await admin.GetAsync("/api/etablissement/categories-frais").Ok()).GetArrayLength());

        var (_, monthlyAsCategory) = await admin.Post("/api/etablissement/categories-frais", new { name = "Mensualité", amount = 1000m }).Fail();
        Assert.Contains("pas une catégorie", monthlyAsCategory.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Category_amount_applies_to_promotions_and_is_collected_like_a_month()
    {
        var admin = await app.InstalledAdmin();
        var inf = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        var acc = await admin.Post("/api/etablissement/promotions", Promotion("ACC1", 60000m)).Ok();
        var enrollmentId = await Enroll(admin, inf.GetProperty("id").GetString()!, "Mukendi", "0812345678");

        var category = await admin.Post("/api/etablissement/categories-frais", new { name = "Tenue scolaire", amount = 15000m, applyToPromotions = true }).Ok();
        Assert.Equal(2, category.GetProperty("promotionCount").GetInt32());
        var categoryId = category.GetProperty("id").GetString();

        // Montant propre à une promotion.
        await admin.Put($"/api/etablissement/promotions/{acc.GetProperty("id").GetString()}",
            Promotion("ACC1", 60000m, [new { categoryId, amount = 18000m }])).Ok();
        var promotions = await admin.GetAsync("/api/etablissement/promotions").Ok();
        decimal FeeOf(string code) => promotions.EnumerateArray().First(p => p.GetProperty("code").GetString() == code)
            .GetProperty("fees").EnumerateArray().First(f => f.GetProperty("categoryId").GetString() == categoryId).GetProperty("amount").GetDecimal();
        Assert.Equal(15000m, FeeOf("INF1"));
        Assert.Equal(18000m, FeeOf("ACC1"));

        // Le frais figure au compte et s'encaisse comme un mois.
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        var line = account.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("label").GetString() == "Tenue scolaire");
        var receipt = await admin.Post("/api/paiements", new
        {
            enrollmentId, amount = 15000m, method = "Especes", feeItemIds = new[] { line.GetProperty("feeItemId").GetString() },
        }).Ok();
        Assert.Equal("Tenue scolaire", receipt.GetProperty("allocations")[0].GetProperty("label").GetString());

        // Encaissée : elle ne peut plus être supprimée, seulement désactivée.
        var (status, _) = await admin.DeleteAsync($"/api/etablissement/categories-frais/{categoryId}").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        await admin.Put($"/api/etablissement/categories-frais/{categoryId}", new { name = "Tenue", amount = 15000m, isActive = false }).Ok();
        var renamed = await admin.GetAsync($"/api/paiements/{receipt.GetProperty("id").GetString()}").Ok();
        Assert.Equal("Tenue", renamed.GetProperty("allocations")[0].GetProperty("label").GetString());

        // L'encaissement par lot reste limité aux mensualités.
        var batch = await admin.Post("/api/paiements/lot/apercu", new
        {
            items = new[] { new { enrollmentId, feeItemIds = new[] { line.GetProperty("feeItemId").GetString() }, method = "Especes" } },
        }).Ok();
        Assert.Equal(1, batch.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Unused_category_is_removed_from_promotions_when_deleted()
    {
        var admin = await app.InstalledAdmin();
        await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        var category = await admin.Post("/api/etablissement/categories-frais", new { name = "Carte d'élève", amount = 5000m, applyToPromotions = true }).Ok();
        await admin.DeleteAsync($"/api/etablissement/categories-frais/{category.GetProperty("id").GetString()}").Ok();
        var promotions = await admin.GetAsync("/api/etablissement/promotions").Ok();
        Assert.All(promotions[0].GetProperty("fees").EnumerateArray(), f => Assert.Equal("Mensualite", f.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task A_category_used_by_a_closed_year_cannot_be_deleted()
    {
        var admin = await app.InstalledAdmin();
        await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        var category = await admin.Post("/api/etablissement/categories-frais", new { name = "Frais de stage", amount = 10000m, applyToPromotions = true }).Ok();
        var year = (await admin.GetAsync("/api/etablissement/annees").Ok())[0].GetProperty("id").GetString();
        await admin.Post($"/api/etablissement/annees/{year}/cloture", new { }).Ok();
        var (_, refused) = await admin.DeleteAsync($"/api/etablissement/categories-frais/{category.GetProperty("id").GetString()}").Fail();
        Assert.Contains("année clôturée", refused.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sponsored_students_are_counted_apart_from_payers_and_up_to_date_students()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var payer = await Enroll(admin, promotionId, "Mukendi", "0812345678");
        var sponsored = await Enroll(admin, promotionId, "Kasongo", "0812345679");
        await admin.Post($"/api/inscriptions/{sponsored}/prises-en-charge", new
        {
            sponsorName = "Diocèse", reason = "Bourse", effectiveFrom = DateOnly.FromDateTime(DateTime.Today), type = "Complete",
        }).Ok();
        // Le payant règle tout ce qui est échu : il est « en ordre ».
        var account = await admin.GetAsync($"/api/inscriptions/{payer}/compte").Ok();
        var due = account.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("isOverdue").GetBoolean()).ToList();
        if (due.Count > 0)
            await admin.Post("/api/paiements", new
            {
                enrollmentId = payer, amount = due.Sum(l => l.GetProperty("remaining").GetDecimal()), method = "Especes",
                feeItemIds = due.Select(l => l.GetProperty("feeItemId").GetString()),
            }).Ok();

        var dashboard = await admin.GetAsync("/api/rapports/tableau-de-bord").Ok();
        Assert.Equal(2, dashboard.GetProperty("activeStudents").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("payingStudents").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("sponsoredStudents").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("studentsUpToDate").GetInt32()); // le pris en charge n'est pas compté « en ordre »
        Assert.True(dashboard.GetProperty("sponsoredAmount").GetDecimal() > 0);
        var stat = dashboard.GetProperty("promotions")[0];
        Assert.Equal(1, stat.GetProperty("paying").GetInt32());
        Assert.Equal(1, stat.GetProperty("sponsored").GetInt32());

        var monthly = await admin.GetAsync("/api/rapports/suivi-mensuel").Ok();
        var row = monthly.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("fullName").GetString()!.StartsWith("KASONGO", StringComparison.Ordinal));
        Assert.Equal("Pris en charge", row.GetProperty("status").GetString());
        Assert.Equal(1, monthly.GetProperty("coveredCount").GetInt32());

        var students = await admin.GetAsync("/api/apprenants").Ok();
        var flagged = students.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("lastName").GetString() == "KASONGO");
        Assert.Equal("Pris en charge", flagged.GetProperty("sponsorship").GetString());

        var debtors = await admin.GetAsync("/api/rapports/debiteurs").Ok();
        Assert.DoesNotContain(debtors.EnumerateArray(), d => d.GetProperty("enrollmentId").GetString() == sponsored);

        var promoter = await admin.GetAsync("/api/pilotage/synthese").Ok();
        Assert.Equal(1, promoter.GetProperty("sponsoredStudents").GetInt32());
        Assert.Equal(1, promoter.GetProperty("payingStudents").GetInt32());

        var list = await admin.GetAsync($"/api/rapports/liste-classe/{promotionId}?format=excel");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Fees_entered_before_the_catalogue_get_their_category_without_change()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", Promotion("INF1", 55000m)).Ok();
        // Frais d'inscription saisi par une version précédente, sans catégorie.
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(app.DataDirectory, "scolia.db")}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO RubriquesFrais (Id, PromotionId, PeriodId, Kind, CategoryId, Label, Amount, DueDate, \"Order\", CreatedAtUtc, Version) " +
                "VALUES ($id, $promotion, NULL, 1, NULL, 'Frais d''inscription', 2000000, $due, 0, '2026-09-01 00:00:00', 0)";
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$promotion", promotion.GetProperty("id").GetString()!.ToUpperInvariant());
            command.Parameters.AddWithValue("$due", DateTime.Today.ToString("yyyy-MM-dd"));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await using (var scope = app.Services.CreateAsyncScope())
            await DependencyInjection.AttachLegacyFeesToCategoriesAsync(scope.ServiceProvider.GetRequiredService<ScoliaDbContext>(),
                scope.ServiceProvider.GetRequiredService<Scolia.Application.Abstractions.IAuditLog>());

        var categories = await admin.GetAsync("/api/etablissement/categories-frais").Ok();
        var category = Assert.Single(categories.EnumerateArray());
        Assert.Equal("Frais d'inscription", category.GetProperty("name").GetString());
        Assert.Equal(20000m, category.GetProperty("amount").GetDecimal());
        var fees = (await admin.GetAsync("/api/etablissement/promotions").Ok())[0].GetProperty("fees").EnumerateArray();
        Assert.Contains(fees, f => f.GetProperty("categoryId").GetString() == category.GetProperty("id").GetString() && f.GetProperty("amount").GetDecimal() == 20000m);
        // La reprise est tracée et la chaîne d'audit reste intacte.
        Assert.True((await admin.GetAsync("/api/audit/verification").Ok()).GetProperty("isIntact").GetBoolean());
    }
}
