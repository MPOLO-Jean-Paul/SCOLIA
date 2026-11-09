using System.IO.Compression;
using System.Security;
using System.Text;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès au format Word (.docx) : huit inserts ID-1 (85,60 × 53,98 mm) par A4, sur le dessin commun
/// <see cref="CardDesign"/> : en-tête centré de l'établissement, « Session AAAA » souligné, identité, cachet rond
/// (image) en bas à droite et QR de contrôle, posés à leur place exacte dans la carte.
/// Le PDF reste conseillé pour une découpe à dimensions exactes.
/// </summary>
public static class CardWordWriter
{
    // A incrémenter ensemble avec le générateur PDF à chaque évolution du modèle.
    internal static readonly string LayoutId = "SCOLIA-CARTES-ID1-2026-11-V4";
    private const int CardTwip = 4853;       // 85,60 mm
    private const int GapTwip = 340;         // 6 mm
    private const int RowGapTwip = 397;      // 7 mm
    private const int CardHeightTwip = 3060; // 53,98 mm
    private const int Emu = 36000;           // 1 mm en EMU
    private const string SansFont = "<w:rFonts w:ascii=\"Montserrat\" w:hAnsi=\"Montserrat\" w:cs=\"Montserrat\"/>";
    private const string SerifFont = "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\" w:cs=\"Times New Roman\"/>";
    private const string SmallFont = "<w:rFonts w:ascii=\"Roboto\" w:hAnsi=\"Roboto\" w:cs=\"Roboto\"/>";
    private const string TinyParagraph = "<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr></w:p>";

    private static int Twip(float mm) => (int)Math.Round(mm * 1440 / 25.4f);
    private static string Hex(string color) => color.TrimStart('#');

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

    /// <summary>Cartes Word avec le cachet dessiné par le moteur PDF de l'ordinateur.</summary>
    public static byte[] Write(CardDocument d) =>
        Write(d, paid => StudentCardPdf.StampPng(d.Header, d.Header.Logo ?? [], paid));

