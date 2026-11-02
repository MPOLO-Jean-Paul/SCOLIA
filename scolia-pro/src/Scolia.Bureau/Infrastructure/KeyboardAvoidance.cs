using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Clavier du téléphone : l'écran de SCOLIA s'arrête là où le clavier commence, et le champ en cours de saisie
/// défile juste au-dessus de lui. Depuis Android 15, l'application occupe tout l'écran et Android ne réduit plus
/// la fenêtre quand le clavier s'ouvre : SCOLIA réserve donc lui-même la hauteur du clavier en bas de l'écran.
/// Sur un téléphone plus ancien qui réduit déjà la fenêtre, la réduction faite par Android est déduite.
/// </summary>
/// <summary>Le clavier vu par SCOLIA : ouvert ou non, et la zone de l'écran qu'il couvre.</summary>
public interface IKeyboardArea
{
    bool IsOpen { get; }
    Rect Area { get; }
    /// <summary>Durée de l'animation d'ouverture ou de fermeture en cours.</summary>
    event Action<TimeSpan>? Changed;
}

/// <summary>Clavier du système, fourni par Avalonia.</summary>
internal sealed class SystemKeyboard : IKeyboardArea
{
    private readonly IInputPane pane;
    public SystemKeyboard(IInputPane pane)
    {
        this.pane = pane;
        pane.StateChanged += (_, e) => Changed?.Invoke(e.AnimationDuration);
    }
    public bool IsOpen => pane.State == InputPaneState.Open;
    public Rect Area => pane.OccludedRect;
    public event Action<TimeSpan>? Changed;
}

public sealed class KeyboardAvoidance
{
    /// <summary>Espace laissé entre le champ et le clavier, pour voir aussi le début du champ suivant.</summary>
    public const double Gap = 16;

    /// <summary>Clavier de remplacement pour les vérifications sans écran ; null sur un vrai téléphone.</summary>
    public static IKeyboardArea? PaneOverride { get; set; }

    private readonly TemplatedControl root;
    private readonly Control content;
    private readonly Thickness baseMargin;
    private TopLevel? top;
    private IKeyboardArea? pane;
    private readonly Dictionary<IInputPane, SystemKeyboard> adapters = [];
    private double closedHeight;
    private double applied;

    /// <param name="root">L'écran entier : sa marge intérieure appartient au système (encoche, barres d'Android).</param>
    /// <param name="content">Le contenu, qui s'arrête au-dessus du clavier par sa marge du bas.</param>
    public KeyboardAvoidance(TemplatedControl root, Control content)
    {
        this.root = root;
        this.content = content;
        baseMargin = content.Margin;
        root.AttachedToVisualTree += (_, _) => Attach();
        root.DetachedFromVisualTree += (_, _) => Detach();
        root.SizeChanged += (_, e) =>
        {
            if (!IsOpen) closedHeight = e.NewSize.Height;
            else Update();
        };
        // Passer d'un champ à l'autre, clavier ouvert : le nouveau champ vient aussi au-dessus du clavier.
        root.AddHandler(InputElement.GotFocusEvent, (_, _) => { if (applied > 0) RevealSoon(); }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Hauteur réservée en bas de l'écran pour le clavier, 0 quand il est fermé.</summary>
    public double Reserved => applied;

    /// <summary>Vrai tant que le clavier couvre une partie de l'écran.</summary>
    public event Action<bool>? KeyboardChanged;

    private bool IsOpen => pane is { IsOpen: true, Area.Height: > 0 };

    /// <summary>
    /// Hauteur à réserver : la partie de l'écran sous le haut du clavier, moins ce qu'Android a déjà retiré
    /// en réduisant la fenêtre. Aucune valeur négative.
    /// </summary>
    public static double Overlap(double rootBottom, Rect keyboard, double alreadyShrunk) =>
        keyboard.Height <= 0 ? 0 : Math.Max(0, rootBottom - keyboard.Top - Math.Max(0, alreadyShrunk));

    /// <summary>Reprend le clavier du système, ou celui des vérifications sans écran.</summary>
    public void Attach()
    {
        Detach();
        top = TopLevel.GetTopLevel(root);
        // Le bureau garde son comportement : seul le téléphone réserve la place du clavier.
        pane = PaneOverride ?? (Platform.IsMobile && top?.InputPane is { } system ? Adapter(system) : null);
        if (pane is null) return;
        if (!IsOpen) closedHeight = root.Bounds.Height;
        pane.Changed += OnPaneChanged;
        Update();
    }

    private void Detach()
    {
        if (pane is not null) pane.Changed -= OnPaneChanged;
        pane = null;
        top = null;
    }

    private SystemKeyboard Adapter(IInputPane system)
    {
        if (!adapters.TryGetValue(system, out var adapter)) adapters[system] = adapter = new SystemKeyboard(system);
        return adapter;
    }

    private void OnPaneChanged(TimeSpan animation)
    {
        Dispatcher.UIThread.Post(Update);
        // Android anime l'ouverture : une seconde mesure à la fin de l'animation donne la hauteur définitive.
        if (animation > TimeSpan.Zero)
            DispatcherTimer.RunOnce(Update, animation + TimeSpan.FromMilliseconds(30));
    }

    /// <summary>Recalcule la réserve ; appelé aussi par les vérifications sans écran.</summary>
    public void Update()
    {
        if (pane is null || top is null) return;
        var reserve = 0d;
        if (IsOpen)
        {
            // Bas du contenu, au-dessus de la zone sûre du système (barre de navigation, indicateur d'accueil).
            var inner = root.Bounds.Height - root.Padding.Bottom;
            var bottom = root.TranslatePoint(new Point(0, inner), top)?.Y ?? inner;
            var shrunk = closedHeight > 0 ? closedHeight - root.Bounds.Height : 0;
            reserve = Overlap(bottom, pane.Area, shrunk);
        }
        if (Math.Abs(reserve - applied) >= 1)
        {
            var wasOpen = applied > 0;
            applied = reserve;
            // La marge intérieure de l'écran reste celle du système (encoche, barre d'état) : seul le contenu se réduit.
            content.Margin = new Thickness(baseMargin.Left, baseMargin.Top, baseMargin.Right, baseMargin.Bottom + reserve);
            root.Classes.Set("keyboard", reserve > 0);
            if (wasOpen != reserve > 0) KeyboardChanged?.Invoke(reserve > 0);
        }
        if (reserve > 0) RevealSoon();
    }

    private void RevealSoon() => Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Background);

    /// <summary>Fait défiler le champ actif juste au-dessus du clavier, avec son libellé au-dessus de lui.</summary>
    public void Reveal()
    {
        if (top?.FocusManager?.GetFocusedElement() is not Control focused || !focused.IsEffectivelyVisible) return;
        // Le libellé du champ est souvent juste au-dessus : il reste visible lui aussi.
        var area = new Rect(focused.Bounds.Size).Inflate(new Thickness(0, 28, 0, Gap));
        // Chaque zone qui défile autour du champ (page, fenêtre) avance juste ce qu'il faut, pas davantage.
        focused.BringIntoView(area);
    }
}
