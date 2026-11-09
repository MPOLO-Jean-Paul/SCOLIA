using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès en PDF (ordinateur), dessinées d'après <see cref="CardDesign"/> : en-tête centré de l'établissement
/// configuré, « Session AAAA » souligné, identité en capitales espacées, QR de contrôle discret en haut à droite.
/// Ni logo ni cachet : ils sont apposés à la main après l'impression, aux deux places laissées libres.
/// Huit cartes ISO ID-1 par A4, avec repères de découpe entre les cartes.
/// </summary>
internal static class StudentCardPdf
{
    // Marqueur du code de mise en page réellement chargé par le serveur.
    internal static readonly string LayoutId = "SCOLIA-CARTES-ID1-2026-11-V5";
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
        var pages = new List<IReadOnlyList<CardItem>>();
        for (var start = 0; start < d.Cards.Count; start += StudentCardRules.CardsPerPage)
            pages.Add(d.Cards.Skip(start).Take(StudentCardRules.CardsPerPage).ToList());

        return Document.Create(doc =>
        {
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
                                    if (index < cards.Count) cell.Element(c => Card(c, d, cards[index]));
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

    private static void Card(IContainer container, CardDocument d, CardItem card)
    {
        var header = d.Header;
        container.Layers(layers =>
        {
            // Le cadre fin sert de repère de découpe ; le fond reste blanc comme la carte modèle.
            layers.PrimaryLayer().Border(0.4f).BorderColor(CardDesign.Frame).Background(CardDesign.Paper);

            // En-tête centré : place libre du logo (apposé à la main), puis nom, service, filet et contacts.
            At(layers, CardDesign.HeaderLeftMm, CardDesign.HeaderTopMm, CardDesign.LogoMm + CardDesign.HeaderGapMm + CardDesign.HeaderTextMm,
                CardDesign.HeaderHeightMm).Row(row =>
            {
                row.ConstantItem(Mm(CardDesign.LogoMm));
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
                At(layers, CardDesign.IdentityLeftMm, CardDesign.IdentityTopMm + i * CardDesign.IdentityStepMm, CardDesign.IdentityWidthMm, CardDesign.IdentityLineMm)
                    .ScaleToFit().Text(text =>
                    {
                        text.Span(label + " : ").Style(Identity(size));
                        text.Span(value).Style(Identity(size));
                    });
            }
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
