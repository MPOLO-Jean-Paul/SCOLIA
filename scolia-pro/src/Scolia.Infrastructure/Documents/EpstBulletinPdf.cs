using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Bulletin du primaire et du secondaire, une page A4 paysage par élève : points par
/// période et par examen, totaux du trimestre ou du semestre, total général, pourcentage,
/// place, application et conduite. Une case vide signifie « aucune évaluation saisie ».
/// </summary>
public static class EpstBulletinPdf
{
    private const string Ink = "#172638";
    private const string Muted = "#526173";
    private const string Border = "#8E9AA8";
    private const string Light = "#EEF2F6";
    private const string Fail = "#B3261E";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static string N(decimal? value) => value is { } v ? v.ToString("0.##", Fr) : "";
    private static string Pct(decimal? value) => value is { } v ? v.ToString("0.0", Fr) + " %" : "";

    public static byte[] Build(EpstBulletinPrintDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentService.Initialize();
        return Document.Create(doc =>
        {
            foreach (var pupil in document.Pupils)
                doc.Page(page => Page(page, document, pupil));
        }).GeneratePdf();
    }

    private static void Page(PageDescriptor page, EpstBulletinPrintDto d, EpstPupilResultDto pupil)
    {
        var c = d.Class;
        var termWord = c.Terms.Count == 3 ? "Trimestre" : "Semestre";
        page.Size(PageSizes.A4.Landscape());
        page.Margin(22);
        page.DefaultTextStyle(style => style.FontFamily("Roboto").FontSize(8).FontColor(Ink));

        page.Header().Column(header =>
        {
            header.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO").Bold().FontSize(9);
                    left.Item().Text(string.IsNullOrWhiteSpace(d.Ministry)
                        ? "Ministère de l'Éducation nationale et de la Nouvelle citoyenneté" : d.Ministry).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(d.Division))
                        left.Item().Text(d.Division).FontSize(8).FontColor(Muted);
                });
                if (d.SchoolLogo is { Length: > 0 } logo)
                    row.ConstantItem(46).Height(46).Image(logo).FitArea();
                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(d.SchoolName).Bold().FontSize(11);
                    var place = string.Join(" · ", new[] { d.SchoolAddress, d.SchoolCity }
                        .Where(v => !string.IsNullOrWhiteSpace(v)));
                    if (place.Length > 0) right.Item().AlignRight().Text(place).FontSize(8).FontColor(Muted);
                });
            });
            header.Item().PaddingTop(5).AlignCenter()
                .Text($"BULLETIN DE L'ÉLÈVE — {c.CycleName.ToUpper(Fr)} — ANNÉE SCOLAIRE {c.YearLabel}")
                .Bold().FontSize(11);
            header.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(t => { t.Span("Élève : ").FontColor(Muted); t.Span(pupil.FullName).Bold(); });
                row.ConstantItem(150).Text(t => { t.Span("Matricule : ").FontColor(Muted); t.Span(pupil.Matricule); });
                row.ConstantItem(170).Text(t => { t.Span("Classe : ").FontColor(Muted); t.Span(c.ClassName).Bold(); });
                row.ConstantItem(90).AlignRight().Text(t => { t.Span("Effectif : ").FontColor(Muted); t.Span(c.Headcount.ToString(Fr)); });
            });
        });

        page.Content().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3.2f);                    // branche
                cols.ConstantColumn(30);                      // maximum par période
                foreach (var term in c.Terms)
                {
                    foreach (var _ in term.PeriodCodes) cols.RelativeColumn(1);
                    cols.RelativeColumn(1.15f);               // total du trimestre / semestre
                }
                cols.RelativeColumn(1.3f);                    // total général
                cols.RelativeColumn(1.1f);                    // maximum général
            });

            IContainer H(IContainer x) => x.Border(0.6f).BorderColor(Border).Background(Light)
                .PaddingVertical(2).PaddingHorizontal(2).AlignCenter().AlignMiddle();
            IContainer Cell(IContainer x) => x.Border(0.5f).BorderColor(Border)
                .PaddingVertical(2).PaddingHorizontal(2).AlignCenter().AlignMiddle();

            table.Header(h =>
            {
                h.Cell().RowSpan(2).Element(H).Text("BRANCHES").Bold();
                h.Cell().RowSpan(2).Element(H).Text("Max. période").FontSize(6.5f);
                for (var t = 0; t < c.Terms.Count; t++)
                    h.Cell().ColumnSpan((uint)c.Terms[t].PeriodCodes.Count + 1).Element(H)
                        .Text($"{(t == 0 ? "1er" : $"{t + 1}e")} {termWord.ToLower(Fr)}").Bold();
                h.Cell().RowSpan(2).Element(H).Text("TOTAL GÉNÉRAL").Bold().FontSize(7);
                h.Cell().RowSpan(2).Element(H).Text("MAX.").Bold();
                foreach (var term in c.Terms)
                {
                    foreach (var code in term.PeriodCodes) h.Cell().Element(H).Text(code).FontSize(7);
                    h.Cell().Element(H).Text("TOT.").Bold().FontSize(7);
                }
            });

            var periodIndex = 0;
            foreach (var line in pupil.Subjects)
            {
                table.Cell().Element(Cell).AlignLeft().Text(line.Name);
                var subject = c.Subjects.First(s => s.Id == line.SubjectId);
                table.Cell().Element(Cell).Text(N(subject.PeriodMax)).FontColor(Muted);
                periodIndex = 0;
                for (var t = 0; t < c.Terms.Count; t++)
                {
                    foreach (var _ in c.Terms[t].PeriodCodes)
                        table.Cell().Element(Cell).Text(N(line.Periods[periodIndex++].Points));
                    table.Cell().Element(Cell).Background(Light).Text(N(line.Terms[t].Points)).SemiBold();
                }
                table.Cell().Element(Cell).Background(Light).Text(N(line.Total)).Bold()
                    .FontColor(line.Failed ? Fail : Ink);
                table.Cell().Element(Cell).Text(N(line.Max)).FontColor(Muted);
            }

            void SummaryRow(string label, Func<int, string> periodCell, Func<int, string> termCell,
                string total, string max, bool bold = true)
            {
                table.Cell().ColumnSpan(2).Element(H).AlignLeft().Text(label).Bold();
                var index = 0;
                for (var t = 0; t < c.Terms.Count; t++)
                {
                    foreach (var _ in c.Terms[t].PeriodCodes)
                    {
                        var text = table.Cell().Element(Cell).Text(periodCell(index++));
                        if (bold) text.SemiBold();
                    }
                    table.Cell().Element(Cell).Background(Light).Text(termCell(t)).Bold();
                }
                table.Cell().Element(Cell).Background(Light).Text(total).Bold();
                table.Cell().Element(Cell).Text(max).FontColor(Muted);
            }

            SummaryRow("MAXIMA GÉNÉRAUX", i => N(pupil.PeriodTotals[i].Max), t => N(pupil.TermTotals[t].Max),
                N(pupil.Max), "", bold: false);
            SummaryRow("TOTAUX", i => N(pupil.PeriodTotals[i].Points), t => N(pupil.TermTotals[t].Points),
                N(pupil.Total), N(pupil.Max));
            SummaryRow("POURCENTAGE",
                i => pupil.PeriodTotals[i] is { Points: { } p, Max: > 0 } x ? Pct(p / x.Max * 100) : "",
                t => pupil.TermTotals[t] is { Points: { } p, Max: > 0 } x ? Pct(p / x.Max * 100) : "",
                Pct(pupil.Percent), "");
            if (c.ShowRank)
                SummaryRow("PLACE / EFFECTIF", _ => "",
                    t => pupil.TermRanks[t] is { } r ? $"{r}/{c.Headcount}" : "",
                    pupil.Rank is { } rank ? $"{rank}/{c.Headcount}" : "", "");
            var conductByPeriod = pupil.Conduct.ToDictionary(x => x.PeriodCode);
            var codes = c.Periods.Select(p => p.Code).ToList();
            SummaryRow("APPLICATION",
                i => conductByPeriod.TryGetValue(codes[i], out var m) ? m.Application ?? "" : "", _ => "", "", "", bold: false);
            SummaryRow("CONDUITE",
                i => conductByPeriod.TryGetValue(codes[i], out var m) ? m.Conduct ?? "" : "", _ => "", "", "", bold: false);
        });

        page.Footer().Column(footer =>
        {
            footer.Item().PaddingTop(4).Row(row =>
            {
                var status = pupil.MissingCells > 0
                    ? $"Résultats incomplets : {pupil.MissingCells} évaluation(s) non saisie(s)."
                    : pupil.FailedSubjects > 0
                        ? $"Branche(s) en échec (sous {N(c.SubjectPassPercent)} %) : {pupil.FailedSubjects}."
                        : "Aucune branche en échec.";
                row.RelativeItem().Text(status).FontColor(pupil.MissingCells > 0 || pupil.FailedSubjects > 0 ? Fail : Muted);
                row.ConstantItem(220).AlignRight().Text("Application et conduite : E, TB, B, AB, M, MA")
                    .FontSize(7).FontColor(Muted);
            });
            footer.Item().PaddingTop(10).Row(row =>
            {
                foreach (var who in new[] { "Le titulaire", "Le chef d'établissement", "Le parent ou tuteur" })
                    row.RelativeItem().AlignCenter().Column(sig =>
                    {
                        sig.Item().AlignCenter().Text(who).FontSize(8);
                        sig.Item().PaddingTop(22).AlignCenter().Text("………………………………").FontColor(Muted);
                    });
            });
            footer.Item().PaddingTop(4).Row(row =>
            {
                var rule = c.RegulationValidatedAtUtc is { } at
                    ? $"Règlement pédagogique {c.YearLabel} validé par {c.RegulationValidatedBy} le {at.ToLocalTime():dd/MM/yyyy}"
                    : "Règlement pédagogique validé";
                row.RelativeItem().Text($"SCOLIA · {rule} · Une case vide signifie : aucune évaluation saisie.")
                    .FontSize(6.5f).FontColor(Muted);
                row.ConstantItem(60).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(6.5f).FontColor(Muted));
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        });
    }
}
