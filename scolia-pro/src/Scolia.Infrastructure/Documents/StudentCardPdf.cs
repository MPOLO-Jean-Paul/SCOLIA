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
    private const string Navy = "#0B3B6F";
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
    /// Carte 85,60 x 53,98 mm : les seuls contenus obligatoires sont l'établissement configuré
    /// et le dossier scolaire. Le QR remplace le sceau rond du modèle papier.
    /// </summary>
    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] schoolLogo)
    {
        var authority = First(d.Header.Ministry, d.Header.Direction, d.Header.Division);
        var place = First(d.Header.Address, d.Header.CityZone, d.Header.Country);
        var schoolName = d.Header.Institute.Trim().ToUpperInvariant();
        var fullName = StudentCardRules.FullName(card.LastName, card.MiddleName, card.FirstName);

        container.CornerRadius(Mm(1.8f)).Border(0.6f).BorderColor(Frame).Background(Paper)
            .Padding(Mm(1.6f)).Column(column =>
        {
            // En-tête : logo réel de l'école et dénomination, jamais un emblème intégré.
            column.Item().Height(Mm(11.6f)).Row(row =>
            {
                row.ConstantItem(Mm(10.6f)).AlignMiddle().Image(schoolLogo).FitArea();
                row.RelativeItem().PaddingLeft(Mm(1.6f)).AlignMiddle().Column(header =>
                {
                    header.Spacing(Mm(0.25f));
                    header.Item().Text(schoolName)
                        .FontSize(schoolName.Length > 92 ? 4.1f : schoolName.Length > 58 ? 4.7f : 6.1f)
                        .Bold().FontColor(Navy).LineHeight(1.04f);
                    if (authority is not null)
                        header.Item().Text(authority).FontSize(3.9f).FontColor(Muted).LineHeight(1.0f);
                    if (place is not null)
                        header.Item().Text(place).FontSize(4f).FontColor(Muted).LineHeight(1.0f);
                });
            });
            column.Item().Height(Mm(0.32f)).Background(Navy);

            // Titre central du modèle ; le QR occupe l'ancienne zone du cachet.
            column.Item().Height(Mm(19.8f)).Row(row =>
            {
                row.RelativeItem().PaddingTop(Mm(2.0f)).Column(title =>
                {
                    title.Item().Text(text =>
                    {
                        text.Span("Session ").FontSize(13f).Bold().FontColor(Navy);
                        text.Span(StudentCardRules.SessionYear(d.Session)).FontSize(14.7f).Bold().FontColor("#1261A0");
                    });
                    title.Item().PaddingTop(Mm(0.9f)).LineHorizontal(0.55f).LineColor("#1261A0");
                    title.Item().PaddingTop(Mm(0.8f)).Text("ACCÈS AUX EXAMENS")
                        .FontSize(6.2f).SemiBold().Italic().FontColor(Navy);
                    title.Item().PaddingTop(Mm(0.5f)).Text(d.Session)
                        .FontSize(5.0f).FontColor(Muted);
                });
                row.ConstantItem(Mm(18.8f)).AlignMiddle().Column(qr =>
                {
                    qr.Item().Height(Mm(17f)).AlignCenter()
                        .Image(DocumentService.QrPng(card.QrText)).FitArea();
                    qr.Item().AlignCenter().Text("Contrôle QR")
                        .FontSize(3.7f).FontColor(Muted);
                });
            });

            // Présentation nominative proche du modèle fourni.
            column.Item().PaddingTop(Mm(0.7f)).Column(identity =>
            {
                identity.Spacing(Mm(0.7f));
                Field(identity, "MATRICULE", card.Matricule, 7.2f);
                Field(identity, "NOMS", fullName, fullName.Length > 48 ? 5.2f
                    : fullName.Length > 32 ? 6f : 7.1f);
                Field(identity, "PROMOTION", card.Classe, 7.2f);
            });
            column.Item().ExtendVertical().AlignBottom()
                .Text($"Année scolaire {d.AcademicYear}").FontSize(4.3f).FontColor(Muted);
        });
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

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
