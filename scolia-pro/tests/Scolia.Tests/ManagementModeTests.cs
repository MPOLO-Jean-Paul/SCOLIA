using System.Net;
using System.Text.Json;
using Scolia.Domain.School;
using Scolia.Domain.Security;

namespace Scolia.Tests;

public sealed class ManagementModeTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    private static Task<JsonElement> Mode(HttpClient client, string mode, string password = "Scolia2026!") =>
        client.Put("/api/organisation/gestion", new { mode, currentPassword = password }).Ok();

    private async Task<HttpClient> Account(HttpClient admin, string name, string role)
    {
        await admin.Post("/api/utilisateurs", new { userName = name, displayName = name, role, password = "Provisoire1" }).Ok();
        return await app.FirstLogin(name, "Provisoire1", "Personnel2026!");
    }

    [Fact]
    public void Separate_mode_preserves_school_administration_and_personal_roles()
    {
        var secretary = Permissions.For(Role.Secretariat, SchoolManagementMode.Separee);
        foreach (var permission in new[] { Permissions.StudentsRead, Permissions.StudentsWrite, Permissions.StudentFinanceRead,
            Permissions.AcademicsRead, Permissions.AcademicsWrite, Permissions.StaffRead, Permissions.StaffManage,
            Permissions.SettingsWrite, Permissions.UsersManage, Permissions.BackupManage, Permissions.AuditRead })
            Assert.Contains(permission, secretary);
        foreach (var permission in new[] { Permissions.PaymentsRead, Permissions.PaymentsCreate, Permissions.PaymentsCancel,
            Permissions.CashClose, Permissions.ExpensesWrite, Permissions.ReportsRead, Permissions.PilotageRead,
            Permissions.PayrollManage, Permissions.SponsorshipManage }) Assert.DoesNotContain(permission, secretary);
        foreach (var role in new[] { Role.Promoteur, Role.Enseignant, Role.Etudiant })
            Assert.Equal(Permissions.For(role), Permissions.For(role, SchoolManagementMode.Separee));
        Assert.DoesNotContain(Permissions.PayrollManage, Permissions.For(Role.Comptabilite));
        Assert.Contains(Permissions.PayrollManage, Permissions.For(Role.Comptabilite, SchoolManagementMode.Separee));
    }

    [Fact]
    public async Task Existing_school_stays_mixed_and_activation_requires_a_comptable_and_own_code()
    {
        using var admin = await app.InstalledAdmin();
        Assert.Equal("Mixte", (await admin.GetAsync("/api/organisation/gestion").Ok()).GetProperty("mode").GetString());
        foreach (var input in new[] { new { mode = "Separee", currentPassword = "Scolia2026!" }, new { mode = "Mixte", currentPassword = "Incorrect2026!" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/organisation/gestion", input).Fail()).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Put("/api/organisation/gestion", new { mode = 99, currentPassword = "Scolia2026!" }).Fail()).Status);
        await admin.Post("/api/utilisateurs", new { userName = "compta-mode", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        var result = await Mode(admin, "Separee");
        Assert.Equal("Separee", result.GetProperty("managementMode").GetString());
        Assert.Equal(2, result.GetProperty("permissionPolicyVersion").GetInt32());
        await Mode(admin, "Separee");
        var trail = await admin.GetAsync("/api/audit?q=organisation.mode-modifie").Ok();
        Assert.Equal(1, trail.GetProperty("total").GetInt32());
        Assert.DoesNotContain("Scolia2026!", trail.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mode_change_revokes_old_sessions_and_enforces_the_new_rights_before_restoring_mixed()
    {
        using var admin = await app.InstalledAdmin(demo: true);
        using var oldSecretary = await Account(admin, "secretaire-mode", "Secretariat");
        using var oldAccountant = await Account(admin, "comptable-mode", "Comptabilite");
        var enrollment = (await admin.GetAsync("/api/apprenants?taille=1").Ok()).GetProperty("items")[0].GetProperty("enrollmentId").GetString();
        await Mode(admin, "Separee");
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSecretary.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldAccountant.GetAsync("/api/session")).StatusCode);
        using var accountant = await app.Login("comptable-mode", "Personnel2026!");
        foreach (var path in new[] { "/api/paiements", "/api/depenses", "/api/caisse/situation", "/api/rapports/tableau-de-bord", "/api/pilotage/synthese", "/api/prises-en-charge" })
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/paiements", "/api/depenses", "/api/caisse/arretes", "/api/personnel/paie/2026-11/preparation",
            $"/api/inscriptions/{enrollment}/prises-en-charge" })
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post(path, new { })).StatusCode);
        await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        await admin.GetAsync($"/api/inscriptions/{enrollment}/prises-en-charge").Ok();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/inscriptions/{enrollment}/releve.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/organisation/gestion")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Put("/api/organisation/gestion", new { mode = "Mixte", currentPassword = "Personnel2026!" })).StatusCode);
        await accountant.GetAsync("/api/personnel").Ok();
        await accountant.GetAsync("/api/personnel/paie/2026-11").Ok();

        await Mode(admin, "Mixte");
        await admin.GetAsync("/api/rapports/tableau-de-bord").Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await accountant.GetAsync("/api/session")).StatusCode);
        using var mixedAccountant = await app.Login("comptable-mode", "Personnel2026!");
        await mixedAccountant.GetAsync("/api/paiements").Ok();
        Assert.Equal(HttpStatusCode.Forbidden, (await mixedAccountant.GetAsync("/api/personnel/paie/2026-11")).StatusCode);
    }

    [Fact]
    public async Task Separate_accountant_manages_salary_and_sponsorship_while_secretary_keeps_hr_without_changing_salary()
    {
        using var admin = await app.InstalledAdmin(demo: true);
        using var oldAccountant = await Account(admin, "compta-operations", "Comptabilite");
        await Mode(admin, "Separee");
        using var accountant = await app.Login("compta-operations", "Personnel2026!");
        var original = (await accountant.GetAsync("/api/personnel").Ok())[0];
        var id = original.GetProperty("id").GetString();
        var originalSalary = original.GetProperty("baseSalary").GetDecimal();
        Assert.Equal(0m, (await admin.GetAsync("/api/personnel").Ok())[0].GetProperty("baseSalary").GetDecimal());
        object Hr(decimal salary) => new
        {
            lastName = original.GetProperty("lastName").GetString(), middleName = original.GetProperty("middleName").GetString(),
            firstName = original.GetProperty("firstName").GetString(), gender = original.GetProperty("gender").GetString(),
            function = "Fonction administrative actualisée", phone = "0812345678", hireDate = original.GetProperty("hireDate").GetString(),
            baseSalary = salary, isActive = true
        };
        await admin.Put($"/api/personnel/{id}", Hr(0)).Ok();
        Assert.Equal(originalSalary, (await accountant.GetAsync("/api/personnel").Ok())[0].GetProperty("baseSalary").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Put($"/api/personnel/{id}", Hr(50000))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Put($"/api/personnel/{id}", Hr(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Put($"/api/personnel/{id}/salaire", new { baseSalary = 50000m })).StatusCode);
        await accountant.Put($"/api/personnel/{id}/salaire", new { baseSalary = 50000m }).Ok();
        var period = DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await accountant.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok();
        var payroll = (await accountant.GetAsync($"/api/personnel/paie/{period}").Ok()).EnumerateArray().Single(p => p.GetProperty("staffMemberId").GetString() == id);
        var paid = await accountant.Post($"/api/personnel/paie/bulletins/{payroll.GetProperty("id").GetString()}/paiement?mode=Banque", new { }).Ok();
        Assert.Equal(50000m, paid.GetProperty("net").GetDecimal());

        var enrollment = (await admin.GetAsync("/api/apprenants?taille=1").Ok()).GetProperty("items")[0].GetProperty("enrollmentId").GetString();
        var account = await admin.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        var fee = account.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("remaining").GetDecimal() > 0).GetProperty("feeItemId").GetString();
        await accountant.Post($"/api/inscriptions/{enrollment}/prises-en-charge", new
        {
            sponsorName = "Association de test", reason = "Aide validée", effectiveFrom = DateOnly.FromDateTime(DateTime.Today),
            lines = new[] { new { feeItemId = fee, percent = 50m } }
        }).Ok();
        Assert.Single((await admin.GetAsync($"/api/inscriptions/{enrollment}/prises-en-charge").Ok()).EnumerateArray());
    }
}
