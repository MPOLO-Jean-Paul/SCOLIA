using Avalonia;
using Avalonia.Controls;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Préserve l'accès au dernier contenu quand le total ou le bouton fixe grandit.</summary>
public static class StickyLayout
{
    public static double BottomSpace(bool visible, double height, double normal, double minimum)
        => visible ? Math.Max(minimum, double.IsFinite(height) && height > 0 ? Math.Ceiling(height) + 12 : minimum) : normal;

    public static void Track(Border bar, Control host, Thickness normal, double minimum)
    {
        void Update()
        {
            var bottom = BottomSpace(bar.IsVisible, bar.Bounds.Height, normal.Bottom, minimum);
            var margin = new Thickness(normal.Left, normal.Top, normal.Right, bottom);
            if (host.Margin != margin) host.Margin = margin;
        }
        bar.SizeChanged += (_, _) => Update();
        bar.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty) Update();
        };
        bar.AttachedToVisualTree += (_, _) => Update();
    }
}
