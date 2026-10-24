using System.IO.Compression;
using System.Security;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès au format Word (.docx), écrit directement en OOXML sans logiciel tiers. Même grille que le PDF :
/// A4, deux colonnes de 92 mm, espacement de 6 mm, rangées de 63 mm séparées de 7 mm, huit cartes par page.
/// Le rendu exact dépend du logiciel d'ouverture ; le PDF reste la version prête à imprimer.
/// </summary>
public static class CardWordWriter
{
    private const int Twip = 57;   // 1 mm ≈ 56,7 twips
    private const int Emu = 36000; // 1 mm en EMU
    private const int QrSizeMm = 25;

    public static byte[] Write(CardDocument d)
    {
        var qrs = new Dictionary<string, int>();
        var body = new StringBuilder();
        var rowsPerPage = StudentCardRules.CardsPerPage / 2;
        var cardRows = (d.Cards.Count + 1) / 2;
        body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"10772\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>")
            .Append("<w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders>")
            .Append("<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>")
            .Append("<w:tblGrid><w:gridCol w:w=\"5216\"/><w:gridCol w:w=\"340\"/><w:gridCol w:w=\"5216\"/></w:tblGrid>");
        for (var row = 0; row < cardRows; row++)
        {
            if (row > 0 && row % rowsPerPage == 0) body.Append("</w:tbl><w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:tbl>" + TableHeader());
            else if (row > 0) body.Append(GapRow());
            body.Append("<w:tr><w:trPr><w:trHeight w:val=\"").Append(63 * Twip).Append("\" w:hRule=\"exact\"/></w:trPr>");
            for (var col = 0; col < 2; col++)
            {
                var index = row * 2 + col;
                if (col == 1) body.Append("<w:tc><w:tcPr><w:tcW w:w=\"340\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>");
                body.Append("<w:tc><w:tcPr><w:tcW w:w=\"5216\" w:type=\"dxa\"/><w:tcBorders>")
                    .Append("<w:top w:val=\"single\" w:sz=\"4\" w:color=\"94A3B8\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"94A3B8\"/>")
                    .Append("<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"94A3B8\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"94A3B8\"/>")
                    .Append("</w:tcBorders></w:tcPr>");
                if (index < d.Cards.Count) AppendCard(body, d, d.Cards[index], qrs);
                else body.Append("<w:p/>");
                body.Append("</w:tc>");
            }
            body.Append("</w:tr>");
        }
        body.Append("</w:tbl>");

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Entry(zip, "[Content_Types].xml", ContentTypes());
            Entry(zip, "_rels/.rels", RootRels);
            Entry(zip, "word/_rels/document.xml.rels", DocumentRels(qrs));
            Entry(zip, "word/document.xml", Document(body.ToString()));
            foreach (var (text, id) in qrs)
            {
                using var png = new MemoryStream(DocumentService.QrPng(text));
                var entry = zip.CreateEntry($"word/media/qr{id}.png");
                using var target = entry.Open();
                png.CopyTo(target);
            }
        }
        return stream.ToArray();
    }

    private static void AppendCard(StringBuilder body, CardDocument d, CardItem card, Dictionary<string, int> qrs)
    {
        // Bande de promotion, puis en-tête, titre, session, identité et QR.
        body.Append($"<w:p><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{card.Color.TrimStart('#')}\"/><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"{7 * Twip}\" w:lineRule=\"exact\"/></w:pPr></w:p>");
        var header = string.Join(" · ", new[] { d.Header.Country, d.Header.Ministry, d.Header.Direction, d.Header.Division, d.Header.CityZone, d.Header.Institute }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        body.Append(Paragraph(header, 11, false, false, "center"));
        body.Append(Paragraph("CARTE D'ACCÈS", 19, true, true, "center"));
        body.Append(Paragraph($"SESSION {d.Session}", 28, true, false, "center"));
        if (!qrs.TryGetValue(card.QrText, out var id))
        {
            id = qrs.Count + 1;
            qrs[card.QrText] = id;
        }
        // Identité à gauche, QR à droite : une table imbriquée sans bordure, comme sur la carte imprimée.
        body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5216\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>")
            .Append("<w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders>")
            .Append("<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>")
            .Append("<w:tblGrid><w:gridCol w:w=\"3600\"/><w:gridCol w:w=\"1616\"/></w:tblGrid><w:tr>")
            .Append("<w:tc><w:tcPr><w:tcW w:w=\"3600\" w:type=\"dxa\"/></w:tcPr>")
            .Append(Label("NOM : ", card.LastName)).Append(Label("POST-NOM : ", card.MiddleName))
            .Append(Label("PRÉNOM : ", card.FirstName)).Append(Label("CLASSE : ", card.Classe)).Append("</w:tc>")
            .Append("<w:tc><w:tcPr><w:tcW w:w=\"1616\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"right\"/></w:pPr>").Append(Qr(id)).Append("</w:p></w:tc>")
            .Append("</w:tr></w:tbl><w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/></w:pPr></w:p>");
    }

    private static string Label(string label, string value) =>
        $"<w:p><w:pPr><w:spacing w:before=\"20\" w:after=\"20\"/><w:ind w:left=\"200\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"17\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(label)}</w:t></w:r><w:r><w:rPr><w:sz w:val=\"17\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(value)}</w:t></w:r></w:p>";

    private static string Paragraph(string text, int halfPoints, bool bold, bool underline, string align)
    {
        var props = $"<w:rPr>{(bold ? "<w:b/>" : string.Empty)}{(underline ? "<w:u w:val=\"single\"/>" : string.Empty)}<w:sz w:val=\"{halfPoints}\"/></w:rPr>";
        return $"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"{align}\"/>{props}</w:pPr><w:r>{props}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>";
    }

    private static string Qr(int id)
    {
        const int size = QrSizeMm * Emu;
        return $"<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{size}\" cy=\"{size}\"/>" +
            $"<wp:docPr id=\"{id}\" name=\"QR {id}\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            $"<pic:pic><pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"qr{id}.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            $"<pic:blipFill><a:blip r:embed=\"rQr{id}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{size}\" cy=\"{size}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
            "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
    }

    private static string GapRow() =>
        "<w:tr><w:trPr><w:trHeight w:val=\"" + 7 * Twip + "\" w:hRule=\"exact\"/></w:trPr>" +
        "<w:tc><w:tcPr><w:tcW w:w=\"5216\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>" +
        "<w:tc><w:tcPr><w:tcW w:w=\"340\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>" +
        "<w:tc><w:tcPr><w:tcW w:w=\"5216\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc></w:tr>";

    private static string TableHeader() =>
        "<w:tblPr><w:tblW w:w=\"10772\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>" +
        "<w:tblGrid><w:gridCol w:w=\"5216\"/><w:gridCol w:w=\"340\"/><w:gridCol w:w=\"5216\"/></w:tblGrid>";

    private static string Document(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>" + body +
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"567\" w:right=\"567\" w:bottom=\"567\" w:left=\"567\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>" +
        "</w:body></w:document>";

    private static string ContentTypes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private static string DocumentRels(Dictionary<string, int> qrs)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var id in qrs.Values)
            sb.Append($"<Relationship Id=\"rQr{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/qr{id}.png\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Esc(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

}
