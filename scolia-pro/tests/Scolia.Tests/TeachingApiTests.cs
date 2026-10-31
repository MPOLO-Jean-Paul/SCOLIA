using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

/// <summary>Cours, attributions et charges des enseignants (règles de la version 79).</summary>
public sealed class TeachingApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    private const string Cours = "/api/scolarite/cours";
    private const string Charges = "/api/scolarite/enseignants/charges";

    public void Dispose() => app.Dispose();

    private static async Task<string> PromotionAsync(HttpClient admin)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 55000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        return promotion.GetProperty("id").GetString()!;
    }

    private static async Task<string> TeacherAsync(HttpClient admin, string name, DateOnly hireDate)
    {
        var staff = await admin.Post("/api/personnel", new
        {
            lastName = name, middleName = "", firstName = "Jean", gender = "M", function = "Enseignant", phone = "0812345678", email = (string?)null,
            hireDate, exitDate = (DateOnly?)null, baseSalary = 300000m, isActive = true,
        }).Ok();
        return staff.GetProperty("id").GetString()!;
    }

    private static async Task<string> CourseAsync(HttpClient admin, string promotionId, int number, string name, decimal hours)
    {
        var course = await admin.Post(Cours, new { promotionId, number, name, coefficient = 2m, hours, maxScore = 20m }).Ok();
        return course.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Course_numbers_names_and_hours_follow_the_v79_rules()
    {
        var admin = await app.InstalledAdmin();
        var promotionId = await PromotionAsync(admin);

        var anatomy = await admin.Post(Cours, new { promotionId, number = 1, name = "Anatomie", coefficient = 4m, hours = 30.5m, maxScore = 20m }).Ok();
        Assert.Equal(30.5m, anatomy.GetProperty("hours").GetDecimal());
        Assert.Equal("INF1", anatomy.GetProperty("promotionCode").GetString());

        // Numéro déjà pris, nom identique à casse et accents près, volume nul, plus de deux décimales.
        var (sameNumber, _) = await admin.Post(Cours, new { promotionId, number = 1, name = "Physiologie", coefficient = 2m, hours = 10m, maxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, sameNumber);
        var (sameName, _) = await admin.Post(Cours, new { promotionId, number = 2, name = "  ANATOMIE ", coefficient = 2m, hours = 10m, maxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, sameName);
        var (noHours, _) = await admin.Post(Cours, new { promotionId, number = 2, name = "Physiologie", coefficient = 2m, hours = 0m, maxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noHours);
        var (tooPrecise, _) = await admin.Post(Cours, new { promotionId, number = 2, name = "Physiologie", coefficient = 2m, hours = 1.234m, maxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise);

        var next = await admin.GetAsync($"{Cours}/numero-suivant?promotionId={promotionId}").Ok();
        Assert.Equal(2, next.GetProperty("numero").GetInt32());
    }

    [Fact]
    public async Task Assignment_keeps_one_active_teacher_and_a_history()
    {
        var admin = await app.InstalledAdmin();
        var promotionId = await PromotionAsync(admin);
        var courseId = await CourseAsync(admin, promotionId, 1, "Anatomie", 30m);
        var first = await TeacherAsync(admin, "DUPONT", new DateOnly(2026, 1, 1));
        var second = await TeacherAsync(admin, "MUKENDI", new DateOnly(2026, 1, 1));

        var assigned = await admin.Put($"{Cours}/{courseId}/attribution", new { teacherId = first, confirmReplace = false }).Ok();
        Assert.Equal("DUPONT Jean", assigned.GetProperty("teacherName").GetString());

        // Réattribuer sans confirmation est refusé ; avec confirmation, l'ancien enseignant reste dans l'historique.
        var (noConfirm, _) = await admin.Put($"{Cours}/{courseId}/attribution", new { teacherId = second, confirmReplace = false }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, noConfirm);
        var replaced = await admin.Put($"{Cours}/{courseId}/attribution", new { teacherId = second, confirmReplace = true }).Ok();
        Assert.Equal("MUKENDI Jean", replaced.GetProperty("teacherName").GetString());

        var history = (await admin.GetAsync($"{Cours}/{courseId}/attributions").Ok()).EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history.Count(h => h.GetProperty("isCurrent").GetBoolean()));
        Assert.Contains(history, h => h.GetProperty("endReason").GetString() == "Réattribué");

        // Retirer l'attribution rend le cours disponible et garde la trace.
        var unassigned = await admin.Post($"{Cours}/{courseId}/retrait-attribution", new { reason = "Congé de l'enseignant" }).Ok();
        Assert.Equal(JsonValueKind.Null, unassigned.GetProperty("teacherName").ValueKind);
        var afterUnassign = (await admin.GetAsync($"{Cours}/{courseId}/attributions").Ok()).EnumerateArray().ToList();
        Assert.Equal(2, afterUnassign.Count);
        Assert.Equal(0, afterUnassign.Count(h => h.GetProperty("isCurrent").GetBoolean()));
    }

    [Fact]
    public async Task Archiving_needs_no_active_assignment_and_archived_courses_leave_the_charges()
    {
        var admin = await app.InstalledAdmin();
        var promotionId = await PromotionAsync(admin);
        var courseId = await CourseAsync(admin, promotionId, 1, "Anatomie", 30m);
        var teacher = await TeacherAsync(admin, "DUPONT", new DateOnly(2026, 1, 1));
        await CourseAsync(admin, promotionId, 2, "Physiologie", 45m);
        await admin.Put($"{Cours}/{courseId}/attribution", new { teacherId = teacher, confirmReplace = false }).Ok();

        var (assigned, _) = await admin.Post($"{Cours}/{courseId}/archivage", new { }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, assigned);

        var before = await admin.GetAsync(Charges).Ok();
        Assert.Equal(30m, before.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "DUPONT Jean").GetProperty("hours").GetDecimal());

        await admin.Post($"{Cours}/{courseId}/retrait-attribution", new { reason = "Réorganisation" }).Ok();
        await admin.Post($"{Cours}/{courseId}/archivage", new { }).Ok();
        var visible = await admin.GetAsync($"{Cours}?promotionId={promotionId}").Ok();
        Assert.Single(visible.EnumerateArray());
        var withArchived = await admin.GetAsync($"{Cours}?promotionId={promotionId}&archives=true").Ok();
        Assert.Equal(2, withArchived.GetArrayLength());

        // Le numéro d'un cours archivé reste réservé ; la réactivation revérifie le nom.
        var (reuse, _) = await admin.Post(Cours, new { promotionId, number = 1, name = "Nouveau", coefficient = 2m, hours = 10m, maxScore = 20m }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, reuse);

        var after = await admin.GetAsync(Charges).Ok();
        Assert.Equal(0m, after.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "DUPONT Jean").GetProperty("hours").GetDecimal());
    }

    [Fact]
    public async Task Teacher_charge_adds_full_course_hours_and_lists_teachers_without_courses()
    {
        var admin = await app.InstalledAdmin();
        var promotionId = await PromotionAsync(admin);
        var anatomy = await CourseAsync(admin, promotionId, 1, "Anatomie", 30.5m);
        var physiology = await CourseAsync(admin, promotionId, 2, "Physiologie", 45m);
        var teacher = await TeacherAsync(admin, "DUPONT", new DateOnly(2026, 1, 1));
        var idle = await TeacherAsync(admin, "MUKENDI", new DateOnly(2026, 1, 1));
        await admin.Put($"{Cours}/{anatomy}/attribution", new { teacherId = teacher, confirmReplace = false }).Ok();
        await admin.Put($"{Cours}/{physiology}/attribution", new { teacherId = teacher, confirmReplace = false }).Ok();

        var charges = (await admin.GetAsync(Charges).Ok()).EnumerateArray().ToList();
        var dupont = charges.Single(t => t.GetProperty("name").GetString() == "DUPONT Jean");
        Assert.Equal(2, dupont.GetProperty("courses").GetInt32());
        Assert.Equal(75.5m, dupont.GetProperty("hours").GetDecimal());
        var mukendi = charges.Single(t => t.GetProperty("teacherId").GetString() == idle);
        Assert.Equal(0m, mukendi.GetProperty("hours").GetDecimal());

        var detail = (await admin.GetAsync($"/api/scolarite/enseignants/{teacher}/cours").Ok()).EnumerateArray().ToList();
        Assert.Equal(2, detail.Count);
    }

    [Fact]
    public async Task Teacher_outside_the_school_year_cannot_be_assigned()
    {
        var admin = await app.InstalledAdmin();
        var promotionId = await PromotionAsync(admin);
        var courseId = await CourseAsync(admin, promotionId, 1, "Anatomie", 30m);
        var future = await TeacherAsync(admin, "FUTUR", DateOnly.FromDateTime(DateTime.Today.AddYears(3)));
        var (status, _) = await admin.Put($"{Cours}/{courseId}/attribution", new { teacherId = future, confirmReplace = false }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
