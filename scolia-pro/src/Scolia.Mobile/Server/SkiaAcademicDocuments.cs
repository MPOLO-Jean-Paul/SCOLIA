using System.Globalization;
using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;
using Scolia.Infrastructure.Documents;

namespace Scolia.Mobile.Server;

/// <summary>
/// Bulletins EPST, procès-verbaux et relevés LMD quand le serveur de l'établissement est un téléphone.
/// Mêmes informations que sur l'ordinateur (QuestPDF), dessinées avec SkiaSharp. Une case vide signifie
/// « aucune évaluation saisie », jamais zéro.
/// </summary>
public sealed partial class SkiaAcademicDocuments : IAcademicDocumentService
{
    private const string Navy = "#13294B";
    private const string Blue = "#1D4ED8";
    private const string Soft = "#E8EEF7";
    private const string Muted = "#64748B";
    private const string Ink = "#1E293B";
    private const string Red = "#DC2626";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly PdfTableLook Grid = new(S(7.5f, true, "#FFFFFF"), Navy, null, true, 3.5f, Navy);

    private static PdfStyle S(float size, bool bold = false, string color = Ink) => new(size, bold, false, color);
    private static string N(decimal? v) => v is { } x ? x.ToString("0.##", Fr) : "";
    private static string Pct(decimal? v) => v is { } x ? x.ToString("0.0", Fr) + " %" : "";

    private static Func<PdfFlow, float> Header(string school, string? place, byte[]? logo, string? ministry, string title, string subtitle) => f =>
    {
        var top = f.Y;
        var image = logo is { Length: > 0 } own ? own : DocumentService.DefaultLogo();
        f.Image(image, f.Left, top, 40, 40);
        var x = f.Left + 50;
        var w = f.ContentWidth - 50;
        var y = top;
        y += f.DrawText("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO", S(8.5f, true, Navy), x, y, w);
        if (!string.IsNullOrWhiteSpace(ministry)) y += f.DrawText(ministry, S(7.5f, false, Muted), x, y, w);
        y += f.DrawText(school.ToUpper(Fr), S(11, true, Navy), x, y, w);
        if (!string.IsNullOrWhiteSpace(place)) y += f.DrawText(place, S(7.5f, false, Muted), x, y, w);
        y = Math.Max(y, top + 42) + 4;
        f.Fill(f.Left, y, f.ContentWidth, 2, Blue);
        y += 8;
        y += f.DrawText(title, S(12, true, Navy), f.Left, y, f.ContentWidth, PdfAlign.Center);
        y += f.DrawText(subtitle, S(8, false, Muted), f.Left, y, f.ContentWidth, PdfAlign.Center);
        return y - top + 6;
    };

    private static Action<PdfFlow, int, int> Footer(string text) => (f, page, total) =>
    {
        var y = f.Height - f.Left - 12;
        f.Line(f.Left, y, f.Right, y, "#CBD5E1", 0.5f);
        f.DrawText(text, S(6.5f, false, Muted), f.Left, y + 3, f.ContentWidth - 70);
        f.DrawText($"{page} / {total}", S(6.5f, false, Muted), f.Right - 60, y + 3, 60, PdfAlign.Right);
    };

    private static void NewPage(PdfFlow f) => f.Ensure(f.Height);

    private static void Signatures(PdfFlow f, params string[] who)
    {
        f.Ensure(50);
        var w = f.ContentWidth / who.Length;
        for (var i = 0; i < who.Length; i++)
            f.DrawText(who[i], S(8, false, Muted), f.Left + i * w, f.Y, w, PdfAlign.Center);
        var sy = f.Y + 32;
        for (var i = 0; i < who.Length; i++)
            f.Line(f.Left + i * w + w * 0.2f, sy, f.Left + i * w + w * 0.8f, sy, Ink, 0.5f);
        f.Y = sy + 6;
    }

