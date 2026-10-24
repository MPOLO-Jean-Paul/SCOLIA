using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Cartes d'accès en PDF, dans la mise en page de la version 79 : en-tête officiel avec le sceau, titre de session,
/// identité à gauche, QR à droite et bande de promotion en bas. Huit cartes par A4, séparées par des interstices
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

    private const float BandHeight = 5.6f;

    private static void Card(IContainer container, CardDocument d, CardItem card, byte[] seal)
    {
        // Hauteur fixe : la carte ne déborde jamais de sa case, la bande reste collée au bas.
        container.Border(0.5f).BorderColor(Frame).Background(Paper).Column(column =>
        {
            column.Item().Height(Mm(CardHeight - BandHeight)).Column(body =>
            {
                // En-tête officiel : sceau à gauche, sept mentions et l'année scolaire centrées.
                body.Item().PaddingTop(Mm(1.6f)).PaddingHorizontal(Mm(3)).Height(Mm(16.4f)).Row(row =>
                {
                    row.ConstantItem(Mm(13.5f)).AlignMiddle().Image(seal).FitArea();
                    row.RelativeItem().PaddingLeft(Mm(1.5f)).AlignMiddle().Column(text =>
                    {
                        Mention(text, d.Header.Country, 5.1f, true, Ink);
                        Mention(text, d.Header.Ministry, 4.4f, false, Ink);
                        Mention(text, d.Header.Direction, 4.4f, false, Ink);
                        Mention(text, d.Header.Division, 4.4f, false, Ink);
                        Mention(text, d.Header.CityZone, 4.4f, false, Ink);
                        Mention(text, d.Header.Institute, 4.9f, true, Ink);
                        Mention(text, d.Header.Address, 4.4f, false, Ink);
                        text.Item().AlignCenter().Text($"Année scolaire {d.AcademicYear}").FontSize(4.9f).Bold().FontColor(Navy);
                    });
                });

                body.Item().PaddingTop(Mm(1.2f)).PaddingHorizontal(Mm(3)).LineHorizontal(0.7f).LineColor(Navy);

                // Titre : session en gras, puis « CARTE D'ACCÈS » souligné.
                var title = $"SESSION {d.Session}";
                body.Item().PaddingTop(Mm(1.2f)).AlignCenter().Text(title).FontSize(FitSession(title)).Bold().FontColor(Navy);
                body.Item().PaddingTop(Mm(0.2f)).AlignCenter().Text("CARTE D'ACCÈS").FontSize(7.4f).Bold().Underline().FontColor(Ink);

                // Identité à gauche, QR à droite.
                body.Item().PaddingTop(Mm(2f)).PaddingHorizontal(Mm(4.2f)).Row(row =>
                {
                    row.RelativeItem().Column(identity =>
                    {
                        identity.Spacing(Mm(0.8f));
                        Field(identity, "NOM", card.LastName);
                        Field(identity, "POST-NOM", card.MiddleName);
                        Field(identity, "PRÉNOM", card.FirstName);
                        Field(identity, "CLASSE", card.Classe);
                    });
                    row.ConstantItem(Mm(20)).AlignMiddle().Image(DocumentService.QrPng(card.QrText)).FitArea();
                });
            });

            // Bande de promotion, collée au bas de la carte.
            column.Item().Height(Mm(BandHeight)).Background(card.Color);
        });
    }

    private static void Mention(ColumnDescriptor text, string? value, float size, bool bold, string color)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var line = text.Item().AlignCenter().Text(value.Trim().ToUpperInvariant()).FontSize(size).FontColor(color).LineHeight(1.05f);
        if (bold) line.Bold();
    }

    /// <summary>Une ligne par champ : libellé et valeur côte à côte, « NOM : MUKENDI ».</summary>
    private static void Field(ColumnDescriptor identity, string label, string value)
    {
        identity.Item().Text(text =>
        {
            text.Span($"{label} : ").FontSize(5.2f).Bold().FontColor(Muted);
            text.Span(value).FontSize(value.Length > 26 ? 6.6f : 7.4f).Bold().FontColor(Ink);
        });
    }

    /// <summary>Taille du titre de session : réduite si l'intitulé est long, pour tenir sur une ligne.</summary>
    private static float FitSession(string title) => title.Length > 24 ? 9.4f : title.Length > 20 ? 10.6f : 11.8f;

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
