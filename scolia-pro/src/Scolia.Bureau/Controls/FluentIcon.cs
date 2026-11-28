using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Controls;

/// <summary>Tracé Fluent à la taille optique adaptée, sans étirer les limites de la forme.</summary>
public sealed class FluentIcon : TemplatedControl
{
    public static readonly StyledProperty<MaterialIconKind> KindProperty =
        AvaloniaProperty.Register<FluentIcon, MaterialIconKind>(nameof(Kind));
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<FluentIcon, Geometry?>(nameof(Data));

    static FluentIcon() => AffectsRender<FluentIcon>(KindProperty, DataProperty, ForegroundProperty);

    public FluentIcon()
    {
        IsHitTestVisible = false;
        UseLayoutRounding = true;
    }

    public MaterialIconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(24, 24);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0 || Foreground is null) return;
        double canvas;
        var geometry = Data is { } data ? FluentGlyphs.ForSize(data, size, out canvas)
            : FluentGlyphs.ForSize(Kind, size, out canvas);
        var scale = size / canvas;
        var offset = new Vector((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset));
        context.DrawGeometry(Foreground, null, geometry);
    }
}
