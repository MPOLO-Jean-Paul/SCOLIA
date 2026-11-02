using System.Net;

namespace Scolia.Tests;

/// <summary>Encaissement par lot : candidats, un reçu par apprenant, échecs isolés, idempotence et contrôle sans écriture.</summary>
public sealed class PaymentBatchTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static async Task<string> PromotionAsync(HttpClient admin)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        return promotion.GetProperty("id").GetString()!;
    }

    private static async Task<string> EnrollmentAsync(HttpClient admin, string promotionId, string lastName)
    {
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        return student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> MonthIdsAsync(HttpClient admin, string enrollmentId, int count)
    {
        var (ids, _) = await admin.OpenMonths(enrollmentId, count);
        return ids;
    }

    private static object Item(string enrollmentId, IEnumerable<string> feeItemIds, string method = "Especes", string? reference = null) =>
        new { enrollmentId, feeItemIds = feeItemIds.ToList(), method, reference };

    [Fact]
    public async Task Candidates_list_students_with_a_balance_and_their_open_months()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var first = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var second = await EnrollmentAsync(admin, promotion, "NGANDU");
        var before = (await admin.GetAsync($"/api/paiements/lot/candidats?promotionId={promotion}").Ok()).EnumerateArray()
            .Single(c => c.GetProperty("enrollmentId").GetString() == first).GetProperty("months").GetArrayLength();

        await admin.PayMonths(first, 1);

        var candidates = (await admin.GetAsync($"/api/paiements/lot/candidats?promotionId={promotion}").Ok()).EnumerateArray().ToList();
        Assert.Equal(2, candidates.Count);
        var paid = candidates.Single(c => c.GetProperty("enrollmentId").GetString() == first);
        Assert.Equal(before - 1, paid.GetProperty("months").GetArrayLength());
        Assert.Equal(before, candidates.Single(c => c.GetProperty("enrollmentId").GetString() == second).GetProperty("months").GetArrayLength());

        var filtered = (await admin.GetAsync("/api/paiements/lot/candidats?q=NGANDU").Ok()).EnumerateArray().ToList();
        Assert.Equal(new[] { second }, filtered.Select(c => c.GetProperty("enrollmentId").GetString()!));
    }

    [Fact]
    public async Task Batch_writes_one_receipt_per_student_and_reports_each_failure_on_its_own_line()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var first = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var second = await EnrollmentAsync(admin, promotion, "NGANDU");
        var third = await EnrollmentAsync(admin, promotion, "KABONGO");
        var firstMonths = await MonthIdsAsync(admin, first, 2);
        var secondMonths = await MonthIdsAsync(admin, second, 3);
        var thirdMonths = await MonthIdsAsync(admin, third, 1);

        var result = await admin.Post("/api/paiements/lot", new
        {
            items = new object[]
            {
                Item(first, firstMonths),
                Item(second, Array.Empty<string>()),
                Item(third, thirdMonths, "MobileMoney"),
                Item(second, secondMonths),
            },
        }).Ok();

        Assert.Equal(2, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, result.GetProperty("failed").GetInt32());
        Assert.Equal(55000m * 2 + 55000m * 3, result.GetProperty("total").GetDecimal());
        var lines = result.GetProperty("lines").EnumerateArray().ToList();
        Assert.Matches(@"^RC-\d{4}-\d{6}$", lines[0].GetProperty("receiptNumber").GetString());
        Assert.Contains("mois", lines[1].GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Contains("référence", lines[2].GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(lines[3].GetProperty("receiptNumber").GetString());

        var firstAccount = await admin.GetAsync($"/api/inscriptions/{first}/compte").Ok();
        Assert.Equal(110000m, firstAccount.GetProperty("totalPaid").GetDecimal());
        var thirdAccount = await admin.GetAsync($"/api/inscriptions/{third}/compte").Ok();
        Assert.Equal(0m, thirdAccount.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task A_resent_batch_with_the_same_key_does_not_pay_twice()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var student = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, student, 1);
        var body = new { items = new[] { Item(student, months) }, idempotencyKey = "lot-reseau-1" };

        var first = await admin.Post("/api/paiements/lot", body).Ok();
        var again = await admin.Post("/api/paiements/lot", body).Ok();

        Assert.Equal(first.GetProperty("lines")[0].GetProperty("receiptId").GetString(), again.GetProperty("lines")[0].GetProperty("receiptId").GetString());
        var account = await admin.GetAsync($"/api/inscriptions/{student}/compte").Ok();
        Assert.Equal(55000m, account.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Preview_reports_amounts_and_similar_receipts_without_writing()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var student = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, student, 1);
        await admin.Post("/api/paiements/lot", new { items = new[] { Item(student, months) } }).Ok();

        var again = await MonthIdsAsync(admin, student, 2);
        var preview = await admin.Post("/api/paiements/lot/apercu", new { items = new[] { Item(student, again) } }).Ok();
        Assert.Equal(1, preview.GetProperty("count").GetInt32());
        Assert.Equal(0, preview.GetProperty("errors").GetInt32());
        Assert.Equal(110000m, preview.GetProperty("total").GetDecimal());
        Assert.Equal(0, preview.GetProperty("duplicateCount").GetInt32());

        var same = await MonthIdsAsync(admin, student, 1);
        var duplicate = await admin.Post("/api/paiements/lot/apercu", new { items = new[] { Item(student, same) } }).Ok();
        Assert.Equal(1, duplicate.GetProperty("duplicateCount").GetInt32());
        var receipts = await admin.GetAsync($"/api/inscriptions/{student}/compte").Ok();
        Assert.Equal(55000m, receipts.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Empty_oversized_or_future_batches_are_refused()
    {
        var admin = await app.InstalledAdmin();
        var empty = await admin.Post("/api/paiements/lot", new { items = Array.Empty<object>() }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);

        var promotion = await PromotionAsync(admin);
        var student = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, student, 1);
        var future = await admin.Post("/api/paiements/lot", new { date = DateOnly.FromDateTime(DateTime.Today.AddDays(1)), items = new[] { Item(student, months) } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, future.Status);
        var account = await admin.GetAsync($"/api/inscriptions/{student}/compte").Ok();
        Assert.Equal(0m, account.GetProperty("totalPaid").GetDecimal());
    }
}
