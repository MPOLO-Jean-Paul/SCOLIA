using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>Procès-verbal de délibération et palmarès d'une classe du primaire ou du secondaire.</summary>
public static class EpstPalmaresPdf
{
    private const string Ink = "#172638";
    private const string Muted = "#526173";
    private const string Border = "#8E9AA8";
    private const string Light = "#EEF2F6";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static string Label(string? outcome) => outcome switch
    {
        "Passe" => "Passe",
        "Repechage" => "Repêchage",
        "Redouble" => "Double",
        "PasseApresRepechage" => "Passe après repêchage",
        "Reoriente" => "Réorienté",
        "Incomplet" => "Incomplet",
        _ => "",
    };

    public static byte[] Build(EpstPalmaresPrintDto d)
    {
        ArgumentNullException.ThrowIfNull(d);
        DocumentService.Initialize();
        var del = d.Deliberation;
        var final = del.Status == "Cloturee";
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(s => s.FontFamily("Roboto").FontSize(8.5f).FontColor(Ink));
            page.Header().Column(h =>
            {
                h.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO").Bold().FontSize(9);
                        left.Item().Text(string.IsNullOrWhiteSpace(d.Ministry)
                            ? "Ministère de l'Éducation nationale et de la Nouvelle citoyenneté" : d.Ministry);
                        if (!string.IsNullOrWhiteSpace(d.Division)) left.Item().Text(d.Division).FontColor(Muted);
                    });
                    if (d.SchoolLogo is { Length: > 0 } logo) row.ConstantItem(44).Height(44).Image(logo).FitArea();
                    row.RelativeItem().AlignRight().Column(r =>
                    {
                        r.Item().AlignRight().Text(d.SchoolName).Bold().FontSize(11);
                        var place = string.Join(" · ", new[] { d.SchoolAddress, d.SchoolCity }
                            .Where(v => !string.IsNullOrWhiteSpace(v)));
                        if (place.Length > 0) r.Item().AlignRight().Text(place).FontColor(Muted);
                    });
                });
                h.Item().PaddingTop(8).AlignCenter().Text("PROCÈS-VERBAL DE DÉLIBÉRATION ET PALMARÈS").Bold().FontSize(12);
                h.Item().AlignCenter().Text($"{d.CycleName} · {del.ClassName} · Année scolaire {del.YearLabel}").FontColor(Muted);
                h.Item().PaddingTop(4).AlignCenter().Text(final
                    ? "Décisions définitives, repêchage compris"
                    : "Décisions de la première session ; le repêchage est en cours").FontSize(8).FontColor(Muted);
            });
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(24); c.ConstantColumn(34); c.RelativeColumn(4); c.RelativeColumn(1.6f);
                        c.RelativeColumn(1.3f); c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(2.2f);
                    });
                    IContainer H(IContainer x) => x.Border(0.6f).BorderColor(Border).Background(Light).Padding(3).AlignCenter();
                    IContainer C(IContainer x) => x.Border(0.5f).BorderColor(Border).Padding(3);
                    t.Header(h =>
                    {
                        foreach (var title in new[] { "N°", "Place", "Nom et prénom", "Matricule", "%", "Échecs",
                                     "1re session", final ? "Décision finale" : "Décision" })
                            h.Cell().Element(H).Text(title).Bold();
                    });
                    var n = 1;
                    foreach (var p in del.Pupils)
                    {
                        t.Cell().Element(C).AlignCenter().Text((n++).ToString(Fr));
                        t.Cell().Element(C).AlignCenter().Text(p.Rank?.ToString(Fr) ?? "");
                        t.Cell().Element(C).Text(p.FullName);
                        t.Cell().Element(C).Text(p.Matricule);
                        t.Cell().Element(C).AlignRight().Text(p.Percent is { } v ? v.ToString("0.0", Fr) : "");
                        t.Cell().Element(C).AlignCenter().Text(p.FailedSubjects.ToString(Fr));
                        t.Cell().Element(C).Text(Label(p.Jury ?? p.Computed) +
                            (p.Jury is not null ? " (jury)" : ""));
                        t.Cell().Element(C).Text(p.EffectiveLabel).Bold();
                    }
                });
                var counts = del.Pupils.GroupBy(p => p.EffectiveLabel).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key} : {g.Count()}");
                col.Item().PaddingTop(8).Text($"Effectif : {del.Pupils.Count} · " + string.Join(" · ", counts)).SemiBold();
                col.Item().PaddingTop(4).Text(
                    $"Seuil de réussite : {del.PassPercent.ToString("0.##", Fr)} % · seuil d'une branche : " +
                    $"{del.SubjectPassPercent.ToString("0.##", Fr)} % · repêchage jusqu'à {del.MaxRepechageSubjects} branche(s).")
                    .FontColor(Muted);
                var motives = del.Pupils.Where(p => p.Jury is not null).ToList();
                if (motives.Count > 0)
                {
                    col.Item().PaddingTop(8).Text("Décisions du jury").Bold();
                    foreach (var p in motives)
                        col.Item().Text($"{p.FullName} : {Label(p.Jury)} — {p.JuryReason}");
                }
                col.Item().PaddingTop(28).Row(row =>
                {
                    foreach (var who in new[] { "Le secrétaire du jury", "Le président du jury", "Le chef d'établissement" })
                        row.RelativeItem().AlignCenter().Column(sig =>
                        {
                            sig.Item().AlignCenter().Text(who);
                            sig.Item().PaddingTop(26).AlignCenter().Text("………………………………").FontColor(Muted);
                        });
                });
            });
            page.Footer().Row(row =>
            {
                var closed = del.ClosedAtUtc ?? del.FirstSessionClosedAtUtc;
                row.RelativeItem().Text($"SCOLIA · Délibération clôturée par {del.ClosedBy ?? del.FirstSessionClosedBy}" +
                    (closed is { } at ? $" le {at.ToLocalTime():dd/MM/yyyy HH:mm}" : "")).FontSize(6.5f).FontColor(Muted);
                row.ConstantItem(50).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(6.5f).FontColor(Muted));
                    t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                });
            });
        })).GeneratePdf();
    }
}
