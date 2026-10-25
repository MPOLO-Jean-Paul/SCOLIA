using System.IO.Compression;
using System.Security;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès au format Word (.docx), écrit en OOXML sans logiciel tiers. Même grille et même mise en page que le PDF :
/// A4, cartes de 92 × 63 mm sur deux colonnes, interstices de 6 mm et 7 mm, huit cartes par page, en-tête avec le sceau,
/// identité à gauche, QR à droite, bande de promotion en pied de carte. Le rendu exact dépend du logiciel d'ouverture ;
/// le PDF reste la version prête à imprimer.
/// </summary>
public static class CardWordWriter
{
    private const int CardTwip = 5216;      // 92 mm
    private const int GapTwip = 340;        // 6 mm
    private const int RowGapTwip = 397;     // 7 mm
    private const int CardHeightTwip = 3572; // 63 mm
    private const int BandTwip = 352;       // 6,2 mm
    private const int SealTwip = 1500;      // colonne du sceau
    private const int QrTwip = 1500;        // colonne du QR
    private const int InnerTwip = 170;      // marge intérieure de la carte : 3 mm
    private const int InnerWidthTwip = CardTwip - 2 * InnerTwip;
    private const int Emu = 36000;          // 1 mm en EMU
    private const string TinyParagraph = "<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr></w:p>";

    private sealed class Images
    {
        public readonly Dictionary<string, (string Rid, byte[] Bytes)> ByKey = new();

        public string Add(string key, Func<byte[]> bytes)
        {
            if (!ByKey.TryGetValue(key, out var image))
            {
                image = ($"rImg{ByKey.Count + 1}", bytes());
                ByKey[key] = image;
            }
            return image.Rid;
        }
    }

