using System.IO.Compression;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Infrastructure.Documents;
using Xunit;

namespace Scolia.Tests;

/// <summary>Cartes d'accès : format ID-1, en-tête personnalisé, session soulignée, identité et QR en Word et PDF.</summary>
public class CardDesignTests
{
    private static CardDocument Sample() => new(
        new SchoolCardHeader("République démocratique du Congo", null, null, null, null, "École de démonstration", null, DocumentService.DefaultLogo()),
        "NOVEMBRE 2026", "2026-2027",
        [new CardItem("MUKENDI", "KABEYA", "Grâce", "INF1", "#F5C518", "qr", "INF101"), new CardItem("NGOY", "KAZADI", "Paul", "TLM1", "#1D4ED8", "qr2", "TLM104")]);

    private static string WordXml(CardDocument document)
    {
        using var zip = new ZipArchive(new MemoryStream(CardWordWriter.Write(document)));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Esc(string text) => System.Security.SecurityElement.Escape(text);

    [Fact]
    public void La_carte_Word_reprend_le_modele_Session_et_les_trois_champs_d_identite()
    {
        var xml = WordXml(Sample());
        foreach (var label in new[] { "Session 2026", "MATRICULE : INF101", "NOMS : MUKENDI KABEYA Grâce", "PROMOTION : INF1", "ÉCOLE DE DÉMONSTRATION" })
            Assert.Contains(Esc(label), xml, StringComparison.Ordinal);
        // Session soulignée ; seul le QR est posé dans chaque carte : ni logo ni cachet imprimés.
        Assert.Contains("<w:u w:val=\"single\"", xml, StringComparison.Ordinal);
        Assert.Equal(Sample().Cards.Count, System.Text.RegularExpressions.Regex.Count(xml, "<w:drawing>"));
        Assert.DoesNotContain("INSTITUT TECHNIQUE MÉDICAL CŒUR DE DIEU", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void L_en_tete_reprend_le_nom_le_service_et_les_contacts_configures()
    {
        var configured = Sample() with
        {
            Header = new SchoolCardHeader(
                "République démocratique du Congo", "Ministère de l'Enseignement", "Direction des études", "Division du budget",
                "Lubumbashi", "Université de démonstration", "Avenue de l'Exemple 12", DocumentService.DefaultLogo(),
                "+243 812 345 678", "secretariat@ecole.example", "www.ecole.example", "Servir et soigner")
        };
        var xml = WordXml(configured);
        foreach (var item in new[] { "UNIVERSITÉ DE DÉMONSTRATION", "DIRECTION DES ÉTUDES · DIVISION DU BUDGET",
            "Tél. +243 812 345 678", "secretariat@ecole.example", "www.ecole.example",
            "Avenue de l'Exemple 12, Lubumbashi, République démocratique du Congo" })
            Assert.Contains(Esc(item), xml, StringComparison.Ordinal);
        // Le ministère ne sert que si aucun service n'est configuré.
        Assert.DoesNotContain("MINISTÈRE DE L'ENSEIGNEMENT", xml, StringComparison.Ordinal);
        Assert.Equal("MINISTÈRE DE L'ENSEIGNEMENT", CardDesign.Department(configured.Header with { Direction = null, Division = null }));

        var pdf = new DocumentService().StudentCards(configured);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Les_noms_suivent_la_carte_modele_et_restent_dans_la_carte()
    {
        var card = new CardItem("MUKENDI", "TSHIDIBI", "DANNY", "L3 MSI", "#000000", "qr", "23MT355SI");
        Assert.Equal("MUKENDI TSHIDIBI Danny", CardDesign.DisplayName(card));
        Assert.Equal(CardDesign.IdentityMaxPt, CardDesign.IdentityPt(card));
        var longName = card with { LastName = "KALOMBO WA NKONGOLO", MiddleName = "MUTOMBO KABEYA", FirstName = "Jean-Pierre Emmanuel" };
        Assert.True(CardDesign.IdentityPt(longName) < CardDesign.IdentityMaxPt);
        Assert.True(CardDesign.IdentityPt(longName) >= CardDesign.IdentityMinPt);
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
    public void Ni_logo_ni_cachet_ne_sont_imprimes_ils_sont_apposes_a_la_main()
    {
        // Sans logo configuré, les cartes se font quand même : le logo et le cachet sont apposés après l'impression.
        var doc = Sample() with { Header = Sample().Header with { Logo = null } };
        Assert.Equal("%PDF", Encoding.ASCII.GetString(new DocumentService().StudentCards(doc), 0, 4));
        using var zip = new ZipArchive(new MemoryStream(CardWordWriter.Write(Sample())));
        // Seules images : un QR par carte, jamais le logo de l'établissement.
        var logo = DocumentService.DefaultLogo();
        var media = zip.Entries.Where(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal)).ToList();
        Assert.Equal(Sample().Cards.Count, media.Count);
        foreach (var entry in media)
        {
            using var image = entry.Open();
            using var memory = new MemoryStream();
            image.CopyTo(memory);
            Assert.NotEqual(logo, memory.ToArray());
        }
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
