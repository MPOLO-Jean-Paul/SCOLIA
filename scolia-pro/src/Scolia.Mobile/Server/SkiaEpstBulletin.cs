using System.Globalization;
using Scolia.Application.Dtos;
using Scolia.Infrastructure.Documents;
using SkiaSharp;

namespace Scolia.Mobile.Server;

/// <summary>
/// Bulletin EPST du téléphone serveur : même modèle officiel que l'ordinateur
/// (<see cref="EpstBulletinLayout"/>), dessiné case par case avec SkiaSharp.
/// </summary>
public sealed partial class SkiaAcademicDocuments
{
    private const string BRule = "#222222";
    private const string BShade = "#E5E7EB";
    private const string BTotal = "#F3F4F6";
    private const string BBlack = "#1F1F1F";
    private const string BMuted = "#4B5563";
    private const string BInk = "#111111";

    public byte[] EpstBulletins(EpstBulletinPrintDto d) =>
        PdfFlow.Create(PdfFlow.A4Width, PdfFlow.A4Height, 18, f =>
        {
            var first = true;
            foreach (var pupil in d.Pupils)
            {
                if (!first) NewPage(f);
                first = false;
                Bulletin(f, d, EpstBulletinLayout.For(d, pupil));
            }
        });

    private static PdfStyle B(float size, bool bold = false, string color = BInk) => new(size, bold, false, color);

    /// <summary>Texte d'une ligne centré verticalement dans une case ; réduit s'il déborde.</summary>
    private static void CellText(PdfFlow f, string text, PdfStyle style, float x, float y, float w, float h, PdfAlign align = PdfAlign.Center)
    {
        if (text.Length == 0) return;
        var size = style.Size;
        while (size > 4.5f && PdfFlow.Measure(text, style with { Size = size }) > w - 2) size -= 0.25f;
        var s = style with { Size = size };
        var lines = PdfFlow.Wrap(text, s, w - 2);
        var lh = PdfFlow.LineHeight(s);
        var top = y + (h - lines.Count * lh) / 2;
        f.DrawText(text, s, x + 1, top, w - 2, align);
    }

    private static void Flag(PdfFlow f, float x, float y, float w, float h)
    {
        var canvas = f.Canvas;
        canvas.Save();
        canvas.ClipRect(SKRect.Create(x, y, w, h));
        canvas.Translate(x, y);
        canvas.Scale(w / 800f, h / 600f);
        using (var sky = PdfFlow.Paint(DrcFlag.Sky)) canvas.DrawRect(0, 0, 800, 600, sky);
        foreach (var (color, width) in new[] { (DrcFlag.Yellow, 200f), (DrcFlag.Red, 140f) })
        {
            using var stripe = PdfFlow.Paint(color);
            stripe.StrokeWidth = width;
            canvas.DrawLine(-40, 630, 840, -30, stripe);
        }
        using (var path = new SKPath())
        {
            var star = DrcFlag.Star();
            path.MoveTo(star[0].X, star[0].Y);
            foreach (var (px, py) in star.Skip(1)) path.LineTo(px, py);
            path.Close();
            using var yellow = PdfFlow.Paint(DrcFlag.Yellow);
            canvas.DrawPath(path, yellow);
        }
        canvas.Restore();
    }

    private static void BoxRow(PdfFlow f, string label, string value, int minimum, float x, float y)
    {
        f.DrawText(label, B(7.5f, false, BMuted), x, y + 1, 44);
        var bx = x + 44;
        foreach (var ch in EpstBulletinLayout.Boxes(value, minimum))
        {
            f.Stroke(bx, y, 11, 12, BRule, 0.6f);
            CellText(f, ch, B(7.5f, true), bx, y, 11, 12);
            bx += 11;
        }
    }

