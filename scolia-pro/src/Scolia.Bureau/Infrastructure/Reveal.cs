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

    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(12);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(48);
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);
    private static readonly TransformOperations Lowered = TransformOperations.Parse("translateY(3px)");

    // Cartes arrivées dans la même « vague » (même affichage de page) : leur rang fixe le décalage.
    private static int wave;
    private static DateTime lastArrival;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, CancellationTokenSource> Active = new();

    static Reveal()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is true)
            {
                control.AttachedToVisualTree += OnAttached;
                control.DetachedFromVisualTree += OnDetached;
            }
            else
            {
                control.AttachedToVisualTree -= OnAttached;
                control.DetachedFromVisualTree -= OnDetached;
                Reset(control);
            }
        });
    }

    /// <summary>Vrai si la fenêtre autorise les animations (préférence « Animations »).</summary>
    internal static bool MotionAllowed(Visual visual) =>
        visual.GetVisualAncestors().OfType<LayoutTransformControl>().Any(c => c.Classes.Contains("anim"));

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control || !MotionAllowed(control)) return;
        // Téléphone : le contenu s'affiche d'un coup. La cascade coûte un rendu transparent par carte
        // et retarde le premier toucher sur les Android modestes.
        if (Platform.IsMobile || PhoneLayout.Active) return;
        // Une carte recyclée par une liste virtualisée ne rejoue pas l'animation pendant le défilement.
        if (control is DataGrid or ListBox ||
            control.GetVisualAncestors().Any(a => a is DataGrid or ListBox || a is Control parent && GetEnabled(parent))) return;
        Reset(control);

        var now = DateTime.UtcNow;
        wave = now - lastArrival < TimeSpan.FromMilliseconds(350) ? wave + 1 : 0;
        lastArrival = now;
        var delay = TimeSpan.FromTicks(Math.Min(Step.Ticks * wave, MaxDelay.Ticks));

        var run = new CancellationTokenSource();
        Active.Add(control, run);
        control.Opacity = 0.78;
        control.RenderTransform = Lowered;
        Dispatcher.UIThread.Post(() =>
        {
            if (!Active.TryGetValue(control, out var current) || !ReferenceEquals(current, run)) return;
            if (!control.IsEffectivelyVisible || !MotionAllowed(control)) { Reset(control); return; }
            var easing = Motion.Spring;
            control.Transitions =
            [
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = Duration, Delay = delay, Easing = easing },
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Duration, Delay = delay, Easing = easing }
            ];
            control.Opacity = 1;
            control.RenderTransform = TransformOperations.Identity;
            // Fin de l'apparition : on rend la main aux styles (survol, appui) sans valeur locale résiduelle.
            _ = FinishAsync(control, run, delay + Duration + TimeSpan.FromMilliseconds(32));
        }, DispatcherPriority.Render);
    }

    private static async Task FinishAsync(Control control, CancellationTokenSource run, TimeSpan duration)
    {
        try { await Task.Delay(duration, run.Token); }
        catch (OperationCanceledException) { return; }
        Dispatcher.UIThread.Post(() =>
        {
            if (Active.TryGetValue(control, out var current) && ReferenceEquals(current, run)) Reset(control);
        });
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control) Reset(control);
    }

    private static void Reset(Control control)
    {
        if (!Active.TryGetValue(control, out var run)) return;
        Active.Remove(control);
        run.Cancel();
        run.Dispose();
        control.ClearValue(Animatable.TransitionsProperty);
        control.ClearValue(Visual.OpacityProperty);
        control.ClearValue(Visual.RenderTransformProperty);
    }
}
