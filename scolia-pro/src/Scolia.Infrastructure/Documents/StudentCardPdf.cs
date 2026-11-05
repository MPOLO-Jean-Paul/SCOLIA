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
    /// Modèle institutionnel : en-tête compact configuré, session centrée et
    /// soulignée, identité à gauche, QR dans sa zone supérieure droite.
    /// Le petit logo inférieur rappelle le cachet du modèle sans masquer le QR.
    /// </summary>
    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] schoolLogo)
    {
        var authority = Join(d.Header.Ministry, d.Header.Direction, d.Header.Division);
        var location = Join(d.Header.CityZone, d.Header.Address, d.Header.Country);
        var schoolName = d.Header.Institute.Trim().ToUpperInvariant();
        var fullName = StudentCardRules.FullName(card.LastName, card.MiddleName, card.FirstName);

        container.Border(0.45f).BorderColor(Frame).Background(Paper)
            .Padding(Mm(1.5f)).Column(column =>
        {
            // Petite entête de document officiel, semblable à la carte photographiée.
            // Toutes les mentions viennent des paramètres, aucune n'est codée en dur.
            column.Item().Height(Mm(11.8f)).Row(row =>
            {
                row.ConstantItem(Mm(11.4f)).AlignMiddle().Image(schoolLogo).FitArea();
                row.RelativeItem().PaddingLeft(Mm(1.5f)).AlignMiddle().Column(header =>
                {
                    header.Spacing(Mm(0.18f));
                    header.Item().Text(schoolName)
                        .FontSize(schoolName.Length > 86 ? 4f : schoolName.Length > 54 ? 4.6f : 5.6f)
                        .SemiBold().FontColor(Ink).LineHeight(1.03f);
                    if (authority is not null)
                        header.Item().Text(authority).FontSize(3.2f).FontColor(Ink).LineHeight(1f);
                    if (location is not null)
                        header.Item().Text(location).FontSize(3.0f).FontColor(Muted).LineHeight(1f);
                });
            });
            column.Item().Height(Mm(0.22f)).Background("#BCC2CA");

            // QR maintenu dans la colonne à droite de la session (pas dans l'en-tête).
            column.Item().Height(Mm(17.8f)).Row(row =>
            {
                row.RelativeItem().PaddingTop(Mm(1.7f)).Column(title =>
                {
                    title.Item().AlignCenter().Text("Session " + StudentCardRules.SessionYear(d.Session))
                        .FontSize(14.2f).Bold().Underline().FontColor("#737982");
                    title.Item().PaddingTop(Mm(0.6f)).AlignCenter()
                        .Text("ACCÈS AUX EXAMENS").FontSize(5.2f).SemiBold().FontColor(Ink);
                    title.Item().PaddingTop(Mm(0.4f)).AlignCenter()
                        .Text(d.Session).FontSize(4.1f).FontColor(Muted);
                });
                row.ConstantItem(Mm(18.8f)).AlignMiddle().Column(qr =>
                {
                    qr.Item().Height(Mm(17f)).AlignCenter()
                        .Image(DocumentService.QrPng(card.QrText)).FitArea();
                    qr.Item().AlignCenter().Text("Contrôle QR")
                        .FontSize(3.7f).FontColor(Muted);
                });
            });

            // Le modèle source présente une lecture en trois lignes à gauche.
            column.Item().PaddingTop(Mm(1.6f)).Row(row =>
            {
                row.RelativeItem().Column(identity =>
                {
                    identity.Spacing(Mm(1.1f));
                    Field(identity, "MATRICULE", card.Matricule, 7.0f);
                    Field(identity, "NOMS", fullName, fullName.Length > 90 ? 4f
                        : fullName.Length > 60 ? 4.8f : fullName.Length > 40 ? 5.7f : 6.9f);
                    Field(identity, "PROMOTION", card.Classe, 7.0f);
                });
                // Le même logo configuré remplace une image de sceau fixe en bas.
                row.ConstantItem(Mm(11f)).AlignRight().AlignBottom()
                    .Width(Mm(7f)).Height(Mm(7f)).Image(schoolLogo).FitArea();
            });
            column.Item().ExtendVertical().AlignBottom()
                .Text("Année scolaire " + d.AcademicYear).FontSize(4.1f).FontColor(Muted);
        });
    }

    private static string? Join(params string?[] values)
    {
        var parts = values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        var joined = string.Join(" · ", parts);
        return joined.Length == 0 ? null : joined;
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
