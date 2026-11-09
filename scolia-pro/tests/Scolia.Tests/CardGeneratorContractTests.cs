using System.Text.Json;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Documents;

namespace Scolia.Tests;

/// <summary>Impossible de publier des modèles PDF / Word divergents ou
/// d'annoncer un nouveau moteur lorsque seule l'interface a été actualisée.</summary>
public sealed class CardGeneratorContractTests
{
    [Fact]
    public void Le_moteur_charge_et_le_contrat_partage_ont_le_meme_identifiant()
    {
        Assert.Equal(CardGeneratorManifest.Id, DocumentService.CardGeneratorId);
        Assert.Equal("SCOLIA-CARTES-ID1-2026-11-V5", CardGeneratorManifest.Id);
        Assert.Equal(5, CardGeneratorManifest.Revision);
        Assert.Equal(8, CardGeneratorManifest.CardsPerA4);
        Assert.Equal("pdf,docx", CardGeneratorManifest.ExportFormats);
    }

    [Fact]
    public async Task Le_serveur_annonce_le_moteur_reel_du_pdf_et_du_word()
    {
        using var app = new ScoliaApp();
        using var client = app.Client();
        using var response = await client.GetAsync("/api/sante");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DocumentService.CardGeneratorId,
            document.RootElement.GetProperty("cardGenerator").GetString());
    }
}
