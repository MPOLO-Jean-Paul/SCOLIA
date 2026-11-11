using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// A4 pédagogique provisoire : jamais un bulletin ministériel certifié.
/// Aucune note, moyenne, décision de jury ou cote manquante n'est déduite ici.
/// Le moteur utilise les mêmes polices intégrées que les autres PDF de SCOLIA.
/// </summary>
public static class SchoolCycleBulletinPdf
{
    private const string Ink = "#172638";
    private const string Muted = "#526173";
    private const string Border = "#BFC9D3";
    private const string Light = "#F1F4F7";
    private const string Alert = "#9A3229";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static byte[] Build(SchoolCycleBulletinPrintDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var preview = document.Preview ?? throw new ArgumentException("Aperçu pédagogique obligatoire.", nameof(document));
        if (preview.IsOfficial)
            throw new InvalidOperationException("Ce générateur ne peut produire que des documents pédagogiques non officiels.");

        DocumentService.Initialize();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(style => style.FontFamily("Roboto").FontSize(9).FontColor(Ink));

            page.Header().Column(header =>
            {
                header.Item().Text("DOCUMENT PÉDAGOGIQUE NON OFFICIEL")
                    .Bold().FontSize(10).FontColor(Alert);
                header.Item().PaddingTop(7).Text(document.SchoolName)
                    .Bold().FontSize(13).FontColor(Ink);
                var address = string.Join(" · ", new[] { document.SchoolAddress, document.SchoolCity }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(address))
                    header.Item().Text(address).FontSize(8).FontColor(Muted);
                header.Item().PaddingTop(8).BorderBottom(1).BorderColor(Border);
            });

            page.Content().PaddingTop(15).Column(column =>
            {
                column.Spacing(7);
                column.Item().Text(preview.DocumentTitle).Bold().FontSize(14).FontColor(Ink);
                column.Item().Text($"Année : {preview.AcademicYear}    Cycle : {preview.Cycle}    Classe : {preview.Promotion}")
                    .FontSize(9).FontColor(Muted);
                column.Item().Text($"Élève : {document.StudentName}    Matricule : {document.Matricule}")
                    .FontSize(9).SemiBold();
                var particulars = string.Join(" · ", new[] { preview.GradeBand, preview.OptionLabel }
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value != "Cycle non précisé"));
                if (!string.IsNullOrWhiteSpace(particulars))
                    column.Item().Text(particulars).FontSize(8).FontColor(Muted);

                column.Item().PaddingVertical(7).BorderBottom(0.7f).BorderColor(Border)
                    .Text(preview.Notice).FontSize(8).FontColor(Alert);

                if (preview.SubjectRows is { Count: > 0 } subjects)
                    WriteSubjectTables(column, subjects, preview.Terms ?? []);
                else
                    WriteObservations(column, preview.Terms ?? []);
            });

            page.Footer().BorderTop(0.6f).BorderColor(Border).PaddingTop(6)
                .Row(row =>
                {
                    row.RelativeItem().Text("SCOLIA · Relevé de consultation, non certifié · Aucun classement ni décision de réussite")
                        .FontSize(7).FontColor(Alert);
                    row.ConstantItem(65).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(7).FontColor(Muted));
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
        })).GeneratePdf();
    }

    private static void WriteObservations(
        ColumnDescriptor content, IReadOnlyList<SchoolCycleBulletinTermDto> terms)
    {
        if (terms.Count == 0)
        {
            content.Item().Text("Aucune période pédagogique configurée.").FontColor(Muted);
            return;
        }

        foreach (var term in terms)
        {
            content.Item().PaddingTop(9).Text(term.Label).Bold().FontSize(11);
            foreach (var period in term.Periods)
            {
                content.Item().Border(0.6f).BorderColor(Border).Padding(9).Column(cell =>
                {
                    cell.Spacing(5);
                    cell.Item().Text($"{period.Code} · {period.Name}").SemiBold();
                    if (period.Entries.Count == 0)
                        cell.Item().Text("Aucune observation saisie.").FontColor(Muted);
                    foreach (var entry in period.Entries)
                    {
                        cell.Item().Text(entry.Title).Bold();
                        cell.Item().Text(entry.Observation ?? "Observation non renseignée.")
                            .FontColor(Ink);
                        if (!string.IsNullOrWhiteSpace(entry.Comment))
                            cell.Item().Text(entry.Comment).Italic().FontColor(Muted);
                    }
                });
            }
        }
    }

    private static void WriteSubjectTables(
        ColumnDescriptor content,
        IReadOnlyList<SchoolCycleBulletinSubjectDto> subjects,
        IReadOnlyList<SchoolCycleBulletinTermDto> terms)
    {
        // Répéter les branches par groupes de quatre périodes pour garder des
        // cellules lisibles en A4 portrait, même sur un petit programme.
        var periods = terms.SelectMany(term => term.Periods)
            .GroupBy(period => period.Id).Select(group => group.First()).ToArray();
        if (periods.Length == 0)
        {
            content.Item().Text("Aucune période pédagogique configurée.").FontColor(Muted);
            return;
        }

        for (var index = 0; index < periods.Length; index += 4)
        {
            var group = periods.Skip(index).Take(4).ToArray();
            content.Item().PaddingTop(9).Text($"Branches · périodes {group.First().Code} à {group.Last().Code}")
                .Bold().FontSize(10);
            content.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.4f);
                    foreach (var _ in group)
                        columns.RelativeColumn(1.5f);
                });
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Branche");
                    foreach (var period in group)
                        header.Cell().Element(HeaderCell).Text($"{period.Code}\n{period.Name}");
                });
                foreach (var subject in subjects)
                {
                    table.Cell().Element(BodyCell).Text(text =>
                    {
                        text.Span(subject.SubjectName).SemiBold();
                        if (!subject.IsActive)
                            text.Span("\n(archivée)").FontSize(7).FontColor(Muted);
                    });
                    foreach (var period in group)
                    {
                        var cell = subject.Periods.FirstOrDefault(p => p.SchoolCyclePeriodId == period.Id);
                        var entries = cell?.Entries ?? [];
                        table.Cell().Element(BodyCell).Text(entries.Count == 0
                            ? "—"
                            : string.Join("\n", entries.Select(entry =>
                            {
                                var value = entry.Score is { } score && entry.MaxScore is { } maximum
                                    ? $"{score.ToString("0.##", Fr)} / {maximum.ToString("0.##", Fr)}"
                                    : "Non coté";
                                return entries.Count == 1 ? value : $"{entry.Title} : {value}";
                            })));
                    }
                }
            });
        }

        content.Item().PaddingTop(6).Text("— : aucune évaluation saisie. Les évaluations multiples ne sont pas additionnées.")
            .FontSize(7.5f).FontColor(Muted);
    }

    private static IContainer HeaderCell(IContainer cell) =>
        cell.Border(0.5f).BorderColor(Border).Background(Light).Padding(5);

    private static IContainer BodyCell(IContainer cell) =>
        cell.Border(0.5f).BorderColor(Border).Padding(5);
}
