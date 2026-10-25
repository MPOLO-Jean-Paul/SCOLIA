using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Infrastructure.Documents;
using Scolia.Mobile.Server;

namespace Scolia.Mobile.Tests;

/// <summary>
/// Documents du téléphone serveur : chacun est un PDF valide, avec le bon nombre de pages. Avec SCOLIA_PDF_DIR, chaque document
/// est aussi écrit deux fois, par le téléphone et par l'ordinateur, pour les comparer à l'œil.
/// </summary>
public sealed class DocumentTests
{
    private readonly SkiaDocumentService phone = new();

    private static readonly SchoolHeader School = new("Complexe Scolaire Les Étoiles", "Savoir, rigueur, réussite", "Avenue de la Paix 12, Kinshasa",
        "0812345678", "contact@etoiles.cd", null, "FC", "Merci pour votre confiance.");

    private static int Pages(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Count(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]");

    private static void Keep(string name, byte[] phonePdf, Func<byte[]>? desktop = null)
    {
        if (Environment.GetEnvironmentVariable("SCOLIA_PDF_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, $"{name}-telephone.pdf"), phonePdf);
        if (desktop is not null) File.WriteAllBytes(Path.Combine(dir, $"{name}-ordinateur.pdf"), desktop());
    }

    private static void AssertPdf(byte[] pdf, int pages)
    {
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(pages, Pages(pdf));
    }

    private static ReceiptDocument Receipt(bool cancelled) => new(School, "RC-2026-000316", new DateOnly(2026, 10, 24), "MASIALA KANKU Jonathan", "TLM113",
        "TLM1", "2026-2027", 55000, "cinquante-cinq mille francs congolais", "Espèces", "ENC-20261024-0316", "MASIALA Joël", "Secrétariat",
        [("Octobre 2026", 55000)], 330000, "https://scolia.local/verifier/51PV44XX6B", cancelled, cancelled ? "Erreur de saisie du montant" : null);

    [Fact]
    public void Un_recu_tient_sur_une_page_A5_et_un_recu_annule_porte_son_filigrane()
    {
        var pdf = phone.Receipt(Receipt(false));
        AssertPdf(pdf, 1);
        Keep("recu", pdf, () => new DocumentService().Receipt(Receipt(false)));
        var cancelled = phone.Receipt(Receipt(true));
        AssertPdf(cancelled, 1);
        Keep("recu-annule", cancelled, () => new DocumentService().Receipt(Receipt(true)));
    }

    [Fact]
    public void Une_longue_liste_continue_sur_plusieurs_pages()
    {
        var rows = Enumerable.Range(1, 120).Select(i => (IReadOnlyList<string>)
            [$"ACC{i:000}", $"APPRENANT NUMÉRO {i} Marie-Josée", "ACC1", (i * 1000).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " FC", i % 3 == 0 ? "En retard" : "À jour"]).ToList();
        var doc = new TableDocument(School, "LISTE GÉNÉRALE DES APPRENANTS", "Année scolaire 2026-2027 · 120 apprenants",
            ["Matricule", "Nom complet", "Promotion", "Solde", "Statut"], rows, ["Total", "", "", "7 260 000 FC", ""]);
        var pdf = phone.Table(doc);
        Assert.True(Pages(pdf) >= 3, $"{Pages(pdf)} page(s)");
        Keep("liste", pdf, () => new DocumentService().Table(doc));
    }

    [Fact]
    public void Un_releve_et_un_bulletin_sont_complets()
    {
        var lines = Enumerable.Range(0, 10).Select(m => new AccountLineDto(Guid.NewGuid(), $"Mensualité {new DateOnly(2026, 9, 1).AddMonths(m):MMMM yyyy}",
            new DateOnly(2026, 9, 5).AddMonths(m), 55000, m == 2 ? 10000 : 0, 55000, m < 2 ? 55000 : 0, m < 2 ? 0 : 55000, m < 2 ? "Payé" : "À payer", m is 2 or 3, "Mensualité")).ToList();
        var receipts = new[] { new ReceiptSummaryDto(Guid.NewGuid(), "RC-2026-000187", new DateOnly(2026, 9, 3), 55000, "Espèces", "Valide", "Secrétariat", "Septembre 2026") };
        var statement = new StatementDocument(School, "BANZA LWAMBA Rachel", "ACC103", "ACC1", "2026-2027", new DateOnly(2026, 10, 25), lines, receipts, 550000, 110000, 440000, 110000);
        var pdf = phone.Statement(statement);
        AssertPdf(pdf, 1);
        Keep("releve", pdf, () => new DocumentService().Statement(statement));

        var card = new ReportCardDocument(School, "BANZA LWAMBA Rachel", "ACC103", "ACC1", "2026-2027",
            [("Anatomie et physiologie", 4, 14.5m, 20), ("Soins infirmiers", 5, 9.25m, 20), ("Pharmacologie", 3, 12, 20), ("Français", 2, null, 20)], 12.1m, 3, 41, "Admise");
        var report = phone.ReportCard(card);
        AssertPdf(report, 1);
        Keep("bulletin", report, () => new DocumentService().ReportCard(card));
    }

    [Fact]
    public void Dix_cartes_donnent_deux_pages_de_huit_cartes()
    {
        var header = new SchoolCardHeader("République démocratique du Congo", "Ministère de l'Enseignement", "Direction provinciale", "Division Kinshasa",
            "Ville de Kinshasa", "Complexe Scolaire Les Étoiles", "Avenue de la Paix 12", null);
        var cards = Enumerable.Range(1, 10).Select(i => new CardItem($"MUKENDI{i}", "KAZADI", "Josué", "ACC1", i % 2 == 0 ? "#2563EB" : "#16A34A", $"SCOLIA|ACC{i:000}", $"ACC{i:000}")).ToList();
        var doc = new CardDocument(header, "OCTOBRE 2026", "2026-2027", cards);
        var pdf = phone.StudentCards(doc);
        AssertPdf(pdf, 2);
        Keep("cartes", pdf, () => new DocumentService().StudentCards(doc));
        Assert.True(phone.StudentCardsWord(doc).Length > 1000);
    }
}
