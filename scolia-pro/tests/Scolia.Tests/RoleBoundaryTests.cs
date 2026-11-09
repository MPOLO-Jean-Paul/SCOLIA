using System.Net;

namespace Scolia.Tests;

/// <summary>
/// Limites entre les rôles relevées par l'audit visuel : la comptabilité gère les paiements et les sauvegardes,
/// mais n'efface pas les dossiers des apprenants, n'importe pas de listes et ne restaure pas la base ;
/// le promoteur ne voit que la synthèse de pilotage.
/// </summary>
public sealed class RoleBoundaryTests : IDisposable
{
    private readonly ScoliaApp app = new();

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Accounting_and_promoter_accounts_stay_within_their_role()
    {
        var admin = await app.InstalledAdmin(demo: true);
        await admin.Post("/api/utilisateurs", new { userName = "compta", displayName = "Comptable", role = "Comptabilite", password = "Provisoire1" }).Ok();
        await admin.Post("/api/utilisateurs", new { userName = "promo", displayName = "Promoteur", role = "Promoteur", password = "Provisoire1" }).Ok();
        var accountant = await app.FirstLogin("compta", "Provisoire1", "Compta2026!");
        var promoter = await app.FirstLogin("promo", "Provisoire1", "Promo2026!");

        // Comptabilité : sauvegardes oui ; effacement des dossiers, import et restauration non.
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/sauvegardes")).StatusCode);
        var (erase, _) = await accountant.Post("/api/effacement/apprenants", new { scope = "tous", password = "Compta2026!" }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, erase);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/import/modele")).StatusCode);
        var (restore, _) = await accountant.Post("/api/sauvegardes/scolia-x.db/restauration", new { }).Fail();
        Assert.Equal(HttpStatusCode.Forbidden, restore);
        Assert.NotEmpty((await admin.GetAsync("/api/apprenants").Ok()).GetProperty("items").EnumerateArray());

        // Promoteur : la synthèse seulement, aucun tableau de bord détaillé ni rapport nominatif.
        Assert.Equal(HttpStatusCode.OK, (await promoter.GetAsync("/api/pilotage/synthese")).StatusCode);
        foreach (var url in new[] { "/api/rapports/tableau-de-bord", "/api/rapports/aujourdhui", "/api/apprenants", "/api/paiements", "/api/sauvegardes" })
            Assert.Equal(HttpStatusCode.Forbidden, (await promoter.GetAsync(url)).StatusCode);
    }
}