    private static string Place(string? address, string? city) =>
        string.Join(" · ", new[] { address, city }.Where(v => !string.IsNullOrWhiteSpace(v)));

    // ------------------------------------------------------------- EPST : procès-verbal et palmarès

    private static string EpstLabel(string? outcome) => outcome switch
    {
        "Passe" => "Passe", "Repechage" => "Repêchage", "Redouble" => "Double",
        "PasseApresRepechage" => "Passe après repêchage", "Reoriente" => "Réorienté", "Incomplet" => "Incomplet", _ => "",
    };

    public byte[] EpstPalmares(EpstPalmaresPrintDto d)
    {
        var del = d.Deliberation;
        var final = del.Status == "Cloturee";
        return PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 30, f =>
        {
            f.Table(
                [new(0.5f, PdfAlign.Center), new(0.6f, PdfAlign.Center), new(3.4f), new(1.4f), new(0.9f, PdfAlign.Right), new(0.8f, PdfAlign.Center), new(1.7f), new(1.9f)],
                new[] { "N°", "Place", "Nom et prénom", "Matricule", "%", "Échecs", "1re session", final ? "Décision finale" : "Décision" }
                    .Select(t => new PdfCell(t)).ToList(),
                del.Pupils.Select((p, i) => (IReadOnlyList<PdfCell>)
                [
                    new((i + 1).ToString(Fr)), new(p.Rank?.ToString(Fr) ?? ""), new(p.FullName), new(p.Matricule),
                    new(p.Percent is { } v ? v.ToString("0.0", Fr) : ""), new(p.FailedSubjects.ToString(Fr)),
                    new(EpstLabel(p.Jury ?? p.Computed) + (p.Jury is not null ? " (jury)" : "")), new(p.EffectiveLabel, S(8.5f, true)),
                ]), S(8.5f), Grid);
            f.Y += 8;
            var counts = string.Join(" · ", del.Pupils.GroupBy(p => p.EffectiveLabel).OrderBy(g => g.Key).Select(g => $"{g.Key} : {g.Count()}"));
            f.Paragraph($"Effectif : {del.Pupils.Count} · {counts}", S(9, true), spaceAfter: 4);
            f.Paragraph($"Seuil de réussite : {N(del.PassPercent)} % · seuil d'une branche : {N(del.SubjectPassPercent)} % · repêchage jusqu'à {del.MaxRepechageSubjects} branche(s).",
                S(8, false, Muted), spaceAfter: 6);
            foreach (var p in del.Pupils.Where(p => p.Jury is not null))
                f.Paragraph($"{p.FullName} : {EpstLabel(p.Jury)} — {p.JuryReason}", S(8));
            f.Y += 16;
            Signatures(f, "Le secrétaire du jury", "Le président du jury", "Le chef d'établissement");
        },
        Header(d.SchoolName, Place(d.SchoolAddress, d.SchoolCity), d.SchoolLogo, d.Ministry ?? "Ministère de l'Éducation nationale et de la Nouvelle citoyenneté",
            "PROCÈS-VERBAL DE DÉLIBÉRATION ET PALMARÈS",
            $"{d.CycleName} · {del.ClassName} · Année scolaire {del.YearLabel} · " + (final ? "décisions définitives" : "première session, repêchage en cours")),
        Footer($"SCOLIA · Délibération clôturée par {del.ClosedBy ?? del.FirstSessionClosedBy}"), 16);
    }

    // ------------------------------------------------------------- LMD

    private static string StageTitle(string stage) => stage switch
    {
        "S1" => "Résultats partiels du premier semestre",
        "PremiereSession" => "Résultats de la première session",
        _ => "Résultats définitifs de l'année",
    };

    private static string PromotionLine(LmdPromotionResultsDto p) =>
        (p.PromotionName.Contains(p.Level, StringComparison.OrdinalIgnoreCase) ? p.PromotionName : $"{p.PromotionName} ({p.Level})") +
        (p.Filiere is { } fil && !p.PromotionName.Contains(fil, StringComparison.OrdinalIgnoreCase) ? $" · {fil}" : "");

    public byte[] LmdTranscripts(LmdTranscriptPrintDto d)
    {
        var p = d.Promotion;
        return PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 30, f =>
        {
            var first = true;
            foreach (var s in d.Students)
            {
                if (!first) NewPage(f);
                first = false;
                f.DrawSpans([("Étudiant : ", S(9, false, Muted)), (s.FullName, S(10, true)), ($"    Matricule : {s.Matricule}", S(9, false, Muted))],
                    f.Left, f.Y, f.ContentWidth);
                f.Y += 18;
                foreach (var semester in s.Units.GroupBy(u => u.Semester))
                {
                    var summary = semester.Key == "S1" ? s.S1 : s.S2;
                    f.Paragraph(semester.Key == "S1" ? "Premier semestre" : "Deuxième semestre", S(10, true, Navy), spaceAfter: 2);
                    var rows = new List<IReadOnlyList<PdfCell>>();
                    foreach (var ue in semester)
                    {
                        rows.Add([new(ue.Code, S(8, true)), new(ue.Name, S(8, true)), new(N(ue.Credits), S(8, true)),
                            new(N(ue.Average), S(8, true, ue.Acquired ? Ink : Red)), new(""),
                            new(ue.Average is null ? "" : ue.Acquired ? (ue.ByCompensation ? "Acquise (comp.)" : "Acquise") : "Non acquise",
                                S(7.5f, false, ue.Acquired ? Ink : Red))]);
                        var distributed = ue.Elements.Sum(e => e.Weight) == ue.Credits;
                        foreach (var ec in ue.Elements)
                            rows.Add([new("  " + ec.Code, S(8, false, Muted)), new("  " + ec.Name), new(distributed ? N(ec.Weight) : "", S(8, false, Muted)),
                                new(N(ec.Mark)), new(ec.Session?.ToString(Fr) ?? "", S(8, false, Muted)), new(ec.Appreciation ?? "", S(8, false, Muted))]);
                    }
                    f.Table([new(1.1f), new(4), new(0.9f, PdfAlign.Center), new(0.9f, PdfAlign.Center), new(0.7f, PdfAlign.Center), new(1.4f, PdfAlign.Center)],
                        new[] { "Code", "Unité d'enseignement / élément constitutif", "Crédits", "Note", "Sess.", "Résultat" }.Select(t => new PdfCell(t)).ToList(),
                        rows, S(8), Grid);
                    f.Paragraph($"Moyenne du semestre : {N(summary.Average)}/{N(p.MarkScale)} · crédits capitalisés : {N(summary.CreditsAcquired)}/{N(summary.CreditsPlanned)}",
                        S(8.5f, true), spaceAfter: 6);
                }
                f.Paragraph(p.Stage == "S1"
                    ? $"Crédits du premier semestre : {N(s.S1.CreditsAcquired)}/{N(s.S1.CreditsPlanned)}" + (s.Rank is { } r1 ? $" · rang {r1}/{p.Students.Count}" : "")
                    : $"Moyenne annuelle : {N(s.Average)}/{N(p.MarkScale)} · crédits capitalisés : {N(s.CreditsAcquired)}/{N(s.CreditsPlanned)}" +
                      (s.Rank is { } r ? $" · rang {r}/{p.Students.Count}" : ""), S(9, true), spaceAfter: 4);
                if (s.Decision is { } decision) f.Paragraph("Décision du jury : " + decision, S(10, true, Navy), spaceAfter: 4);
                else if (p.Stage == "S1")
                    f.Paragraph("Résultats partiels : la décision de l'année est prise après la session du deuxième semestre.", S(8, false, Muted), spaceAfter: 4);
                f.Y += 14;
                Signatures(f, "Le secrétaire académique", "Le président du jury");
            }
        },
        Header(d.SchoolName, Place(d.SchoolAddress, d.SchoolCity), d.SchoolLogo, "Enseignement supérieur et universitaire",
            "RELEVÉ DE NOTES", $"{StageTitle(p.Stage)} · Année académique {p.YearLabel} · {PromotionLine(p)}"),
        Footer($"SCOLIA · UE acquise à {N(p.UeValidationMark)}/{N(p.MarkScale)} · Sess. 2 : meilleure des deux notes retenue"), 16);
    }

    public byte[] LmdMinutes(LmdTranscriptPrintDto d)
    {
        var p = d.Promotion;
        var s1Only = p.Stage == "S1";
        return PdfFlow.Create(PdfFlow.A4Height, PdfFlow.A4Width, 24, f =>
        {
            IReadOnlyList<PdfColumn> columns = s1Only
                ? [new(0.5f, PdfAlign.Center), new(0.6f, PdfAlign.Center), new(4), new(1.6f), new(1, PdfAlign.Center), new(1, PdfAlign.Center), new(2.4f)]
                : [new(0.5f, PdfAlign.Center), new(0.6f, PdfAlign.Center), new(4), new(1.6f), new(1, PdfAlign.Center), new(1, PdfAlign.Center),
                   new(1, PdfAlign.Center), new(1, PdfAlign.Center), new(0.9f, PdfAlign.Center), new(2.4f)];
            var head = (s1Only
                ? new[] { "N°", "Rang", "Nom et prénom", "Matricule", "Moy. S1", "Crédits S1", "Observation" }
                : new[] { "N°", "Rang", "Nom et prénom", "Matricule", "Moy. S1", "Moy. S2", "Moy. année", "Crédits", "Dettes", "Décision" })
                .Select(t => new PdfCell(t)).ToList();
            var rows = p.Students.Select((s, i) => (IReadOnlyList<PdfCell>)(s1Only
                ? [new((i + 1).ToString(Fr)), new(s.Rank?.ToString(Fr) ?? ""), new(s.FullName), new(s.Matricule), new(N(s.S1.Average)),
                   new($"{N(s.S1.CreditsAcquired)}/{N(s.S1.CreditsPlanned)}"), new(s.Computed)]
                : [new((i + 1).ToString(Fr)), new(s.Rank?.ToString(Fr) ?? ""), new(s.FullName), new(s.Matricule), new(N(s.S1.Average)),
                   new(N(s.S2.Average)), new(N(s.Average), S(8.5f, true)), new($"{N(s.CreditsAcquired)}/{N(s.CreditsPlanned)}"),
                   new(N(s.CreditsPlanned - s.CreditsAcquired)), new((s.Decision ?? s.Computed) + (s.Jury is not null ? " (jury)" : ""), S(8.5f, true))]));
            f.Table(columns, head, rows, S(8.5f), Grid);
            f.Y += 8;
            foreach (var s in p.Students.Where(s => s.Jury is not null))
                f.Paragraph($"{s.FullName} : {s.Jury} — {s.JuryReason}", S(8));
            f.Paragraph($"Règlement : UE acquise à {N(p.UeValidationMark)}/{N(p.MarkScale)}, passage avec au plus {N(p.MaxDebtCredits)} crédits en dette.",
                S(8, false, Muted), spaceAfter: 14);
            Signatures(f, "Le secrétaire du jury", "Le président du jury", "Les membres du jury");
        },
        Header(d.SchoolName, Place(d.SchoolAddress, d.SchoolCity), d.SchoolLogo, "Enseignement supérieur et universitaire",
            "PROCÈS-VERBAL DE DÉLIBÉRATION", $"{StageTitle(p.Stage)} · Année académique {p.YearLabel} · {PromotionLine(p)}"),
        Footer($"SCOLIA · Publié par {p.ClosedBy ?? p.FirstSessionClosedBy ?? p.S1PublishedBy}"), 16);
    }
}
