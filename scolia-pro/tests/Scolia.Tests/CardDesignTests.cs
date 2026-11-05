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
    public void La_carte_Word_reprend_le_modele_Session_et_les_trois_champs_d_identite()
    {
        var bytes = CardWordWriter.Write(Sample());
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        foreach (var label in new[] { "Session 2026", "ACCÈS AUX EXAMENS", "MATRICULE : ",
            "NOMS : ", "MUKENDI KABEYA GRÂCE", "PROMOTION : ", "INF1", "Contrôle QR", "ÉCOLE DE DÉMONSTRATION" })
            Assert.Contains(System.Security.SecurityElement.Escape(label), xml, StringComparison.Ordinal);
        // Le logo ne doit jamais être remplacé par un nom d'école étranger.
        Assert.DoesNotContain("INSTITUT TECHNIQUE MÉDICAL CŒUR DE DIEU", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Grille_A4_et_taille_physique_des_inserts_sont_exacts()
    {
        Assert.Equal(85.6f, Scolia.Domain.Students.StudentCardRules.CardWidthMm);
        Assert.Equal(53.98f, Scolia.Domain.Students.StudentCardRules.CardHeightMm);
        Assert.True(Scolia.Domain.Students.StudentCardRules.FitsOnPage());
        Assert.Equal("2026", Scolia.Domain.Students.StudentCardRules.SessionYear("NOVEMBRE 2026"));
        Assert.Equal("MUKENDI KABEYA GRÂCE",
            Scolia.Domain.Students.StudentCardRules.FullName("Mukendi", "Kabeya", "Grâce"));

        using var zip = new ZipArchive(new MemoryStream(CardWordWriter.Write(Sample())));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        Assert.Contains("w:gridCol w:w=\"4853\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:trHeight w:val=\"3060\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:top=\"907\"", xml, StringComparison.Ordinal);
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
