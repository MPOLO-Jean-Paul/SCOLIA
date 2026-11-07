using System.IO.Compression;
using System.Security;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès au format Word (.docx) : huit inserts ID-1 (85,60 × 53,98 mm) par A4,
/// logo et identité de l'établissement configurés, Session en grand, QR à la place du cachet,
/// matricule, noms et promotion. Le PDF reste conseillé pour une découpe à dimensions exactes.
/// </summary>
public static class CardWordWriter
{
    // A incrémenter ensemble avec le générateur PDF à chaque évolution du modèle.
    internal static readonly string LayoutId = "SCOLIA-CARTES-ID1-2026-11-V3";
    private const int CardTwip = 4853;       // 85,60 mm
    private const int GapTwip = 340;         // 6 mm
    private const int RowGapTwip = 397;      // 7 mm
    private const int CardHeightTwip = 3060; // 53,98 mm
    private const int SealTwip = 680;
    private const int HeaderRightTwip = 1320; // panneau d'adresse a droite        // emplacement du logo
    private const int QrTwip = 1120;         // QR dans l'ancienne zone du cachet
    private const int InnerTwip = 114;       // 2 mm de marge interne
    private const int InnerWidthTwip = CardTwip - 2 * InnerTwip;
    private const int Emu = 36000;          // 1 mm en EMU
    private const string WordFont = "<w:rFonts w:ascii=\"Roboto\" w:hAnsi=\"Roboto\" w:cs=\"Roboto\"/>";
    private const string TinyParagraph = "<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr></w:p>";

    private sealed class Images
    {
        public readonly Dictionary<string, (string Rid, byte[] Bytes, string Extension)> ByKey = new();

        public string Add(string key, Func<byte[]> bytes)
        {
            if (!ByKey.TryGetValue(key, out var image))
            {
                var content = bytes();
                image = ($"rImg{ByKey.Count + 1}", content,
                    content.Length >= 4 && content[0] == 0x89 && content[1] == 'P' ? "png" : "jpg");
                ByKey[key] = image;
            }
            return image.Rid;
        }
    }

