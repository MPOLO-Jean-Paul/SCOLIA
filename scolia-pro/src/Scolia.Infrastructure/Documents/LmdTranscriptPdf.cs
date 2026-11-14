using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Relevé de notes LMD, une page A4 par étudiant : UE et EC par semestre, notes, crédits,
/// moyennes, crédits capitalisés et décision publiée. Étape « S1 » : résultats partiels.
/// </summary>
public static class LmdTranscriptPdf
{
    internal const string Ink = "#172638";
    internal const string Muted = "#526173";
    internal const string Border = "#8E9AA8";
    internal const string Light = "#EEF2F6";
    internal const string Fail = "#B3261E";
    internal static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    internal static string N(decimal? v) => v is { } x ? x.ToString("0.##", Fr) : "";

    internal static string StageTitle(string stage) => stage switch
    {
        "S1" => "RÉSULTATS PARTIELS DU PREMIER SEMESTRE",
        "PremiereSession" => "RÉSULTATS DE LA PREMIÈRE SESSION",
        _ => "RÉSULTATS DÉFINITIFS DE L'ANNÉE",
    };

    internal static void Header(ColumnDescriptor h, LmdTranscriptPrintDto d, string title)
    {
        var p = d.Promotion;
        h.Item().Row(row =>
        {
            if (d.SchoolLogo is { Length: > 0 } logo) row.ConstantItem(44).Height(44).Image(logo).FitArea();
            row.RelativeItem().PaddingLeft(d.SchoolLogo is { Length: > 0 } ? 8 : 0).Column(c =>
            {
                c.Item().Text("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO").Bold().FontSize(8.5f);
                c.Item().Text("Enseignement supérieur et universitaire").FontSize(8).FontColor(Muted);
                c.Item().Text(d.SchoolName).Bold().FontSize(11);
                var place = string.Join(" · ", new[] { d.SchoolAddress, d.SchoolCity }.Where(v => !string.IsNullOrWhiteSpace(v)));
                if (place.Length > 0) c.Item().Text(place).FontSize(8).FontColor(Muted);
            });
        });
        h.Item().PaddingTop(6).AlignCenter().Text(title).Bold().FontSize(11);
        h.Item().AlignCenter().Text($"{StageTitle(p.Stage)} · Année académique {p.YearLabel}").FontSize(8.5f).FontColor(Muted);
        var name = p.PromotionName.Contains(p.Level, StringComparison.OrdinalIgnoreCase) ? p.PromotionName : $"{p.PromotionName} ({p.Level})";
        h.Item().AlignCenter().Text(name + (p.Filiere is { } f && !name.Contains(f, StringComparison.OrdinalIgnoreCase) ? $" · {f}" : ""))
            .FontSize(8.5f).SemiBold();
    }