    public static byte[] Write(CardDocument d)
    {
        var images = new Images();
        var sealRid = images.Add("seal", () => d.Header.Logo is { Length: > 0 } logo && IsSupportedImage(logo) ? logo : StudentCardPdf.DefaultSeal());
        var body = new StringBuilder();
        var rowsPerPage = StudentCardRules.CardsPerPage / 2;
        var cardRows = (d.Cards.Count + 1) / 2;
        body.Append(Grid());
        for (var row = 0; row < cardRows; row++)
        {
            if (row > 0 && row % rowsPerPage == 0) body.Append("</w:tbl><w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr><w:r><w:br w:type=\"page\"/></w:r></w:p>").Append(Grid());
            else if (row > 0) body.Append(GapRow());
            var left = row * 2;
            var right = left + 1;
            var first = left < d.Cards.Count ? d.Cards[left] : null;
            var second = right < d.Cards.Count ? d.Cards[right] : null;

            // Chaque carte tient sur deux rangées de la grille : le contenu, puis la bande de promotion collée au bas.
            body.Append($"<w:tr><w:trPr><w:trHeight w:val=\"{CardHeightTwip - BandTwip}\" w:hRule=\"exact\"/></w:trPr>");
            ContentCell(body, d, first, sealRid, images);
            body.Append(GapCell());
            ContentCell(body, d, second, sealRid, images);
            body.Append("</w:tr>");
            body.Append($"<w:tr><w:trPr><w:trHeight w:val=\"{BandTwip}\" w:hRule=\"exact\"/></w:trPr>");
            BandCell(body, first);
            body.Append(GapCell());
            BandCell(body, second);
            body.Append("</w:tr>");
        }
        body.Append("</w:tbl>");

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Entry(zip, "[Content_Types].xml", ContentTypes());
            Entry(zip, "_rels/.rels", RootRels);
            Entry(zip, "word/_rels/document.xml.rels", DocumentRels(images));
            Entry(zip, "word/document.xml", Document(body.ToString()));
            foreach (var (rid, bytes) in images.ByKey.Values)
            {
                using var target = zip.CreateEntry($"word/media/{rid}.png").Open();
                target.Write(bytes, 0, bytes.Length);
            }
        }
        return stream.ToArray();
    }

    private static void AppendCard(StringBuilder body, CardDocument d, CardItem card, string sealRid, Images images)
    {
        var qrRid = images.Add("qr:" + card.QrText, () => DocumentService.QrPng(card.QrText));
        var content = new StringBuilder();

        // En-tête : sceau à gauche, mentions centrées à droite.
        content.Append(TableStart(new[] { SealTwip, InnerWidthTwip - SealTwip }, InnerTwip));
        content.Append($"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{SealTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Picture(sealRid, 13.5, 13.5, 1, "Sceau")).Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{InnerWidthTwip - SealTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        Mention(content, d.Header.Country, 10, true);
        Mention(content, d.Header.Ministry, 9, false);
        Mention(content, d.Header.Direction, 9, false);
        Mention(content, d.Header.Division, 9, false);
        Mention(content, d.Header.CityZone, 9, false);
        Mention(content, d.Header.Institute, 10, true);
        Mention(content, d.Header.Address, 9, false);
        content.Append("</w:tc></w:tr></w:tbl>");

        // Bandeau bleu marine : le titre de la carte et la session, comme sur le PDF.
        content.Append(Paragraph(string.Empty, 4, false, false, "left", null, 40));
        content.Append(TableStart(new[] { CardTwip / 2, CardTwip - CardTwip / 2 }));
        content.Append("<w:tr><w:trPr><w:trHeight w:val=\"340\" w:hRule=\"exact\"/></w:trPr>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip / 2}\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"0B3B6F\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Paragraph("CARTE D'ACCÈS", 15, true, false, "left", "FFFFFF", indentTwip: InnerTwip)).Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip - CardTwip / 2}\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"0B3B6F\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Paragraph($"SESSION {d.Session}", d.Session.Length > 14 ? 12 : 14, true, false, "right", "FFFFFF", indentTwip: InnerTwip)).Append("</w:tc></w:tr></w:tbl>");
        content.Append(Paragraph(string.Empty, 4, false, false, "left", null, 80));

        // Identité à gauche, QR à droite.
        content.Append(TableStart(new[] { InnerWidthTwip - QrTwip, QrTwip }, InnerTwip));
        content.Append($"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{InnerWidthTwip - QrTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        Field(content, "NOM", card.LastName);
        Field(content, "POST-NOM", card.MiddleName);
        Field(content, "PRÉNOM", card.FirstName);
        content.Append($"</w:tc><w:tc><w:tcPr><w:tcW w:w=\"{QrTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Picture(qrRid, 18, 18, 2, "QR"))
            .Append(Paragraph("Contrôle d'accès", 8, false, false, "center", "5B6878"))
            .Append("</w:tc></w:tr></w:tbl>");
        content.Append(Paragraph($"Année scolaire {d.AcademicYear}", 10, true, false, "left", "0B3B6F", 80, indentTwip: InnerTwip + 60));

        // Le paragraphe final est obligatoire dans une cellule, après la dernière table.
        body.Append(content).Append(TinyParagraph);
    }

    private static void ContentCell(StringBuilder body, CardDocument d, CardItem? card, string sealRid, Images images)
    {
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/>{Sides(card is not null, top: true, bottom: false)}</w:tcPr>");
        if (card is null) body.Append(TinyParagraph);
        else AppendCard(body, d, card, sealRid, images);
        body.Append("</w:tc>");
    }

    /// <summary>Bande de la promotion : la classe à gauche, le matricule à droite, en blanc ou en encre selon la couleur.</summary>
    private static void BandCell(StringBuilder body, CardItem? card)
    {
        var fill = card is null ? string.Empty : $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{card.Color.TrimStart('#')}\"/>";
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/>{Sides(card is not null, top: false, bottom: true)}{fill}<w:vAlign w:val=\"center\"/></w:tcPr>");
        if (card is null) body.Append(TinyParagraph);
        else
        {
            var ink = StudentCardPdf.InkOn(card.Color).TrimStart('#');
            body.Append($"<w:p><w:pPr><w:tabs><w:tab w:val=\"right\" w:pos=\"{CardTwip - 2 * InnerTwip - 60}\"/></w:tabs><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"220\" w:lineRule=\"exact\"/><w:ind w:left=\"{InnerTwip + 60}\"/></w:pPr>")
                .Append(Run("CLASSE  ", 9, ink, bold: false)).Append(Run(card.Classe, 14, ink));
            if (card.Matricule.Length > 0)
                body.Append("<w:r><w:tab/></w:r>").Append(Run("MATRICULE  ", 9, ink, bold: false)).Append(Run(card.Matricule, 14, ink));
            body.Append("</w:p>");
        }
        body.Append("</w:tc>");
    }

    private static string GapCell() => $"<w:tc><w:tcPr><w:tcW w:w=\"{GapTwip}\" w:type=\"dxa\"/></w:tcPr>{TinyParagraph}</w:tc>";

    private static string Grid() =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"10772\" w:type=\"dxa\"/><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>" +
        "<w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders>" +
        "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>" +
        $"<w:tblGrid><w:gridCol w:w=\"{CardTwip}\"/><w:gridCol w:w=\"{GapTwip}\"/><w:gridCol w:w=\"{CardTwip}\"/></w:tblGrid>";

    private static string GapRow() =>
        $"<w:tr><w:trPr><w:trHeight w:val=\"{RowGapTwip}\" w:hRule=\"exact\"/></w:trPr>" +
        $"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>" +
        $"<w:tc><w:tcPr><w:tcW w:w=\"{GapTwip}\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc>" +
        $"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc></w:tr>";

    private static string TableStart(int[] widths, int indentTwip = 0)
    {
        var grid = string.Concat(widths.Select(w => $"<w:gridCol w:w=\"{w}\"/>"));
        return $"<w:tbl><w:tblPr><w:tblW w:w=\"{widths.Sum()}\" w:type=\"dxa\"/><w:tblInd w:w=\"{indentTwip}\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>" +
            "<w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders>" +
            "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>" +
            $"<w:tblGrid>{grid}</w:tblGrid>";
    }

    /// <summary>Cadre fin et gris sur les côtés et sur les bords indiqués. Une case vide n'a pas de cadre.</summary>
    private static string Sides(bool framed, bool top, bool bottom) =>
        $"<w:tcBorders>{Side("top", framed && top)}{Side("left", framed)}{Side("bottom", framed && bottom)}{Side("right", framed)}</w:tcBorders>";

    private static string Side(string side, bool on) =>
        on ? $"<w:{side} w:val=\"single\" w:sz=\"4\" w:color=\"AEB8C5\"/>" : $"<w:{side} w:val=\"nil\"/>";

    private static void Mention(StringBuilder body, string? value, int halfPoints, bool bold)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        body.Append(Paragraph(value.Trim().ToUpperInvariant(), halfPoints, bold, false, "center", "1B2430"));
    }

    /// <summary>Un champ : petit libellé gris au-dessus, valeur en gras dessous, comme sur le PDF.</summary>
    private static void Field(StringBuilder body, string label, string value)
    {
        var size = value.Length > 24 ? 14 : 17;
        body.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"130\" w:lineRule=\"exact\"/><w:jc w:val=\"left\"/></w:pPr>")
            .Append(Run(label, 9, "5B6878")).Append("</w:p>");
        body.Append($"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"60\" w:line=\"{size * 12}\" w:lineRule=\"exact\"/><w:jc w:val=\"left\"/></w:pPr>")
            .Append(Run(value, size, "1B2430")).Append("</w:p>");
    }

    private static string Run(string text, int halfPoints, string color, bool bold = true) =>
        $"<w:r><w:rPr>{(bold ? "<w:b/>" : string.Empty)}<w:color w:val=\"{color}\"/><w:sz w:val=\"{halfPoints}\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";

    private static string Paragraph(string text, int halfPoints, bool bold, bool underline, string align, string? color, int beforeTwip = 0, int afterTwip = 0, int indentTwip = 0)
    {
        var props = $"<w:rPr>{(bold ? "<w:b/>" : string.Empty)}{(underline ? "<w:u w:val=\"single\"/>" : string.Empty)}" +
            $"{(color is null ? string.Empty : $"<w:color w:val=\"{color}\"/>")}<w:sz w:val=\"{halfPoints}\"/></w:rPr>";
        var line = Math.Max(halfPoints * 13, 120);
        var spacing = $"<w:spacing w:before=\"{beforeTwip}\" w:after=\"{afterTwip}\" w:line=\"{line}\" w:lineRule=\"exact\"/>";
        var run = text.Length == 0 ? string.Empty : $"<w:r>{props}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";
        var indent = indentTwip > 0 ? $"<w:ind w:left=\"{indentTwip}\" w:right=\"{indentTwip}\"/>" : string.Empty;
        return $"<w:p><w:pPr>{spacing}{indent}<w:jc w:val=\"{align}\"/>{props}</w:pPr>{run}</w:p>";
    }

    /// <summary>Image en ligne de taille fixe, en millimètres, centrée dans sa cellule.</summary>
    private static string Picture(string rid, double widthMm, double heightMm, int id, string name)
    {
        var cx = (long)(widthMm * Emu);
        var cy = (long)(heightMm * Emu);
        return "<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:drawing>" +
            $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/>" +
            $"<wp:docPr id=\"{id}\" name=\"{name}\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            $"<pic:pic><pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{name}.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            $"<pic:blipFill><a:blip r:embed=\"{rid}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
            "</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
    }

    private static bool IsSupportedImage(byte[] bytes) =>
        bytes.Length > 8 && ((bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G') || (bytes[0] == 0xFF && bytes[1] == 0xD8));

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

    private static string DocumentRels(Images images)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (rid, _) in images.ByKey.Values)
            sb.Append($"<Relationship Id=\"{rid}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/{rid}.png\"/>");
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
