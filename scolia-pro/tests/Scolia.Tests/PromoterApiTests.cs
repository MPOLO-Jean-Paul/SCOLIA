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
}
