using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Bulletin du primaire et du secondaire sur le modèle des bulletins officiels de la RDC,
/// une page A4 portrait par élève. La mise en page vient de <see cref="EpstBulletinLayout"/>,
/// partagée avec le téléphone serveur. Une case vide signifie « aucune évaluation saisie ».
/// </summary>
public static class EpstBulletinPdf
{
    private const string Ink = "#111111";
    private const string Muted = "#4B5563";
    private const string Rule = "#222222";
    private const string Shade = "#E5E7EB";
    private const string Black = "#1F1F1F";
    private const string Fail = "#B3261E";

    public static byte[] Build(EpstBulletinPrintDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentService.Initialize();
        return Document.Create(doc =>
        {
            foreach (var pupil in document.Pupils)
                doc.Page(page => Page(page, document, EpstBulletinLayout.For(document, pupil)));
        }).GeneratePdf();
    }

    private static void BoxRow(RowDescriptor row, string value, int minimum)
    {
        foreach (var ch in EpstBulletinLayout.Boxes(value, minimum))
            row.ConstantItem(11).Height(12).Border(0.6f).BorderColor(Rule).AlignCenter().AlignMiddle()
                .Text(ch).FontSize(7.5f).Bold();
    }

    private static void Field(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingVertical(1).Text(t =>
        {
            t.Span(label + " : ").FontSize(7.5f).FontColor(Muted);
            t.Span(value).FontSize(8.5f).Bold();
        });
    }

    private static void Page(PageDescriptor page, EpstBulletinPrintDto d, EpstBulletinLayout l)
    {
        page.Size(PageSizes.A4);
        page.Margin(18);
        page.DefaultTextStyle(style => style.FontFamily("Roboto").FontSize(7.5f).FontColor(Ink));

        page.Header().Column(header =>
        {
            header.Item().Border(0.8f).BorderColor(Rule).Padding(5).Row(row =>
            {
                row.ConstantItem(52).Height(39).Svg(DrcFlag.Svg);
                row.RelativeItem().AlignCenter().AlignMiddle().Column(mid =>
                {
                    mid.Item().AlignCenter().Text("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO").Bold().FontSize(10.5f);
                    mid.Item().AlignCenter().Text(l.Ministry.ToUpper(CultureInfo.GetCultureInfo("fr-FR"))).Bold().FontSize(8);
                    if (l.Division is { } division)
                        mid.Item().AlignCenter().Text(division).FontSize(7.5f).FontColor(Muted);
                });
                if (d.SchoolLogo is { Length: > 0 } logo)
                    row.ConstantItem(52).Height(39).Image(logo).FitArea();
                else
                    row.ConstantItem(52);
            });
            header.Item().BorderLeft(0.8f).BorderRight(0.8f).BorderBottom(0.8f).BorderColor(Rule).Row(row =>
            {
                row.RelativeItem().BorderRight(0.8f).BorderColor(Rule).Padding(5).Column(left =>
                {
                    foreach (var (label, value) in l.SchoolFields) Field(left, label, value);
                    left.Item().PaddingTop(2).Row(code =>
                    {
                        code.ConstantItem(42).AlignMiddle().Text("CODE :").FontSize(7.5f).FontColor(Muted);
                        BoxRow(code, l.SchoolCode, EpstBulletinLayout.CodeBoxes);
                    });
                });
                row.RelativeItem().Padding(5).Column(right =>
                {
                    foreach (var (label, value) in l.PupilFields) Field(right, label, value);
                    right.Item().PaddingTop(2).Row(perm =>
                    {
                        perm.ConstantItem(42).AlignMiddle().Text("N° PERM. :").FontSize(7.5f).FontColor(Muted);
                        BoxRow(perm, l.PermanentNumber, EpstBulletinLayout.PermanentBoxes);
                    });
                });
            });
            header.Item().BorderLeft(0.8f).BorderRight(0.8f).BorderBottom(0.8f).BorderColor(Rule)
                .Background(Shade).PaddingVertical(3).Column(title =>
                {
                    title.Item().AlignCenter().Text(l.Title).Bold().FontSize(10);
                    title.Item().AlignCenter().Text(l.Subtitle).Bold().FontSize(8.5f);
                });
        });

        page.Content().PaddingTop(4).Column(content =>
        {
            content.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    foreach (var w in l.Columns) cols.RelativeColumn(w);
                });
                foreach (var h in l.Head)
                {
                    var cell = table.Cell().Row((uint)h.Row + 1).Column((uint)h.Column + 1)
                        .RowSpan((uint)h.RowSpan).ColumnSpan((uint)h.ColumnSpan)
                        .Border(0.6f).BorderColor(Rule).Background(h.Black ? Black : Shade)
                        .PaddingVertical(2).PaddingHorizontal(1).AlignCenter().AlignMiddle();
                    cell.Text(h.Text).Bold().FontSize(h.Column == 0 ? 8.5f : 6.5f);
                }
                var rowNumber = 4u;
                foreach (var r in l.Rows)
                {
                    var label = table.Cell().Row(rowNumber).Column(1).Border(0.5f).BorderColor(Rule)
                        .Background(r.Kind == BulletinRowKind.Subject ? Colors.White : Shade)
                        .PaddingVertical(1.6f).PaddingHorizontal(3).AlignMiddle();
                    var text = label.Text(r.Label).FontSize(r.Kind == BulletinRowKind.Subject ? 7.5f : 7f);
                    if (r.Kind != BulletinRowKind.Subject) text.Bold();
                    for (var i = 0; i < r.Cells.Count; i++)
                    {
                        var c = r.Cells[i];
                        var box = table.Cell().Row(rowNumber).Column((uint)i + 2).Border(0.5f).BorderColor(Rule)
                            .Background(c.Tone switch
                            {
                                BulletinTone.Blocked => Black,
                                BulletinTone.Total or BulletinTone.Alert => "#F3F4F6",
                                _ => r.Kind == BulletinRowKind.Maxima ? Shade : Colors.White,
                            })
                            .PaddingVertical(1.6f).PaddingHorizontal(1).AlignCenter().AlignMiddle();
                        var value = box.Text(c.Text).FontSize(7.3f).FontColor(c.Tone == BulletinTone.Alert ? Fail : Ink);
                        if (c.Bold) value.Bold();
                    }
                    rowNumber++;
                }
            });

            content.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem(1.45f).PaddingRight(8).Column(left =>
                {
                    foreach (var m in l.Mentions)
                    {
                        var text = left.Item().PaddingBottom(2).Text("– " + m.Text).FontSize(7.8f);
                        if (m.Struck) text.Strikethrough().FontColor(Muted);
                        else text.Bold();
                    }
                    if (l.Decision is { } decision)
                        left.Item().PaddingTop(2).Text(t =>
                        {
                            t.Span("DÉCISION : ").FontSize(8).FontColor(Muted);
                            t.Span(decision).FontSize(9.5f).Bold();
                        });
                    if (l.Warning is { } warning)
                        left.Item().PaddingTop(2).Text(warning).FontSize(7.5f).FontColor(Fail).Bold();
                    left.Item().PaddingTop(12).Text("Signature de l'élève").FontSize(7.5f).FontColor(Muted);
                });
                row.RelativeItem().Column(right =>
                {
                    right.Item().AlignCenter().Text(l.PlaceAndDate).FontSize(8);
                    right.Item().PaddingTop(4).Row(sign =>
                    {
                        sign.RelativeItem().Column(seal =>
                        {
                            seal.Item().Height(52).Border(0.6f).BorderColor(Muted).AlignCenter().AlignMiddle()
                                .Text("Sceau de l'école").FontSize(7).FontColor(Muted);
                        });
                        sign.ConstantItem(8);
                        sign.RelativeItem(1.3f).Column(chief =>
                        {
                            chief.Item().AlignCenter().Text("Le Chef d'Établissement").FontSize(8).Bold();
                            chief.Item().PaddingTop(30).AlignCenter().Text("Noms et signature").FontSize(7).FontColor(Muted);
                        });
                    });
                });
            });
        });

        page.Footer().Column(footer =>
        {
            footer.Item().Text(EpstBulletinLayout.Footnote).FontSize(7);
            footer.Item().Text(EpstBulletinLayout.Notice).FontSize(7).Italic();
            footer.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text(l.SystemNote).FontSize(6).FontColor(Muted);
                row.ConstantItem(50).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(6).FontColor(Muted));
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        });
    }
}

