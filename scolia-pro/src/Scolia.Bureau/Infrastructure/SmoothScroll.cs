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
        public double LastApplied;
        public int Generation;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> States = new();
    private static bool installed;

    /// <summary>Installe le défilement amorti sur toutes les zones défilantes de l'application (une fois).</summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnWheel, RoutingStrategies.Tunnel);
        // Le clavier, la barre de défilement et le pavé tactile restent prioritaires.
        InputElement.PointerPressedEvent.AddClassHandler<ScrollViewer>((viewer, _) => Stop(viewer), RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<ScrollViewer>((viewer, _) => Stop(viewer), RoutingStrategies.Tunnel);
    }

    /// <summary>Un cran de souris (pas entier) plutôt qu'un pavé tactile (pas fractionnaires).</summary>
    public static bool IsWheelNotch(Vector delta) =>
        Math.Abs(delta.X) < 0.001 && Math.Abs(delta.Y) >= 0.999 && Math.Abs(delta.Y - Math.Round(delta.Y)) < 0.001;

    private static void OnWheel(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        if (e.Handled || Platform.IsMobile || PhoneLayout.Active || e.KeyModifiers != KeyModifiers.None)
        {
            Stop(viewer);
            return;
        }
        if (!IsWheelNotch(e.Delta) || !Reveal.MotionAllowed(viewer))
        {
            Stop(viewer);
            return;
        }
        // Seule la zone défilante la plus proche du pointeur réagit ; un tableau garde son propre défilement.
        if (e.Source is not Visual source) return;
        ScrollViewer? nearest = null;
        foreach (var visual in source.GetSelfAndVisualAncestors())
        {
            if (visual is DataGrid or TextBox or ComboBox) return;
            if (visual is ScrollViewer candidate && CanScroll(candidate, e.Delta.Y)) { nearest = candidate; break; }
            if (visual == viewer) break;
        }
        if (nearest != viewer) { Stop(viewer); return; }

        var state = States.GetOrCreateValue(viewer);
        var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        // Une nouvelle direction annule la destination précédente, sans inertie opposée au doigt.
        if (state.Running && (Math.Abs(viewer.Offset.Y - state.LastApplied) > 0.5 ||
            (state.Target - viewer.Offset.Y) * e.Delta.Y > 0))
            Stop(viewer);
        var from = state.Running ? state.Target : viewer.Offset.Y;
        state.Target = Math.Clamp(from - e.Delta.Y * Step, 0, max);
        e.Handled = true;
        if (state.Running) return;
        state.Running = true;
        state.LastApplied = viewer.Offset.Y;
        var generation = ++state.Generation;
        state.Last = System.Diagnostics.Stopwatch.GetTimestamp();
        var top = TopLevel.GetTopLevel(viewer);
        if (top is null) { Jump(viewer, state); return; }
        top.RequestAnimationFrame(_ => Tick(viewer, state, top, generation));
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

    private static void Stop(ScrollViewer viewer)
    {
        if (!States.TryGetValue(viewer, out var state) || !state.Running) return;
        state.Running = false;
        state.Generation++;
    }

    private static void Tick(ScrollViewer viewer, State state, TopLevel top, int generation)
    {
        // Un ancien callback ne peut plus déplacer une page masquée ou une liste
        // repositionnée par une recherche, une actualisation ou une navigation.
        if (!state.Running || state.Generation != generation) return;
        if (!viewer.IsEffectivelyVisible || TopLevel.GetTopLevel(viewer) != top ||
            !Reveal.MotionAllowed(viewer) || Math.Abs(viewer.Offset.Y - state.LastApplied) > 0.5)
        {
            Stop(viewer);
            return;
        }
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var dt = Math.Clamp((now - state.Last) / (double)System.Diagnostics.Stopwatch.Frequency, 0, 0.05);
        state.Last = now;
        var max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        state.Target = Math.Clamp(state.Target, 0, max);
        var y = viewer.Offset.Y;
        var next = state.Target - (state.Target - y) * Math.Exp(-dt / Tau);
        if (Math.Abs(state.Target - next) < 0.5)
        {
            Jump(viewer, state);
            return;
        }
        viewer.Offset = new Vector(viewer.Offset.X, next);
        state.LastApplied = viewer.Offset.Y;
        top.RequestAnimationFrame(_ => Tick(viewer, state, top, generation));
    }
}
