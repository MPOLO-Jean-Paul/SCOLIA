using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Scolia.Tests;

public sealed class SecurityApiTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Installation_happens_once_and_signs_in_the_administrator()
    {
        var admin = await app.InstalledAdmin();
        var session = await admin.GetAsync("/api/session").Ok();
        Assert.Equal("Administrateur", session.GetProperty("role").GetString());
        var (status, _) = await app.Client().Post("/api/installation", new { schoolName = "X" }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Wizard_creates_secretariat_and_accounting_accounts_with_their_own_rights()
    {
        var client = app.Client();
        var before = await client.GetAsync("/api/installation").Ok();
        Assert.False(before.GetProperty("isConfigured").GetBoolean());
        await client.Post("/api/installation", new
        {
            schoolName = "Institut de test", shortName = "IT", city = "Kinshasa", currencyCode = "CDF", currencySymbol = "FC",
            yearLabel = "2026-2027", startMonth = 9, monthCount = 10, adminUserName = "admin", adminDisplayName = "Administrateur",
            adminPassword = "Scolia2026!", loadDemoData = false,
            accounts = new[]
            {
                new { role = "Secretariat", userName = "secretariat", displayName = "Secrétariat", password = "Secretariat1" },
                new { role = "Comptabilite", userName = "comptabilite", displayName = "Comptabilité", password = "Comptable1" },
            },
        }).Ok();
        var after = await client.GetAsync("/api/installation").Ok();
        Assert.True(after.GetProperty("isConfigured").GetBoolean());

        var secretary = await app.Login("secretariat", "Secretariat1");
        Assert.Equal("Secretariat", (await secretary.GetAsync("/api/session").Ok()).GetProperty("role").GetString());
        var accountant = await app.Login("comptabilite", "Comptable1");
        Assert.Equal("Comptabilite", (await accountant.GetAsync("/api/session").Ok()).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/utilisateurs")).StatusCode);
    }

    [Fact]
    public async Task Api_requires_authentication_and_anti_csrf_header()
    {
        await app.InstalledAdmin();
        var anonymous = app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/apprenants")).StatusCode);
        var noHeader = app.CreateClient();
        var response = await noHeader.PostAsJsonAsync("/api/session", new { userName = "admin", password = "Scolia2026!" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Account_is_locked_after_five_failures_without_revealing_existence()
    {
        await app.InstalledAdmin();
        var client = app.Client();
        var unknown = await client.Post("/api/session", new { userName = "inconnu", password = "x" }).Fail();
        for (var i = 0; i < 5; i++)
        {
            var (code, failure) = await client.Post("/api/session", new { userName = "admin", password = "mauvais" }).Fail();
            Assert.True(code == HttpStatusCode.BadRequest, $"Tentative {i + 1} : {(int)code} {failure}");
        }
        var (status, body) = await client.Post("/api/session", new { userName = "admin", password = "Scolia2026!" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("verrouillé", body.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal("Identifiant ou mot de passe incorrect.", unknown.Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Roles_restrict_sensitive_operations()
    {
        var admin = await app.InstalledAdmin(demo: true);
        await admin.Post("/api/utilisateurs", new { userName = "secretaire", displayName = "Secrétaire", role = "Secretariat", password = "Provisoire1" }).Ok();
        var secretary = await app.Login("secretaire", "Provisoire1");
        Assert.True((await secretary.GetAsync("/api/session").Ok()).GetProperty("mustChangePassword").GetBoolean());
        var receipts = await secretary.GetAsync("/api/paiements?pageSize=1").Ok();
        var id = receipts.GetProperty("items")[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await secretary.Post($"/api/paiements/{id}/annulation", new { reason = "Tentative non autorisée" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretary.GetAsync("/api/utilisateurs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretary.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task Editing_a_student_updates_receipts_already_issued()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var receiptId = (await admin.GetAsync("/api/paiements?pageSize=1").Ok()).GetProperty("items")[0].GetProperty("id").GetString();
        var before = await admin.GetAsync($"/api/paiements/{receiptId}").Ok();
        var studentId = before.GetProperty("studentId").GetString();
        var student = await admin.GetAsync($"/api/apprenants/{studentId}").Ok();
        await admin.Put($"/api/apprenants/{studentId}", new
        {
            lastName = "CHANGEMENT", middleName = "NOUVEAU", firstName = "Test", gender = "M", version = student.GetProperty("version").GetInt64(),
            guardianName = "Tuteur de test",
        }).Ok();
        var after = await admin.GetAsync($"/api/paiements/{receiptId}").Ok();
        Assert.Equal("CHANGEMENT NOUVEAU Test", after.GetProperty("studentName").GetString());
        // Sans payeur saisi au paiement, le reçu affiche le tuteur actuel.
        if (before.GetProperty("payerName").ValueKind == JsonValueKind.Null)
            Assert.Equal("Tuteur de test", after.GetProperty("payerName").GetString());
        Assert.Equal(before.GetProperty("amount").GetDecimal(), after.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Payment_by_month_takes_the_monthly_fee_and_refuses_any_other_amount()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var student = (await admin.GetAsync("/api/apprenants?taille=1").Ok()).GetProperty("items")[0];
        var enrollmentId = student.GetProperty("enrollmentId").GetString();
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        var month = account.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("remaining").GetDecimal() > 0);
        var feeItemId = month.GetProperty("feeItemId").GetString();
        var monthly = month.GetProperty("remaining").GetDecimal();

        // Un montant qui ne correspond pas au mois choisi est refusé : aucun règlement partiel.
        var (refused, _) = await admin.Post("/api/paiements", new
        {
            enrollmentId, amount = monthly + 1, feeItemIds = new[] { feeItemId }, method = "Especes", idempotencyKey = Guid.NewGuid().ToString("N"),
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused);

        // Inscription et examen ne se règlent pas par mois.
        var fee = account.GetProperty("lines").EnumerateArray().FirstOrDefault(l => l.GetProperty("kind").GetString() != "Mensualite" && l.GetProperty("remaining").GetDecimal() > 0);
        if (fee.ValueKind != System.Text.Json.JsonValueKind.Undefined)
        {
            var (notMonthly, _) = await admin.Post("/api/paiements", new
            {
                enrollmentId, amount = fee.GetProperty("remaining").GetDecimal(), feeItemIds = new[] { fee.GetProperty("feeItemId").GetString() }, method = "Especes", idempotencyKey = Guid.NewGuid().ToString("N"),
            }).Fail();
            Assert.Equal(HttpStatusCode.BadRequest, notMonthly);
        }

        // Le mois choisi est encaissé à son montant dû, sans saisie du montant.
        var receipt = await admin.Post("/api/paiements", new
        {
            enrollmentId, amount = monthly, feeItemIds = new[] { feeItemId }, method = "Especes", idempotencyKey = Guid.NewGuid().ToString("N"),
        }).Ok();
        Assert.Equal(monthly, receipt.GetProperty("amount").GetDecimal());
        var after = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        var settled = after.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("feeItemId").GetString() == feeItemId);
        Assert.Equal(0m, settled.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task Disabling_a_user_closes_open_sessions()
    {
        var admin = await app.InstalledAdmin();
        var created = await admin.Post("/api/utilisateurs", new { userName = "compta", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        var accountant = await app.Login("compta", "Provisoire1");
        await accountant.GetAsync("/api/session").Ok();
        await admin.Put($"/api/utilisateurs/{created.GetProperty("id").GetString()}", new { displayName = "Comptable", role = "Comptabilite", isActive = false }).Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await accountant.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Last_administrator_cannot_be_removed()
    {
        var admin = await app.InstalledAdmin();
        var me = await admin.GetAsync("/api/session").Ok();
        var (status, _) = await admin.Put($"/api/utilisateurs/{me.GetProperty("id").GetString()}", new { displayName = "Admin", role = "Secretariat", isActive = true }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Security_headers_are_sent()
    {
        var response = await app.Client().GetAsync("/api/sante");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}

public sealed class BusinessApiTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    private static async Task<(string PromotionId, string EnrollmentId, string StudentId)> NewStudent(HttpClient admin, decimal monthly = 55000)
    {
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = monthly,
            oneTimeFees = new[] { new { kind = "Inscription", label = "Frais d'inscription", amount = 20000m, dueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-40)) } },
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var student = await admin.Post("/api/apprenants", new
        {
            identity = new { lastName = "Mukendi", middleName = "Kabeya", firstName = "grâce", gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        Assert.Equal("INF101", student.GetProperty("matricule").GetString());
        Assert.Equal("MUKENDI", student.GetProperty("lastName").GetString());
        Assert.Equal("Grâce", student.GetProperty("firstName").GetString());
        return (promotionId, student.GetProperty("enrollments")[0].GetProperty("id").GetString()!, student.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Duplicate_identity_requires_confirmation()
    {
        var admin = await app.InstalledAdmin();
        var (promotionId, _, _) = await NewStudent(admin);
        var payload = new { identity = new { lastName = "MUKENDI", middleName = "kabeya", firstName = "Grace", gender = "F" }, promotionId, confirmDuplicate = false };
        var (status, body) = await admin.Post("/api/apprenants", payload).Fail();
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("INF101", body.GetProperty("candidates")[0].GetProperty("matricule").GetString());
        var confirmed = await admin.Post("/api/apprenants", payload with { confirmDuplicate = true }).Ok();
        Assert.Equal("INF102", confirmed.GetProperty("matricule").GetString());
    }

    [Fact]
    public async Task Payment_is_allocated_numbered_idempotent_and_cancellable()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        Assert.Equal(550000m, account.GetProperty("totalNet").GetDecimal());

        var receipt = await admin.PayMonths(enrollmentId, 1, key: "cle-unique-1");
        Assert.Matches(@"^RC-\d{4}-000001$", receipt.GetProperty("number").GetString());
        var allocation = receipt.GetProperty("allocations").EnumerateArray().Single();
        Assert.Equal(55000m, allocation.GetProperty("amount").GetDecimal());
        var monthId = allocation.GetProperty("feeItemId").GetString();

        // Même clé : le même reçu revient, sans second encaissement.
        var again = await admin.PayMonths(enrollmentId, 1, key: "cle-unique-1");
        Assert.Equal(receipt.GetProperty("id").GetString(), again.GetProperty("id").GetString());

        var second = await admin.PayMonths(enrollmentId, 1, method: "MobileMoney", reference: "MP123");
        Assert.EndsWith("000002", second.GetProperty("number").GetString(), StringComparison.Ordinal);

        // Un mois déjà réglé ne se paie pas une seconde fois.
        var (paidAgain, body) = await admin.Post("/api/paiements", new { enrollmentId, amount = 55000m, feeItemIds = new[] { monthId }, method = "Especes" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, paidAgain);
        Assert.Contains("déjà réglé", body.GetProperty("title").GetString(), StringComparison.Ordinal);

        // Mobile Money sans référence refusé ; un montant différent du mois choisi refusé.
        var (ids, total) = await admin.OpenMonths(enrollmentId, 1);
        var missingReference = await admin.Post("/api/paiements", new { enrollmentId, amount = total, feeItemIds = ids, method = "MobileMoney" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, missingReference.Status);
        var mismatch = await admin.Post("/api/paiements", new { enrollmentId, amount = total + 1000m, feeItemIds = ids, method = "Especes" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.Status);

        await admin.Post($"/api/paiements/{receipt.GetProperty("id").GetString()}/annulation", new { reason = "Erreur de saisie" }).Ok();
        var after = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        Assert.Equal(55000m, after.GetProperty("totalPaid").GetDecimal());
        var verify = await app.Client().GetAsync($"/api/verifier/{receipt.GetProperty("verificationCode").GetString()}").Ok();
        Assert.Equal("Annulé", verify.GetProperty("status").GetString());

        var pdf = await admin.GetAsync($"/api/paiements/{receipt.GetProperty("id").GetString()}/recu.pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_temporary_renvoi_needs_confirmation_then_becomes_definitive()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        const string Registre = "/api/scolarite/discipline/registre";
        var today = DateOnly.FromDateTime(DateTime.Today);

        var first = await admin.Post(Registre, new { enrollmentId, measure = "RenvoiTemporaire", reason = "Retards répétés", decisionDate = today, firstDay = today, lastDay = today.AddDays(2) }).Ok();
        Assert.Equal("Renvoi temporaire", first.GetProperty("measure").GetString());
        Assert.Equal(3, first.GetProperty("days").GetInt32());
        Assert.Equal("Suspendu", first.GetProperty("effectiveStatus").GetString());

        // Deuxième renvoi temporaire de l'année : avertissement tant que la confirmation n'est pas donnée.
        var (blocked, warning) = await admin.Post(Registre, new { enrollmentId, measure = "RenvoiTemporaire", reason = "Nouvelle absence", decisionDate = today, firstDay = today.AddDays(5), lastDay = today.AddDays(6) }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, blocked);
        Assert.Contains("Confirmez", warning.GetProperty("title").GetString(), StringComparison.Ordinal);

        var definitive = await admin.Post(Registre, new { enrollmentId, measure = "RenvoiTemporaire", reason = "Nouvelle absence", decisionDate = today, firstDay = today.AddDays(5), lastDay = today.AddDays(6), confirmDefinitive = true }).Ok();
        Assert.Equal("Renvoi définitif", definitive.GetProperty("measure").GetString());
        Assert.True(definitive.GetProperty("isAutomatic").GetBoolean());
        Assert.Equal("Sorti", definitive.GetProperty("effectiveStatus").GetString());

        // Un apprenant renvoyé définitivement ne reçoit plus de sanction, seulement une convocation.
        var (refused, _) = await admin.Post(Registre, new { enrollmentId, measure = "RenvoiTemporaire", reason = "Encore une fois", decisionDate = today, firstDay = today.AddDays(10), lastDay = today.AddDays(11) }).Fail();
        Assert.Equal(HttpStatusCode.Conflict, refused);
    }

    [Fact]
    public async Task Convocation_reception_rules_and_printable_document()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        const string Registre = "/api/scolarite/discipline/registre";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var appointment = DateTime.Now.AddDays(3).Date.AddHours(10);

        var convocation = await admin.Post(Registre, new
        {
            enrollmentId, measure = "Convocation", reason = "<b>Comportement</b> en classe", decisionDate = today, appointmentAt = appointment, requestKey = "convocation-1",
        }).Ok();
        Assert.Equal("Convocation", convocation.GetProperty("measure").GetString());
        Assert.Equal("Actif", convocation.GetProperty("effectiveStatus").GetString());
        var id = convocation.GetProperty("id").GetString();

        // Répétition du même enregistrement : une seule convocation.
        var repeated = await admin.Post(Registre, new
        {
            enrollmentId, measure = "Convocation", reason = "<b>Comportement</b> en classe", decisionDate = today, appointmentAt = appointment, requestKey = "convocation-1",
        }).Ok();
        Assert.Equal(id, repeated.GetProperty("id").GetString());

        // Absence du parent refusée avant le rendez-vous ; réception avec compte rendu obligatoire.
        var (early, _) = await admin.Put($"{Registre}/{id}/reception", new { reception = "Absent", note = "Non présent" }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, early);
        var (noNote, _) = await admin.Put($"{Registre}/{id}/reception", new { reception = "Recu", note = " " }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noNote);
        var received = await admin.Put($"{Registre}/{id}/reception", new { reception = "Recu", note = "Parent reçu et informé" }).Ok();
        Assert.Equal("Parent reçu", received.GetProperty("reception").GetString());

        // La convocation s'imprime avec les textes échappés.
        var html = await admin.GetAsync($"{Registre}/{id}/convocation");
        var page = await html.Content.ReadAsStringAsync();
        Assert.Contains("CONVOCATION DU PARENT", page, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Comportement&lt;/b&gt;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Comportement</b>", page, StringComparison.Ordinal);

        var csv = await admin.GetAsync($"{Registre}/export.csv");
        Assert.Contains("Matricule;Nom complet", await csv.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_a_definitive_renvoi_keeps_the_record_and_restores_the_status()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        const string Registre = "/api/scolarite/discipline/registre";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var definitive = await admin.Post(Registre, new { enrollmentId, measure = "RenvoiDefinitif", reason = "Faute grave", decisionDate = today, confirmDefinitive = true }).Ok();
        Assert.Equal("Sorti", definitive.GetProperty("effectiveStatus").GetString());
        var id = definitive.GetProperty("id").GetString();

        var (noReason, _) = await admin.Post($"{Registre}/{id}/annulation", new { reason = "  " }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        var cancelled = await admin.Post($"{Registre}/{id}/annulation", new { reason = "Décision prise par erreur" }).Ok();
        Assert.Equal("Annulée", cancelled.GetProperty("state").GetString());
        Assert.Equal("Actif", cancelled.GetProperty("effectiveStatus").GetString());

        var history = await admin.GetAsync($"/api/scolarite/discipline/inscriptions/{enrollmentId}/historique").Ok();
        Assert.Single(history.EnumerateArray());
    }

    [Fact]
    public async Task Revision_rises_after_each_committed_write()
    {
        var admin = await app.InstalledAdmin();
        var before = (await admin.GetAsync("/api/revision").Ok()).GetProperty("revision").GetInt64();
        await NewStudent(admin);
        var after = (await admin.GetAsync("/api/revision").Ok()).GetProperty("revision").GetInt64();
        Assert.True(after > before);
    }

    [Fact]
    public async Task Concurrent_payments_never_pay_a_month_twice()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin, monthly: 10000);
        var (ids, _) = await admin.OpenMonths(enrollmentId, 10);
        // Douze versements simultanés sur les dix mois : chaque mois ne passe qu'une fois.
        var tasks = Enumerable.Range(0, 12)
            .Select(i => admin.Post("/api/paiements", new { enrollmentId, amount = 10000m, method = "Especes", feeItemIds = new[] { ids[i % 10] } })).ToList();
        var responses = await Task.WhenAll(tasks);
        Assert.Equal(10, responses.Count(r => r.IsSuccessStatusCode));
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        Assert.Equal(100000m, account.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(0m, account.GetProperty("balance").GetDecimal()); // Les frais d'inscription ne font pas partie du suivi de scolarité.
    }

    [Fact]
    public async Task Sponsorship_reduces_balance_and_protects_paid_items()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        var monthly = account.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("label").GetString() != "Frais d'inscription").Select(l => l.GetProperty("feeItemId").GetString()).ToList();
        // Un mois se paie en entier : aucun règlement partiel n'est enregistré.
        await admin.Post("/api/paiements", new { enrollmentId, amount = 55000m, method = "Especes", feeItemIds = new[] { monthly[0] } }).Ok();

        var refused = await admin.Post($"/api/inscriptions/{enrollmentId}/prises-en-charge", new
        {
            sponsorName = "Bourse", reason = "Excellence", effectiveFrom = DateOnly.FromDateTime(DateTime.Today), lines = monthly.Select(id => new { feeItemId = id, percent = 50m }),
        }).Fail();
        Assert.Contains("déjà été payé", refused.Body.GetProperty("title").GetString(), StringComparison.Ordinal);

        await admin.Post($"/api/inscriptions/{enrollmentId}/prises-en-charge", new
        {
            sponsorName = "Bourse", reason = "Excellence", effectiveFrom = DateOnly.FromDateTime(DateTime.Today), lines = monthly.Skip(1).Select(id => new { feeItemId = id, percent = 50m }),
        }).Ok();
        var after = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        Assert.Equal(9 * 27500m, after.GetProperty("totalCovered").GetDecimal());
    }

    [Fact]
    public async Task Promotion_change_carries_payments_to_equivalent_months()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        await admin.PayMonths(enrollmentId, 1);
        var target = await admin.Post("/api/etablissement/promotions", new
        {
            code = "ACC1", name = "Accoucheuse", level = 1, isActive = true, monthlyFee = 60000m,
            oneTimeFees = new[] { new { kind = "Inscription", label = "Frais d'inscription", amount = 20000m, dueDate = DateOnly.FromDateTime(DateTime.Today) } },
        }).Ok();
        await admin.Put($"/api/inscriptions/{enrollmentId}/promotion", new { promotionId = target.GetProperty("id").GetString() }).Ok();
        var account = await admin.GetAsync($"/api/inscriptions/{enrollmentId}/compte").Ok();
        Assert.Equal("ACC1", account.GetProperty("promotionCode").GetString());
        Assert.Equal(55000m, account.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(600000m - 55000m, account.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Monthly_fee_cannot_drop_below_amounts_already_paid()
    {
        var admin = await app.InstalledAdmin();
        var (promotionId, enrollmentId, _) = await NewStudent(admin);
        await admin.PayMonths(enrollmentId, 1);
        var (status, _) = await admin.Put($"/api/etablissement/promotions/{promotionId}", new { code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 30000m, oneTimeFees = new[] { new { kind = "Inscription", label = "Frais d'inscription", amount = 20000m, dueDate = DateOnly.FromDateTime(DateTime.Today) } } }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        await admin.Put($"/api/etablissement/promotions/{promotionId}", new { code = "INF1", name = "Première année", level = 1, isActive = true, monthlyFee = 65000m, oneTimeFees = new[] { new { kind = "Inscription", label = "Frais d'inscription", amount = 20000m, dueDate = DateOnly.FromDateTime(DateTime.Today) } } }).Ok();
    }

    [Fact]
    public async Task Cash_closing_locks_the_period()
    {
        var admin = await app.InstalledAdmin();
        var (_, enrollmentId, _) = await NewStudent(admin);
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        await admin.PayMonths(enrollmentId, 1, date: yesterday);
        await admin.Post("/api/depenses", new { category = "Fournitures", beneficiary = "Librairie", description = "Craies", amount = 5000m, method = "Especes", date = yesterday }).Ok();
        var situation = await admin.GetAsync($"/api/caisse/situation?date={yesterday:yyyy-MM-dd}").Ok();
        Assert.Equal(50000m, situation.GetProperty("expectedCash").GetDecimal());
        var (_, noNote) = await admin.Post("/api/caisse/arretes", new { date = yesterday, countedCash = 49000m }).Fail();
        Assert.Equal("note", noNote.GetProperty("code").GetString());
        var closing = await admin.Post("/api/caisse/arretes", new { date = yesterday, countedCash = 49000m, note = "Manque de 1 000 FC constaté" }).Ok();
        Assert.Equal(-1000m, closing.GetProperty("difference").GetDecimal());
        var (ids, total) = await admin.OpenMonths(enrollmentId, 1);
        var (_, locked) = await admin.Post("/api/paiements", new { enrollmentId, amount = total, feeItemIds = ids, method = "Especes", date = yesterday }).Fail();
        Assert.Equal("date", locked.GetProperty("code").GetString());
        var overspend = await admin.Post("/api/depenses", new { category = "Divers", beneficiary = "X", description = "Trop", amount = 999999m, method = "Especes" }).Fail();
        Assert.Contains("insuffisantes", overspend.Body.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_chain_detects_tampering()
    {
        var admin = await app.InstalledAdmin();
        await NewStudent(admin);
        var ok = await admin.GetAsync("/api/audit/verification").Ok();
        Assert.True(ok.GetProperty("isIntact").GetBoolean());
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(app.DataDirectory, "scolia.db")}"))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE JournalAudit SET Summary = 'falsifié' WHERE Id = 2";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        var broken = await admin.GetAsync("/api/audit/verification").Ok();
        Assert.False(broken.GetProperty("isIntact").GetBoolean());
        Assert.Equal(2, broken.GetProperty("brokenAtId").GetInt64());
    }

    [Fact]
    public async Task Demo_dashboard_reports_and_exports_work()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var dashboard = await admin.GetAsync("/api/rapports/tableau-de-bord").Ok();
        Assert.Equal(128, dashboard.GetProperty("activeStudents").GetInt32());
        Assert.InRange(dashboard.GetProperty("recoveryRate").GetDecimal(), 1, 100);
        var debtors = await admin.GetAsync("/api/rapports/debiteurs").Ok();
        Assert.True(debtors.GetArrayLength() > 0);
        Assert.StartsWith("https://wa.me/243", debtors[0].GetProperty("whatsAppLink").GetString(), StringComparison.Ordinal);
        var xlsx = await admin.GetAsync("/api/rapports/debiteurs/export?format=xlsx");
        using (var book = new XLWorkbook(await xlsx.Content.ReadAsStreamAsync())) Assert.True(book.Worksheet(1).LastRowUsed()!.RowNumber() > 6);
        var promotions = await admin.GetAsync("/api/etablissement/promotions").Ok();
        var pdf = await admin.GetAsync($"/api/rapports/liste-classe/{promotions[0].GetProperty("id").GetString()}");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var results = await admin.GetAsync($"/api/scolarite/resultats?promotionId={promotions[0].GetProperty("id").GetString()}").Ok();
        Assert.Equal(1, results.GetProperty("rows")[0].GetProperty("rank").GetInt32());
        var hits = await admin.GetAsync("/api/recherche?q=kasongo").Ok();
        Assert.True(hits.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Attendance_grades_and_payroll_flows()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var promotion = (await admin.GetAsync("/api/etablissement/promotions").Ok())[0].GetProperty("id").GetString();
        var sheet = await admin.GetAsync($"/api/scolarite/presences?promotionId={promotion}&date={DateTime.Today:yyyy-MM-dd}").Ok();
        var rows = sheet.GetProperty("rows").EnumerateArray().Select(r => new { enrollmentId = r.GetProperty("enrollmentId").GetString(), status = "Présent", note = (string?)null }).ToList();
        await admin.Put($"/api/scolarite/presences?promotionId={promotion}&date={DateTime.Today:yyyy-MM-dd}", rows).Ok();
        var future = await admin.Put($"/api/scolarite/presences?promotionId={promotion}&date={DateTime.Today.AddDays(3):yyyy-MM-dd}", rows).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, future.Status);

        var period = DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(5, (await admin.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok()).GetProperty("crees").GetInt32());
        Assert.Equal(0, (await admin.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok()).GetProperty("crees").GetInt32());
        var entries = await admin.GetAsync($"/api/personnel/paie/{period}").Ok();
        var id = entries[0].GetProperty("id").GetString();
        await admin.Put($"/api/personnel/paie/bulletins/{id}", new { allowances = 10000m, deductions = 0m, advances = 0m }).Ok();
        var paid = await admin.Post($"/api/personnel/paie/bulletins/{id}/paiement", new { }).Ok();
        Assert.Equal("Paye", paid.GetProperty("status").GetString());
        var expenses = await admin.GetAsync("/api/depenses?categorie=Salaires").Ok();
        Assert.Equal(1, expenses.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Payroll_change_is_traced_with_the_acting_user()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var period = DateTime.Today.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await admin.Post($"/api/personnel/paie/{period}/preparation", new { }).Ok();
        var id = (await admin.GetAsync($"/api/personnel/paie/{period}").Ok())[0].GetProperty("id").GetString();
        await admin.Put($"/api/personnel/paie/bulletins/{id}", new { allowances = 15000m, deductions = 0m, advances = 0m }).Ok();

        var trail = (await admin.GetAsync("/api/audit?q=paie.modifiee").Ok()).GetRawText();
        Assert.Contains("paie.modifiee", trail, StringComparison.Ordinal);
        Assert.Contains("\"userName\":\"Administrateur\"", trail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_journal_exports_as_csv_and_the_export_is_traced()
    {
        var admin = await app.InstalledAdmin();
        await admin.Post("/api/sauvegardes", new { }).Ok();
        var response = await admin.GetAsync("/api/audit/export?format=csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xEF, bytes[0]); // BOM UTF-8 : Excel affiche correctement les accents
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Numéro;Date UTC;Utilisateur;Action", text, StringComparison.Ordinal);
        Assert.Contains("sauvegarde.creee", text, StringComparison.Ordinal);
        Assert.Contains("audit.exporte", (await admin.GetAsync("/api/audit?q=audit.exporte").Ok()).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Untraced_write_is_refused_by_the_database_context()
    {
        await app.InstalledAdmin();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Scolia.Infrastructure.Persistence.ScoliaDbContext>();
        var user = await db.Users.FirstAsync();
        user.DisplayName = "Modifié sans trace";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Legacy_workbook_import_is_simulated_then_applied_once()
    {
        var admin = await app.InstalledAdmin();
        using var stream = new MemoryStream();
        using (var book = new XLWorkbook())
        {
            var p = book.AddWorksheet("PROMOTIONS");
            p.Cell(4, 1).Value = "Code";
            p.Cell(5, 1).Value = "INF1"; p.Cell(5, 2).Value = "Première année"; p.Cell(5, 3).Value = "Infirmière"; p.Cell(5, 4).Value = "1"; p.Cell(5, 6).Value = 55000; p.Cell(5, 8).Value = "Active";
            var a = book.AddWorksheet("APPRENANTS");
            a.Cell(4, 1).Value = "Matricule";
            object[][] students = [["INF105", "KALONJI", "MBOMBO", "Ruth", "F", "0812345678"], ["INF106", "NGOY", "KAZADI", "Daniel", "M", "812345679"]];
            for (var i = 0; i < students.Length; i++)
            {
                for (var c = 0; c < 5; c++) a.Cell(5 + i, c + 1).Value = students[i][c].ToString();
                a.Cell(5 + i, 8).Value = "INF1"; a.Cell(5 + i, 10).Value = students[i][5].ToString(); a.Cell(5 + i, 14).Value = "Actif";
            }
            var pay = book.AddWorksheet("PAIEMENTS");
            pay.Cell(4, 1).Value = "ID";
            object[][] payments = [[1, "GKWCB", "INF105", "Septembre", 55000], [2, "GKWCB", "INF105", "Octobre", 20000], [3, "5MACY", "INF106", "Septembre", 55000]];
            for (var i = 0; i < payments.Length; i++)
            {
                pay.Cell(5 + i, 1).Value = (int)payments[i][0]; pay.Cell(5 + i, 2).Value = DateTime.Today.AddDays(-10); pay.Cell(5 + i, 3).Value = payments[i][1].ToString();
                pay.Cell(5 + i, 4).Value = payments[i][2].ToString(); pay.Cell(5 + i, 6).Value = "INF1"; pay.Cell(5 + i, 7).Value = payments[i][3].ToString(); pay.Cell(5 + i, 9).Value = (int)payments[i][4];
            }
            book.SaveAs(stream);
        }
        async Task<JsonElement> Import(bool apply)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(stream.ToArray()), "fichiers", "SYSTEME_SECRETARIAT.xlsx");
            return await admin.PostAsync($"/api/import/scolia?appliquer={apply}", form).Ok();
        }
        var simulation = await Import(false);
        Assert.Equal(2, simulation.GetProperty("students").GetInt32());
        Assert.Equal(2, simulation.GetProperty("receipts").GetInt32());
        Assert.Equal(0, (await admin.GetAsync("/api/apprenants?statut=").Ok()).GetProperty("total").GetInt32());

        var applied = await Import(true);
        Assert.True(applied.GetProperty("applied").GetBoolean());
        var list = await admin.GetAsync("/api/apprenants?q=INF106").Ok();
        Assert.Equal("0812345679", list.GetProperty("items")[0].GetProperty("guardianPhone").GetString()); // Zéro initial restauré.
        var receipts = await admin.GetAsync("/api/paiements").Ok();
        Assert.Equal(130000m, receipts.GetProperty("sum").GetDecimal());

        var again = await Import(true);
        Assert.Equal(0, again.GetProperty("receipts").GetInt32());
        Assert.Equal(130000m, (await admin.GetAsync("/api/paiements").Ok()).GetProperty("sum").GetDecimal());
    }

    [Fact]
    public async Task Backup_is_created_verified_and_downloadable()
    {
        var admin = await app.InstalledAdmin();
        var backup = await admin.Post("/api/sauvegardes", new { }).Ok();
        var name = backup.GetProperty("name").GetString()!;
        Assert.True((await admin.GetAsync("/api/sauvegardes/integrite").Ok()).GetProperty("intacte").GetBoolean());
        var download = await admin.GetAsync($"/api/sauvegardes/{name}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString((await download.Content.ReadAsByteArrayAsync())[..15]), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/sauvegardes/..%2F..%2Fscolia.db")).StatusCode);
    }

    [Fact]
    public async Task Restore_brings_back_the_saved_state_from_a_standalone_file()
    {
        var admin = await app.InstalledAdmin();
        var name = (await admin.Post("/api/sauvegardes", new { }).Ok()).GetProperty("name").GetString()!;
        var folder = Path.Combine(app.DataDirectory, "sauvegardes");
        // Une sauvegarde est un fichier unique : rien d'autre à copier sur la clé USB.
        Assert.False(File.Exists(Path.Combine(folder, name + "-wal")));
        Assert.False(File.Exists(Path.Combine(folder, name + "-shm")));

        await admin.Post("/api/utilisateurs", new { userName = "compta", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        Assert.Contains("compta", (await admin.GetAsync("/api/utilisateurs").Ok()).GetRawText(), StringComparison.Ordinal);

        await admin.Post($"/api/sauvegardes/{name}/restauration", new { }).Ok();
        Assert.DoesNotContain("compta", (await admin.GetAsync("/api/utilisateurs").Ok()).GetRawText(), StringComparison.Ordinal);
        Assert.Contains((await admin.GetAsync("/api/sauvegardes").Ok()).EnumerateArray(), b => b.GetProperty("name").GetString()!.Contains("avantrestauration", StringComparison.Ordinal));
        Assert.True((await admin.GetAsync("/api/audit/verification").Ok()).GetProperty("isIntact").GetBoolean());
        Assert.Contains("sauvegarde.restauree", (await admin.GetAsync("/api/audit").Ok()).GetRawText(), StringComparison.Ordinal);

        await using var connection = new SqliteConnection($"Data Source={Path.Combine(app.DataDirectory, "scolia.db")}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", await command.ExecuteScalarAsync() as string);
    }
}