    /// <summary>Cartes Word ; <paramref name="stamp"/> rend l'image PNG transparente du cachet, avec ou sans « PAYÉ ».</summary>
    public static byte[] Write(CardDocument d, Func<bool, byte[]> stamp)
    {
        var images = new Images();
        var schoolLogo = d.Header.Logo is { Length: > 0 } logo ? logo :
            throw new InvalidOperationException("Le logo de l'établissement est nécessaire pour créer ses cartes d'accès.");
        if (!IsSupportedImage(schoolLogo))
            throw new InvalidOperationException("Le logo des cartes doit être au format PNG ou JPEG.");
        var logoRid = images.Add("school-logo", () => schoolLogo);
        var body = new StringBuilder();
        var rowsPerPage = StudentCardRules.CardsPerPage / 2;
        var cardRows = (d.Cards.Count + 1) / 2;
        var drawing = 0;
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
            body.Append($"<w:tr><w:trPr><w:trHeight w:val=\"{CardHeightTwip}\" w:hRule=\"atLeast\"/><w:cantSplit/></w:trPr>");
            ContentCell(body, d, first, logoRid, images, stamp, ref drawing);
            body.Append(GapCell());
            ContentCell(body, d, second, logoRid, images, stamp, ref drawing);
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
            Entry(zip, "word/fontTable.xml", FontTable);
            foreach (var (rid, bytes, ext) in images.ByKey.Values)
            {
                using var target = zip.CreateEntry($"word/media/{rid}.{ext}").Open();
                target.Write(bytes, 0, bytes.Length);
            }
        }
        return stream.ToArray();
    }

    private static void AppendCard(StringBuilder body, CardDocument d, CardItem card, string logoRid, Images images,
        Func<bool, byte[]> stamp, ref int drawing)
    {
        var qrRid = images.Add("qr:" + card.QrText, () => DocumentService.QrPng(card.QrText));
        var stampRid = images.Add(card.Paid ? "stamp:paid" : "stamp", () => stamp(card.Paid));
        var header = d.Header;

        // Ancrages : le cachet et le QR sont posés à leur place exacte dans la carte, par-dessus le texte comme un tampon.
        body.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr>")
            .Append(Floating(stampRid, CardDesign.StampLeftMm, CardDesign.StampTopMm, CardDesign.StampMm, CardDesign.StampMm, ++drawing, "Cachet"))
            .Append(Floating(qrRid, CardDesign.QrLeftMm, CardDesign.QrMarginMm, CardDesign.QrMm, CardDesign.QrMm, ++drawing, "QR de contrôle"))
            .Append("</w:p>");
        body.Append(Spacer(CardDesign.HeaderTopMm - 0.35f));

        // En-tête centré : logo, puis nom, service, filet et contacts.
        var textTwip = Twip(CardDesign.HeaderTextMm);
        var logoTwip = Twip(CardDesign.LogoMm + CardDesign.HeaderGapMm);
        body.Append(TableStart([logoTwip, textTwip], Twip(CardDesign.HeaderLeftMm)));
        body.Append($"<w:tr><w:trPr><w:trHeight w:val=\"{Twip(CardDesign.HeaderHeightMm)}\" w:hRule=\"exact\"/></w:trPr>");
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{logoTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>")
            .Append(Picture(logoRid, CardDesign.LogoMm, CardDesign.LogoMm, ++drawing, "Logo de l'établissement", "left")).Append("</w:tc>");
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{textTwip}\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/></w:tcPr>");
        var name = CardDesign.SchoolName(header);
        body.Append(Paragraph(name, SerifFont, HalfPoints(CardDesign.SchoolNamePt(name)), false, false, "left", CardDesign.HeaderInk, after: 20));
        if (CardDesign.Department(header) is { } department)
            body.Append(Paragraph(department, SerifFont, HalfPoints(CardDesign.DepartmentPt), false, false, "left", CardDesign.HeaderInk, tracking: 6, after: 20));
        var contacts = CardDesign.Contacts(header);
        for (var i = 0; i < contacts.Count; i++)
            body.Append(Paragraph(contacts[i], SmallFont, HalfPoints(CardDesign.ContactPt), false, false, "left", CardDesign.HeaderMuted, rule: i == 0));
        body.Append("</w:tc></w:tr></w:tbl>");

        // « Session 2026 » : grand, gras, souligné, centré.
        body.Append(Spacer(CardDesign.SessionTopMm - CardDesign.HeaderTopMm - CardDesign.HeaderHeightMm));
        body.Append(Paragraph(CardDesign.SessionTitle(d.Session), SansFont, HalfPoints(CardDesign.SessionPt), true, true, "center",
            CardDesign.SessionInk, lineTwip: Twip(CardDesign.IdentityTopMm - CardDesign.SessionTopMm)));

        // Identité : trois lignes de même corps, capitales espacées.
        var size = HalfPoints(CardDesign.IdentityPt(card));
        var tracking = (int)Math.Round(CardDesign.IdentityTracking * size * 10);
        foreach (var (label, value) in CardDesign.Identity(card))
            body.Append(Paragraph(label + " : " + value, SansFont, size, false, false, "left", CardDesign.IdentityInk,
                tracking: tracking, lineTwip: Twip(CardDesign.IdentityStepMm), indentTwip: Twip(CardDesign.IdentityLeftMm)));
    }

    private static int HalfPoints(float points) => Math.Max(6, (int)Math.Round(points * 2));

    private static string Spacer(float mm) =>
        $"<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"{Math.Max(20, Twip(mm))}\" w:lineRule=\"exact\"/></w:pPr></w:p>";

    private static void ContentCell(StringBuilder body, CardDocument d, CardItem? card, string logoRid, Images images,
        Func<bool, byte[]> stamp, ref int drawing)
    {
        body.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{CardTwip}\" w:type=\"dxa\"/>{Sides(card is not null, top: true, bottom: true)}</w:tcPr>");
        if (card is null) body.Append(TinyParagraph);
        else AppendCard(body, d, card, logoRid, images, stamp, ref drawing);
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

    private static string Paragraph(string text, string font, int halfPoints, bool bold, bool underline, string align, string color,
        int tracking = 0, int lineTwip = 0, int indentTwip = 0, int after = 0, bool rule = false)
    {
        var props = $"<w:rPr>{font}{(bold ? "<w:b/>" : string.Empty)}{(underline ? $"<w:u w:val=\"single\" w:color=\"{Hex(color)}\"/>" : string.Empty)}" +
            $"<w:color w:val=\"{Hex(color)}\"/>{(tracking > 0 ? $"<w:spacing w:val=\"{tracking}\"/>" : string.Empty)}<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr>";
        var line = lineTwip > 0 ? $"w:line=\"{lineTwip}\" w:lineRule=\"exact\"" : $"w:line=\"{Math.Max(halfPoints * 11, 90)}\" w:lineRule=\"exact\"";
        var border = rule ? $"<w:pBdr><w:top w:val=\"single\" w:sz=\"3\" w:space=\"1\" w:color=\"{Hex(CardDesign.HeaderMuted)}\"/></w:pBdr>" : string.Empty;
        var indent = indentTwip > 0 ? $"<w:ind w:left=\"{indentTwip}\"/>" : string.Empty;
        return $"<w:p><w:pPr>{border}<w:spacing w:before=\"{(rule ? 30 : 0)}\" w:after=\"{after}\" {line}/>{indent}<w:jc w:val=\"{align}\"/>{props}</w:pPr>" +
            $"<w:r>{props}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r></w:p>";
    }

    /// <summary>Image flottante posée à une position exacte de la carte (en millimètres depuis son coin supérieur gauche).</summary>
    private static string Floating(string rid, float xMm, float yMm, float widthMm, float heightMm, int id, string name)
    {
        var cx = (long)(widthMm * Emu);
        var cy = (long)(heightMm * Emu);
        return "<w:r><w:drawing>" +
            $"<wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"{251658240 + id}\" behindDoc=\"0\" locked=\"1\" layoutInCell=\"1\" allowOverlap=\"1\">" +
            "<wp:simplePos x=\"0\" y=\"0\"/>" +
            $"<wp:positionH relativeFrom=\"column\"><wp:posOffset>{(long)(xMm * Emu)}</wp:posOffset></wp:positionH>" +
            $"<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>{(long)(yMm * Emu)}</wp:posOffset></wp:positionV>" +
            $"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapNone/>" +
            $"<wp:docPr id=\"{id}\" name=\"{name}\"/><wp:cNvGraphicFramePr/>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            $"<pic:pic><pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{name}.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            $"<pic:blipFill><a:blip r:embed=\"{rid}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            $"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
            "</pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";
    }

    /// <summary>Polices : Montserrat, remplacée par Century Gothic sur un poste qui ne l'a pas.</summary>
    private const string FontTable =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:fonts xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:font w:name=\"Montserrat\"><w:altName w:val=\"Century Gothic\"/><w:family w:val=\"swiss\"/><w:pitch w:val=\"variable\"/></w:font>" +
        "<w:font w:name=\"Times New Roman\"><w:family w:val=\"roman\"/><w:pitch w:val=\"variable\"/></w:font>" +
        "<w:font w:name=\"Roboto\"><w:altName w:val=\"Arial\"/><w:family w:val=\"swiss\"/><w:pitch w:val=\"variable\"/></w:font>" +
        "</w:fonts>";

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
        "<Override PartName=\"/word/fontTable.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private static string DocumentRels(Images images)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rFonts\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable\" Target=\"fontTable.xml\"/>");
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
