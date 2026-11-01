using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Passage d'une page à l'autre comme sur Android 16 (« axe partagé ») : l'ancienne page s'efface en glissant à peine,
/// puis la nouvelle arrive de quelques points sur le côté en apparaissant. Au retour, le mouvement va dans l'autre sens.
/// Bien plus léger qu'un glissement de toute la largeur : l'œil suit le contenu sans être emporté.
/// </summary>
public sealed class SharedAxisTransition : IPageTransition
{
    public SharedAxisTransition(TimeSpan duration, double distance)
    {
        Duration = duration;
        Distance = distance;
    }

    public TimeSpan Duration { get; }
    public double Distance { get; }

    /// <summary>Part de la durée où l'ancienne page s'efface ; la nouvelle n'apparaît qu'ensuite, jamais en même temps.</summary>
    public const double FadeOutShare = 0.35;

    /// <summary>Courbe « standard » de Material 3 : départ franc, arrivée très douce.</summary>
    private static readonly Easing Standard = new SplineEasing(0.2, 0, 0, 1);

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        var shift = forward ? Distance : -Distance;
        var tasks = new List<Task>(4);
        if (from is not null)
        {
            tasks.Add(Fade(from, [(0, 1), (FadeOutShare, 0), (1, 0)], cancellationToken));
            tasks.Add(Slide(from, 0, -shift, cancellationToken));
        }
        if (to is not null)
        {
            to.IsVisible = true;
            // Les deux pages ne sont jamais à moitié visibles ensemble : pas de voile ni de scintillement.
            tasks.Add(Fade(to, [(0, 0), (FadeOutShare, 0), (1, 1)], cancellationToken));
            tasks.Add(Slide(to, shift, 0, cancellationToken));
        }
        await Task.WhenAll(tasks);
        if (from is not null && !cancellationToken.IsCancellationRequested) from.IsVisible = false;
    }

    private Task Fade(Visual target, (double Cue, double Opacity)[] steps, CancellationToken token)
    {
        var animation = new Animation { Duration = Duration, Easing = new LinearEasing(), FillMode = FillMode.Forward };
        foreach (var (cue, opacity) in steps)
            animation.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(Visual.OpacityProperty, opacity) } });
        return animation.RunAsync(target, token);
    }

    private Task Slide(Visual target, double fromX, double toX, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = Duration,
            Easing = Standard,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.XProperty, fromX) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.XProperty, toX) } },
            },
        };
        return animation.RunAsync(target, token);
    }
}
