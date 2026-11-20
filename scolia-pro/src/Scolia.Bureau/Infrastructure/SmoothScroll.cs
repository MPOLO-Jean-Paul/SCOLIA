using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Défilement à la molette amorti, comme sur macOS : chaque cran de molette fixe une destination et la page y
/// glisse en décélérant (constante de temps de 70 ms) au lieu de sauter de 50 points. Les crans successifs
/// s'additionnent sans à-coup.
/// <para>
/// Le pavé tactile (pas fractionnaires, déjà fluides), les touches de modification (Ctrl pour le zoom, Maj),
/// les tableaux (qui gèrent leur propre défilement), le téléphone et le réglage « Animations » désactivé
/// gardent le comportement natif.
/// </para>
/// </summary>
public static class SmoothScroll
{
    /// <summary>Distance d'un cran de molette.</summary>
    public const double Step = 64;

    /// <summary>Constante de temps de l'approche exponentielle.</summary>
    private const double Tau = 0.070;

    private sealed class State
    {
        public double Target;
        public bool Running;
        public long Last;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> States = new();
    private static bool installed;

    /// <summary>Installe le défilement amorti sur toutes les zones défilantes de l'application (une fois).</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnWheel, RoutingStrategies.Tunnel);
    }

    /// <summary>Un cran de souris (pas entier) plutôt qu'un pavé tactile (pas fractionnaires).</summary>
    public static bool IsWheelNotch(Vector delta) =>
        Math.Abs(delta.X) < 0.001 && Math.Abs(delta.Y) >= 0.999 && Math.Abs(delta.Y - Math.Round(delta.Y)) < 0.001;

    private static void OnWheel(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        if (e.Handled || Platform.IsMobile || PhoneLayout.Active || e.KeyModifiers != KeyModifiers.None) return;
        if (!IsWheelNotch(e.Delta) || !Reveal.MotionAllowed(viewer)) return;
        // Seule la zone défilante la plus proche du pointeur réagit ; un tableau garde son propre défilement.
        if (e.Source is not Visual source) return;
        ScrollViewer? nearest = null;
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is DataGrid or TextBox or ComboBox) return;
            if (visual is ScrollViewer candidate && CanScroll(candidate, e.Delta.Y)) { nearest = candidate; break; }
            if (visual == viewer) break;
        }
        if (nearest != viewer) return;

        var state = States.GetOrCreateValue(viewer);
        var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var from = state.Running ? state.Target : viewer.Offset.Y;
        state.Target = Math.Clamp(from - e.Delta.Y * Step, 0, max);
        e.Handled = true;
        if (state.Running) return;
        state.Running = true;
        state.Last = System.Diagnostics.Stopwatch.GetTimestamp();
        var top = TopLevel.GetTopLevel(viewer);
        if (top is null) { Jump(viewer, state); return; }
        top.RequestAnimationFrame(_ => Tick(viewer, state, top));
    }

    private static bool CanScroll(ScrollViewer viewer, double deltaY)
    {
        var max = viewer.Extent.Height - viewer.Viewport.Height;
        if (max <= 0.5) return false;
        return deltaY > 0 ? viewer.Offset.Y > 0.5 : viewer.Offset.Y < max - 0.5;
    }

    private static void Jump(ScrollViewer viewer, State state)
    {
        viewer.Offset = new Vector(viewer.Offset.X, state.Target);
        state.Running = false;
    }

    private static void Tick(ScrollViewer viewer, State state, TopLevel top)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = Math.Clamp((now - state.Last) / (double)System.Diagnostics.Stopwatch.Frequency, 0, 0.05);
        state.Last = now;
        var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        state.Target = Math.Clamp(state.Target, 0, max);
        var y = viewer.Offset.Y;
        var next = state.Target - (state.Target - y) * Math.Exp(-dt / Tau);
        if (Math.Abs(state.Target - next) < 0.5 || !viewer.IsEffectivelyVisible)
        {
            Jump(viewer, state);
            return;
        }
        viewer.Offset = new Vector(viewer.Offset.X, next);
        top.RequestAnimationFrame(_ => Tick(viewer, state, top));
    }
}
