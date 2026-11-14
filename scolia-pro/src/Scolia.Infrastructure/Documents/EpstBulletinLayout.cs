using System.Globalization;
using System.Text.RegularExpressions;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>Case d'en-tête du tableau des branches, placée sur une grille de trois lignes.</summary>
public sealed record BulletinHeadCell(string Text, int Row, int Column, int RowSpan = 1, int ColumnSpan = 1, bool Black = false);

/// <summary>
/// Aspect d'une case : <c>Blocked</c> est la case noircie du modèle officiel (sans objet),
/// <c>Total</c> une colonne de totaux, <c>Alert</c> une branche en échec.
/// </summary>
public enum BulletinTone { Normal, Total, Blocked, Alert }

public sealed record BulletinCell(string Text, BulletinTone Tone = BulletinTone.Normal, bool Bold = false);

public enum BulletinRowKind { Maxima, Subject, Summary }

public sealed record BulletinRow(BulletinRowKind Kind, string Label, IReadOnlyList<BulletinCell> Cells);

/// <summary>Mention de décision ; les mentions écartées par la délibération sont biffées.</summary>
public sealed record BulletinMention(string Text, bool Struck);

/// <summary>
/// Mise en page du bulletin EPST sur le modèle des bulletins officiels de la RDC, commune
/// à l'ordinateur (QuestPDF) et au téléphone serveur (SkiaSharp) : identification de l'école
/// et de l'élève, branches regroupées par maxima, travaux journaliers, examens et totaux par
/// trimestre (primaire) ou semestre (secondaire), total général, examen de repêchage,
/// lignes de synthèse et mentions de décision à biffer.
/// </summary>
public sealed class EpstBulletinLayout
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public const string DefaultMinistry = "Ministère de l'Éducation nationale et de la Nouvelle citoyenneté";
    public const int CodeBoxes = 10;
    public const int PermanentBoxes = 14;

    public required string Ministry { get; init; }
    public string? Division { get; init; }
    public required IReadOnlyList<(string Label, string Value)> SchoolFields { get; init; }
    public required string SchoolCode { get; init; }
    public required IReadOnlyList<(string Label, string Value)> PupilFields { get; init; }
    public required string PermanentNumber { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }

    /// <summary>Largeurs relatives des colonnes ; la première est celle des branches.</summary>
    public required IReadOnlyList<float> Columns { get; init; }
    public required IReadOnlyList<BulletinHeadCell> Head { get; init; }
    public required IReadOnlyList<BulletinRow> Rows { get; init; }

    public required IReadOnlyList<BulletinMention> Mentions { get; init; }
    public string? Decision { get; init; }
    public required string PlaceAndDate { get; init; }
    public string? Warning { get; init; }
    public required string SystemNote { get; init; }

    public const string Footnote = "(1) Biffer la mention inutile.";
    public const string Notice = "Note importante : le bulletin est sans valeur s'il est raturé ou surchargé.";

    private static string N(decimal? value) => value is { } v ? v.ToString("0.##", Fr) : "";
    private static string Pct(decimal? points, decimal max) =>
        points is { } p && max > 0 ? (p / max * 100).ToString("0.0", Fr) : "";

    /// <summary>« P1 » devient « 1re P », « P4 » devient « 4e P » ; un autre code reste tel quel.</summary>
    public static string PeriodLabel(string code)
    {
        var m = Regex.Match(code, "^P(\\d+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!m.Success) return code;
        var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return n == 1 ? "1re P" : $"{n}e P";
    }

    public static string TermLabel(int index, int count) => count switch
    {
        2 => index == 0 ? "PREMIER SEMESTRE" : "SECOND SEMESTRE",
        3 => index == 0 ? "1er TRIMESTRE" : $"{index + 1}e TRIMESTRE",
        _ => index == 0 ? "1re PARTIE" : $"{index + 1}e PARTIE",
    };

    private enum ColumnKind { Work, Exam, TermTotal, Grand, Black, RepechagePercent, RepechageSign }
    private sealed record Column(ColumnKind Kind, int Index);

    public static EpstBulletinLayout For(EpstBulletinPrintDto d, EpstPupilResultDto pupil)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(pupil);
        var c = d.Class;
        var info = d.Details?.FirstOrDefault(x => x.EnrollmentId == pupil.EnrollmentId);
        // Le repêchage figure sur le bulletin du secondaire, comme sur le modèle officiel.
        var withRepechage = !string.Equals(c.Scope, "Primaire", StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- colonnes
        var periodIndex = c.Periods.Select((p, i) => (p, i)).ToDictionary(x => x.p.Code, x => x.i);
        var columns = new List<Column>();
        var head = new List<BulletinHeadCell> { new("BRANCHES", 0, 0, RowSpan: 3) };
        var weights = new List<float> { 4.2f };
        for (var t = 0; t < c.Terms.Count; t++)
        {
            var codes = c.Terms[t].PeriodCodes;
            var work = codes.Where(code => !c.Periods[periodIndex[code]].IsExam).ToList();
            var exams = codes.Where(code => c.Periods[periodIndex[code]].IsExam).ToList();
            var first = columns.Count + 1;
            head.Add(new(TermLabel(t, c.Terms.Count), 0, first, ColumnSpan: work.Count + exams.Count + 1));
            if (work.Count > 0)
                head.Add(new("TRAV. JOURNAL.", 1, first, ColumnSpan: work.Count));
            foreach (var code in work)
            {
                head.Add(new(PeriodLabel(code), 2, columns.Count + 1));
                columns.Add(new(ColumnKind.Work, periodIndex[code]));
                weights.Add(1f);
            }
            foreach (var code in exams)
            {
                head.Add(new(exams.Count == 1 ? "EXAM." : code, 1, columns.Count + 1, RowSpan: 2));
                columns.Add(new(ColumnKind.Exam, periodIndex[code]));
                weights.Add(1.05f);
            }
            head.Add(new("TOT.", 1, columns.Count + 1, RowSpan: 2));
            columns.Add(new(ColumnKind.TermTotal, t));
            weights.Add(1.1f);
        }
        head.Add(new("T.G.", 0, columns.Count + 1, RowSpan: 3));
        columns.Add(new(ColumnKind.Grand, 0));
        weights.Add(1.2f);
        if (withRepechage)
        {
            head.Add(new("", 0, columns.Count + 1, RowSpan: 3, Black: true));
            columns.Add(new(ColumnKind.Black, 0));
            weights.Add(0.35f);
            head.Add(new("EXAMEN DE REPÊCHAGE", 0, columns.Count + 1, ColumnSpan: 2));
            head.Add(new("%", 1, columns.Count + 1, RowSpan: 2));
            columns.Add(new(ColumnKind.RepechagePercent, 0));
            weights.Add(0.95f);
            head.Add(new("SIGN. PROF.", 1, columns.Count + 1, RowSpan: 2));
            columns.Add(new(ColumnKind.RepechageSign, 0));
            weights.Add(1.15f);
        }

        // ---------------------------------------------------------------- lignes
        static BulletinCell Blocked() => new("", BulletinTone.Blocked);
        BulletinRow Row(BulletinRowKind kind, string label, Func<Column, BulletinCell> cell) =>
            new(kind, label, columns.Select(col => col.Kind == ColumnKind.Black ? Blocked() : cell(col)).ToList());

        var rows = new List<BulletinRow>();
        var maxOf = c.Subjects.ToDictionary(s => s.Id, s => s.PeriodMax);
        var groups = pupil.Subjects
            .Select((line, order) => (line, order))
            .GroupBy(x => maxOf.TryGetValue(x.line.SubjectId, out var m) ? m : 0m)
            .OrderBy(g => g.Key);
        var repechage = info?.RepechageScores.ToDictionary(x => x.SubjectId, x => x.Percent) ?? [];
        foreach (var group in groups)
        {
            var model = group.First().line;
            rows.Add(Row(BulletinRowKind.Maxima, "MAXIMA", col => col.Kind switch
            {
                ColumnKind.Work or ColumnKind.Exam => new(N(model.Periods[col.Index].Max), BulletinTone.Total, true),
                ColumnKind.TermTotal => new(N(model.Terms[col.Index].Max), BulletinTone.Total, true),
                ColumnKind.Grand => new(N(model.Max), BulletinTone.Total, true),
                _ => Blocked(),
            }));
            foreach (var (line, _) in group.OrderBy(x => x.order))
                rows.Add(Row(BulletinRowKind.Subject, line.Name, col => col.Kind switch
                {
                    ColumnKind.Work or ColumnKind.Exam => new(N(line.Periods[col.Index].Points)),
                    ColumnKind.TermTotal => new(N(line.Terms[col.Index].Points), BulletinTone.Total, true),
                    ColumnKind.Grand => new(N(line.Total), line.Failed ? BulletinTone.Alert : BulletinTone.Total, true),
                    ColumnKind.RepechagePercent => new(repechage.TryGetValue(line.SubjectId, out var r) ? N(r) : ""),
                    _ => new(""),
                }));
        }

        rows.Add(Row(BulletinRowKind.Summary, "MAXIMA GÉNÉRAUX", col => col.Kind switch
        {
            ColumnKind.Work or ColumnKind.Exam => new(N(pupil.PeriodTotals[col.Index].Max), BulletinTone.Normal, true),
            ColumnKind.TermTotal => new(N(pupil.TermTotals[col.Index].Max), BulletinTone.Total, true),
            ColumnKind.Grand => new(N(pupil.Max), BulletinTone.Total, true),
            _ => Blocked(),
        }));
        rows.Add(Row(BulletinRowKind.Summary, "TOTAUX", col => col.Kind switch
        {
            ColumnKind.Work or ColumnKind.Exam => new(N(pupil.PeriodTotals[col.Index].Points), BulletinTone.Normal, true),
            ColumnKind.TermTotal => new(N(pupil.TermTotals[col.Index].Points), BulletinTone.Total, true),
            ColumnKind.Grand => new(N(pupil.Total), BulletinTone.Total, true),
            _ => Blocked(),
        }));
        rows.Add(Row(BulletinRowKind.Summary, "POURCENTAGE", col => col.Kind switch
        {
            ColumnKind.Work or ColumnKind.Exam => new(Pct(pupil.PeriodTotals[col.Index].Points, pupil.PeriodTotals[col.Index].Max)),
            ColumnKind.TermTotal => new(Pct(pupil.TermTotals[col.Index].Points, pupil.TermTotals[col.Index].Max), BulletinTone.Total, true),
            ColumnKind.Grand => new(pupil.Percent is { } p ? p.ToString("0.0", Fr) : "", BulletinTone.Total, true),
            _ => Blocked(),
        }));
        if (c.ShowRank)
            rows.Add(Row(BulletinRowKind.Summary, "PLACE / NBRE D'ÉLÈVES", col => col.Kind switch
            {
                ColumnKind.Work or ColumnKind.Exam => new(""),
                ColumnKind.TermTotal => new(pupil.TermRanks[col.Index] is { } r ? $"{r}/{c.Headcount}" : "", BulletinTone.Total, true),
                ColumnKind.Grand => new(pupil.Rank is { } rank ? $"{rank}/{c.Headcount}" : "", BulletinTone.Total, true),
                _ => Blocked(),
            }));
        var conduct = pupil.Conduct.ToDictionary(x => x.PeriodCode);
        BulletinCell Mark(Column col, Func<EpstConductCellDto, string?> pick) =>
            col.Kind == ColumnKind.Work
                ? new(conduct.TryGetValue(c.Periods[col.Index].Code, out var m) ? pick(m) ?? "" : "")
                : Blocked();
        rows.Add(Row(BulletinRowKind.Summary, "APPLICATION", col => Mark(col, m => m.Application)));
        rows.Add(Row(BulletinRowKind.Summary, "CONDUITE", col => Mark(col, m => m.Conduct)));
        rows.Add(Row(BulletinRowKind.Summary, "SIGNAT. DU RESPONSABLE", col => col.Kind switch
        {
            ColumnKind.Work or ColumnKind.TermTotal => new(""),
            _ => Blocked(),
        }));

        // ---------------------------------------------------------------- identification
        var dots = "…………………";
        string Or(string? value) => string.IsNullOrWhiteSpace(value) ? dots : value.Trim();
        var schoolFields = new List<(string, string)>
        {
            ("PROVINCE", Or(d.Province)),
            ("VILLE", Or(d.SchoolCity)),
            ("COMMUNE / TER.", Or(d.Commune)),
            ("ÉCOLE", d.SchoolName),
        };
        var pupilFields = new List<(string, string)>
        {
            ("ÉLÈVE", pupil.FullName),
            ("SEXE", Or(info?.Sex)),
            ("NÉ(E) À", Or(info?.BirthPlace)),
            ("LE", info?.BirthDate is { } born ? born.ToString("dd/MM/yyyy", Fr) : dots),
            ("CLASSE", c.ClassName),
        };

        // ---------------------------------------------------------------- décision
        var outcome = info?.Outcome;
        var known = outcome is not null and not "Incomplet";
        var subjects = info?.RepechageSubjects is { Count: > 0 } list ? string.Join(", ", list) : "………………………………";
        var mentions = new List<BulletinMention>
        {
            new("L'élève passe dans la classe supérieure (1)",
                known && outcome is not ("Passe" or "PasseApresRepechage")),
            new("L'élève double la classe (1)", known && outcome != "Redouble"),
            new("L'élève a échoué et est orienté(e) vers ………………………… (1)", known && outcome != "Reoriente"),
        };
        if (withRepechage || outcome is "Repechage" or "PasseApresRepechage")
            mentions.Add(new($"L'élève ne pourra passer dans la classe supérieure s'il n'a subi avec succès un examen de repêchage en : {subjects} (1)",
                known && outcome != "Repechage"));
        var decision = outcome switch
        {
            "Passe" => "PASSE",
            "PasseApresRepechage" => "PASSE APRÈS REPÊCHAGE",
            "Redouble" => "DOUBLE",
            "Reoriente" => "A ÉCHOUÉ — RÉORIENTÉ(E)",
            "Repechage" => "REPÊCHAGE",
            _ => null,
        };

        var rule = c.RegulationValidatedAtUtc is { } at
            ? $"règlement pédagogique {c.YearLabel} validé par {c.RegulationValidatedBy} le {at.ToLocalTime():dd/MM/yyyy}"
            : $"règlement pédagogique {c.YearLabel} validé";
        return new EpstBulletinLayout
        {
            Ministry = string.IsNullOrWhiteSpace(d.Ministry) ? DefaultMinistry : d.Ministry.Trim(),
            Division = string.IsNullOrWhiteSpace(d.Division) ? null : d.Division.Trim(),
            SchoolFields = schoolFields,
            SchoolCode = (d.SchoolCode ?? string.Empty).Trim(),
            PupilFields = pupilFields,
            PermanentNumber = (info?.PermanentNumber ?? string.Empty).Trim(),
            Title = $"BULLETIN DE L'ÉLÈVE — {c.CycleName.ToUpper(Fr)}",
            Subtitle = $"CLASSE : {c.ClassName.ToUpper(Fr)}    ANNÉE SCOLAIRE {c.YearLabel}",
            Columns = weights,
            Head = head,
            Rows = rows,
            Mentions = mentions,
            Decision = decision,
            PlaceAndDate = $"Fait à {Or(d.SchoolCity)}, le {(d.IssuedOn is { } on ? on.ToString("dd/MM/yyyy", Fr) : dots)}",
            Warning = pupil.MissingCells > 0
                ? $"Résultats incomplets : {pupil.MissingCells} évaluation(s) non saisie(s)."
                : null,
            SystemNote = $"Établi avec SCOLIA selon le {rule}. Case vide : aucune évaluation saisie. " +
                         "Application et conduite : E, TB, B, AB, M, MA.",
        };
    }

    /// <summary>Découpe une valeur en cases (code de l'école, N° permanent) ; cases vides à remplir à la main.</summary>
    public static IReadOnlyList<string> Boxes(string value, int minimum)
    {
        var chars = value.Where(ch => !char.IsWhiteSpace(ch)).Select(ch => ch.ToString()).ToList();
        while (chars.Count < minimum) chars.Add("");
        return chars;
    }
}
