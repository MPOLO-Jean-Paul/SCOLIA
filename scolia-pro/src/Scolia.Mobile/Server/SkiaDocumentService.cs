using System.Globalization;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;
using Scolia.Infrastructure.Documents;

namespace Scolia.Mobile.Server;

/// <summary>
/// Documents PDF du téléphone serveur : reçus, relevés, listes, bulletins et cartes d'accès, avec la même charte et la même
/// disposition que ceux de l'ordinateur (QuestPDF). Dessinés avec SkiaSharp, déjà présent sur Android.
/// </summary>
public sealed class SkiaDocumentService : IDocumentService
{
    private const string Navy = "#13294B";
    private const string Blue = "#1D4ED8";
    private const string Soft = "#E8EEF7";
    private const string Muted = "#64748B";
    private const string Border = "#CBD5E1";
    private const string Ink = "#1E293B";
    private const string Red = "#DC2626";
    private const string Amber = "#B45309";
    private const string Green = "#15803D";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static string M(decimal amount, string symbol) => amount.ToString("N0", Fr) + " " + symbol;

    private static PdfStyle S(float size, bool bold = false, string color = Ink, bool italic = false) => new(size, bold, italic, color);

    // ---------- En-tête et pied communs ----------

    private static Func<PdfFlow, float> Header(SchoolHeader school, string title, string? subtitle) => f =>
    {
        var logo = school.Logo is { Length: > 0 } own ? own : DocumentService.DefaultLogo();
        var top = f.Y;
        var textX = f.Left + 64;
        var textW = f.ContentWidth - 64;
        var contact = string.Join(" · ", new[] { school.Address, school.Phone, school.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var blockH = PdfFlow.TextHeight(school.Name.ToUpper(Fr), S(13, true), textW)
            + (string.IsNullOrWhiteSpace(school.Motto) ? 0 : PdfFlow.TextHeight(school.Motto, S(8.5f, italic: true), textW))
            + (contact.Length == 0 ? 0 : PdfFlow.TextHeight(contact, S(8), textW));
        var rowH = Math.Max(54, blockH);
        f.Image(logo, f.Left, top + (rowH - 54) / 2, 54, 54);
        var y = top + (rowH - blockH) / 2;
        y += f.DrawText(school.Name.ToUpper(Fr), S(13, true, Navy), textX, y, textW);
        if (!string.IsNullOrWhiteSpace(school.Motto)) y += f.DrawText(school.Motto, S(8.5f, false, Muted, true), textX, y, textW);
        if (contact.Length > 0) f.DrawText(contact, S(8, false, Muted), textX, y, textW);
        y = top + rowH + 8;
        f.Fill(f.Left, y, f.ContentWidth, 3, Blue);
        y += 3 + 10;
        y += f.DrawText(title, S(15, true, Navy), f.Left, y, f.ContentWidth);
        if (!string.IsNullOrWhiteSpace(subtitle)) y += f.DrawText(subtitle, S(9, false, Muted), f.Left, y, f.ContentWidth);
        return y - top;
    };

    private static Action<PdfFlow, int, int> Footer(string? text) => (f, page, total) =>
    {
        var y = f.Height - f.Left - 14;
        f.Line(f.Left, y, f.Right, y, Border, 0.5f);
        var left = text ?? $"Document généré par SCOLIA le {DateTime.Now.ToString("dd/MM/yyyy à HH:mm", Fr)}";
        f.DrawText(left, S(7.5f, false, Muted), f.Left, y + 4, f.ContentWidth - 90);
        f.DrawText($"Page {page} / {total}", S(7.5f, false, Muted), f.Right - 80, y + 4, 80, PdfAlign.Right);
    };

    private static readonly PdfTableLook Solid = new(S(8.5f, true, "#FFFFFF"), Navy, null, true, 5, Navy);

    // ---------- Reçu ----------

    public byte[] Receipt(ReceiptDocument d)
    {
        var qr = DocumentService.QrPng(d.VerificationUrl);
        var cur = d.School.CurrencySymbol;
        return PdfFlow.Create(PdfFlow.A5Width, PdfFlow.A5Height, 26, f =>
        {
            f.Y += 10;
            // Numéro et date dans deux cases pâles.
            var half = (f.ContentWidth - 8) / 2;
            var boxH = 8 + PdfFlow.LineHeight(S(7.5f)) + PdfFlow.LineHeight(S(13, true)) + 8;
            f.Fill(f.Left, f.Y, half, boxH, Soft);
            f.Fill(f.Left + half + 8, f.Y, half, boxH, Soft);
            f.DrawText("N° du reçu", S(7.5f, false, Muted), f.Left + 8, f.Y + 8, half - 16);
            f.DrawText(d.Number, S(13, true, Navy), f.Left + 8, f.Y + 8 + PdfFlow.LineHeight(S(7.5f)), half - 16);
            f.DrawText("Date", S(7.5f, false, Muted), f.Left + half + 16, f.Y + 8, half - 16);
            f.DrawText(d.Date.ToString("dd MMMM yyyy", Fr), S(11, true, Navy), f.Left + half + 16, f.Y + 8 + PdfFlow.LineHeight(S(7.5f)), half - 16);
            f.Y += boxH + 8;

            // Apprenant, dans un cadre fin.
            var inner = f.ContentWidth - 16;
            var line = $"Matricule {d.Matricule} · Promotion {d.Promotion} · Année {d.AcademicYear}";
            var payer = string.IsNullOrWhiteSpace(d.PayerName) ? null : $"Versé par : {d.PayerName}";
            var studentH = 8 + PdfFlow.LineHeight(S(7.5f, true)) + PdfFlow.TextHeight(d.StudentName, S(11, true), inner)
                + PdfFlow.TextHeight(line, S(8.5f), inner) + (payer is null ? 0 : PdfFlow.TextHeight(payer, S(8.5f), inner)) + 8;
            f.Stroke(f.Left, f.Y, f.ContentWidth, studentH, Border, 0.5f);
            var y = f.Y + 8;
            y += f.DrawText("APPRENANT", S(7.5f, true, Blue), f.Left + 8, y, inner);
            y += f.DrawText(d.StudentName, S(11, true), f.Left + 8, y, inner);
            y += f.DrawText(line, S(8.5f, false, Muted), f.Left + 8, y, inner);
            if (payer is not null) f.DrawText(payer, S(8.5f, false, Muted), f.Left + 8, y, inner);
            f.Y += studentH + 8;

            // Répartition du versement.
            f.Table([new PdfColumn(3), new PdfColumn(1.4f, PdfAlign.Right)],
                [new PdfCell("Affectation"), new PdfCell("Montant")],
                d.Allocations.Select(a => (IReadOnlyList<PdfCell>)[new PdfCell(a.Label), new PdfCell(M(a.Amount, cur))]),
                S(9.5f), Solid,
                [new PdfCell("TOTAL VERSÉ"), new PdfCell(M(d.Amount, cur), S(11, true, Navy))]);
            f.Y += 8;

            f.Y += f.DrawSpans([("Arrêté le présent reçu à la somme de : ", S(8.5f, false, Muted)), (d.AmountInWords + ".", S(8.5f, false, Ink, true))], f.Left, f.Y, f.ContentWidth);
            f.Y += 8;

            // Mode, reste à payer, signature à gauche ; QR de vérification à droite.
            f.Ensure(140);
            var top = f.Y;
            var leftW = f.ContentWidth - 100;
            y = top;
            var mode = $"Mode de paiement : {d.Method}" + (string.IsNullOrWhiteSpace(d.Reference) ? string.Empty : $" · Réf. {d.Reference}");
            y += f.DrawText(mode, S(8.5f), f.Left, y, leftW);
            y += f.DrawText($"Reste à payer pour l'année : {M(d.BalanceAfter, cur)}", S(8.5f, true, d.BalanceAfter > 0 ? Amber : Green), f.Left, y, leftW);
            y += 14;
            y += f.DrawText($"Le caissier : {d.Cashier}", S(8.5f), f.Left, y, leftW);
            y += 18;
            f.Line(f.Left, y, f.Left + 150, y, Muted, 0.5f);
            y += 1;
            y += f.DrawText("Signature et cachet", S(7, false, Muted), f.Left, y, leftW);
            if (d.Cancelled) y += 6 + f.DrawText($"Reçu annulé : {d.CancellationReason}", S(8, true, Red), f.Left, y + 6, leftW);
            f.Image(qr, f.Right - 92, top, 92, 92, sharp: true);
            f.DrawText("Vérifier l'authenticité", S(6.5f, false, Muted), f.Right - 92, top + 92, 92, PdfAlign.Center);
            f.Y = Math.Max(y, top + 92 + 10);
        },
        Header(d.School, "REÇU DE PAIEMENT", null), Footer(d.School.ReceiptFooter), 22,
        d.Cancelled ? Watermark : null);
    }

    /// <summary>Filigrane d'un reçu annulé, en travers de la page.</summary>
    private static void Watermark(PdfFlow f)
    {
        f.Canvas.Save();
        f.Canvas.Translate(f.Width / 2, f.Height / 2);
        f.Canvas.RotateDegrees(-30);
        using var font = PdfFlow.Font(new PdfStyle(64, true));
        using var paint = PdfFlow.Paint("#DC262655");
        var w = font.MeasureText("ANNULÉ");
        f.Canvas.DrawText("ANNULÉ", -w / 2, 22, font, paint);
        f.Canvas.Restore();
    }

    // ---------- Relevé de compte ----------

    public byte[] Statement(StatementDocument d)
    {
        var cur = d.School.CurrencySymbol;
        return PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 32, f =>
        {
            f.Y += 12;
            var right = 170f;
            var leftW = f.ContentWidth - right - 20;
            var line = $"Matricule {d.Matricule} · {d.Promotion} · Année scolaire {d.AcademicYear}";
            var leftH = PdfFlow.TextHeight(d.StudentName, S(12, true), leftW) + PdfFlow.TextHeight(line, S(9), leftW);
            var rightH = PdfFlow.LineHeight(S(8)) + PdfFlow.LineHeight(S(15, true)) + (d.Overdue > 0 ? PdfFlow.LineHeight(S(8)) : 0);
            var boxH = Math.Max(leftH, rightH) + 20;
            f.Fill(f.Left, f.Y, f.ContentWidth, boxH, Soft);
            var y = f.Y + 10;
            y += f.DrawText(d.StudentName, S(12, true, Navy), f.Left + 10, y, leftW);
            f.DrawText(line, S(9, false, Muted), f.Left + 10, y, leftW);
            var rx = f.Right - 10 - right;
            y = f.Y + 10;
            y += f.DrawText("Reste à payer", S(8, false, Muted), rx, y, right, PdfAlign.Right);
            y += f.DrawText(M(d.Balance, cur), S(15, true, d.Balance > 0 ? Amber : Green), rx, y, right, PdfAlign.Right);
            if (d.Overdue > 0) f.DrawText($"dont exigible : {M(d.Overdue, cur)}", S(8, false, Red), rx, y, right, PdfAlign.Right);
            f.Y += boxH + 10;

            f.Table(
                [new PdfColumn(2.4f), new PdfColumn(1.2f), new PdfColumn(1.2f, PdfAlign.Right), new PdfColumn(1.1f, PdfAlign.Right),
                 new PdfColumn(1.2f, PdfAlign.Right), new PdfColumn(1.2f, PdfAlign.Right), new PdfColumn(1.2f)],
                new[] { "Rubrique", "Échéance", "Montant", "Pris en charge", "Payé", "Reste", "Statut" }.Select(t => new PdfCell(t, Align: PdfAlign.Left)).ToList(),
                d.Lines.Select(l => (IReadOnlyList<PdfCell>)
                [
                    new PdfCell(l.Label), new PdfCell(l.DueDate.ToString("dd/MM/yyyy", Fr)), new PdfCell(l.Amount.ToString("N0", Fr)),
                    new PdfCell(l.Covered == 0 ? "—" : l.Covered.ToString("N0", Fr)), new PdfCell(l.Paid.ToString("N0", Fr)),
                    new PdfCell(l.Remaining.ToString("N0", Fr), S(9, false, l.IsOverdue ? Red : Ink)),
                    new PdfCell(l.Status, S(9, false, l.IsOverdue ? Red : Muted)),
                ]),
                S(9), Solid with { HeadStyle = S(8, true, "#FFFFFF"), Padding = 4 },
                [new PdfCell("TOTAL NET À PAYER : " + M(d.TotalNet, cur), Span: 4, Align: PdfAlign.Left), new PdfCell(d.TotalPaid.ToString("N0", Fr)),
                 new PdfCell(d.Balance.ToString("N0", Fr)), new PdfCell(string.Empty)]);
            f.Y += 16;
            f.Ensure(60);
            f.Y += f.DrawText("Reçus enregistrés", S(9, true, Navy), f.Left, f.Y, f.ContentWidth);
            f.Table(
                [new PdfColumn(1.4f), new PdfColumn(1), new PdfColumn(2.6f), new PdfColumn(1), new PdfColumn(1.1f, PdfAlign.Right)],
                new[] { "N° reçu", "Date", "Affectation", "Mode", "Montant" }.Select(t => new PdfCell(t, Align: PdfAlign.Left)).ToList(),
                d.Receipts.Select(r =>
                {
                    var cancelled = r.Status != "Valide";
                    var tone = S(9, false, cancelled ? Muted : Ink);
                    return (IReadOnlyList<PdfCell>)
                    [
                        new PdfCell(r.Number + (cancelled ? " (annulé)" : string.Empty), tone), new PdfCell(r.Date.ToString("dd/MM/yyyy", Fr)),
                        new PdfCell(r.Allocations ?? string.Empty, S(8)), new PdfCell(r.Method), new PdfCell(r.Amount.ToString("N0", Fr), tone),
                    ];
                }),
                S(9), new PdfTableLook(S(8, true, Navy), null, Navy, false, 4, Navy));
        },
        Header(d.School, "RELEVÉ DE COMPTE", $"Situation arrêtée au {d.AsOf.ToString("dd/MM/yyyy", Fr)}"), Footer(null), 22);
    }

    // ---------- Liste ----------

    public byte[] Table(TableDocument d)
    {
        var widths = d.Columns.Select((title, c) =>
        {
            var longest = d.Rows.Take(300).Select(r => c < r.Count ? r[c].Length : 0).DefaultIfEmpty(0).Max();
            return Math.Clamp(Math.Max(title.Length * 0.8f, longest), 3f, 30f);
        }).ToArray();
        var amounts = d.Columns.Select((_, c) =>
        {
            var values = d.Rows.Select(r => c < r.Count ? r[c] : string.Empty).Where(v => v.Length > 0).ToList();
            return values.Count > 0 && values.All(v => v.EndsWith(" FC", StringComparison.Ordinal) || v.EndsWith(" $", StringComparison.Ordinal));
        }).ToArray();
        var columns = widths.Select((w, c) => new PdfColumn(w, amounts[c] ? PdfAlign.Right : PdfAlign.Left)).ToList();
        var (pw, ph) = d.Landscape ? (PdfFlow.A4Height, PdfFlow.A4Width) : (PdfFlow.A4Width, PdfFlow.A4Height);
        return PdfFlow.Create(pw, ph, 28, f =>
        {
            f.Y += 10;
            f.Table(columns, d.Columns.Select(c => new PdfCell(c)).ToList(),
                d.Rows.Select(r => (IReadOnlyList<PdfCell>)r.Select(v => new PdfCell(v)).ToList()),
                S(8.5f), Solid with { HeadStyle = S(8, true, "#FFFFFF"), Padding = 4, RightInset = 8 },
                d.Totals?.Select(v => new PdfCell(v)).ToList());
        },
        Header(d.School, d.Title, d.Subtitle), Footer(null), 22);
    }

    // ---------- Rapport à plusieurs parties (rapport journalier) ----------

    /// <summary>
    /// Serveur du téléphone : les rapports se suivent dans un seul document, chacun sous son propre titre
    /// (le rendu de l'ordinateur principal, lui, met chaque bulletin sur sa page).
    /// </summary>
    public byte[] Reports(IReadOnlyList<ReportDocument> documents)
    {
        if (documents.Count == 0) throw new ArgumentException("Aucun rapport à produire.", nameof(documents));
        var first = documents[0];
        var sections = new List<ReportSection>();
        foreach (var d in documents)
        {
            var figures = d.Figures.Select(f => (IReadOnlyList<string>)new[] { f.Label, f.Value }).ToList();
            sections.Add(new ReportSection($"{d.Title} — {d.Subtitle}", new[] { "Rubrique", "Valeur" }, figures));
            sections.AddRange(d.Sections);
            if (d.Note is { Length: > 0 } note)
                sections.Add(new ReportSection("Observations", new[] { "Note" }, new List<IReadOnlyList<string>> { new[] { note } }));
        }
        return Report(new ReportDocument(first.School, first.Title, $"{documents.Count} document(s)", [], sections));
    }

    public byte[] Report(ReportDocument d) => PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 28, f =>
    {
        f.Y += 10;
        const float gap = 8, tile = 40;
        var width = (f.ContentWidth - 3 * gap) / 4;
        foreach (var chunk in d.Figures.Chunk(4))
        {
            f.Ensure(tile + gap);
            var x = f.Left;
            foreach (var (label, value) in chunk)
            {
                f.Fill(x, f.Y, width, tile, "#E8EEF7");
                f.DrawText(label, S(7.5f, color: "#64748B"), x + 8, f.Y + 7, width - 16);
                f.DrawText(value, S(11, true, Navy), x + 8, f.Y + 19, width - 16);
                x += width + gap;
            }
            f.Y += tile + gap;
        }
        foreach (var section in d.Sections)
        {
            f.Y += 6;
            f.Ensure(40);
            f.Paragraph(section.Title, S(11, true, Navy), spaceAfter: 4);
            if (section.Rows.Count == 0)
            {
                f.Paragraph(section.Empty ?? "Rien à signaler.", S(8.5f, color: "#64748B", italic: true), spaceAfter: 4);
                continue;
            }
            var widths = section.Columns.Select((title, c) =>
            {
                var longest = section.Rows.Take(300).Select(r => c < r.Count ? r[c].Length : 0).DefaultIfEmpty(0).Max();
                return Math.Clamp(Math.Max(title.Length * 0.8f, longest), 3f, 30f);
            }).ToArray();
            var amounts = section.Columns.Select((_, c) =>
            {
                var values = section.Rows.Select(r => c < r.Count ? r[c] : string.Empty).Where(v => v.Length > 0).ToList();
                return values.Count > 0 && values.All(v => v.EndsWith(" FC", StringComparison.Ordinal) || v.EndsWith(" $", StringComparison.Ordinal));
            }).ToArray();
            var columns = widths.Select((w, c) => new PdfColumn(w, amounts[c] ? PdfAlign.Right : PdfAlign.Left)).ToList();
            f.Table(columns, section.Columns.Select(c => new PdfCell(c)).ToList(),
                section.Rows.Select(r => (IReadOnlyList<PdfCell>)r.Select(v => new PdfCell(v)).ToList()),
                S(8.5f), Solid with { HeadStyle = S(8, true, "#FFFFFF"), Padding = 4, RightInset = 8 },
                section.Totals?.Select(v => new PdfCell(v)).ToList());
        }
        if (d.Note is { Length: > 0 } note)
        {
            f.Y += 8;
            f.Paragraph(note, S(7.5f, color: "#64748B", italic: true));
        }
        if (d.VerificationUrl is { Length: > 0 } url)
        {
            f.Y += 8;
            f.Ensure(64);
            f.Image(DocumentService.QrPng(url), f.Left, f.Y, 60, 60, sharp: true);
            f.DrawText("Vérifier ce document", S(8, true, Navy), f.Left + 70, f.Y + 18, f.ContentWidth - 70);
            f.DrawText(url, S(7, color: "#64748B"), f.Left + 70, f.Y + 32, f.ContentWidth - 70);
            f.Y += 64;
        }
    },
    Header(d.School, d.Title, d.Subtitle), Footer(null), 22);