    public static byte[] Write(CardDocument d)
    {
        var images = new Images();
        var schoolLogo = d.Header.Logo is { Length: > 0 } logo ? logo :
            throw new InvalidOperationException("Le logo de l'établissement est nécessaire pour créer ses cartes d'accès.");
        if (!IsSupportedImage(schoolLogo))
            throw new InvalidOperationException("Le logo des cartes doit être au format PNG ou JPEG.");
        var sealRid = images.Add("school-logo", () => schoolLogo);
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

            // Chaque ligne de la grille porte deux cartes ID-1 complètes.
            body.Append($"<w:tr><w:trPr><w:trHeight w:val=\"{CardHeightTwip}\" w:hRule=\"atLeast\"/></w:trPr>");
            ContentCell(body, d, first, sealRid, images);
            body.Append(GapCell());
            ContentCell(body, d, second, sealRid, images);
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
            foreach (var (rid, bytes, ext) in images.ByKey.Values)
            {
                using var target = zip.CreateEntry($"word/media/{rid}.{ext}").Open();
                target.Write(bytes, 0, bytes.Length);
            }
        }
        return stream.ToArray();
    }

    private static void AppendCard(StringBuilder body, CardDocument d, CardItem card, string logoRid, Images images)
    {
        var qrRid = images.Add("qr:" + card.QrText, () => DocumentService.QrPng(card.QrText));
        var content = new StringBuilder();
        var names = StudentCardRules.FullName(card.LastName, card.MiddleName, card.FirstName);
        var schoolName = d.Header.Institute.Trim().ToUpperInvariant();
        var administration = CardHeaderLayout.AdministrativeLines(d.Header);
        var sideInfo = CardHeaderLayout.AddressLines(d.Header);
        var contacts = CardHeaderLayout.ContactLines(d.Header);

        // En-tête : logo à gauche, mentions institutionnelles au centre,
        // panneau d'adresse à droite marqué par une séparation verticale.
        var centralTwip = InnerWidthTwip - SealTwip - HeaderRightTwip;
        content.Append(TableStart(new[] { SealTwip, centralTwip, HeaderRightTwip }, InnerTwip));
        content.Append($"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{SealTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Picture(logoRid, 10, 10, 1, "Logo établissement", "left")).Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{centralTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        foreach (var mention in administration)
            Mention(content, mention, mention.Length > 110 ? 6 : 7, false, "left");
        Mention(content, schoolName, schoolName.Length > 86 ? 8 :
            schoolName.Length > 54 ? 9 : 10, true, "left");
        content.Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{HeaderRightTwip}\" w:type=\"dxa\"/>" +
            "<w:tcBorders><w:left w:val=\"single\" w:sz=\"5\" w:color=\"AEB8C5\"/></w:tcBorders>" +
            "<w:vAlign w:val=\"center\"/></w:tcPr>");
        foreach (var line in sideInfo)
            Mention(content, line, line.Length > 85 ? 5 : line.Length > 55 ? 6 : 7, false, "left");
        content.Append("</w:tc></w:tr></w:tbl>");

        // Titre de session agrandi, sans compromettre les trois lignes d'identité.
        content.Append(TableStart(new[] { InnerWidthTwip - QrTwip, QrTwip }, InnerTwip));
        content.Append($"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{InnerWidthTwip - QrTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        content.Append(Paragraph("Session " + StudentCardRules.SessionYear(d.Session), 26, true, true, "center", "616975", 9));
        content.Append(Paragraph("ACCÈS AUX EXAMENS", 12, true, false, "center", "1B2430", 6));
        content.Append(Paragraph(d.Session, 9, false, false, "center", "5B6878", 5));
        content.Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{QrTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        content.Append(Picture(qrRid, 14.0, 14.0, 2, "QR de contrôle"));
        content.Append(Paragraph("Contrôle QR", 7, false, false, "center", "5B6878"));
        content.Append("</w:tc></w:tr></w:tbl>");

        // Zone nominative obligatoirement après la session, jamais dans
        // une cellule sous une autre ligne à hauteur EXACTE qui la masquerait.
        Field(content, "MATRICULE", card.Matricule, 15);
        Field(content, "NOMS", names, names.Length > 90 ? 9 : names.Length > 60 ? 11
            : names.Length > 40 ? 13 : 16);
        Field(content, "PROMOTION", card.Classe, 15);

        // Contacts, site internet et devise sont placés en bas, à côté du
        // second logo de l'école ; ils ne masquent ni identité ni QR.
        content.Append(TableStart(new[] { InnerWidthTwip - SealTwip, SealTwip }, InnerTwip));
        content.Append($"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{InnerWidthTwip - SealTwip}\" w:type=\"dxa\"/></w:tcPr>");
        foreach (var mention in contacts)
            content.Append(Paragraph(mention, 8, false, false, "left", "5B6878"));
        content.Append(Paragraph("Année scolaire " + d.AcademicYear, 8, false, false, "left", "5B6878"));
        content.Append("</w:tc>");
        content.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{SealTwip}\" w:type=\"dxa\"/></w:tcPr>");
        content.Append(Picture(logoRid, 9.0, 9.0, 3, "Logo inférieur établissement", "right"));
        content.Append("</w:tc></w:tr></w:tbl>");

        body.Append(content).Append(TinyParagraph);
    }

    private static void ContentCell(StringBuilder body, CardDocument d, CardItem? card, string sealRid, Images images)
    {
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/>{Sides(card is not null, top: true, bottom: true)}</w:tcPr>");
        if (card is null) body.Append(TinyParagraph);
        else AppendCard(body, d, card, sealRid, images);
        body.Append("</w:tc>");
    }

    private static string GapCell() => $"<w:tc><w:tcPr><w:tcW w:w=\"{GapTwip}\" w:type=\"dxa\"/></w:tcPr>{TinyParagraph}</w:tc>";

    private static string Grid() =>
        $"<w:tbl><w:tblPr><w:tblW w:w=\"{2 * CardTwip + GapTwip}\" w:type=\"dxa\"/><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>" +
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

    private static void Mention(StringBuilder body, string? value, int halfPoints, bool bold, string alignment = "center")
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        body.Append(Paragraph(value.Trim().ToUpperInvariant(), halfPoints, bold, false, alignment, "1B2430", 0, 14));
    }

    /// <summary>Matricule, noms complets et promotion, sans ligne prédéfinie.</summary>
    private static void Field(StringBuilder body, string label, string value, int halfPoints)
    {
        body.Append($"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"36\" w:line=\"{Math.Max(halfPoints * 13, 170)}\" w:lineRule=\"atLeast\"/><w:jc w:val=\"left\"/></w:pPr>")
            .Append(Run(label + " : ", 13, "1B2430"))
            .Append(Run(value.Trim().ToUpperInvariant(), halfPoints, "1B2430"))
            .Append("</w:p>");
    }

    private static string Run(string text, int halfPoints, string color, bool bold = true) =>
        $"<w:r><w:rPr>{WordFont}{(bold ? "<w:b/>" : string.Empty)}<w:color w:val=\"{color}\"/><w:sz w:val=\"{halfPoints}\"/></w:rPr><w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";

    private static string Paragraph(string text, int halfPoints, bool bold, bool underline, string align, string? color, int beforeTwip = 0, int afterTwip = 0, int indentTwip = 0)
    {
        var props = $"<w:rPr>{WordFont}{(bold ? "<w:b/>" : string.Empty)}{(underline ? "<w:u w:val=\"single\"/>" : string.Empty)}" +
            $"{(color is null ? string.Empty : $"<w:color w:val=\"{color}\"/>")}<w:sz w:val=\"{halfPoints}\"/></w:rPr>";
        var line = Math.Max(halfPoints * 13, 120);
        var spacing = $"<w:spacing w:before=\"{beforeTwip}\" w:after=\"{afterTwip}\" w:line=\"{line}\" w:lineRule=\"exact\"/>";
        var run = text.Length == 0 ? string.Empty : $"<w:r>{props}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";
        var indent = indentTwip > 0 ? $"<w:ind w:left=\"{indentTwip}\" w:right=\"{indentTwip}\"/>" : string.Empty;
        return $"<w:p><w:pPr>{spacing}{indent}<w:jc w:val=\"{align}\"/>{props}</w:pPr>{run}</w:p>";
    }

    /// <summary>Image en ligne de taille fixe, en millimètres, centrée dans sa cellule.</summary>
    private static string Picture(string rid, double widthMm, double heightMm, int id, string name, string alignment = "center")
    {
        var cx = (long)(widthMm * Emu);
        var cy = (long)(heightMm * Emu);
        return $"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"{alignment}\"/></w:pPr><w:r><w:drawing>" +
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
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"907\" w:right=\"907\" w:bottom=\"907\" w:left=\"907\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>" +
        "</w:body></w:document>";

    private static string ContentTypes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
        "<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
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
        foreach (var (rid, _, ext) in images.ByKey.Values)
            sb.Append($"<Relationship Id=\"{rid}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/{rid}.{ext}\"/>");
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
