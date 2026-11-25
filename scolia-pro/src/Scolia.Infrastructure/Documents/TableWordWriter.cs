using System.IO.Compression;
using System.Xml.Linq;
using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>Document Word éditable pour les tableaux filtrés, sans modifier le modèle des cartes d'accès.</summary>
public static class TableWordWriter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static XElement Paragraph(string text, bool bold = false, int size = 18) =>
        new(W + "p", new XElement(W + "r",
            new XElement(W + "rPr", bold ? new XElement(W + "b") : null,
                new XElement(W + "sz", new XAttribute(W + "val", size))),
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));

    private static XElement Row(IReadOnlyList<string> values, int columns, bool header = false, bool total = false) =>
        new(W + "tr", new XElement(W + "trPr", header ? new XElement(W + "tblHeader") : null,
            new XElement(W + "cantSplit")),
            Enumerable.Range(0, columns).Select(i => new XElement(W + "tc",
                new XElement(W + "tcPr", header ? new XElement(W + "shd", new XAttribute(W + "fill", "E8EEF6")) : null),
                Paragraph(i < values.Count ? values[i] : string.Empty, header || total, 16))));

    public static byte[] Write(TableDocument table)
    {
        var columns = table.Columns.Count == 0 ? new[] { "Information" } : table.Columns;
        var content = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW", new XAttribute(W + "w", "5000"), new XAttribute(W + "type", "pct")),
                new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }
                    .Select(s => new XElement(W + s, new XAttribute(W + "val", "single"),
                        new XAttribute(W + "sz", "4"), new XAttribute(W + "color", "D5DCE5"))))),
            Row(columns, columns.Count, header: true),
            table.Rows.Select(r => Row(r, columns.Count)),
            table.Rows.Count == 0 ? Row(["Aucune donnée pour les critères sélectionnés."], columns.Count) : null,
            table.Totals is null ? null : Row(table.Totals, columns.Count, total: true));
        var doc = new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W),
            new XElement(W + "body", Paragraph(table.School.Name, true, 28), Paragraph(table.Title, true, 24),
                Paragraph(table.Subtitle ?? string.Empty), content,
                new XElement(W + "sectPr",
                    new XElement(W + "pgSz", new XAttribute(W + "w", table.Landscape ? 16838 : 11906),
                        new XAttribute(W + "h", table.Landscape ? 11906 : 16838),
                        table.Landscape ? new XAttribute(W + "orient", "landscape") : null),
                    new XElement(W + "pgMar", new XAttribute(W + "top", 720), new XAttribute(W + "bottom", 720),
                        new XAttribute(W + "left", 720), new XAttribute(W + "right", 720))))));
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            WriteXml(zip, "[Content_Types].xml", XDocument.Parse("""
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """));
            WriteXml(zip, "_rels/.rels", XDocument.Parse("""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """));
            WriteXml(zip, "word/document.xml", doc);
        }
        return stream.ToArray();
    }

    private static void WriteXml(ZipArchive zip, string path, XDocument doc)
    {
        using var target = zip.CreateEntry(path).Open();
        doc.Save(target);
    }
}