    public static byte[] Build(LmdTranscriptPrintDto d)
    {
        ArgumentNullException.ThrowIfNull(d);
        DocumentService.Initialize();
        var p = d.Promotion;
        return Document.Create(doc =>
        {
            foreach (var s in d.Students)
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(t => t.FontFamily("Roboto").FontSize(8).FontColor(Ink));
                    page.Header().Column(h =>
                    {
                        Header(h, d, "RELEVÉ DE NOTES");
                        h.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text(t => { t.Span("Étudiant : ").FontColor(Muted); t.Span(s.FullName).Bold(); });
                            r.ConstantItem(160).AlignRight().Text(t => { t.Span("Matricule : ").FontColor(Muted); t.Span(s.Matricule); });
                        });
                    });
                    page.Content().PaddingTop(8).Column(col =>
                    {
                        foreach (var semester in s.Units.GroupBy(u => u.Semester))
                        {
                            var summary = semester.Key == "S1" ? s.S1 : s.S2;
                            col.Item().PaddingTop(4).Text(semester.Key == "S1" ? "Premier semestre" : "Deuxième semestre").Bold().FontSize(9.5f);
                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(52); c.RelativeColumn(4); c.ConstantColumn(44); c.ConstantColumn(44);
                                    c.ConstantColumn(40); c.ConstantColumn(56);
                                });
                                IContainer H(IContainer x) => x.Border(0.6f).BorderColor(Border).Background(Light).Padding(2).AlignCenter();
                                IContainer C(IContainer x) => x.Border(0.5f).BorderColor(Border).Padding(2);
                                t.Header(hh =>
                                {
                                    foreach (var title in new[] { "Code", "Unité d'enseignement / élément constitutif", "Crédits", "Note", "Sess.", "Résultat" })
                                        hh.Cell().Element(H).Text(title).Bold().FontSize(7);
                                });
                                foreach (var ue in semester)
                                {
                                    t.Cell().Element(C).Background(Light).Text(ue.Code).Bold();
                                    t.Cell().Element(C).Background(Light).Text(ue.Name).Bold();
                                    t.Cell().Element(C).Background(Light).AlignCenter().Text(N(ue.Credits)).Bold();
                                    t.Cell().Element(C).Background(Light).AlignCenter().Text(N(ue.Average)).Bold()
                                        .FontColor(ue.Acquired ? Ink : Fail);
                                    t.Cell().Element(C).Background(Light).Text("");
                                    t.Cell().Element(C).Background(Light).AlignCenter()
                                        .Text(ue.Average is null ? "" : ue.Acquired ? (ue.ByCompensation ? "Acquise (comp.)" : "Acquise") : "Non acquise")
                                        .FontSize(7).FontColor(ue.Acquired ? Ink : Fail);
                                    // Crédits des EC affichés seulement s'ils sont répartis dans la maquette.
                                    var distributed = ue.Elements.Sum(e => e.Weight) == ue.Credits;
                                    foreach (var ec in ue.Elements)
                                    {
                                        t.Cell().Element(C).PaddingLeft(6).Text(ec.Code).FontColor(Muted);
                                        t.Cell().Element(C).PaddingLeft(6).Text(ec.Name);
                                        t.Cell().Element(C).AlignCenter().Text(distributed ? N(ec.Weight) : "").FontColor(Muted);
                                        t.Cell().Element(C).AlignCenter().Text(N(ec.Mark));
                                        t.Cell().Element(C).AlignCenter().Text(ec.Session?.ToString(Fr) ?? "").FontColor(Muted);
                                        t.Cell().Element(C).AlignCenter().Text(ec.Appreciation ?? "").FontColor(Muted);
                                    }
                                }
                            });
                            col.Item().PaddingTop(2).Text(
                                $"Moyenne du semestre : {N(summary.Average)}/{N(p.MarkScale)} · crédits capitalisés : " +
                                $"{N(summary.CreditsAcquired)}/{N(summary.CreditsPlanned)}").SemiBold();
                        }
                        col.Item().PaddingTop(8).BorderTop(0.6f).BorderColor(Border).PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text(p.Stage == "S1"
                                ? $"Crédits du premier semestre : {N(s.S1.CreditsAcquired)}/{N(s.S1.CreditsPlanned)}"
                                : $"Moyenne annuelle : {N(s.Average)}/{N(p.MarkScale)} · crédits capitalisés : {N(s.CreditsAcquired)}/{N(s.CreditsPlanned)}")
                                .Bold();
                            if (s.Rank is { } rank) r.ConstantItem(90).AlignRight().Text($"Rang : {rank}/{p.Students.Count}");
                        });
                        if (s.Decision is { } decision)
                            col.Item().PaddingTop(3).Text(t => { t.Span("Décision du jury : ").FontColor(Muted); t.Span(decision).Bold().FontSize(9.5f); });
                        else if (p.Stage == "S1")
                            col.Item().PaddingTop(3).Text("Résultats partiels : la décision de l'année est prise après la session du deuxième semestre.")
                                .FontColor(Muted);
                        col.Item().PaddingTop(22).Row(r =>
                        {
                            r.RelativeItem().AlignCenter().Text("Le secrétaire académique");
                            r.RelativeItem().AlignCenter().Text("Le président du jury");
                        });
                    });
                    page.Footer().Row(r =>
                    {
                        r.RelativeItem().Text($"SCOLIA · UE acquise à {N(p.UeValidationMark)}/{N(p.MarkScale)} · " +
                            "Sess. 2 : note de la deuxième session retenue (meilleure des deux)").FontSize(6.5f).FontColor(Muted);
                        r.ConstantItem(50).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Muted));
                            t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
                        });
                    });
                });
        }).GeneratePdf();
    }
}

