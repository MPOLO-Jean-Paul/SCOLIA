using System.Net;

namespace Scolia.Tests;

/// <summary>Vérification réelle des droits du Promoteur sur le serveur HTTP, pas seulement sur le menu.</summary>
public sealed class PromoterApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Promoteur_ne_peut_acceder_qu_aux_totaux_sans_donnees_nominatives()
    {
        var admin = await app.InstalledAdmin(demo: true);
        var created = await admin.Post("/api/utilisateurs", new
        {
            userName = "direction",
            displayName = "Responsable",
            role = "Promoteur",
            password = "Provisoire1",
        }).Ok();
        Assert.Equal("Promoteur", created.GetProperty("role").GetString());

        var promoteur = await app.Login("direction", "Provisoire1");
        var session = await promoteur.GetAsync("/api/session").Ok();
        Assert.Equal("Promoteur", session.GetProperty("role").GetString());

        var permissions = session.GetProperty("permissions").EnumerateArray()
            .Select(x => x.GetString()).ToList();
        Assert.Single(permissions);
        Assert.Contains("pilotage.lire", permissions);

        var overview = await promoteur.GetAsync("/api/pilotage/synthese").Ok();
        Assert.True(overview.TryGetProperty("activeStudents", out var students));
        Assert.True(students.GetInt32() >= 0);
        Assert.True(overview.TryGetProperty("promotions", out var promotions));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, promotions.ValueKind);

        var json = overview.GetRawText();
        Assert.DoesNotContain("fullName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guardianPhone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("receiptNumber", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matricule", json, StringComparison.OrdinalIgnoreCase);

        foreach (var endpoint in new[]
        {
            "/api/apprenants", "/api/paiements", "/api/rapports/tableau-de-bord",
            "/api/personnel", "/api/utilisateurs", "/api/audit", "/api/sauvegardes",
        })
        {
            var response = await promoteur.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await promoteur.Post("/api/utilisateurs", new { userName = "intrus", displayName = "X", role = "Secretariat", password = "Provisoire1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await promoteur.Post("/api/paiements", new { enrollmentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await promoteur.Post("/api/personnel/" + Guid.NewGuid() + "/signature/Entree", new { })).StatusCode);
        await admin.GetAsync("/api/pilotage/synthese").Ok();
    }
    [Fact]
    public async Task Une_connexion_promoteur_memorise_le_poste_sans_transmettre_ses_droits()
    {
        using var admin = await app.InstalledAdmin();
        await admin.Post("/api/utilisateurs", new
        {
            userName = "promoteur2", displayName = "Direction",
            role = "Promoteur", password = "Provisoire1",
        }).Ok();

        using var promoter = await app.Login("promoteur2", "Provisoire1");
        const string deviceId = "promoteur-telephone-001";
        promoter.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var grant = await promoter.Post("/api/jumelage/mon-appareil", new { }).Ok();
        var grantId = grant.GetProperty("grantId").GetGuid();
        var secret = grant.GetProperty("deviceSecret").GetString()!;
        Assert.NotEmpty(secret);

        using var returning = app.Client();
        returning.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var verified = await returning.Post("/api/jumelage/appareil/verifier", new
            { grantId, deviceId, secret }).Ok();
        Assert.True(verified.GetProperty("expiresUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await returning.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await returning.Post("/api/session/appareil", new { grantId, deviceId, secret })).StatusCode);
        var resumed = await returning.Post("/api/session", new
            { userName = "promoteur2", password = "Provisoire1" }).Ok();
        Assert.Equal("promoteur2", resumed.GetProperty("userName").GetString());
        Assert.Single(resumed.GetProperty("permissions").EnumerateArray());
        Assert.Equal("pilotage.lire", resumed.GetProperty("permissions")[0].GetString());

        admin.DefaultRequestHeaders.Add("X-Scolia-Poste", "poste-secretariat");
        var prohibited = await admin.Post("/api/jumelage/mon-appareil", new { }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, prohibited.Status);
    }

}
