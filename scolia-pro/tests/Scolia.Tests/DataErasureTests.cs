using System.Net;

namespace Scolia.Tests;

/// <summary>Effacement volontaire : mot de passe de confirmation, portée (sélection ou tous), sauvegarde préalable.</summary>
public sealed class DataErasureTests : IDisposable
{
    private const string AdminPassword = "Scolia2026!";
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

    /// <summary>Crée un apprenant et renvoie son identifiant d'apprenant et d'inscription.</summary>
    private static async Task<(string StudentId, string EnrollmentId)> StudentAsync(HttpClient admin, string promotionId, string lastName)
    {
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Kabeya", firstName = "Grace", gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        return (student.GetProperty("id").GetString()!, student.GetProperty("enrollments")[0].GetProperty("id").GetString()!);
    }

    private static async Task<decimal> PaidAsync(HttpClient admin, string enrollmentId) =>
        (await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok()).GetProperty("totalPaid").GetDecimal();

    [Fact]
    public async Task A_wrong_password_erases_nothing_and_is_recorded()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var (studentId, enrollmentId) = await StudentAsync(admin, promotion, "MUKENDI");
        await admin.PayMonths(enrollmentId, 1);

        var (status, body) = await admin.Post("/api/effacement/paiements", new { scope = "selection", studentIds = new[] { studentId }, password = "mauvais" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("aucune donnée", body.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(55000m, await PaidAsync(admin, enrollmentId));

        var audit = (await admin.GetAsync("/api/audit?taille=50").Ok()).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("donnees.effacement.refuse", audit);
    }

    [Fact]
    public async Task Erasing_the_payments_of_selected_students_leaves_their_files_and_others_untouched()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var (firstStudent, firstEnrollment) = await StudentAsync(admin, promotion, "MUKENDI");
        var (secondStudent, secondEnrollment) = await StudentAsync(admin, promotion, "NGANDU");
        await admin.PayMonths(firstEnrollment, 2);
        await admin.PayMonths(secondEnrollment, 1);

        var summary = await admin.Post("/api/effacement/paiements", new { scope = "selection", studentIds = new[] { firstStudent }, password = AdminPassword }).Ok();
        // Deux mois réglés en un seul versement : un reçu, deux imputations.
        Assert.Equal(1, summary.GetProperty("receipts").GetInt32());
        Assert.Equal(2, summary.GetProperty("allocations").GetInt32());

        // Le dossier reste ; son compte est comme s'il n'avait rien payé. Les autres paiements ne bougent pas.
        Assert.Equal(0m, await PaidAsync(admin, firstEnrollment));
        var account = await admin.GetAsync($"/api/inscriptions/{firstEnrollment}/compte").Ok();
        Assert.Equal(550000m, account.GetProperty("balance").GetDecimal());
        Assert.Equal(55000m, await PaidAsync(admin, secondEnrollment));
        await admin.GetAsync($"/api/apprenants/{firstStudent}").Ok();

        var backups = (await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();
        // Le nom de fichier de la sauvegarde ne garde que les lettres : on cherche le motif sans tirets.
        Assert.Contains(backups, n => n.Contains("avanteffacementpaiements", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Erasing_all_payments_clears_every_account_but_keeps_every_student()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var (_, first) = await StudentAsync(admin, promotion, "MUKENDI");
        var (_, second) = await StudentAsync(admin, promotion, "NGANDU");
        await admin.PayMonths(first, 1);
        await admin.PayMonths(second, 3);

        await admin.Post("/api/effacement/paiements", new { scope = "tous", password = AdminPassword }).Ok();

        Assert.Equal(0m, await PaidAsync(admin, first));
        Assert.Equal(0m, await PaidAsync(admin, second));
        var students = await admin.GetAsync("/api/apprenants").Ok();
        Assert.Equal(2, students.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Erasing_selected_students_removes_their_files_and_payments_only()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var (removedStudent, removedEnrollment) = await StudentAsync(admin, promotion, "MUKENDI");
        var (keptStudent, keptEnrollment) = await StudentAsync(admin, promotion, "NGANDU");
        await admin.PayMonths(removedEnrollment, 1);
        await admin.PayMonths(keptEnrollment, 1);

        var summary = await admin.Post("/api/effacement/apprenants", new { scope = "selection", studentIds = new[] { removedStudent }, password = AdminPassword }).Ok();
        Assert.Equal(1, summary.GetProperty("students").GetInt32());
        Assert.Equal(1, summary.GetProperty("enrollments").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/apprenants/{removedStudent}")).StatusCode);
        Assert.Equal(55000m, await PaidAsync(admin, keptEnrollment));
        await admin.GetAsync($"/api/apprenants/{keptStudent}").Ok();
    }

    [Fact]
    public async Task Erasing_all_students_empties_the_student_list_and_keeps_the_school_settings()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await PromotionAsync(admin);
        var (_, enrollment) = await StudentAsync(admin, promotion, "MUKENDI");
        await StudentAsync(admin, promotion, "NGANDU");
        await admin.PayMonths(enrollment, 1);

        await admin.Post("/api/effacement/apprenants", new { scope = "tous", password = AdminPassword }).Ok();

        var students = await admin.GetAsync("/api/apprenants").Ok();
        Assert.Equal(0, students.GetProperty("items").GetArrayLength());
        var promotions = await admin.GetAsync("/api/etablissement/promotions").Ok();
        Assert.Contains(promotions.EnumerateArray(), p => p.GetProperty("id").GetString() == promotion);
    }

    [Fact]
    public async Task The_secretary_confirms_with_her_own_password()
    {
        var admin = await app.InstalledAdmin();
        await admin.Post("/api/utilisateurs", new { userName = "secretaire", displayName = "Secrétaire", role = "Secretariat", password = "Provisoire1" }).Ok();
        var secretary = await app.Login("secretaire", "Provisoire1");
        var promotion = await PromotionAsync(admin);
        var (studentId, enrollmentId) = await StudentAsync(admin, promotion, "MUKENDI");
        await admin.PayMonths(enrollmentId, 1);

        // Le mot de passe de l'administrateur ne vaut pas pour la session de la secrétaire.
        var (refused, _) = await secretary.Post("/api/effacement/paiements", new { scope = "selection", studentIds = new[] { studentId }, password = AdminPassword }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Equal(55000m, await PaidAsync(admin, enrollmentId));

        await secretary.Post("/api/effacement/paiements", new { scope = "selection", studentIds = new[] { studentId }, password = "Provisoire1" }).Ok();
        Assert.Equal(0m, await PaidAsync(admin, enrollmentId));
    }

    [Fact]
    public async Task A_selection_is_required_and_the_scope_must_be_known()
    {
        var admin = await app.InstalledAdmin();
        var (noStudent, _) = await admin.Post("/api/effacement/paiements", new { scope = "selection", studentIds = Array.Empty<string>(), password = AdminPassword }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noStudent);
        var (unknown, _) = await admin.Post("/api/effacement/apprenants", new { scope = "peut-etre", password = AdminPassword }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
    }
}
