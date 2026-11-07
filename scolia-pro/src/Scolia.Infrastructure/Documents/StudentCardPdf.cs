using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Modèle officiel type porte-badge : logo + nom dynamiques, Session en grand,
/// QR à la place du sceau, matricule / noms / promotion alignés.
 /// Huit cartes ISO ID-1 par A4, avec repères de découpe entre les cartes.
/// </summary>
internal static class StudentCardPdf
{
    // Marqueur du code de mise en page réellement chargé par le serveur.
    internal static readonly string LayoutId = "SCOLIA-CARTES-ID1-2026-11-V3";
    private const string Ink = "#1B2430";
    private const string Muted = "#5B6878";
    private const string Frame = "#AEB8C5";
    private const string Guide = "#8A96A6";
    private const string Paper = "#FFFFFF";

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
            foreach (var cards in pages)
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    // Pas de marge native : la grille est placée à la main, les repères peuvent ainsi sortir de la zone utile.
                    page.Margin(0);
                    page.DefaultTextStyle(s => s.FontFamily("Roboto").FontSize(7).FontColor(Ink));
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

    /// <summary>
    /// Modèle institutionnel : en-tête compact configuré, session centrée et
    /// soulignée, identité à gauche, QR dans sa zone supérieure droite.
    /// Le petit logo inférieur rappelle le cachet du modèle sans masquer le QR.
    /// </summary>
    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] schoolLogo)
    {
        var administration = CardHeaderLayout.AdministrativeLines(d.Header);
        var sideInfo = CardHeaderLayout.AddressLines(d.Header);
        var contacts = CardHeaderLayout.ContactLines(d.Header);
        var schoolName = d.Header.Institute.Trim().ToUpperInvariant();
        var fullName = StudentCardRules.FullName(card.LastName, card.MiddleName, card.FirstName);

        container.Border(0.45f).BorderColor(Frame).Background(Paper)
            .Padding(Mm(1.35f)).Column(column =>
        {
            // Logo de l'école à gauche ; mentions administratives au centre ;
            // adresse et localisation à droite, séparées par une barre discrète.
            column.Item().Height(Mm(15.6f)).Row(row =>
            {
                row.ConstantItem(Mm(10.2f)).AlignMiddle().Image(schoolLogo).FitArea();
                row.RelativeItem().PaddingLeft(Mm(1.15f)).PaddingRight(Mm(0.9f))
                    .AlignMiddle().Column(header =>
                {
                    header.Spacing(Mm(0.22f));
                    foreach (var mention in administration)
                        header.Item().Text(mention).FontSize(mention.Length > 110 ? 2.45f :
                            mention.Length > 75 ? 2.65f : 2.95f)
                            .FontColor(Ink).LineHeight(1.12f);
                    // Seul le nom de l'institution est en gras dans l'en-tête.
                    header.Item().Text(schoolName)
                        .FontSize(schoolName.Length > 86 ? 3.75f :
                            schoolName.Length > 54 ? 4.15f : 5.1f)
                        .Bold().FontColor(Ink).LineHeight(1.1f);
                });
                row.ConstantItem(Mm(23.4f)).BorderLeft(0.55f).BorderColor(Frame)
                    .PaddingLeft(Mm(1.2f)).AlignMiddle().Column(address =>
                {
                    address.Spacing(Mm(0.45f));
                    foreach (var line in sideInfo)
                        address.Item().Text(line).FontSize(line.Length > 90 ? 2.5f :
                            line.Length > 55 ? 2.8f : 3.2f)
                            .FontColor(Muted).LineHeight(1.13f);
                });
            });
            column.Item().Height(Mm(0.2f)).Background("#BCC2CA");

            // Titres de session visuellement dominants, sans déborder sur l'identité.
            column.Item().Height(Mm(14.4f)).Row(row =>
            {
                row.RelativeItem().PaddingTop(Mm(0.7f)).Column(title =>
                {
                    title.Item().AlignCenter().Text("Session " + StudentCardRules.SessionYear(d.Session))
                        .FontSize(12.8f).Bold().Underline().FontColor("#616975");
                    title.Item().PaddingTop(Mm(0.4f)).AlignCenter()
                        .Text("ACCÈS AUX EXAMENS").FontSize(6.1f).SemiBold().FontColor(Ink);
                    title.Item().PaddingTop(Mm(0.25f)).AlignCenter()
                        .Text(d.Session).FontSize(4.25f).FontColor(Muted);
                });
                row.ConstantItem(Mm(16.4f)).AlignMiddle().Column(qr =>
                {
                    qr.Item().Height(Mm(12f)).AlignCenter()
                        .Image(DocumentService.QrPng(card.QrText)).FitArea();
                    qr.Item().AlignCenter().Text("Contrôle QR")
                        .FontSize(3.15f).FontColor(Muted);
                });
            });

            // Identité plus grande et grasse, avec adaptation douce aux noms longs.
            column.Item().PaddingTop(Mm(0.5f)).Column(identity =>
            {
                identity.Spacing(Mm(0.48f));
                Field(identity, "MATRICULE", card.Matricule, 7.35f);
                Field(identity, "NOMS", fullName, fullName.Length > 90 ? 4.4f
                    : fullName.Length > 60 ? 5.2f : fullName.Length > 40 ? 6.2f : 7.6f);
                Field(identity, "PROMOTION", card.Classe, 7.35f);
            });

            // Pied épuré : uniquement devise et année à gauche,
            // deuxième logo strictement en bas à droite, comme celui de l'en-tête.
            column.Item().ExtendVertical().AlignBottom().Row(row =>
            {
                row.RelativeItem().AlignBottom().Column(footer =>
                {
                    footer.Spacing(Mm(0.22f));
                    foreach (var mention in contacts)
                        footer.Item().Text(mention).FontSize(3.7f).FontColor(Muted).LineHeight(1.15f);
                    footer.Item().Text("Année scolaire " + d.AcademicYear)
                        .FontSize(4.15f).FontColor(Muted);
                });
                row.ConstantItem(Mm(10.2f)).AlignRight().AlignBottom()
                    .Width(Mm(9.4f)).Height(Mm(9.4f)).Image(schoolLogo).FitArea();
            });
        });
    }

    private static void Field(ColumnDescriptor identity, string label, string value, float size)
    {
        identity.Item().Text(text =>
        {
            text.Span(label + " : ").FontSize(6.45f).Bold().FontColor(Ink);
            text.Span(value.Trim().ToUpperInvariant()).FontSize(size).Bold().FontColor(Ink);
        });
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
