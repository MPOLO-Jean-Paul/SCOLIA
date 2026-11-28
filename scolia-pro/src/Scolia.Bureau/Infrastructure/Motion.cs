using Avalonia.Animation.Easings;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Rythme du mouvement, à la manière de macOS : départ franc, arrivée longue et douce, jamais de rebond visible.
/// <para>
/// <see cref="Spring"/> reproduit un ressort très amorti (comme les animations « smooth » d'Apple) : l'élément
/// atteint presque sa place au premier tiers de la durée puis s'y pose. <see cref="Smooth"/> est la courbe des
/// survols et des changements de couleur, plus courte.
/// </para>
/// </summary>
public static class Motion
{
    /// <summary>Ressort amorti des déplacements, zooms et apparitions.</summary>
    public static Easing Spring { get; } = new SpringEasing();

    /// <summary>Courbe de décélération douce des couleurs et opacités (ease-out d'AppKit).</summary>
    public static Easing Smooth { get; } = new SplineEasing(0.25, 0.1, 0.25, 1);

    /// <summary>Durée d'un survol ou d'un appui.</summary>
    public static TimeSpan Hover { get; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Durée d'une apparition (feuille, dialogue, page).</summary>
    public static TimeSpan Present { get; } = TimeSpan.FromMilliseconds(280);
}

/// <summary>
/// Ressort amorti normalisé sur [0, 1] : amortissement 0,86 (dépassement inférieur à 0,5 %, imperceptible),
/// raideur choisie pour que le mouvement soit posé à la fin de la durée.
/// </summary>
public sealed class SpringEasing : Easing
{
    private const double Damping = 0.86;
    private const double Omega = 9.0;
    private static readonly double End = Raw(1);

    public override double Ease(double progress)
    {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        return Raw(progress) / End;
    }

    private static double Raw(double t)
    {
        var damped = Omega * Math.Sqrt(1 - Damping * Damping);
        var decay = Math.Exp(-Damping * Omega * t);
        return 1 - decay * (Math.Cos(damped * t) + Damping * Omega / damped * Math.Sin(damped * t));
    }
}
