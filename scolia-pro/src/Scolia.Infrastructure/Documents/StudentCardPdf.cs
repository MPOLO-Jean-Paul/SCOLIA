using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès en PDF, sur la grille de la version 79 : en-tête officiel avec le sceau, bandeau de titre et de session,
/// identité à gauche, QR à droite et bande de promotion avec la classe et le matricule. Huit cartes par A4, séparées par des interstices
/// de 6 et 7 mm où passent les repères de découpe : on coupe au milieu de l'interstice, sans rien retirer à la carte.
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

    private const float PageMargin = 10;
    private const float CardWidth = 92;
    private const float CardHeight = 63;
    private const float ColumnGap = 6;
    private const float RowGap = 7;

    private static byte[]? defaultSeal;

    public static byte[] Build(CardDocument d)
    {
        var seal = d.Header.Logo ?? DefaultSeal();
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
                                    if (index < cards.Count) cell.Element(c => Card(c, d, cards[index], seal));
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    private const float BandHeight = 6.2f;
    private const float RibbonHeight = 6f;

    /// <summary>
    /// Carte de 92 × 63 mm, du haut vers le bas : en-tête officiel avec le sceau, bandeau « Carte d'accès » et session,
    /// identité en libellés et valeurs, QR de contrôle légendé, puis la bande de la promotion avec la classe et le matricule.
    /// Coins arrondis et filet fin : la carte se lit d'un coup d'œil au contrôle et reste nette à l'impression.
    /// </summary>
    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] seal)
    {
        var bandText = InkOn(card.Color);
        // Hauteur fixe : la carte ne déborde jamais de sa case, la bande reste collée au bas.
        container.CornerRadius(Mm(2.2f)).Border(0.6f).BorderColor(Frame).Background(Paper).Column(column =>
        {
            column.Item().Height(Mm(CardHeight - BandHeight)).Column(body =>
            {
                // En-tête officiel : sceau à gauche, mentions et année scolaire centrées.
                body.Item().PaddingTop(Mm(1.5f)).PaddingHorizontal(Mm(3)).Height(Mm(15.6f)).Row(row =>
                {
                    row.ConstantItem(Mm(12.5f)).AlignMiddle().Image(seal).FitArea();
                    row.RelativeItem().PaddingLeft(Mm(1.5f)).AlignMiddle().Column(text =>
                    {
                        Mention(text, d.Header.Country, 5f, true, Ink);
                        Mention(text, d.Header.Ministry, 4.3f, false, Muted);
                        Mention(text, d.Header.Direction, 4.3f, false, Muted);
                        Mention(text, d.Header.Division, 4.3f, false, Muted);
                        Mention(text, d.Header.CityZone, 4.3f, false, Muted);
                        Mention(text, d.Header.Institute, 5.2f, true, Navy);
                        Mention(text, d.Header.Address, 4.3f, false, Muted);
                    });
                });

                // Bandeau : le titre de la carte et la session, lisibles de loin au contrôle.
                body.Item().PaddingTop(Mm(1)).Height(Mm(RibbonHeight)).Background(Navy).PaddingHorizontal(Mm(3)).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text("CARTE D'ACCÈS").FontSize(7.6f).Bold().FontColor(Colors.White).LetterSpacing(0.06f);
                    var session = $"SESSION {d.Session}";
                    row.AutoItem().AlignMiddle().Text(session).FontSize(FitSession(session)).Bold().FontColor(Colors.White);
                });

                // Identité à gauche, QR de contrôle à droite.
                body.Item().PaddingTop(Mm(2f)).PaddingHorizontal(Mm(4f)).Row(row =>
                {
                    row.RelativeItem().Column(identity =>
                    {
                        identity.Spacing(Mm(1f));
                        Field(identity, "NOM", card.LastName);
                        Field(identity, "POST-NOM", card.MiddleName);
                        Field(identity, "PRÉNOM", card.FirstName);
                    });
                    row.ConstantItem(Mm(19)).Column(qr =>
                    {
                        qr.Item().Height(Mm(17)).AlignCenter().Image(DocumentService.QrPng(card.QrText)).FitArea();
                        qr.Item().AlignCenter().Text("Contrôle d'accès").FontSize(4.2f).FontColor(Muted);
                    });
                });
                // Bas de carte : l'année scolaire à gauche, la place du visa de la direction à droite.
                body.Item().ExtendVertical().AlignBottom().PaddingHorizontal(Mm(4f)).PaddingBottom(Mm(1.4f)).Row(row =>
                {
                    row.RelativeItem().AlignBottom().Text($"Année scolaire {d.AcademicYear}").FontSize(5.2f).SemiBold().FontColor(Navy);
                    row.ConstantItem(Mm(30)).AlignBottom().Column(visa =>
                    {
                        visa.Item().PaddingBottom(Mm(0.6f)).LineHorizontal(0.4f).LineColor(Frame);
                        visa.Item().AlignCenter().Text("Visa de la direction").FontSize(4.2f).FontColor(Muted);
                    });
                });
            });

            // Bande de promotion : la classe et le matricule, dans la couleur de la promotion.
            column.Item().Height(Mm(BandHeight)).Background(card.Color).PaddingHorizontal(Mm(4f)).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(t =>
                {
                    t.Span("CLASSE  ").FontSize(5f).FontColor(bandText);
                    t.Span(card.Classe).FontSize(7.6f).Bold().FontColor(bandText);
                });
                if (card.Matricule.Length > 0)
                    row.AutoItem().AlignMiddle().Text(t =>
                    {
                        t.Span("MATRICULE  ").FontSize(5f).FontColor(bandText);
                        t.Span(card.Matricule).FontSize(7.6f).Bold().FontColor(bandText);
                    });
            });
        });
    }

    private static void Mention(ColumnDescriptor text, string? value, float size, bool bold, string color)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var line = text.Item().AlignCenter().Text(value.Trim().ToUpperInvariant()).FontSize(size).FontColor(color).LineHeight(1.05f);
        if (bold) line.Bold();
    }

    /// <summary>Un champ : petit libellé gris au-dessus, valeur en gras dessous, comme sur une pièce d'identité.</summary>
    private static void Field(ColumnDescriptor identity, string label, string value)
    {
        identity.Item().Column(field =>
        {
            field.Item().Text(label).FontSize(4.4f).SemiBold().FontColor(Muted).LetterSpacing(0.05f);
            field.Item().Text(value).FontSize(value.Length > 24 ? 6.8f : 8.2f).Bold().FontColor(Ink);
        });
    }

    /// <summary>Taille de la session dans le bandeau : réduite si l'intitulé est long, pour tenir sur une ligne.</summary>
    private static float FitSession(string title) => title.Length > 24 ? 6.4f : title.Length > 20 ? 7f : 7.6f;

    /// <summary>Texte de la bande : blanc sur une couleur foncée, encre sur une couleur claire comme le jaune.</summary>
    internal static string InkOn(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length < 6) return Ink;
        var r = Convert.ToInt32(h[..2], 16) / 255.0;
        var g = Convert.ToInt32(h[2..4], 16) / 255.0;
        var b = Convert.ToInt32(h[4..6], 16) / 255.0;
        return 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.55 ? Ink : "#FFFFFF";
    }

    /// <summary>
    /// Repère de découpe vertical : pointillés au centre de l'interstice horizontal, sur toute la hauteur de la rangée.
    /// La coupe passe ainsi entre deux cartes, sans entamer leur bordure ni leur bande de couleur.
    /// </summary>
    private static void CutLineDown(ColumnDescriptor gutter)
    {
        for (var i = 0; i < 12; i++)
        {
            gutter.Item().Height(Mm(2.5f)).AlignCenter().Width(Mm(0.3f)).Background(Guide);
            gutter.Item().Height(Mm(2.5f));
        }
    }

    /// <summary>Repère de découpe horizontal : pointillés au centre de l'interstice vertical, sur toute la largeur de la grille.</summary>
    private static void CutLineAcross(RowDescriptor gutter)
    {
        for (var i = 0; i < 38; i++)
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
