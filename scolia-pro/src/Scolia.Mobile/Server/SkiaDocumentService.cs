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

    private const string CardNavy = "#0B3B6F";
    private const string CardInk = "#1B2430";
    private const string CardMuted = "#5B6878";
    private const string Frame = "#AEB8C5";
    private const string Guide = "#8A96A6";
    private static float Mm(float v) => v * 72f / 25.4f;

    /// <summary>Huit cartes de 92 × 63 mm par A4, sur la même grille que les cartes de l'ordinateur, avec les repères de découpe.</summary>
    public byte[] StudentCards(CardDocument d)
    {
        var seal = d.Header.Logo ?? DocumentService.DefaultLogo();
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
                    Card(f, d, cards[i], seal, x, y);
                }
                CutLines(f);
            }
        });
    }

    private static void Card(PdfFlow f, CardDocument d, CardItem card, byte[] seal, float x, float y)
    {
        var w = Mm(StudentCardRules.CardWidthMm);
        var h = Mm(StudentCardRules.CardHeightMm);
        var radius = Mm(2.2f);
        f.Canvas.Save();
        using (var clip = new SkiaSharp.SKPath())
        {
            clip.AddRoundRect(SkiaSharp.SKRect.Create(x, y, w, h), radius, radius);
            f.Canvas.ClipPath(clip, antialias: true);
            f.Fill(x, y, w, h, "#FFFFFF");
            // Bande de la promotion, en bas, sans texte.
            f.Fill(x, y + h - Mm(6.2f), w, Mm(6.2f), card.Color.StartsWith('#') ? card.Color : "#" + card.Color);
        }
        f.Canvas.Restore();
        f.Stroke(x, y, w, h, Frame, 0.6f, radius);

        // En-tête officiel : sceau à gauche, mentions centrées.
        var top = y + Mm(1.5f);
        var headH = Mm(15.6f);
        f.Image(seal, x + Mm(3), top, Mm(12.5f), headH);
        var mentions = new List<(string Text, PdfStyle Style)>();
        void Mention(string? value, float size, bool bold, string color)
        {
            if (!string.IsNullOrWhiteSpace(value)) mentions.Add((value.Trim().ToUpperInvariant(), new PdfStyle(size, bold, false, color)));
        }
        Mention(d.Header.Country, 5f, true, CardInk);
        Mention(d.Header.Ministry, 4.3f, false, CardMuted);
        Mention(d.Header.Direction, 4.3f, false, CardMuted);
        Mention(d.Header.Division, 4.3f, false, CardMuted);
        Mention(d.Header.CityZone, 4.3f, false, CardMuted);
        Mention(d.Header.Institute, 5.2f, true, CardNavy);
        Mention(d.Header.Address, 4.3f, false, CardMuted);
        var tx = x + Mm(3) + Mm(12.5f) + Mm(1.5f);
        var tw = w - Mm(6) - Mm(14);
        var textH = mentions.Sum(m => PdfFlow.TextHeight(m.Text, m.Style, tw) * 0.8f);
        var my = top + Math.Max(0, (headH - textH) / 2);
        foreach (var (text, style) in mentions) my += f.DrawText(text, style, tx, my, tw, PdfAlign.Center) * 0.8f;

        // Bandeau du titre et de la session.
        var ribbonY = top + headH + Mm(1);
        f.Fill(x, ribbonY, w, Mm(6), CardNavy);
        var titleStyle = new PdfStyle(7.6f, true, false, "#FFFFFF");
        f.DrawText("CARTE D'ACCÈS", titleStyle, x + Mm(3), ribbonY + (Mm(6) - PdfFlow.LineHeight(titleStyle)) / 2, w / 2);
        var session = $"SESSION {d.Session}";
        var sessionStyle = new PdfStyle(session.Length > 24 ? 6.4f : session.Length > 20 ? 7f : 7.6f, true, false, "#FFFFFF");
        f.DrawText(session, sessionStyle, x + w / 2, ribbonY + (Mm(6) - PdfFlow.LineHeight(sessionStyle)) / 2, w / 2 - Mm(3), PdfAlign.Right);

        // Identité à gauche, QR de contrôle à droite.
        var idY = ribbonY + Mm(6) + Mm(2);
        var idW = w - Mm(8) - Mm(19);
        foreach (var (label, value) in new[] { ("NOM", card.LastName), ("POST-NOM", card.MiddleName), ("PRÉNOM", card.FirstName), ("CLASSE", card.Classe) })
        {
            var used = f.DrawSpans([($"{label} : ", new PdfStyle(5.6f, true, false, CardMuted)), (value, new PdfStyle(value.Length > 24 ? 6.8f : 7.8f, true, false, CardInk))], x + Mm(4), idY, idW);
            idY += used + Mm(1.4f) - 2;
        }
        var qrX = x + w - Mm(4) - Mm(19);
        var qrY = ribbonY + Mm(6) + Mm(2);
        f.Image(DocumentService.QrPng(card.QrText), qrX, qrY, Mm(19), Mm(17), sharp: true);
        f.DrawText("Contrôle d'accès", new PdfStyle(4.2f, false, false, CardMuted), qrX, qrY + Mm(17), Mm(19), PdfAlign.Center);

        // Bas de carte : l'année à gauche, le visa de la direction à droite.
        var baseY = y + h - Mm(6.2f) - Mm(1.4f);
        var yearStyle = new PdfStyle(5.2f, true, false, CardNavy);
        f.DrawText($"Année scolaire {d.AcademicYear}", yearStyle, x + Mm(4), baseY - PdfFlow.LineHeight(yearStyle), w / 2);
        var visaX = x + w - Mm(4) - Mm(30);
        var visaStyle = new PdfStyle(4.2f, false, false, CardMuted);
        var visaTop = baseY - PdfFlow.LineHeight(visaStyle);
        f.Line(visaX, visaTop - Mm(0.6f), visaX + Mm(30), visaTop - Mm(0.6f), Frame, 0.4f);
        f.DrawText("Visa de la direction", visaStyle, visaX, visaTop, Mm(30), PdfAlign.Center);
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
