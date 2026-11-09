using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès en PDF (ordinateur), dessinées d'après <see cref="CardDesign"/> : en-tête centré de l'établissement
/// configuré, « Session AAAA » souligné, identité en capitales espacées, cachet rond en bas à droite marqué « PAYÉ »
/// pour l'apprenant en ordre, QR de contrôle discret en haut à droite.
/// Huit cartes ISO ID-1 par A4, avec repères de découpe entre les cartes.
/// </summary>
internal static class StudentCardPdf
{
    // Marqueur du code de mise en page réellement chargé par le serveur.
    internal static readonly string LayoutId = "SCOLIA-CARTES-ID1-2026-11-V4";
    private const string Guide = "#8A96A6";

    private static readonly float MmPt = 72f / 25.4f;
    private static float Mm(float value) => value * MmPt;

    // Mesures réelles ISO/IEC 7810 ID-1, sans agrandissement à l'impression.
    private const float PageMargin = StudentCardRules.MarginMm;
    private const float CardWidth = StudentCardRules.CardWidthMm;
    private const float CardHeight = StudentCardRules.CardHeightMm;
    private const float ColumnGap = StudentCardRules.ColumnGapMm;
    private const float RowGap = StudentCardRules.RowGapMm;

    private static byte[]? defaultSeal;

