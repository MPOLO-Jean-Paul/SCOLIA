using System.Text;
using ClosedXML.Excel;
using Scolia.Application.Abstractions;
using Scolia.Infrastructure.Documents;

namespace Scolia.Tests;

/// <summary>Les listes vides et les lignes incomplètes doivent rester exportables, en PDF et Excel.</summary>
public sealed class ExportReliabilityTests
{
    private static readonly SchoolHeader School =
        new("Institut de démonstration", null, null, null, null, null, "FC", null);

    [Fact]
    public void Empty_excel_table_is_an_openable_workbook_with_explanation()
    {
        var table = new TableDocument(School, "Liste d'apprenants", null, [], []);
        var bytes = new SpreadsheetService().Export(table);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);

        Assert.Equal("Information", sheet.Cell(5, 1).GetString());
        Assert.Contains("Aucune donnée", sheet.Cell(6, 1).GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Excel_keeps_its_header_width_and_numeric_amounts()
    {
        var table = new TableDocument(School, "Encaissements", null,
            ["Apprenant", "Montant"],
            [new[] { "MUKENDI" }, new[] { "NGANDU", "55 000 FC", "Colonne parasite" }]);
        var bytes = new SpreadsheetService().Export(table);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);

        Assert.Equal(string.Empty, sheet.Cell(6, 2).GetString());
        Assert.Equal(55000m, (decimal)sheet.Cell(7, 2).GetDouble());
        Assert.True(sheet.Cell(7, 3).IsEmpty());
    }

    [Fact]
    public void Empty_and_irregular_pdf_tables_produce_real_documents()
    {
        var documents = new[]
        {
            new TableDocument(School, "État vide", null, [], []),
            new TableDocument(School, "Journal", null, ["Date", "Montant"],
                [new[] { "24/11/2026" }, new[] { "24/11/2026", "55 000 FC", "Colonne parasite" }])
        };

        var writer = new DocumentService();
        foreach (var document in documents)
        {
            var pdf = writer.Table(document);
            Assert.True(pdf.Length > 100);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }
}