    // ---------- Bulletin ----------

    public byte[] ReportCard(ReportCardDocument d) => PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 36, f =>
    {
        f.Y += 12;
        var line = $"Matricule {d.Matricule} · {d.Promotion}";
        var boxH = PdfFlow.TextHeight(d.StudentName, S(12, true), f.ContentWidth - 20) + PdfFlow.TextHeight(line, S(9.5f), f.ContentWidth - 20) + 20;
        f.Fill(f.Left, f.Y, f.ContentWidth, boxH, Soft);
        var y = f.Y + 10;
        y += f.DrawText(d.StudentName, S(12, true, Navy), f.Left + 10, y, f.ContentWidth - 20);
        f.DrawText(line, S(9.5f, false, Muted), f.Left + 10, y, f.ContentWidth - 20);
        f.Y += boxH + 10;

        f.Table([new PdfColumn(4), new PdfColumn(1.2f, PdfAlign.Center), new PdfColumn(1.4f, PdfAlign.Center), new PdfColumn(1.6f, PdfAlign.Center)],
            new[] { "Cours", "Coefficient", "Moyenne / 20", "Points" }.Select(t => new PdfCell(t, Align: PdfAlign.Left)).ToList(),
            d.Lines.Select(l => (IReadOnlyList<PdfCell>)
            [
                new PdfCell(l.Course), new PdfCell(l.Coefficient.ToString("0.##", Fr)),
                new PdfCell(l.Average?.ToString("0.00", Fr) ?? "—", S(9.5f, false, l.Average < 10 ? Red : Ink)),
                new PdfCell(l.Average is null ? "—" : (l.Average.Value * l.Coefficient).ToString("0.00", Fr)),
            ]),
            S(9.5f), Solid);
        f.Y += 10;

        // Moyenne, rang et décision dans trois cases.
        f.Ensure(70);
        var w = (f.ContentWidth - 16) / 3;
        var boxes = new[]
        {
            ("Moyenne générale", d.Average?.ToString("0.00", Fr) + " / 20"),
            ("Rang", d.Rank is null ? "—" : $"{d.Rank} / {d.ClassSize}"),
            ("Décision", d.Decision),
        };
        var bh = boxes.Max(b => PdfFlow.LineHeight(S(8)) + PdfFlow.TextHeight(b.Item2 ?? "—", S(13, true), w - 16)) + 16;
        for (var i = 0; i < boxes.Length; i++)
        {
            var x = f.Left + i * (w + 8);
            f.Stroke(x, f.Y, w, bh, Border, 0.5f);
            f.DrawText(boxes[i].Item1, S(8, false, Muted), x + 8, f.Y + 8, w - 16);
            f.DrawText(boxes[i].Item2 ?? "—", S(13, true, Navy), x + 8, f.Y + 8 + PdfFlow.LineHeight(S(8)), w - 16);
        }
        f.Y += bh + 30;

        // Signatures.
        f.Ensure(60);
        f.DrawText("Le titulaire", S(9.5f, false, Muted), f.Left, f.Y, 200);
        f.DrawText("La direction", S(9.5f, false, Muted), f.Right - 140, f.Y, 140);
        var sy = f.Y + PdfFlow.LineHeight(S(9.5f)) + 30;
        f.Line(f.Left, sy, f.Left + 140, sy, Ink, 0.5f);
        f.Line(f.Right - 140, sy, f.Right, sy, Ink, 0.5f);
        f.Y = sy + 6;
    },
    Header(d.School, "BULLETIN DE NOTES", $"Année scolaire {d.AcademicYear}"), Footer(null), 22);

    // ---------- Cartes d'accès ----------
    // Même dessin que l'ordinateur (CardDesign) : en-tête centré, « Session AAAA » souligné, identité, QR.
    // Ni logo ni cachet : ils sont apposés à la main après l'impression.

    private const string Guide = "#8A96A6";
    private static float Mm(float v) => v * 72f / 25.4f;

    private static readonly Dictionary<string, SkiaSharp.SKTypeface> CardSans = new(StringComparer.Ordinal);

    private static SkiaSharp.SKTypeface CardFont(string file)
    {
        lock (CardSans)
        {
            if (CardSans.TryGetValue(file, out var cached)) return cached;
            using var stream = typeof(DocumentService).Assembly.GetManifestResourceStream($"Scolia.Infrastructure.Documents.Fonts.{file}")
                ?? throw new InvalidOperationException($"Police {file} introuvable.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var face = SkiaSharp.SKTypeface.FromData(SkiaSharp.SKData.CreateCopy(memory.ToArray())) ?? SkiaSharp.SKTypeface.Default;
            CardSans[file] = face;
            return face;
        }
    }

    private static SkiaSharp.SKFont CardText(string file, float sizePt) =>
        new(CardFont(file), sizePt) { Subpixel = true, LinearMetrics = true, Hinting = SkiaSharp.SKFontHinting.None, Edging = SkiaSharp.SKFontEdging.Antialias };

    /// <summary>Huit cartes ID-1 par A4, sur la même grille que les cartes de l'ordinateur, avec les repères de découpe.</summary>
    public byte[] StudentCards(CardDocument d)
    {
        var pages = Enumerable.Range(0, StudentCardRules.PagesFor(d.Cards.Count))
            .Select(p => d.Cards.Skip(p * StudentCardRules.CardsPerPage).Take(StudentCardRules.CardsPerPage).ToList()).ToList();
        var page = 0;
        return PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 0, f =>
        {
            for (page = 0; page < pages.Count; page++)
            {
                if (page > 0) f.Ensure(f.Height + 1);
                var cards = pages[page];
                for (var i = 0; i < cards.Count; i++)
                {
                    var (row, col) = (i / 2, i % 2);
                    var x = Mm(StudentCardRules.MarginMm + col * (StudentCardRules.CardWidthMm + StudentCardRules.ColumnGapMm));
                    var y = Mm(StudentCardRules.MarginMm + row * (StudentCardRules.CardHeightMm + StudentCardRules.RowGapMm));
                    Card(f, d, cards[i], x, y);
                }
                CutLines(f);
            }
        });
    }

    private static void Card(PdfFlow f, CardDocument d, CardItem card, float x, float y)
    {
        var canvas = f.Canvas;
        var w = Mm(StudentCardRules.CardWidthMm);
        var h = Mm(StudentCardRules.CardHeightMm);
        f.Fill(x, y, w, h, CardDesign.Paper);
        f.Stroke(x, y, w, h, CardDesign.Frame, 0.4f);

        // En-tête centré : place libre du logo (apposé à la main), puis nom, service, filet, contacts.
        var header = d.Header;
        var textX = x + Mm(CardDesign.HeaderTextLeftMm);
        var textW = Mm(CardDesign.HeaderTextMm);
        var name = CardDesign.SchoolName(header);
        using var nameFont = CardText("LiberationSerif-Regular.ttf", CardDesign.SchoolNamePt(name));
        using var departmentFont = CardText("LiberationSerif-Regular.ttf", CardDesign.DepartmentPt);
        using var contactFont = CardText("Roboto-Regular.ttf", CardDesign.ContactPt);
        var nameLines = Wrap(name, nameFont, textW);
        var department = CardDesign.Department(header);
        var contacts = CardDesign.Contacts(header).SelectMany(l => Wrap(l, contactFont, textW)).ToList();
        var nameStep = nameFont.Size * 1.05f;
        var blockH = nameLines.Count * nameStep + (department is null ? 0 : departmentFont.Size * 1.25f)
            + (contacts.Count == 0 ? 0 : Mm(0.9f) + contacts.Count * contactFont.Size * 1.12f);
        var top = y + Mm(CardDesign.HeaderTopMm) + Math.Max(0, (Mm(CardDesign.HeaderHeightMm) - blockH) / 2);
        var ink = PdfFlow.Color(CardDesign.HeaderInk);
        var muted = PdfFlow.Color(CardDesign.HeaderMuted);
        var line = top;
        foreach (var text in nameLines)
        {
            line += nameStep;
            Draw(canvas, text, textX, line - nameFont.Size * 0.2f, nameFont, ink, 0.02f);
        }
        if (department is not null)
        {
            line += departmentFont.Size * 1.25f;
            Draw(canvas, department, textX, line - departmentFont.Size * 0.25f, departmentFont, ink, 0.08f);
        }
        if (contacts.Count > 0)
        {
            line += Mm(0.35f);
            using (var rule = new SkiaSharp.SKPaint { Color = muted, StrokeWidth = 0.45f, IsAntialias = true })
                canvas.DrawLine(textX, line, textX + textW, line, rule);
            line += Mm(0.55f);
            foreach (var text in contacts)
            {
                line += contactFont.Size * 1.12f;
                Draw(canvas, text, textX, line - contactFont.Size * 0.22f, contactFont, muted, 0);
            }
        }

        // QR de contrôle, discret dans le coin libre.
        f.Image(DocumentService.QrPng(card.QrText), x + Mm(CardDesign.QrLeftMm), y + Mm(CardDesign.QrMarginMm), Mm(CardDesign.QrMm), Mm(CardDesign.QrMm), sharp: true);

        // « Session 2026 » : grand, gras, souligné, centré.
        using (var sessionFont = CardText("Montserrat-Bold.ttf", CardDesign.SessionPt))
        {
            var title = CardDesign.SessionTitle(d.Session);
            var width = sessionFont.MeasureText(title);
            var sx = x + (w - width) / 2;
            var baseline = y + Mm(CardDesign.SessionTopMm) + sessionFont.Size * 0.95f;
            var color = PdfFlow.Color(CardDesign.SessionInk);
            Draw(canvas, title, sx, baseline, sessionFont, color, 0);
            using var underline = new SkiaSharp.SKPaint { Color = color, StrokeWidth = sessionFont.Size * 0.075f, IsAntialias = true };
            canvas.DrawLine(sx, baseline + sessionFont.Size * 0.14f, sx + width, baseline + sessionFont.Size * 0.14f, underline);
        }

        // Identité : trois lignes de même corps, capitales espacées.
        var lines = CardDesign.Identity(card);
        var identityPt = CardDesign.IdentityPt(card);
        using (var probe = CardText("Montserrat-Medium.ttf", identityPt))
        {
            // Mesure réelle : la ligne la plus longue tient dans la largeur, sans renvoi à la ligne.
            var widest = lines.Max(l => probe.MeasureText(l.Label + " : " + l.Value) + (l.Label.Length + 3 + l.Value.Length) * CardDesign.IdentityTracking * identityPt);
            if (widest > Mm(CardDesign.IdentityWidthMm)) identityPt *= Mm(CardDesign.IdentityWidthMm) / widest;
        }
        using (var identityFont = CardText("Montserrat-Medium.ttf", identityPt))
        {
            var color = PdfFlow.Color(CardDesign.IdentityInk);
            for (var i = 0; i < lines.Count; i++)
            {
                var baseline = y + Mm(CardDesign.IdentityTopMm + i * CardDesign.IdentityStepMm) + identityFont.Size * 1.0f;
                Draw(canvas, lines[i].Label + " : " + lines[i].Value, x + Mm(CardDesign.IdentityLeftMm), baseline, identityFont, color, CardDesign.IdentityTracking);
            }
        }
    }

    /// <summary>Texte sur une ligne, avec un espacement entre les lettres (en cadratins).</summary>
    private static void Draw(SkiaSharp.SKCanvas canvas, string text, float x, float baseline, SkiaSharp.SKFont font, SkiaSharp.SKColor color, float tracking)
    {
        using var paint = new SkiaSharp.SKPaint { Color = color, IsAntialias = true };
        if (tracking <= 0)
        {
            canvas.DrawText(text, x, baseline, SkiaSharp.SKTextAlign.Left, font, paint);
            return;
        }
        var extra = tracking * font.Size;
        foreach (var c in text)
        {
            var glyph = c.ToString();
            canvas.DrawText(glyph, x, baseline, SkiaSharp.SKTextAlign.Left, font, paint);
            x += font.MeasureText(glyph) + extra;
        }
    }

    private static List<string> Wrap(string text, SkiaSharp.SKFont font, float width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && font.MeasureText(candidate) > width) { lines.Add(current); current = word; }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    /// <summary>Repères de découpe en pointillés, au milieu des interstices : la coupe passe entre deux cartes.</summary>
    private static void CutLines(PdfFlow f)
    {
        var left = Mm(StudentCardRules.MarginMm);
        var top = Mm(StudentCardRules.MarginMm);
        var gridW = Mm(2 * StudentCardRules.CardWidthMm + StudentCardRules.ColumnGapMm);
        var gridH = Mm(4 * StudentCardRules.CardHeightMm + 3 * StudentCardRules.RowGapMm);
        using var paint = PdfFlow.Paint(Guide);
        paint.StrokeWidth = Mm(0.3f);
        paint.PathEffect = SkiaSharp.SKPathEffect.CreateDash([Mm(2.5f), Mm(2.5f)], 0);
        var vx = left + Mm(StudentCardRules.CardWidthMm + StudentCardRules.ColumnGapMm / 2);
        f.Canvas.DrawLine(vx, top, vx, top + gridH, paint);
        for (var r = 1; r < 4; r++)
        {
            var hy = top + Mm(r * StudentCardRules.CardHeightMm + (r - 1) * StudentCardRules.RowGapMm + StudentCardRules.RowGapMm / 2);
            f.Canvas.DrawLine(left, hy, left + gridW, hy, paint);
        }
    }

    /// <summary>Cartes au format Word : le même fichier que sur l'ordinateur, écrit sans PDF.</summary>
    public byte[] StudentCardsWord(CardDocument d) => CardWordWriter.Write(d);
}