/// <summary>Drapeau de la RDC : champ bleu ciel, diagonale rouge bordée de jaune, étoile jaune.</summary>
public static class DrcFlag
{
    public const string Sky = "#007FFF";
    public const string Yellow = "#F7D618";
    public const string Red = "#CE1021";

    /// <summary>Sommets de l'étoile à cinq branches dans un repère 800 × 600.</summary>
    public static IReadOnlyList<(float X, float Y)> Star()
    {
        const float cx = 140, cy = 130, outer = 105, inner = 42;
        var points = new List<(float, float)>();
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            points.Add(((float)(cx + radius * Math.Cos(angle)), (float)(cy + radius * Math.Sin(angle))));
        }
        return points;
    }

    public static readonly string Svg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 800 600\" width=\"800\" height=\"600\">" +
        "<defs><clipPath id=\"drc\"><rect width=\"800\" height=\"600\"/></clipPath></defs><g clip-path=\"url(#drc)\">" +
        $"<rect width=\"800\" height=\"600\" fill=\"{Sky}\"/>" +
        $"<line x1=\"-40\" y1=\"630\" x2=\"840\" y2=\"-30\" stroke=\"{Yellow}\" stroke-width=\"200\"/>" +
        $"<line x1=\"-40\" y1=\"630\" x2=\"840\" y2=\"-30\" stroke=\"{Red}\" stroke-width=\"140\"/>" +
        $"<polygon fill=\"{Yellow}\" points=\"" +
        string.Join(" ", Star().Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X:0.#},{p.Y:0.#}"))) +
        "\"/></g></svg>";
}