    private static float Fields(PdfFlow f, IReadOnlyList<(string Label, string Value)> fields, float x, float y, float w)
    {
        var top = y;
        foreach (var (label, value) in fields)
            y += f.DrawSpans([(label + " : ", B(7.5f, false, BMuted)), (value, B(8.5f, true))], x, y, w) + 1;
        return y - top;
    }

    private static void Bulletin(PdfFlow f, EpstBulletinPrintDto d, EpstBulletinLayout l)
    {
        var x0 = f.Left;
        var w = f.ContentWidth;
        var y = f.Y;
        var fr = CultureInfo.GetCultureInfo("fr-FR");

        // En-tête : drapeau, République et ministère, logo de l'école.
        const float bandH = 49;
        f.Stroke(x0, y, w, bandH, BRule, 0.8f);
        Flag(f, x0 + 5, y + 5, 52, 39);
        if (d.SchoolLogo is { Length: > 0 } logo) f.Image(logo, x0 + w - 57, y + 5, 52, 39);
        var ty = y + 7;
        ty += f.DrawText("RÉPUBLIQUE DÉMOCRATIQUE DU CONGO", B(10.5f, true), x0 + 60, ty, w - 120, PdfAlign.Center);
        ty += f.DrawText(l.Ministry.ToUpper(fr), B(8, true), x0 + 60, ty, w - 120, PdfAlign.Center);
        if (l.Division is { } division) f.DrawText(division, B(7.5f, false, BMuted), x0 + 60, ty, w - 120, PdfAlign.Center);
        y += bandH;

        // Identification de l'école et de l'élève.
        var half = w / 2;
        var leftH = Fields(f, l.SchoolFields, x0 + 5, y + 5, half - 10);
        BoxRow(f, "CODE :", l.SchoolCode, EpstBulletinLayout.CodeBoxes, x0 + 5, y + 7 + leftH);
        var rightH = Fields(f, l.PupilFields, x0 + half + 5, y + 5, half - 10);
        BoxRow(f, "N° PERM. :", l.PermanentNumber, EpstBulletinLayout.PermanentBoxes, x0 + half + 5, y + 7 + rightH);
        var idH = Math.Max(leftH, rightH) + 26;
        f.Stroke(x0, y, w, idH, BRule, 0.8f);
        f.Line(x0 + half, y, x0 + half, y + idH, BRule, 0.8f);
        y += idH;

        // Titre.
        const float titleH = 27;
        f.Fill(x0, y, w, titleH, BShade);
        f.Stroke(x0, y, w, titleH, BRule, 0.8f);
        f.DrawText(l.Title, B(10, true), x0, y + 2, w, PdfAlign.Center);
        f.DrawText(l.Subtitle, B(8.5f, true), x0, y + 15, w, PdfAlign.Center);
        y += titleH + 4;

        // Tableau des branches.
        var total = l.Columns.Sum();
        var widths = l.Columns.Select(c => c / total * w).ToArray();
        var xs = new float[widths.Length + 1];
        xs[0] = x0;
        for (var i = 0; i < widths.Length; i++) xs[i + 1] = xs[i] + widths[i];
        const float headRowH = 11;
        foreach (var h in l.Head)
        {
            var cx = xs[h.Column];
            var cw = xs[h.Column + h.ColumnSpan] - cx;
            var cy = y + h.Row * headRowH;
            var ch = h.RowSpan * headRowH;
            f.Fill(cx, cy, cw, ch, h.Black ? BBlack : BShade);
            f.Stroke(cx, cy, cw, ch, BRule, 0.6f);
            CellText(f, h.Text, B(h.Column == 0 ? 8.5f : 6.5f, true), cx, cy, cw, ch);
        }
        y += 3 * headRowH;
        foreach (var r in l.Rows)
        {
            var labelStyle = B(r.Kind == BulletinRowKind.Subject ? 7.5f : 7f, r.Kind != BulletinRowKind.Subject);
            var rh = Math.Max(11.5f, PdfFlow.TextHeight(r.Label, labelStyle, widths[0] - 6) + 2);
            f.Fill(xs[0], y, widths[0], rh, r.Kind == BulletinRowKind.Subject ? "#FFFFFF" : BShade);
            f.Stroke(xs[0], y, widths[0], rh, BRule, 0.5f);
            f.DrawText(r.Label, labelStyle, xs[0] + 3, y + (rh - PdfFlow.TextHeight(r.Label, labelStyle, widths[0] - 6)) / 2, widths[0] - 6);
            for (var i = 0; i < r.Cells.Count; i++)
            {
                var c = r.Cells[i];
                var cx = xs[i + 1];
                var background = c.Tone switch
                {
                    BulletinTone.Blocked => BBlack,
                    BulletinTone.Total or BulletinTone.Alert => BTotal,
                    _ => r.Kind == BulletinRowKind.Maxima ? BShade : "#FFFFFF",
                };
                f.Fill(cx, y, widths[i + 1], rh, background);
                f.Stroke(cx, y, widths[i + 1], rh, BRule, 0.5f);
                CellText(f, c.Text, B(7.3f, c.Bold, c.Tone == BulletinTone.Alert ? "#B3261E" : BInk), cx, y, widths[i + 1], rh);
            }
            y += rh;
        }
        y += 6;

        // Décision, lieu et date, sceau et signature du chef d'établissement.
        var leftW = w * 0.59f - 8;
        var rightX = x0 + w * 0.59f;
        var rightW = w - w * 0.59f;
        var ly = y;
        foreach (var m in l.Mentions)
        {
            var style = B(7.8f, !m.Struck, m.Struck ? BMuted : BInk);
            var text = "– " + m.Text;
            var lines = PdfFlow.Wrap(text, style, leftW);
            var lh = PdfFlow.LineHeight(style);
            f.DrawText(text, style, x0, ly, leftW);
            if (m.Struck)
                for (var i = 0; i < lines.Count; i++)
                {
                    var mid = ly + i * lh + lh * 0.55f;
                    f.Line(x0, mid, x0 + Math.Min(leftW, PdfFlow.Measure(lines[i], style)), mid, BMuted, 0.6f);
                }
            ly += lines.Count * lh + 2;
        }
        if (l.Decision is { } decision)
            ly += f.DrawSpans([("DÉCISION : ", B(8, false, BMuted)), (decision, B(9.5f, true))], x0, ly + 2, leftW) + 2;
        if (l.Warning is { } warning)
            ly += f.DrawText(warning, B(7.5f, true, "#B3261E"), x0, ly + 2, leftW) + 2;
        f.DrawText("Signature de l'élève", B(7.5f, false, BMuted), x0, ly + 12, leftW);
        ly += 24;

        var ry = y + f.DrawText(l.PlaceAndDate, B(8), rightX, y, rightW, PdfAlign.Center) + 4;
        var sealW = (rightW - 8) / 2.3f;
        f.Stroke(rightX, ry, sealW, 52, BMuted, 0.6f);
        CellText(f, "Sceau de l'école", B(7, false, BMuted), rightX, ry, sealW, 52);
        var chiefX = rightX + sealW + 8;
        var chiefW = rightW - sealW - 8;
        f.DrawText("Le Chef d'Établissement", B(8, true), chiefX, ry, chiefW, PdfAlign.Center);
        f.DrawText("Noms et signature", B(7, false, BMuted), chiefX, ry + 40, chiefW, PdfAlign.Center);
        ry += 56;

        // Pied du modèle officiel.
        var fy = Math.Max(Math.Max(ly, ry) + 6, f.Height - 18 - 34);
        fy += f.DrawText(EpstBulletinLayout.Footnote, B(7), x0, fy, w);
        fy += f.DrawText(EpstBulletinLayout.Notice, new PdfStyle(7, false, true, BInk), x0, fy, w);
        f.DrawText(l.SystemNote, B(6, false, BMuted), x0, fy + 2, w);
        f.Y = f.Height - 18;
    }
}
