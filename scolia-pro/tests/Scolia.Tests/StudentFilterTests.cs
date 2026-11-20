using System.Net;
using System.Text.Json;

namespace Scolia.Tests;

/// <summary>Tri de la liste des apprenants par sexe, par situation de paiement et par mois.</summary>
public sealed class StudentFilterTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    private static async Task<string> Enroll(HttpClient admin, string promotionId, string lastName, string firstName, string gender)
    {
        await admin.Post("/api/apprenants", new
        {
            identity = new { lastName, middleName = "Test", firstName, birthPlace = "Kinshasa", birthDate = new DateOnly(2007, 3, 14), gender, guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId, confirmDuplicate = false,
        }).Ok();
        var found = await admin.GetAsync($"/api/apprenants?q={lastName}").Ok();
        return found.GetProperty("items")[0].GetProperty("enrollmentId").GetString()!;
    }

    private static async Task<string[]> Names(HttpClient admin, string query)
    {
        var page = await admin.GetAsync("/api/apprenants?" + query).Ok();
        return page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("lastName").GetString()!.ToUpperInvariant()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Students_are_sorted_by_gender_payment_situation_and_specific_month()
    {
        var admin = await app.InstalledAdmin();
        var promotion = await admin.Post("/api/etablissement/promotions", new
        {
            code = "TRI1", name = "Tri", level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
        }).Ok();
        var promotionId = promotion.GetProperty("id").GetString()!;
        var payer = await Enroll(admin, promotionId, "Payeuse", "Grâce", "F");
        await Enroll(admin, promotionId, "Absent", "Jean", "M");
        var sponsored = await Enroll(admin, promotionId, "Boursiere", "Marie", "F");

        await admin.PayMonths(payer, 2);
        var account = await admin.GetAsync($"/api/inscriptions/{sponsored}/compte").Ok();
        var monthly = account.GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("kind").GetString() == "Mensualite").Select(l => l.GetProperty("feeItemId").GetString()).ToList();
        await admin.Post($"/api/inscriptions/{sponsored}/prises-en-charge", new
        {
            sponsorName = "Bourse", reason = "Excellence", effectiveFrom = DateOnly.FromDateTime(DateTime.Today),
            lines = monthly.Select(id => new { feeItemId = id, percent = 100m }),
        }).Ok();

        // Sexe
        Assert.Equal(["BOURSIERE", "PAYEUSE"], await Names(admin, "sexe=F"));
        Assert.Equal(["ABSENT"], await Names(admin, "sexe=M"));

        // Situation de paiement sur l'année : les pris en charge ne sont ni « en ordre » ni « en retard ».
        Assert.Equal(["ABSENT"], await Names(admin, "paiement=jamais_paye"));
        Assert.Equal(["BOURSIERE"], await Names(admin, "paiement=pris_en_charge"));
        Assert.DoesNotContain("BOURSIERE", await Names(admin, "paiement=en_retard"));
        Assert.DoesNotContain("BOURSIERE", await Names(admin, "paiement=en_ordre"));
        Assert.Contains("ABSENT", await Names(admin, "paiement=en_retard"));

        // Mois précis : le premier mois est payé par « Payeuse », jamais payé par « Absent ».
        var years = await admin.GetAsync("/api/etablissement/annees").Ok();
        var current = years.EnumerateArray().First(y => y.GetProperty("isCurrent").GetBoolean());
        var periods = current.GetProperty("periods").EnumerateArray().OrderBy(p => p.GetProperty("order").GetInt32()).ToList();
        var first = periods[0].GetProperty("id").GetString();
        var last = periods[^1].GetProperty("id").GetString();
        Assert.Equal(["PAYEUSE"], await Names(admin, $"paiement=en_ordre&periodeId={first}"));
        Assert.Equal(["ABSENT"], await Names(admin, $"paiement=jamais_paye&periodeId={first}"));
        Assert.Equal(["ABSENT", "PAYEUSE"], await Names(admin, $"paiement=en_retard&periodeId={last}"));

        // Combinaison avec la promotion, et total paginé cohérent.
        var page = await admin.GetAsync($"/api/apprenants?promotionId={promotionId}&paiement=jamais_paye&taille=1").Ok();
        Assert.Equal(1, page.GetProperty("total").GetInt32());

        // Un mois sans situation de paiement, ou une valeur inconnue, est refusé clairement.
        var (status, _) = await admin.GetAsync($"/api/apprenants?periodeId={first}").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var (unknown, _) = await admin.GetAsync("/api/apprenants?sexe=X").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        var (badPayment, _) = await admin.GetAsync("/api/apprenants?paiement=peut-etre").Fail();
        Assert.Equal(HttpStatusCode.BadRequest, badPayment);
    }
}
