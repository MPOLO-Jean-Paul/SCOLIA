using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Passage d'une page à l'autre comme sur Android 16 (« axe partagé ») : l'ancienne page s'efface en glissant à peine,
/// la nouvelle arrive de quelques points sur le côté en apparaissant. Au retour, le mouvement va dans l'autre sens.
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

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;
        var shift = forward ? Distance : -Distance;
        var tasks = new List<Task>(2);
        if (from is not null)
            tasks.Add(Run(from, 1, 0, 0, -shift * 0.6, TimeSpan.FromMilliseconds(Duration.TotalMilliseconds * 0.45), new CubicEaseIn(), cancellationToken));
        if (to is not null)
        {
            to.IsVisible = true;
            tasks.Add(Run(to, 0, 1, shift, 0, Duration, new CubicEaseOut(), cancellationToken));
        }
        await Task.WhenAll(tasks);
        if (from is not null && !cancellationToken.IsCancellationRequested) from.IsVisible = false;
    }

    private static Task Run(Visual target, double fromOpacity, double toOpacity, double fromX, double toX,
        TimeSpan duration, Easing easing, CancellationToken token)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, fromOpacity), new Setter(TranslateTransform.XProperty, fromX) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, toOpacity), new Setter(TranslateTransform.XProperty, toX) },
                },
            },
        };
        return animation.RunAsync(target, token);
    }
}
