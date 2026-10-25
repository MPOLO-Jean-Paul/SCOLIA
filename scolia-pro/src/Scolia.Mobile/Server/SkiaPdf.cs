using System.Globalization;
using SkiaSharp;

namespace Scolia.Mobile.Server;

/// <summary>Style d'un texte : taille en points, gras, italique, couleur.</summary>
public readonly record struct PdfStyle(float Size, bool Bold = false, bool Italic = false, string Color = "#1E293B")
{
    public PdfStyle With(float? size = null, bool? bold = null, bool? italic = null, string? color = null) =>
        new(size ?? Size, bold ?? Bold, italic ?? Italic, color ?? Color);
}

public enum PdfAlign { Left, Center, Right }

/// <summary>Colonne d'un tableau : largeur relative et alignement.</summary>
public sealed record PdfColumn(float Width, PdfAlign Align = PdfAlign.Left);

/// <summary>Cellule d'un tableau : texte, style propre éventuel et colonnes couvertes.</summary>
public sealed record PdfCell(string Text, PdfStyle? Style = null, int Span = 1, PdfAlign? Align = null);

/// <summary>
/// Petite mise en page PDF sur SkiaSharp, pour le téléphone serveur : SkiaSharp est déjà dans l'application Android,
/// alors que la bibliothèque native de QuestPDF n'existe pas pour Android. Le document est mis en page deux fois :
/// la première passe compte les pages, la seconde dessine, si bien que chaque pied de page affiche « Page n / total ».
/// Le texte est vectoriel, avec les polices Roboto fournies : il reste net à l'impression et se sélectionne.
/// </summary>
public sealed class PdfFlow
{
    public const float A4Width = 595.28f;
    public const float A4Height = 841.89f;
    public const float A5Width = 419.53f;
    public const float A5Height = 595.28f;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static SKTypeface? regular;
    private static SKTypeface? bold;

    private readonly SKDocument? document;
    private readonly float margin;
    private readonly Func<PdfFlow, float>? header;
    private readonly Action<PdfFlow, int, int>? footer;
    private readonly Action<PdfFlow>? overlay;
    private readonly int totalPages;
    private float footerHeight;

    private PdfFlow(SKDocument? document, float width, float height, float margin, Func<PdfFlow, float>? header,
        Action<PdfFlow, int, int>? footer, Action<PdfFlow>? overlay, int totalPages)
    {
        this.document = document;
        Width = width;
        Height = height;
        this.margin = margin;
        this.header = header;
        this.footer = footer;
        this.overlay = overlay;
        this.totalPages = totalPages;
    }

    public float Width { get; }
    public float Height { get; }
    public float Left => margin;
    public float Right => Width - margin;
    public float ContentWidth => Width - 2 * margin;
    public float Bottom => Height - margin - footerHeight;

    /// <summary>Position verticale courante, dans la page en cours.</summary>
    public float Y { get; set; }
    public int Page { get; private set; }
    public SKCanvas Canvas { get; private set; } = null!;

