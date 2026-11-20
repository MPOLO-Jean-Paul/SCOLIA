using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Écran dont l'état change sans action de l'utilisateur (connexion au relais, appareils).</summary>
public interface ILiveRefresh
{
    void RefreshLive();
}

/// <summary>
/// Tant que l'écran est affiché : actualise son état à chaque changement de la connexion au relais, et toutes les
/// trois secondes pour les tentatives en cours. Rien ne tourne quand l'écran est fermé.
/// </summary>
public static class LiveRefresh
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(LiveRefresh));

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private static readonly AttachedProperty<Watcher?> WatcherProperty =
        AvaloniaProperty.RegisterAttached<Control, Watcher?>("Watcher", typeof(LiveRefresh));

    static LiveRefresh()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.GetValue(WatcherProperty)?.Dispose();
            control.SetValue(WatcherProperty, e.NewValue is true ? new Watcher(control) : null);
        });
    }

    private sealed class Watcher : IDisposable
    {
        private readonly Control control;
        private readonly DispatcherTimer timer;

        public Watcher(Control control)
        {
            this.control = control;
            timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => Refresh());
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree += OnDetached;
            if (control.IsAttachedToVisualTree()) OnAttached(null, null!);
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            RemoteConnection.HostStateChanged += OnHostChanged;
            timer.Start();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            RemoteConnection.HostStateChanged -= OnHostChanged;
            timer.Stop();
        }

        private void OnHostChanged() => Dispatcher.UIThread.Post(Refresh);

        private void Refresh() => (control.DataContext as ILiveRefresh)?.RefreshLive();

        public void Dispose()
        {
            OnDetached(null, null!);
            control.AttachedToVisualTree -= OnAttached;
            control.DetachedFromVisualTree -= OnDetached;
        }
    }
}
