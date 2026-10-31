using System.Net;

namespace Scolia.Tests;

/// <summary>Appel des apprenants (version 79) : jours de cours, absences non signées, justifications, discipline et alertes.</summary>
public sealed class AttendanceApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    private const string Presences = "/api/scolarite/presences";
    private const string CourseDays = "/api/scolarite/jours-de-cours";
    private const string Alerts = "/api/scolarite/presences/alertes";
    private const string Summary = "/api/scolarite/presences/bilan";

    public void Dispose() => app.Dispose();

    private static async Task<(string PromotionId, string EnrollmentId)> EnrolledAsync(HttpClient admin, string lastName, DateOnly enrolledOn)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, enrolledOn, confirmDuplicate = false,
        }).Ok();
        return (promotionId, student.GetProperty("enrollments")[0].GetProperty("id").GetString()!);
    }

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Unsigned_past_course_days_become_absences_and_raise_an_alert_at_three()
    {
        var admin = await app.InstalledAdmin();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (promotion, _) = await EnrolledAsync(admin, "MUKENDI", today.AddDays(-10));
        var days = Enumerable.Range(1, 3).Select(i => today.AddDays(-i)).ToList();
        Assert.Equal(3, (await admin.Post($"{CourseDays}?promotionId={promotion}", days).Ok()).GetInt32());

        var sheet = await admin.GetAsync($"{Presences}?promotionId={promotion}&date={Iso(days[0])}").Ok();
        Assert.True(sheet.GetProperty("courseDay").GetBoolean());
        Assert.Equal("Absence non signée", sheet.GetProperty("rows").EnumerateArray().Single().GetProperty("status").GetString());

        var alert = Assert.Single((await admin.GetAsync($"{Alerts}?promotionId={promotion}&du={Iso(days[^1])}&au={Iso(today)}").Ok()).EnumerateArray().ToList());
        Assert.Equal(3, alert.GetProperty("unjustifiedAbsences").GetInt32());
        Assert.Equal(3, alert.GetProperty("longestRun").GetInt32());
        var bilan = (await admin.GetAsync($"{Summary}?promotionId={promotion}&du={Iso(days[^1])}&au={Iso(today)}").Ok()).EnumerateArray().Single();
        Assert.Equal(3, bilan.GetProperty("absent").GetInt32());

        // Une absence justifiée avec motif sort de la série : sous le seuil, plus d'alerte.
        var (tooShort, _) = await admin.Put($"{Presences}?promotionId={promotion}&date={Iso(days[0])}", new[] { new { enrollmentId = alert.GetProperty("enrollmentId").GetString(), status = "Absence justifiée", note = "Oui" } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooShort);
        await admin.Put($"{Presences}?promotionId={promotion}&date={Iso(days[0])}", new[] { new { enrollmentId = alert.GetProperty("enrollmentId").GetString(), status = "Absence justifiée", note = "Consultation médicale" } }).Ok();
        Assert.Empty((await admin.GetAsync($"{Alerts}?promotionId={promotion}&du={Iso(days[^1])}&au={Iso(today)}").Ok()).EnumerateArray().ToList());
    }

    [Fact]
    public async Task A_student_under_a_temporary_renvoi_has_no_automatic_absence_and_cannot_be_marked_present()
    {
        var admin = await app.InstalledAdmin();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);
        var (promotion, enrollment) = await EnrolledAsync(admin, "KABEYA", today.AddDays(-10));
        await admin.Post("/api/scolarite/discipline/registre", new
        {
            enrollmentId = enrollment, measure = "RenvoiTemporaire", reason = "Retards répétés", decisionDate = today, firstDay = yesterday, lastDay = today,
        }).Ok();
        await admin.Post($"{CourseDays}?promotionId={promotion}", new[] { yesterday }).Ok();

        var sheet = await admin.GetAsync($"{Presences}?promotionId={promotion}&date={Iso(yesterday)}").Ok();
        Assert.Equal("Exclu (discipline)", sheet.GetProperty("rows").EnumerateArray().Single().GetProperty("status").GetString());

        var (refused, body) = await admin.Put($"{Presences}?promotionId={promotion}&date={Iso(yesterday)}", new[] { new { enrollmentId = enrollment, status = "Présent", note = (string?)null } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Contains("exclu", body.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.Empty((await admin.GetAsync($"{Alerts}?promotionId={promotion}&du={Iso(yesterday)}&au={Iso(today)}").Ok()).EnumerateArray().ToList());
    }

    [Fact]
    public async Task A_course_day_holding_a_roll_call_cannot_be_released_but_an_empty_one_can()
    {
        var admin = await app.InstalledAdmin();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (promotion, enrollment) = await EnrolledAsync(admin, "NGANDU", today.AddDays(-10));
        var called = today.AddDays(-1);
        var empty = today.AddDays(-2);
        await admin.Post($"{CourseDays}?promotionId={promotion}", new[] { called, empty }).Ok();
        await admin.Put($"{Presences}?promotionId={promotion}&date={Iso(called)}", new[] { new { enrollmentId = enrollment, status = "Présent", note = (string?)null } }).Ok();

        var (blocked, _) = await admin.Post($"{CourseDays}/retrait?promotionId={promotion}", new[] { called }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, blocked);
        Assert.Equal(1, (await admin.Post($"{CourseDays}/retrait?promotionId={promotion}", new[] { empty }).Ok()).GetInt32());
        var remaining = await admin.GetAsync($"{CourseDays}?promotionId={promotion}&du={Iso(today.AddDays(-5))}&au={Iso(today)}").Ok();
        Assert.Single(remaining.EnumerateArray());
    }
}
