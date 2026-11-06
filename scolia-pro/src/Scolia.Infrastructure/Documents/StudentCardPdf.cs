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
        var location = CardHeaderLayout.Location(d.Header);
        var contacts = CardHeaderLayout.ContactLines(d.Header);
        var schoolName = d.Header.Institute.Trim().ToUpperInvariant();
        var fullName = StudentCardRules.FullName(card.LastName, card.MiddleName, card.FirstName);

        container.Border(0.45f).BorderColor(Frame).Background(Paper)
            .Padding(Mm(1.35f)).Column(column =>
        {
            // Les champs institutionnels proviennent du paramétrage, sans mention fixe.
            // La hauteur de l'en-tête est bornée pour ne jamais pousser l'identité
            // au-delà du bord inférieur de la carte.
            column.Item().Height(Mm(15.4f)).Row(row =>
            {
                row.ConstantItem(Mm(10.2f)).AlignMiddle().Image(schoolLogo).FitArea();
                row.RelativeItem().PaddingLeft(Mm(1.15f)).AlignMiddle().Column(header =>
                {
                    header.Spacing(0);
                    foreach (var mention in administration)
                        header.Item().Text(mention).FontSize(mention.Length > 110 ? 2.65f :
                            mention.Length > 75 ? 2.85f : 3.05f).FontColor(Ink).LineHeight(1f);
                    header.Item().Text(schoolName)
                        .FontSize(schoolName.Length > 86 ? 3.8f :
                            schoolName.Length > 54 ? 4.3f : 5.15f)
                        .SemiBold().FontColor(Ink).LineHeight(1f);
                    if (location is not null)
                        header.Item().Text(location).FontSize(location.Length > 115 ? 2.5f :
                            location.Length > 72 ? 2.75f : 3f).FontColor(Muted).LineHeight(1f);
                });
            });
            column.Item().Height(Mm(0.2f)).Background("#BCC2CA");

            // Session moins envahissante, afin de réserver des lignes aux
            // matricules, noms et promotions : la carte reste 85,60 x 53,98 mm.
            column.Item().Height(Mm(14.2f)).Row(row =>
            {
                row.RelativeItem().PaddingTop(Mm(1f)).Column(title =>
                {
                    title.Item().AlignCenter().Text("Session " + StudentCardRules.SessionYear(d.Session))
                        .FontSize(10.5f).Bold().Underline().FontColor("#737982");
                    title.Item().PaddingTop(Mm(0.45f)).AlignCenter()
                        .Text("ACCÈS AUX EXAMENS").FontSize(5f).SemiBold().FontColor(Ink);
                    title.Item().PaddingTop(Mm(0.25f)).AlignCenter()
                        .Text(d.Session).FontSize(3.9f).FontColor(Muted);
                });
                row.ConstantItem(Mm(17f)).AlignMiddle().Column(qr =>
                {
                    qr.Item().Height(Mm(12.4f)).AlignCenter()
                        .Image(DocumentService.QrPng(card.QrText)).FitArea();
                    qr.Item().AlignCenter().Text("Contrôle QR")
                        .FontSize(3.25f).FontColor(Muted);
                });
            });

            // Cette zone n'est pas facultative : l'apprenant est toujours visible.
            column.Item().PaddingTop(Mm(0.45f)).Row(row =>
            {
                row.RelativeItem().Column(identity =>
                {
                    identity.Spacing(Mm(0.55f));
                    Field(identity, "MATRICULE", card.Matricule, 6.5f);
                    Field(identity, "NOMS", fullName, fullName.Length > 90 ? 3.9f
                        : fullName.Length > 60 ? 4.7f : fullName.Length > 40 ? 5.4f : 6.5f);
                    Field(identity, "PROMOTION", card.Classe, 6.5f);
                });
                row.ConstantItem(Mm(7.5f)).AlignRight().AlignBottom()
                    .Width(Mm(6f)).Height(Mm(6f)).Image(schoolLogo).FitArea();
            });
            // Contacts généraux, site et devise conservés dans le bas de la carte.
            column.Item().ExtendVertical().AlignBottom().Column(footer =>
            {
                footer.Spacing(0);
                foreach (var mention in contacts)
                    footer.Item().Text(mention).FontSize(2.9f).FontColor(Muted)
                        .LineHeight(1f);
                footer.Item().Text("Année scolaire " + d.AcademicYear)
                    .FontSize(3.8f).FontColor(Muted);
            });
        });
    }

    private static void Field(ColumnDescriptor identity, string label, string value, float size)
    {
        identity.Item().Text(text =>
        {
            text.Span(label + " : ").FontSize(6.0f).Bold().FontColor(Ink);
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
