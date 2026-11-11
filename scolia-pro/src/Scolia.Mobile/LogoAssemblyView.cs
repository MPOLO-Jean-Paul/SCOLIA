using Android.Content;
using Android.Graphics;
using AndroidView = Android.Views.View;
using Path = Android.Graphics.Path;

namespace Scolia.Mobile;

/// <summary>
/// Logo SCOLIA dessiné en quatre pièces (trait bleu, trait vert, point bleu, point vert) : au lancement, les pièces
/// arrivent des coins en tournant et s'emboîtent, comme un logo décomposé qui se recompose. <see cref="Progress"/>
/// va de 0 (pièces dispersées) à 1 (logo complet) ; sans animation, il vaut 1 d'emblée.
/// </summary>
public sealed class LogoAssemblyView : AndroidView
{
    // Même tracé que le logo d'Avalonia (Palette.axaml, ScoliaMark), sur une grille de 512 points.
    private const float Grid = 512f;
    private readonly Paint blueStroke;
    private readonly Paint greenStroke;
    private readonly Paint blueFill;
    private readonly Paint greenFill;
    private readonly Path upper = new();
    private readonly Path lower = new();
    private float progress = 1f;

    public LogoAssemblyView(Context context, bool dark) : base(context)
    {
        var blue = dark ? Color.Rgb(91, 141, 239) : Color.Rgb(11, 42, 111);
        var green = dark ? Color.Rgb(34, 197, 94) : Color.Rgb(22, 163, 74);
        blueStroke = Stroke(blue);
        greenStroke = Stroke(green);
        blueFill = new Paint(PaintFlags.AntiAlias) { Color = blue };
        greenFill = new Paint(PaintFlags.AntiAlias) { Color = green };
        upper.MoveTo(330, 118); upper.LineTo(206, 118); upper.QuadTo(148, 118, 148, 176); upper.QuadTo(148, 234, 206, 234); upper.LineTo(226, 234);
        lower.MoveTo(286, 278); lower.LineTo(306, 278); lower.QuadTo(364, 278, 364, 336); lower.QuadTo(364, 394, 306, 394); lower.LineTo(182, 394);
        ImportantForAccessibility = Android.Views.ImportantForAccessibility.No;
    }

    private static Paint Stroke(Color color)
    {
        var paint = new Paint(PaintFlags.AntiAlias) { Color = color, StrokeWidth = 62 };
        paint.SetStyle(Paint.Style.Stroke);
        paint.StrokeCap = Paint.Cap.Round;
        paint.StrokeJoin = Paint.Join.Round;
        return paint;
    }

    /// <summary>0 : pièces dispersées et invisibles ; 1 : logo assemblé.</summary>
    public float Progress
    {
        get => progress;
        set { progress = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        var size = Math.Min(Width, Height);
        if (size <= 0) return;
        canvas.Save();
        canvas.Translate((Width - size) / 2f, (Height - size) / 2f);
        canvas.Scale(size / Grid, size / Grid);
        // Tout le logo fait un dernier quart de tour en s'assemblant.
        canvas.Rotate(-90f * (1 - Ease(progress, 0f, 0.85f)), Grid / 2, Grid / 2);

        // Chaque pièce part à son tour (décalage), vient de son coin en tournant et se pose avec un léger rebond.
        Piece(canvas, 0.00f, -150, -130, -140, 237, 176, c => c.DrawPath(upper, blueStroke));
        Piece(canvas, 0.10f, 150, 130, 140, 273, 336, c => c.DrawPath(lower, greenStroke));
        Piece(canvas, 0.22f, 170, -150, 0, 418, 118, c => c.DrawCircle(418, 118, 38, blueFill));
        Piece(canvas, 0.30f, -170, 150, 0, 94, 394, c => c.DrawCircle(94, 394, 38, greenFill));
        canvas.Restore();
    }

    private void Piece(Canvas canvas, float delay, float dx, float dy, float spin, float cx, float cy, Action<Canvas> draw)
    {
        var t = Ease(progress, delay, delay + 0.62f);
        if (t <= 0f) return;
        var settle = Overshoot(t);
        canvas.Save();
        canvas.Translate(dx * (1 - settle), dy * (1 - settle));
        canvas.Rotate(spin * (1 - settle), cx, cy);
        var scale = 0.55f + 0.45f * settle;
        canvas.Scale(scale, scale, cx, cy);
        var alpha = (int)(255 * Math.Min(1f, t * 1.8f));
        canvas.SaveLayerAlpha(0, 0, Grid, Grid, alpha);
        draw(canvas);
        canvas.Restore();
        canvas.Restore();
    }

    /// <summary>Avancement d'une pièce entre deux instants, adouci en fin de course.</summary>
    private static float Ease(float value, float start, float end)
    {
        if (value <= start) return 0f;
        if (value >= end) return 1f;
        var x = (value - start) / (end - start);
        return 1 - (1 - x) * (1 - x) * (1 - x);
    }

    /// <summary>Petit dépassement puis retour, comme une pièce qui s'emboîte.</summary>
    private static float Overshoot(float x)
    {
        const float tension = 1.6f;
        var y = x - 1;
        return y * y * ((tension + 1) * y + tension) + 1;
    }
}