    public static byte[] Build(CardDocument d)
    {
        var schoolLogo = d.Header.Logo is { Length: > 0 } logo ? logo :
            throw new InvalidOperationException("Le logo de l'établissement est nécessaire pour créer ses cartes d'accès.");
        var pages = new List<IReadOnlyList<CardItem>>();
        for (var start = 0; start < d.Cards.Count; start += StudentCardRules.CardsPerPage)
            pages.Add(d.Cards.Skip(start).Take(StudentCardRules.CardsPerPage).ToList());

        return Document.Create(doc =>
        {
            doc.WithMetadata(new DocumentMetadata { Title = "Cartes d'accès · " + d.Session, Creator = "SCOLIA", Producer = "SCOLIA" });
            foreach (var cards in pages)
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    // Pas de marge native : la grille est placée à la main, les repères peuvent ainsi sortir de la zone utile.
                    page.Margin(0);
                    page.DefaultTextStyle(s => s.FontFamily("Roboto").FontSize(7).FontColor(CardDesign.HeaderInk));
                    page.Content().PaddingLeft(Mm(PageMargin)).PaddingTop(Mm(PageMargin)).Column(column =>
                    {
                        for (var row = 0; row < 4; row++)
                        {
                            if (row > 0) column.Item().Height(Mm(RowGap)).Row(gutter => CutLineAcross(gutter));
                            column.Item().Row(line =>
                            {
                                for (var col = 0; col < 2; col++)
                                {
                                    if (col > 0) line.ConstantItem(Mm(ColumnGap)).Column(gutter => CutLineDown(gutter));
                                    var index = row * 2 + col;
                                    var cell = line.ConstantItem(Mm(CardWidth)).Height(Mm(CardHeight));
                                    if (index < cards.Count) cell.Element(c => Card(c, d, cards[index], schoolLogo));
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    /// <summary>Place un élément à une position absolue de la carte, en millimètres.</summary>
    private static IContainer At(LayersDescriptor layers, float xMm, float yMm, float widthMm, float heightMm) =>
        layers.Layer().PaddingLeft(Mm(xMm)).PaddingTop(Mm(yMm)).AlignLeft().AlignTop().Width(Mm(widthMm)).Height(Mm(heightMm));

    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] schoolLogo)
    {
        var header = d.Header;
        container.Layers(layers =>
        {
            // Le cadre fin sert de repère de découpe ; le fond reste blanc comme la carte modèle.
            layers.PrimaryLayer().Border(0.4f).BorderColor(CardDesign.Frame).Background(CardDesign.Paper);

            // En-tête centré : logo, puis nom, service, filet et contacts de l'établissement configuré.
            At(layers, CardDesign.HeaderLeftMm, CardDesign.HeaderTopMm, CardDesign.LogoMm + CardDesign.HeaderGapMm + CardDesign.HeaderTextMm,
                CardDesign.HeaderHeightMm).Row(row =>
            {
                row.ConstantItem(Mm(CardDesign.LogoMm)).AlignMiddle().Height(Mm(CardDesign.LogoMm)).Image(schoolLogo).FitArea();
                row.ConstantItem(Mm(CardDesign.HeaderGapMm));
                row.RelativeItem().AlignMiddle().ScaleToFit().Column(text => HeaderText(text, header));
            });

            // QR de contrôle, discret dans le coin libre.
            At(layers, CardDesign.QrLeftMm, CardDesign.QrMarginMm, CardDesign.QrMm, CardDesign.QrMm)
                .Image(DocumentService.QrPng(card.QrText)).FitArea();

            // « Session 2026 » : grand, gras, souligné, centré.
            At(layers, 0, CardDesign.SessionTopMm, CardWidth, 9f).AlignCenter().Text(text =>
            {
                text.Span(CardDesign.SessionTitle(d.Session)).FontFamily(CardDesign.SansFamily).Bold()
                    .FontSize(CardDesign.SessionPt).FontColor(CardDesign.SessionInk)
                    .Underline().DecorationColor(CardDesign.SessionInk).DecorationThickness(1.1f);
            });

            // Identité : trois lignes de même corps, capitales espacées, gris.
            var size = CardDesign.IdentityPt(card);
            var lines = CardDesign.Identity(card);
            for (var i = 0; i < lines.Count; i++)
            {
                var (label, value) = lines[i];
                At(layers, CardDesign.IdentityLeftMm, CardDesign.IdentityTopMm + i * CardDesign.IdentityStepMm, CardDesign.IdentityWidthMm, 4.4f)
                    .ScaleToFit().Text(text =>
                    {
                        text.Span(label + " : ").Style(Identity(size));
                        text.Span(value).Style(Identity(size));
                    });
            }

            // Cachet en bas à droite, par-dessus comme un tampon.
            At(layers, CardDesign.StampLeftMm, CardDesign.StampTopMm, CardDesign.StampMm, CardDesign.StampMm)
                .Element(c => Stamp(c, header, schoolLogo, card.Paid));
        });
    }

    private static TextStyle Identity(float size) => TextStyle.Default.FontFamily(CardDesign.SansFamily).Medium().FontSize(size)
        .LetterSpacing(CardDesign.IdentityTracking).FontColor(CardDesign.IdentityInk);

    private static void HeaderText(ColumnDescriptor text, SchoolCardHeader header)
    {
        var name = CardDesign.SchoolName(header);
        text.Spacing(Mm(0.35f));
        text.Item().Text(name).FontFamily(CardDesign.SerifFamily).FontSize(CardDesign.SchoolNamePt(name))
            .LetterSpacing(0.02f).LineHeight(1.02f).FontColor(CardDesign.HeaderInk);
        if (CardDesign.Department(header) is { } department)
            text.Item().Text(department).FontFamily(CardDesign.SerifFamily).FontSize(CardDesign.DepartmentPt)
                .LetterSpacing(0.08f).FontColor(CardDesign.HeaderInk);
        var contacts = CardDesign.Contacts(header);
        if (contacts.Count == 0) return;
        text.Item().PaddingTop(Mm(0.25f)).LineHorizontal(0.45f).LineColor(CardDesign.HeaderMuted);
        foreach (var line in contacts)
            text.Item().Text(line).FontFamily("Roboto").FontSize(CardDesign.ContactPt).LineHeight(1.05f).FontColor(CardDesign.HeaderMuted);
    }

    /// <summary>
    /// Cachet rond de l'établissement : double filet, nom en couronne, sigle, logo au centre, et « PAYÉ » en travers
    /// pour l'apprenant en ordre. Couleur d'encre de tampon, un peu transparente.
    /// </summary>
    internal static void Stamp(IContainer container, SchoolCardHeader header, byte[] logo, bool paid)
    {
        const float d = CardDesign.StampMm;
        var ink = CardDesign.WithAlpha(CardDesign.StampInk, CardDesign.StampAlpha);
        var (ringPt, glyphs) = CardDesign.StampRingLayout(CardDesign.StampRing(header));
        var radius = d / 2f - CardDesign.StampTextInsetMm;
        var glyphW = ringPt * CardDesign.PtToMm * 1.6f;
        var glyphH = ringPt * CardDesign.PtToMm * 1.7f;
        var innerD = d - 2 * CardDesign.StampInnerInsetMm;
        var logoD = innerD * CardDesign.StampLogoRatio;

        container.Width(Mm(d)).Height(Mm(d)).Layers(layers =>
        {
            layers.PrimaryLayer().Svg(RingsSvg(d));
            foreach (var (glyph, angle) in glyphs)
            {
                if (glyph == " ") continue;
                layers.Layer().TranslateX(Mm(d / 2f)).TranslateY(Mm(d / 2f)).Rotate(angle)
                    .TranslateX(-Mm(glyphW / 2f)).TranslateY(-Mm(radius + glyphH / 2f))
                    .Unconstrained().Width(Mm(glyphW)).Height(Mm(glyphH)).AlignCenter().AlignMiddle()
                    .Text(glyph).FontFamily(CardDesign.SansFamily).Bold().FontSize(ringPt).FontColor(ink);
            }
            if (CardDesign.StampAcronym(header) is { } acronym)
                layers.Layer().PaddingTop(Mm(CardDesign.StampInnerInsetMm + 0.9f)).AlignCenter().AlignTop()
                    .Text(acronym).FontFamily(CardDesign.SansFamily).Bold().FontSize(4.3f).LetterSpacing(0.08f).FontColor(ink);
            layers.Layer().PaddingTop(Mm(1.2f)).AlignCenter().AlignMiddle().Width(Mm(logoD)).Height(Mm(logoD)).Image(logo).FitArea();
            if (paid)
                layers.Layer().PaddingTop(Mm(2.6f)).AlignCenter().AlignMiddle().Text(CardDesign.PaidMark)
                    .FontFamily(CardDesign.SansFamily).ExtraBold().FontSize(CardDesign.PaidPt).LetterSpacing(0.03f)
                    .FontColor(CardDesign.WithAlpha(CardDesign.PaidInk, CardDesign.PaidAlpha));
        });
    }

    /// <summary>Filets du cachet en SVG, en millimètres.</summary>
    private static string RingsSvg(float d)
    {
        var c = (d / 2f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        string R(float inset) => (d / 2f - inset).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var size = d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var color = CardDesign.StampInk;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}mm\" height=\"{size}mm\" viewBox=\"0 0 {size} {size}\">" +
            $"<g fill=\"none\" stroke=\"{color}\" stroke-opacity=\"0.85\">" +
            $"<circle cx=\"{c}\" cy=\"{c}\" r=\"{R(CardDesign.StampOuterInsetMm)}\" stroke-width=\"0.36\"/>" +
            $"<circle cx=\"{c}\" cy=\"{c}\" r=\"{R(CardDesign.StampSecondInsetMm)}\" stroke-width=\"0.13\"/>" +
            $"<circle cx=\"{c}\" cy=\"{c}\" r=\"{R(CardDesign.StampInnerInsetMm)}\" stroke-width=\"0.24\"/>" +
            "</g></svg>";
    }

    /// <summary>Le cachet seul, en image PNG transparente à 600 ppp : pour les cartes Word.</summary>
    internal static byte[] StampPng(SchoolCardHeader header, byte[] logo, bool paid) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(Mm(CardDesign.StampMm), Mm(CardDesign.StampMm));
            page.Margin(0);
            page.PageColor(Colors.Transparent);
            page.Content().Element(c => Stamp(c, header, logo, paid));
        })).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 600 }).First();

    /// <summary>
    /// Repère de découpe vertical : pointillés au centre de l'interstice horizontal, sur toute la hauteur de la rangée.
    /// La coupe passe ainsi entre deux cartes, sans entamer leur bordure ni leur bande de couleur.
    /// </summary>
    private static void CutLineDown(ColumnDescriptor gutter)
    {
        // Dix paires de 2,5 mm = 50 mm ; ne doit jamais dépasser 53,98 mm,
        // faute de quoi l'interstice agrandirait la rangée et fausserait les coupes.
        for (var i = 0; i < 10; i++)
        {
            gutter.Item().Height(Mm(2.5f)).AlignCenter().Width(Mm(0.3f)).Background(Guide);
            gutter.Item().Height(Mm(2.5f));
        }
    }

    /// <summary>Repère de découpe horizontal : pointillés au centre de l'interstice vertical, sur toute la largeur de la grille.</summary>
    private static void CutLineAcross(RowDescriptor gutter)
    {
        // 35 paires de 2,5 mm = 175 mm, à l'intérieur de la largeur 177,2 mm.
        for (var i = 0; i < 35; i++)
        {
            gutter.ConstantItem(Mm(2.5f)).AlignMiddle().Height(Mm(0.3f)).Background(Guide);
            gutter.ConstantItem(Mm(2.5f));
        }
    }

    /// <summary>Sceau de l'établissement, utilisé à défaut de logo dans le profil.</summary>
    internal static byte[] DefaultSeal()
    {
        if (defaultSeal is not null) return defaultSeal;
        using var stream = typeof(StudentCardPdf).Assembly.GetManifestResourceStream("Scolia.Infrastructure.Documents.Assets.sceau_itm.png")
            ?? throw new InvalidOperationException("Sceau de l'établissement introuvable dans l'application.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        defaultSeal = memory.ToArray();
        return defaultSeal;
    }
}