    /// <summary>
    /// Produit le PDF. <paramref name="content"/> écrit le corps ; l'en-tête est dessiné en haut de chaque page
    /// (il rend sa hauteur), le pied en bas avec le numéro de page, le calque par-dessus (filigrane).
    /// </summary>
    public static byte[] Create(float width, float height, float margin, Action<PdfFlow> content,
        Func<PdfFlow, float>? header = null, Action<PdfFlow, int, int>? footer = null, float footerHeight = 0, Action<PdfFlow>? overlay = null)
    {
        LoadFonts();
        // Première passe sur un canevas d'enregistrement : seulement pour compter les pages.
        var count = new PdfFlow(null, width, height, margin, header, footer, overlay, 0) { footerHeight = footerHeight };
        count.Run(content);
        using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Creator = "SCOLIA", Producer = "SCOLIA", RasterDpi = 300, EncodingQuality = 101 }))
        {
            var flow = new PdfFlow(pdf, width, height, margin, header, footer, overlay, count.Page) { footerHeight = footerHeight };
            flow.Run(content);
            pdf.Close();
        }
        return stream.ToArray();
    }

    private SKPictureRecorder? recorder;

    private void Run(Action<PdfFlow> content)
    {
        StartPage();
        content(this);
        EndPage();
    }

    private void StartPage()
    {
        Page++;
        if (document is not null) Canvas = document.BeginPage(Width, Height);
        else
        {
            recorder?.Dispose();
            recorder = new SKPictureRecorder();
            Canvas = recorder.BeginRecording(new SKRect(0, 0, Width, Height));
        }
        Y = margin;
        if (header is not null) Y = margin + header(this);
    }

    private void EndPage()
    {
        footer?.Invoke(this, Page, totalPages);
        overlay?.Invoke(this);
        if (document is not null) document.EndPage();
        else
        {
            using var picture = recorder?.EndRecording();
        }
    }

    /// <summary>Passe à la page suivante si la hauteur demandée ne tient plus dans celle-ci. Rend vrai si une page a été ajoutée.</summary>
    public bool Ensure(float needed)
    {
        if (Y + needed <= Bottom + 0.01f) return false;
        EndPage();
        StartPage();
        return true;
    }

    // ---------- Polices et texte ----------

    private static void LoadFonts()
    {
        if (regular is not null) return;
        var assembly = typeof(Scolia.Infrastructure.Documents.DocumentService).Assembly;
        SKTypeface Load(string name)
        {
            using var stream = assembly.GetManifestResourceStream($"Scolia.Infrastructure.Documents.Fonts.{name}")
                ?? throw new InvalidOperationException($"Police {name} introuvable.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return SKTypeface.FromData(SKData.CreateCopy(memory.ToArray())) ?? SKTypeface.Default;
        }
        bold = Load("Roboto-Bold.ttf");
        regular = Load("Roboto-Regular.ttf");
    }

    public static SKFont Font(PdfStyle style)
    {
        LoadFonts();
        // Mesures exactes, sans lissage des contours : aux petites tailles, le lissage arrondit la largeur de chaque lettre
        // et l'espacement devient irrégulier.
        var font = new SKFont(style.Bold ? bold : regular, style.Size)
        {
            Subpixel = true, LinearMetrics = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias,
        };
        // Pas de Roboto italique fournie : l'italique est obtenu en inclinant les caractères, comme le fait QuestPDF.
        if (style.Italic) font.SkewX = -0.2f;
        return font;
    }

    public static SKPaint Paint(string color) => new() { Color = Color(color), IsAntialias = true };

    /// <summary>Couleur #RRGGBB ou #RRGGBBAA.</summary>
    public static SKColor Color(string hex)
    {
        var h = hex.TrimStart('#');
        byte P(int i) => byte.Parse(h.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return h.Length == 8 ? new SKColor(P(0), P(2), P(4), P(6)) : new SKColor(P(0), P(2), P(4));
    }

    public static float LineHeight(PdfStyle style) => style.Size * 1.32f;

    public static float Measure(string text, PdfStyle style)
    {
        using var font = Font(style);
        return font.MeasureText(text);
    }

    /// <summary>Coupe un texte en lignes qui tiennent dans la largeur, mot par mot ; un mot trop long est coupé.</summary>
    public static List<string> Wrap(string text, PdfStyle style, float width)
    {
        var lines = new List<string>();
        using var font = Font(style);
        foreach (var paragraph in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (font.MeasureText(candidate) <= width || line.Length == 0 && font.MeasureText(word) <= width)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) lines.Add(line);
                line = word;
                // Mot plus large que la colonne : coupé caractère par caractère.
                while (font.MeasureText(line) > width && line.Length > 1)
                {
                    var cut = line.Length - 1;
                    while (cut > 1 && font.MeasureText(line[..cut]) > width) cut--;
                    lines.Add(line[..cut]);
                    line = line[cut..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Hauteur d'un texte coupé à la largeur donnée.</summary>
    public static float TextHeight(string text, PdfStyle style, float width) => Wrap(text, style, width).Count * LineHeight(style);

    /// <summary>Dessine un texte coupé en lignes dans un cadre, à partir de son haut. Rend la hauteur occupée.</summary>
    public float DrawText(string text, PdfStyle style, float x, float y, float width, PdfAlign align = PdfAlign.Left)
    {
        var lines = Wrap(text, style, width);
        using var font = Font(style);
        using var paint = Paint(style.Color);
        var lh = LineHeight(style);
        var top = y;
        foreach (var line in lines)
        {
            var w = font.MeasureText(line);
            var lx = align switch { PdfAlign.Center => x + (width - w) / 2, PdfAlign.Right => x + width - w, _ => x };
            // Ligne de base : la hauteur du texte, moins la descente, centrée dans l'interligne.
            Canvas.DrawText(line, lx, top + (lh - style.Size) / 2 + style.Size * 0.93f, font, paint);
            top += lh;
        }
        return lines.Count * lh;
    }

    /// <summary>Paragraphe dans le flux, à la marge : il passe à la page suivante s'il ne tient plus.</summary>
    public void Paragraph(string text, PdfStyle style, PdfAlign align = PdfAlign.Left, float spaceAfter = 0)
    {
        var lines = Wrap(text, style, ContentWidth);
        var lh = LineHeight(style);
        foreach (var line in lines)
        {
            Ensure(lh);
            DrawText(line, style, Left, Y, ContentWidth, align);
            Y += lh;
        }
        Y += spaceAfter;
    }

    /// <summary>Texte fait de plusieurs morceaux de styles différents, coupés en lignes ensemble.</summary>
    public float DrawSpans(IReadOnlyList<(string Text, PdfStyle Style)> spans, float x, float y, float width)
    {
        var words = spans.SelectMany(s => s.Text.Split(' ').Select((w, i) => (Word: w, s.Style, Space: i > 0 || s.Text.StartsWith(' ')))).ToList();
        var top = y;
        var lineX = 0f;
        var lh = spans.Max(s => LineHeight(s.Style));
        foreach (var (word, style, _) in words)
        {
            if (word.Length == 0) continue;
            using var font = Font(style);
            var space = font.MeasureText(" ");
            var w = font.MeasureText(word);
            if (lineX > 0 && lineX + space + w > width)
            {
                top += lh;
                lineX = 0;
            }
            if (lineX > 0) lineX += space;
            using var paint = Paint(style.Color);
            Canvas.DrawText(word, x + lineX, top + (lh - style.Size) / 2 + style.Size * 0.93f, font, paint);
            lineX += w;
        }
        return top + lh - y;
    }

    // ---------- Formes et images ----------

    public void Fill(float x, float y, float w, float h, string color, float radius = 0)
    {
        using var paint = Paint(color);
        if (radius > 0) Canvas.DrawRoundRect(x, y, w, h, radius, radius, paint);
        else Canvas.DrawRect(x, y, w, h, paint);
    }

    public void Stroke(float x, float y, float w, float h, string color, float thickness, float radius = 0)
    {
        using var paint = Paint(color);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thickness;
        if (radius > 0) Canvas.DrawRoundRect(x, y, w, h, radius, radius, paint);
        else Canvas.DrawRect(x, y, w, h, paint);
    }

    public void Line(float x1, float y1, float x2, float y2, string color, float thickness)
    {
        using var paint = Paint(color);
        paint.StrokeWidth = thickness;
        Canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    /// <summary>
    /// Image ajustée dans un cadre, sans déformation, centrée. Un QR est agrandi pixel par pixel (<paramref name="sharp"/>) :
    /// lissé, il se brouille et se lit mal.
    /// </summary>
    public void Image(byte[] data, float x, float y, float w, float h, bool sharp = false)
    {
        using var image = SKImage.FromEncodedData(data);
        if (image is null) return;
        var scale = Math.Min(w / image.Width, h / image.Height);
        var iw = image.Width * scale;
        var ih = image.Height * scale;
        var sampling = sharp ? new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        Canvas.DrawImage(image, SKRect.Create(x + (w - iw) / 2, y + (h - ih) / 2, iw, ih), sampling);
    }

    // ---------- Tableau ----------

    /// <summary>
    /// Tableau à largeurs relatives : l'en-tête se répète en haut de chaque page, les lignes alternent blanc et bleu très pâle,
    /// une ligne qui ne tient plus passe entière à la page suivante.
    /// </summary>
    public void Table(IReadOnlyList<PdfColumn> columns, IReadOnlyList<PdfCell> head, IEnumerable<IReadOnlyList<PdfCell>> rows, PdfStyle body,
        PdfTableLook look, IReadOnlyList<PdfCell>? totals = null)
    {
        var total = columns.Sum(c => c.Width);
        var widths = columns.Select(c => c.Width / total * ContentWidth).ToArray();
        float RowHeight(IReadOnlyList<PdfCell> cells, PdfStyle style)
        {
            var max = 0f;
            var c = 0;
            foreach (var cell in cells)
            {
                var w = widths.Skip(c).Take(cell.Span).Sum() - 2 * look.Padding - (IsRight(c, cell) ? look.RightInset : 0);
                max = Math.Max(max, TextHeight(cell.Text, cell.Style ?? style, Math.Max(w, 4)));
                c += cell.Span;
            }
            return max + 2 * look.Padding;
        }
        bool IsRight(int c, PdfCell cell) => (cell.Align ?? columns[c].Align) == PdfAlign.Right;
        void DrawRow(IReadOnlyList<PdfCell> cells, PdfStyle style, float height, string? background, string? topRule, string? bottomRule)
        {
            if (background is not null) Fill(Left, Y, ContentWidth, height, background);
            if (topRule is not null) Line(Left, Y, Right, Y, topRule, 1);
            var x = Left;
            var c = 0;
            foreach (var cell in cells)
            {
                var w = widths.Skip(c).Take(cell.Span).Sum();
                var align = cell.Align ?? columns[c].Align;
                var inset = align == PdfAlign.Right ? look.RightInset : 0;
                DrawText(cell.Text, cell.Style ?? style, x + look.Padding, Y + look.Padding, w - 2 * look.Padding - inset, align);
                x += w;
                c += cell.Span;
            }
            if (bottomRule is not null) Line(Left, Y + height, Right, Y + height, bottomRule, 1);
            Y += height;
        }
        void Head()
        {
            var h = RowHeight(head, look.HeadStyle);
            DrawRow(head, look.HeadStyle, h, look.HeadBackground, null, look.HeadRule);
        }

        Ensure(RowHeight(head, look.HeadStyle) * 2);
        Head();
        var i = 0;
        foreach (var row in rows)
        {
            var h = RowHeight(row, body);
            if (Ensure(h)) Head();
            DrawRow(row, body, h, look.Striped ? (i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD") : null, null, null);
        }
        if (totals is not null)
        {
            var style = body.With(bold: true);
            var h = RowHeight(totals, style);
            if (Ensure(h)) Head();
            DrawRow(totals, style, h, null, look.TotalRule, null);
        }
    }
}

/// <summary>Apparence d'un tableau : en-tête plein ou souligné, lignes alternées, marges des cellules.</summary>
public sealed record PdfTableLook(PdfStyle HeadStyle, string? HeadBackground, string? HeadRule, bool Striped, float Padding, string TotalRule, float RightInset = 0);
