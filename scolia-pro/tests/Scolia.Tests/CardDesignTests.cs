using System.IO.Compression;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Infrastructure.Documents;
using Xunit;

namespace Scolia.Tests;

/// <summary>Cartes d'accès : bandeau, identité ligne par ligne avec la classe, bande de couleur sans texte, en Word comme en PDF.</summary>
public class CardDesignTests
{
    private static CardDocument Sample() => new(
        new SchoolCardHeader("République démocratique du Congo", null, null, null, null, "École de démonstration", null, DocumentService.DefaultLogo()),
        "NOVEMBRE 2026", "2026-2027",
        [new CardItem("MUKENDI", "KABEYA", "Grâce", "INF1", "#F5C518", "qr", "INF101"), new CardItem("NGOY", "KAZADI", "Paul", "TLM1", "#1D4ED8", "qr2", "TLM104")]);

    [Fact]
    public void La_carte_Word_porte_le_bandeau_et_l_identite_en_ligne()
    {
        var bytes = CardWordWriter.Write(Sample());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        foreach (var text in new[] { "CARTE D'ACCÈS", "SESSION NOVEMBRE 2026", "NOM : ", "PRÉNOM : ", "CLASSE : ", "INF1", "QR de vérification" })
            Assert.Contains(System.Security.SecurityElement.Escape(text), xml, StringComparison.Ordinal);
        // La bande de couleur reste sans texte.
        Assert.DoesNotContain("MATRICULE", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void La_carte_PDF_est_produite_avec_le_nouveau_dessin()
    {
        var pdf = new DocumentService().StudentCards(Sample());
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Les_cartes_ne_substituent_jamais_un_logo_generique_a_celui_de_l_ecole()
    {
        var doc = Sample() with { Header = Sample().Header with { Logo = null } };
        Assert.Throws<InvalidOperationException>(() => new DocumentService().StudentCards(doc));
        Assert.Throws<InvalidOperationException>(() => CardWordWriter.Write(doc));
    }

    [Fact]
    public void Le_logo_configure_est_embarque_dans_les_cartes_word()
    {
        var logo = DocumentService.DefaultLogo(); // Uniquement une image de test, pas un fallback de production.
        var bytes = CardWordWriter.Write(Sample());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var image = zip.GetEntry("word/media/rImg1.png")!.Open();
        using var memory = new MemoryStream();
        image.CopyTo(memory);
        Assert.Equal(logo, memory.ToArray());
    }

    [Fact]
    public void Les_documents_generaux_gardent_leur_mise_en_page()
    {
        var logo = DocumentService.DefaultLogo();
        Assert.Equal(0x89, logo[0]); // PNG
        var table = new TableDocument(new SchoolHeader("Institut", null, null, null, null, null, "FC", null), "Liste", null, ["A"], [["1"]]);
        Assert.True(new DocumentService().Table(table).Length > 1000);
        Assert.True(new SpreadsheetService().Export(table).Length > 1000);
    }
}
