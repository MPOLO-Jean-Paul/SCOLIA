using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Contraste élevé des tuiles d'icônes : leur fond conserve la teinte du mode clair
/// même si une ressource dynamique lui transmet la variante sombre. Les pictogrammes
/// restent blancs sur ces couleurs foncées. Les fonds personnalisés ne sont pas affectés.
/// </summary>
public static class TileGlyph
{
    public const double Minimum = 4.5;
    public static readonly Color Dark = Color.Parse("#0B0B0F");

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Border, bool>("Enabled", typeof(TileGlyph));

    public static bool GetEnabled(Border border) => border.GetValue(EnabledProperty);
    public static void SetEnabled(Border border, bool value) => border.SetValue(EnabledProperty, value);

    private static readonly AttachedProperty<Watcher?> WatcherProperty =
        AvaloniaProperty.RegisterAttached<Border, Watcher?>("Watcher", typeof(TileGlyph));

    static TileGlyph()
    {
        EnabledProperty.Changed.AddClassHandler<Border>((border, e) =>
        {
            border.GetValue(WatcherProperty)?.Dispose();
            border.SetValue(WatcherProperty, e.NewValue is true ? new Watcher(border) : null);
        });
    }

    /// <summary>Couleur du pictogramme pour une teinte de tuile donnée.</summary>
    public static Color GlyphFor(Color tile)
    {
        var white = ThemeContrast.Ratio(Colors.White, tile);
        if (white >= Minimum) return Colors.White;
        return ThemeContrast.Ratio(Dark, tile) > white ? Dark : Colors.White;
    }

    private sealed class Watcher : IDisposable
    {
        private readonly Border border;
        private IBrush? watched;
        private bool listening;
        private readonly IDisposable backgroundSubscription;
        private readonly IDisposable childSubscription;

        public Watcher(Border border)
        {
            this.border = border;
            // Les pinceaux des thèmes vivent toute la session : ne les écouter que pendant l'affichage de la tuile,
            // pour ne jamais retenir en mémoire les pages fermées.
            border.AttachedToVisualTree += OnAttached;
            border.DetachedFromVisualTree += OnDetached;
            backgroundSubscription = border.GetObservable(Border.BackgroundProperty).Subscribe(new Observer<IBrush?>(Watch));
            childSubscription = border.GetObservable(Decorator.ChildProperty).Subscribe(new Observer<Control?>(_ => Apply()));
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) { Listen(true); Apply(); }
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Listen(false);

        private void Listen(bool on)
        {
            var visible = on && border.IsAttachedToVisualTree();
            if (visible == listening) return;
            if (watched is AvaloniaObject brush)
            {
                if (visible) brush.PropertyChanged += OnBrushChanged;
                else brush.PropertyChanged -= OnBrushChanged;
            }
            listening = visible;
        }

        private void Watch(IBrush? brush)
        {
            var wasListening = listening;
            Listen(false);
            watched = brush;
            if (wasListening || border.IsAttachedToVisualTree()) Listen(true);
            Apply();
        }

        private void OnBrushChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == SolidColorBrush.ColorProperty || e.Property == Brush.OpacityProperty) Apply();
        }

        private void Apply()
        {
            if (border.Child is not MaterialIcon icon) return;
            if (border.Background is not ISolidColorBrush { Color.A: > 200 } solid)
            {
                icon.ClearValue(MaterialIcon.ForegroundProperty);
                return;
            }
            // La variante sombre du pinceau Tone* ne doit jamais changer les icônes.
            // Replacer uniquement ce fond de tuile par le ton d'origine, sans toucher
            // à la ressource partagée (qui peut servir au texte ailleurs).
            if (IconPalette.TryOriginalColor(solid.Color, out var original))
            {
                border.Background = new SolidColorBrush(original);
                return; // L'observateur rappellera Apply() avec le nouveau fond.
            }
            icon.Foreground = new SolidColorBrush(GlyphFor(solid.Color));
        }

        public void Dispose()
        {
            Listen(false);
            border.AttachedToVisualTree -= OnAttached;
            border.DetachedFromVisualTree -= OnDetached;
            backgroundSubscription.Dispose();
            childSubscription.Dispose();
        }
    }

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => next(value);
    }
}
