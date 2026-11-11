using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Retour tactile du téléphone : léger au toucher, net pour une réussite, distinct pour un refus.</summary>
public enum HapticKind { Tap, Success, Error }

/// <summary>
/// Retour au toucher, comme sur Android : une onde part du doigt sur le bouton ou la ligne touchés, puis s'efface.
/// Les boutons principaux donnent aussi une légère vibration sur le téléphone. L'onde ne gêne jamais le clic :
/// elle est dessinée au-dessus, sans capter le pointeur, et disparaît seule.
/// </summary>
public static class TouchFeedback
{
    /// <summary>Durée de l'onde : assez longue pour être vue, assez courte pour ne jamais retenir l'œil.</summary>
    public static readonly TimeSpan RippleTime = TimeSpan.FromMilliseconds(420);

    /// <summary>Branche l'onde sur tout l'écran ; <paramref name="enabled"/> suit le réglage « Animations ».</summary>
    public static void Attach(Control root, Func<bool> enabled)
    {
        root.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.Source is not Visual source) return;
            if (Target(source) is not { } target || !target.IsEffectivelyEnabled) return;
            // La vibration suit les réglages du téléphone, pas ceux des animations ; l'onde suit les animations.
            if (target is Button { Classes: var classes } && (classes.Contains("primary") || classes.Contains("stickyaction")))
                Platform.Haptic?.Invoke(HapticKind.Tap);
            if (enabled()) Ripple(target, e.GetPosition(target));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>Le bouton, l'onglet ou la ligne de liste sous le doigt ; jamais un champ de saisie.</summary>
    private static Control? Target(Visual source)
    {
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is TextBox or ScrollBar or Slider) return null;
            if (visual is Control control && control.Classes.Contains("noripple")) return null;
            if (visual is Button or ListBoxItem or ToggleButton) return (Control)visual;
        }
        return null;
    }

    private static void Ripple(Control target, Point origin)
    {
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;
        var light = target.Classes.Contains("primary") || target.Classes.Contains("stickyaction") || target.Classes.Contains("danger");
        var color = light ? Color.FromArgb(70, 255, 255, 255)
            : target.TryFindResource("TextBrush", target.ActualThemeVariant, out var res) && res is ISolidColorBrush brush
                ? Color.FromArgb(34, brush.Color.R, brush.Color.G, brush.Color.B)
                : Color.FromArgb(30, 0, 0, 0);
        var radius = target is TemplatedControl templated ? templated.CornerRadius : default;
        var ripple = new RippleAdorner(origin, color, radius) { IsHitTestVisible = false };
        AdornerLayer.SetAdornedElement(ripple, target);
        AdornerLayer.SetIsClipEnabled(ripple, false);
        layer.Children.Add(ripple);
        ripple.Play(() => layer.Children.Remove(ripple));
    }

    private sealed class RippleAdorner(Point origin, Color color, CornerRadius corner) : Control
    {
        private readonly Stopwatch watch = new();
        private DispatcherTimer? timer;

        public void Play(Action done)
        {
            watch.Start();
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
            {
                InvalidateVisual();
                if (watch.Elapsed < RippleTime) return;
                timer?.Stop();
                done();
            });
            timer.Start();
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var t = Math.Clamp(watch.Elapsed / RippleTime, 0, 1);
            // L'onde grandit vite puis ralentit ; elle s'efface pendant la seconde moitié.
            var grow = 1 - Math.Pow(1 - t, 3);
            var far = new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight }
                .Max(p => Math.Sqrt(Math.Pow(p.X - origin.X, 2) + Math.Pow(p.Y - origin.Y, 2)));
            var alpha = t < 0.5 ? 1 : 1 - (t - 0.5) / 0.5;
            var brush = new SolidColorBrush(color, alpha);
            using var clip = context.PushClip(new RoundedRect(bounds, corner));
            context.DrawEllipse(brush, null, origin, far * grow, far * grow);
        }
    }
}
