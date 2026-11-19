using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Apparition en cascade, façon Flutter : quand une page s'affiche, ses cartes montent de quelques pixels en
/// fondu, l'une après l'autre (25 ms d'écart, 150 ms au plus, 200 ms chacune : la page reste prête à l'emploi en moins de 0,4 s). Seulement si l'utilisateur garde les animations
/// (classe « anim » posée par la fenêtre) ; jamais pendant le défilement d'une liste déjà affichée.
/// </summary>
public static class Reveal
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Reveal));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);
    private static readonly TransformOperations Lowered = TransformOperations.Parse("translateY(8px)");

    // Cartes arrivées dans la même « vague » (même affichage de page) : leur rang fixe le décalage.
    private static int wave;
    private static DateTime lastArrival;

    static Reveal()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is true) control.AttachedToVisualTree += OnAttached;
            else control.AttachedToVisualTree -= OnAttached;
        });
    }

    /// <summary>Vrai si la fenêtre autorise les animations (préférence « Animations »).</summary>
    internal static bool MotionAllowed(Visual visual) =>
        visual.GetVisualAncestors().OfType<LayoutTransformControl>().Any(c => c.Classes.Contains("anim"));

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || !MotionAllowed(control)) return;
        // Une carte recyclée par une liste virtualisée ne rejoue pas l'animation pendant le défilement.
        if (control.GetVisualAncestors().Any(a => a is DataGrid or ListBox)) return;

        var now = DateTime.UtcNow;
        wave = now - lastArrival < TimeSpan.FromMilliseconds(350) ? wave + 1 : 0;
        lastArrival = now;
        var delay = TimeSpan.FromTicks(Math.Min(Step.Ticks * wave, MaxDelay.Ticks));

        control.Opacity = 0;
        control.RenderTransform = Lowered;
        Dispatcher.UIThread.Post(() =>
        {
            var easing = new CubicEaseOut();
            control.Transitions =
            [
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = Duration, Delay = delay, Easing = easing },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Duration, Delay = delay, Easing = easing }
            ];
            control.Opacity = 1;
            control.RenderTransform = TransformOperations.Identity;
            // Fin de l'apparition : on rend la main aux styles (survol, appui) sans valeur locale résiduelle.
            _ = Task.Delay(delay + Duration + TimeSpan.FromMilliseconds(60)).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() =>
                {
                    control.ClearValue(Animatable.TransitionsProperty);
                    control.ClearValue(Visual.OpacityProperty);
                    control.ClearValue(Visual.RenderTransformProperty);
                }), TaskScheduler.Default);
        }, DispatcherPriority.Render);
    }
}
