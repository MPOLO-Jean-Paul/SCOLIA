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

    [Fact]
    public async Task Same_student_cannot_be_collected_twice_in_one_batch()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var enrollment = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, enrollment, 1);
        var items = new[] { Item(enrollment, months), Item(enrollment, months) };

        var preview = await admin.Post("/api/paiements/lot/apercu", new { items }).Ok();
        Assert.Equal(1, preview.GetProperty("count").GetInt32());
        Assert.Equal(1, preview.GetProperty("errors").GetInt32());

        var result = await admin.Post("/api/paiements/lot", new { items }).Ok();
        Assert.Equal(1, result.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Contains("déjà", result.GetProperty("lines")[1].GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(55000m, account.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task A_payment_key_cannot_be_reused_for_a_different_student()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var first = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var second = await EnrollmentAsync(admin, promotion, "NGANDU");
        var firstMonth = await MonthIdsAsync(admin, first, 1);
        var secondMonth = await MonthIdsAsync(admin, second, 1);

        var original = new
        {
            enrollmentId = first, amount = 55000m, method = "Especes",
            feeItemIds = firstMonth, idempotencyKey = "transaction-tiers-unique-001"
        };
        await admin.Post("/api/paiements", original).Ok();

        var (status, _) = await admin.Post("/api/paiements", new
        {
            enrollmentId = second, amount = 55000m, method = "Especes",
            feeItemIds = secondMonth, idempotencyKey = "transaction-tiers-unique-001"
        }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);
        var account = await admin.GetAsync($"/api/inscriptions/{second}/compte").Ok();
        Assert.Equal(0m, account.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Reordered_batch_does_not_assign_a_receipt_to_the_wrong_student()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var first = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var second = await EnrollmentAsync(admin, promotion, "NGANDU");
        var firstMonths = await MonthIdsAsync(admin, first, 1);
        var secondMonths = await MonthIdsAsync(admin, second, 1);
        const string key = "batch-ordered-audit-001";

        var original = await admin.Post("/api/paiements/lot", new
        {
            items = new[] { Item(first, firstMonths), Item(second, secondMonths) }, idempotencyKey = key
        }).Ok();
        Assert.Equal(2, original.GetProperty("succeeded").GetInt32());

        var swapped = await admin.Post("/api/paiements/lot", new
        {
            items = new[] { Item(second, secondMonths), Item(first, firstMonths) }, idempotencyKey = key
        }).Ok();
        Assert.Equal(0, swapped.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, swapped.GetProperty("failed").GetInt32());
        Assert.All(swapped.GetProperty("lines").EnumerateArray(),
            line => Assert.Contains("changé", line.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase));

        var paidFirst = await admin.GetAsync($"/api/inscriptions/{first}/compte").Ok();
        var paidSecond = await admin.GetAsync($"/api/inscriptions/{second}/compte").Ok();
        Assert.Equal(55000m, paidFirst.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(55000m, paidSecond.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Replayed_batch_cannot_silently_replace_the_selected_month()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var enrollment = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, enrollment, 2);
        const string key = "batch-months-must-match";

        var original = await admin.Post("/api/paiements/lot", new
        {
            items = new[] { Item(enrollment, months.Take(1)) }, idempotencyKey = key
        }).Ok();
        Assert.Equal(1, original.GetProperty("succeeded").GetInt32());

        var changed = await admin.Post("/api/paiements/lot", new
        {
            items = new[] { Item(enrollment, months.Skip(1)) }, idempotencyKey = key
        }).Ok();
        Assert.Equal(0, changed.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, changed.GetProperty("failed").GetInt32());
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(55000m, account.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Cancelled_receipt_is_not_reported_as_a_successful_idempotent_payment()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var enrollment = await EnrollmentAsync(admin, promotion, "MUKENDI");
        var months = await MonthIdsAsync(admin, enrollment, 1);
        var request = new
        {
            enrollmentId = enrollment, amount = 55000m, method = "Especes",
            feeItemIds = months, idempotencyKey = "cancelled-payment-retry"
        };

        var receipt = await admin.Post("/api/paiements", request).Ok();
        await admin.Post($"/api/paiements/{receipt.GetProperty("id").GetString()}/annulation",
            new { reason = "Erreur d'encaissement" }).Ok();

        var (status, _) = await admin.Post("/api/paiements", request).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(0m, account.GetProperty("totalPaid").GetDecimal());
    }
}
