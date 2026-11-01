using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Scolia.Bureau.Controls;

/// <summary>
/// Logo SCOLIA en quatre pièces (trait bleu, trait vert, point bleu, point vert) qui arrivent des coins en tournant et
/// s'emboîtent, puis le logo descend à sa place. Même animation que l'ouverture d'Android. Sans animations, le logo
/// est dessiné complet d'emblée.
/// </summary>
public sealed class LogoAssembly : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<LogoAssembly, double>(nameof(Progress), 1d);

    public static readonly StyledProperty<IBrush?> PrimaryProperty =
        AvaloniaProperty.Register<LogoAssembly, IBrush?>(nameof(Primary));

    public static readonly StyledProperty<IBrush?> SecondaryProperty =
        AvaloniaProperty.Register<LogoAssembly, IBrush?>(nameof(Secondary));

    /// <summary>Durée de l'assemblage, puis de la descente.</summary>
    public static readonly TimeSpan AssemblyTime = TimeSpan.FromMilliseconds(1100);
    public static readonly TimeSpan DescentTime = TimeSpan.FromMilliseconds(380);

    private const double Grid = 512;
    private static readonly Geometry Upper = Geometry.Parse("M330,118 H206 Q148,118 148,176 Q148,234 206,234 H226");
    private static readonly Geometry Lower = Geometry.Parse("M286,278 H306 Q364,278 364,336 Q364,394 306,394 H182");
    private DispatcherTimer? timer;

    static LogoAssembly()
    {
        AffectsRender<LogoAssembly>(ProgressProperty, PrimaryProperty, SecondaryProperty);
    }

    /// <summary>0 : pièces dispersées ; 1 : assemblées ; jusqu'à 1,35 : le logo descend à sa place.</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public IBrush? Primary { get => GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }
    public IBrush? Secondary { get => GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }

    /// <summary>Fin de l'assemblage : la page peut faire apparaître la suite (nom, message, trois points).</summary>
    public event EventHandler? Assembled;

    /// <summary>Joue l'ouverture ; sans animation, le logo est complet tout de suite.</summary>
    public void Play(bool animate)
    {
        timer?.Stop();
        if (!animate)
        {
            Progress = 1.35;
            Assembled?.Invoke(this, EventArgs.Empty);
            return;
        }
        var total = AssemblyTime + DescentTime;
        var watch = Stopwatch.StartNew();
        var assembled = false;
        Progress = 0;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var elapsed = watch.Elapsed;
            Progress = elapsed < AssemblyTime
                ? elapsed / AssemblyTime
                : 1 + 0.35 * Math.Min(1, (elapsed - AssemblyTime) / DescentTime);
            if (!assembled && elapsed >= AssemblyTime)
            {
                assembled = true;
                Assembled?.Invoke(this, EventArgs.Empty);
            }
            if (elapsed >= total) timer?.Stop();
        });
        timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var primary = Primary ?? Brushes.RoyalBlue;
        var secondary = Secondary ?? Brushes.SeaGreen;
        var assembly = Math.Min(1, Progress);
        // Descente : le logo arrive un peu plus haut et plus grand, puis se pose à sa place.
        var descent = Ease(Math.Clamp((Progress - 1) / 0.35, 0, 1), 0, 1);
        var lift = (1 - descent) * size * 0.35;
        var grow = 1 + 0.3 * (1 - descent);

        var scale = size / Grid;
        var origin = Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2 - lift);
        var whole = Matrix.CreateTranslation(-Grid / 2, -Grid / 2)
            * Matrix.CreateRotation(-Math.PI / 2 * (1 - Ease(assembly, 0, 0.85)))
            * Matrix.CreateScale(grow, grow)
            * Matrix.CreateTranslation(Grid / 2, Grid / 2)
            * Matrix.CreateScale(scale, scale)
            * origin;
        using var _ = context.PushTransform(whole);

        var upperPen = new Pen(primary, 62, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var lowerPen = new Pen(secondary, 62, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        Piece(context, assembly, 0.00, -150, -130, -140, 237, 176, c => c.DrawGeometry(null, upperPen, Upper));
        Piece(context, assembly, 0.10, 150, 130, 140, 273, 336, c => c.DrawGeometry(null, lowerPen, Lower));
        Piece(context, assembly, 0.22, 170, -150, 0, 418, 118, c => c.DrawEllipse(primary, null, new Point(418, 118), 38, 38));
        Piece(context, assembly, 0.30, -170, 150, 0, 94, 394, c => c.DrawEllipse(secondary, null, new Point(94, 394), 38, 38));
    }

    private static void Piece(DrawingContext context, double progress, double delay, double dx, double dy, double spin,
        double cx, double cy, Action<DrawingContext> draw)
    {
        var t = Ease(progress, delay, delay + 0.62);
        if (t <= 0) return;
        var settle = Overshoot(t);
        var size = 0.55 + 0.45 * settle;
        var transform = Matrix.CreateTranslation(-cx, -cy)
            * Matrix.CreateScale(size, size)
            * Matrix.CreateRotation(spin * Math.PI / 180 * (1 - settle))
            * Matrix.CreateTranslation(cx + dx * (1 - settle), cy + dy * (1 - settle));
        using var moved = context.PushTransform(transform);
        using var faded = context.PushOpacity(Math.Min(1, t * 1.8));
        draw(context);
    }

    /// <summary>Avancement entre deux instants, adouci en fin de course (même courbe qu'Android).</summary>
    public static double Ease(double value, double start, double end)
    {
        if (value <= start) return 0;
        if (value >= end) return 1;
        var x = (value - start) / (end - start);
        return 1 - Math.Pow(1 - x, 3);
    }

    /// <summary>Petit dépassement puis retour, comme une pièce qui s'emboîte.</summary>
    public static double Overshoot(double x)
    {
        const double tension = 1.6;
        var y = x - 1;
        return y * y * ((tension + 1) * y + tension) + 1;
    }
}