/// <summary>Procès-verbal de délibération d'une promotion LMD.</summary>
public static class LmdMinutesPdf
{
    public static byte[] Build(LmdTranscriptPrintDto d)
    {
        ArgumentNullException.ThrowIfNull(d);
        DocumentService.Initialize();
        var p = d.Promotion;
        var N = LmdTranscriptPdf.N;
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(t => t.FontFamily("Roboto").FontSize(8).FontColor(LmdTranscriptPdf.Ink));
            page.Header().Column(h => LmdTranscriptPdf.Header(h, d, "PROCÈS-VERBAL DE DÉLIBÉRATION"));
            page.Content().PaddingTop(8).Column(col =>
            {
                col.Item().Table(t =>
                {
                    var s1Only = p.Stage == "S1";
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(24); c.ConstantColumn(32); c.RelativeColumn(4); c.RelativeColumn(1.6f);
                        c.RelativeColumn(1); c.RelativeColumn(1);
                        if (!s1Only) { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); }
                        c.RelativeColumn(2.4f);
                    });
                    IContainer H(IContainer x) => x.Border(0.6f).BorderColor(LmdTranscriptPdf.Border).Background(LmdTranscriptPdf.Light).Padding(3).AlignCenter();
                    IContainer C(IContainer x) => x.Border(0.5f).BorderColor(LmdTranscriptPdf.Border).Padding(3);
                    t.Header(h =>
                    {
                        var titles = s1Only
                            ? new[] { "N°", "Rang", "Nom et prénom", "Matricule", "Moy. S1", "Crédits S1", "Observation" }
                            : new[] { "N°", "Rang", "Nom et prénom", "Matricule", "Moy. S1", "Moy. S2", "Moy. année", "Crédits", "Dettes", "Décision" };
                        foreach (var title in titles) h.Cell().Element(H).Text(title).Bold();
                    });
                    var n = 1;
                    foreach (var s in p.Students)
                    {
                        t.Cell().Element(C).AlignCenter().Text((n++).ToString(LmdTranscriptPdf.Fr));
                        t.Cell().Element(C).AlignCenter().Text(s.Rank?.ToString(LmdTranscriptPdf.Fr) ?? "");
                        t.Cell().Element(C).Text(s.FullName);
                        t.Cell().Element(C).Text(s.Matricule);
                        t.Cell().Element(C).AlignCenter().Text(N(s.S1.Average));
                        if (s1Only)
                        {
                            t.Cell().Element(C).AlignCenter().Text($"{N(s.S1.CreditsAcquired)}/{N(s.S1.CreditsPlanned)}");
                            t.Cell().Element(C).Text(s.Computed);
                            continue;
                        }
                        t.Cell().Element(C).AlignCenter().Text(N(s.S2.Average));
                        t.Cell().Element(C).AlignCenter().Text(N(s.Average)).Bold();
                        t.Cell().Element(C).AlignCenter().Text($"{N(s.CreditsAcquired)}/{N(s.CreditsPlanned)}");
                        t.Cell().Element(C).AlignCenter().Text(N(s.CreditsPlanned - s.CreditsAcquired));
                        t.Cell().Element(C).Text((s.Decision ?? s.Computed) + (s.Jury is not null ? " (jury)" : "")).Bold();
                    }
                });
                var motives = p.Students.Where(s => s.Jury is not null).ToList();
                if (motives.Count > 0)
                {
                    col.Item().PaddingTop(8).Text("Décisions du jury").Bold();
                    foreach (var s in motives) col.Item().Text($"{s.FullName} : {s.Jury} — {s.JuryReason}");
                }
                col.Item().PaddingTop(6).Text($"Règlement : UE acquise à {N(p.UeValidationMark)}/{N(p.MarkScale)}, " +
                    $"passage avec au plus {N(p.MaxDebtCredits)} crédits en dette.").FontColor(LmdTranscriptPdf.Muted);
                col.Item().PaddingTop(24).Row(r =>
                {
                    foreach (var who in new[] { "Le secrétaire du jury", "Le président du jury", "Les membres du jury" })
                        r.RelativeItem().AlignCenter().Column(sig =>
                        {
                            sig.Item().AlignCenter().Text(who);
                            sig.Item().PaddingTop(24).AlignCenter().Text("………………………………").FontColor(LmdTranscriptPdf.Muted);
                        });
                });
            });
            page.Footer().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(LmdTranscriptPdf.Muted));
                var at = p.ClosedAtUtc ?? p.FirstSessionClosedAtUtc ?? p.S1PublishedAtUtc;
                t.Span($"SCOLIA · Publié par {p.ClosedBy ?? p.FirstSessionClosedBy ?? p.S1PublishedBy}" +
                       (at is { } x ? $" le {x.ToLocalTime():dd/MM/yyyy HH:mm}" : ""));
            });
        })).GeneratePdf();
    }
}
