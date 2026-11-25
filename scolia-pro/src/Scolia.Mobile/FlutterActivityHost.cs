#if SCOLIA_FLUTTER_UI
using Android.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Com.Jpservices.Scolia.Flutterbridge;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Flutter.AndroidBinding;
using System.ComponentModel;

namespace Scolia.Mobile;

/// <summary>Hôte expérimental : retrait immédiat de Flutter si le Shell exige un écran natif.</summary>
internal sealed class FlutterActivityHost(MainActivity activity) : IDisposable
{
    private ShellViewModel? shell;
    private ScoliaFlutterSurface? surface;
    private ChannelConnection? connection;
    private View? view;
    private bool active;
    private bool fellBack;
    private bool bridgeFailed;
    private bool reentryScheduled;
    private bool disposed;
    private int generation;

    internal void Resume()
    {
        active = true;
        if (disposed || bridgeFailed || surface is not null) return;
        if (fellBack)
        {
            // Une page native reste maîtresse de l'écran tant qu'elle n'est pas quittée.
            if (shell is null && Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime lifetime &&
                lifetime.MainView?.DataContext is ShellViewModel current)
                Observe(current);
            ScheduleReentry();
            return;
        }
        var request = ++generation;
        _ = TryOpenAsync(request);
    }

    private async Task TryOpenAsync(int request)
    {
        try
        {
            var startup = Platform.StartupTask;
            if (startup is null) return;
            await startup;
            // Le splash natif doit réellement avoir disparu.
            for (var attempt = 0; attempt < 40 && activity.IsStartupOverlayVisible; attempt++)
                await Task.Delay(100);
            if (!active || disposed || fellBack || request != generation ||
                activity.IsStartupOverlayVisible || surface is not null) return;
            if (Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime lifetime ||
                lifetime.MainView?.DataContext is not ShellViewModel current) return;
            Observe(current);
            if (RequiresNative(current) || current.BackstageOpen)
            {
                fellBack = true;
                return;
            }
            surface = new ScoliaFlutterSurface(activity);
            connection = FlutterIntegration.Connect(surface.Engine!, current,
                () => active && !disposed && !fellBack && request == generation && ReferenceEquals(shell, current),
                Fallback);
            view = surface.SurfaceView;
            if (view is null) throw new InvalidOperationException("Flutter view missing.");
            activity.AddContentView(view, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            surface.Start();
        }
        catch (Exception ex)
        {
            if (request != generation || disposed || !active) return;
            Android.Util.Log.Warn("SCOLIA-Flutter", "Interface expérimentale indisponible : " + ex.GetType().Name);
            bridgeFailed = true;
            Fallback();
        }
    }

    private static bool RequiresNative(ShellViewModel value) =>
        value.IsLocked || value.SwitchingAccount || value.HasDialog || value.HasSheet ||
        value.Session is { MustChangePassword: true };

    private void Observe(ShellViewModel current)
    {
        if (ReferenceEquals(shell, current)) return;
        if (shell is not null) shell.PropertyChanged -= ShellChanged;
        shell = current;
        shell.PropertyChanged += ShellChanged;
    }

    private static bool CanReenter(ShellViewModel current) =>
        !RequiresNative(current) && !current.BackstageOpen && !current.Opening && !current.ShowStartup &&
        !current.Working && (current.ShowLogin || current.ShowApp &&
            current.Current is HomePage or StartPage or PromoterDashboardPage);

    private void ShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (shell is not { } current) return;
        if (!fellBack && RequiresNative(current)) Fallback();
        else if (fellBack && e.PropertyName is nameof(ShellViewModel.Current) or nameof(ShellViewModel.HasDialog)
            or nameof(ShellViewModel.HasSheet) or nameof(ShellViewModel.IsLocked) or nameof(ShellViewModel.ShowLogin)
            or nameof(ShellViewModel.BackstageOpen) or nameof(ShellViewModel.Working) or nameof(ShellViewModel.ShowApp))
            ScheduleReentry();
    }

    private void ScheduleReentry()
    {
        if (!active || disposed || bridgeFailed || !fellBack || reentryScheduled ||
            shell is not { } current || !CanReenter(current)) return;
        reentryScheduled = true;
        _ = ReenterAsync(current, generation);
    }

    private async Task ReenterAsync(ShellViewModel current, int request)
    {
        try
        {
            // Laisse les dialogues et la navigation native terminer leur transition.
            await Task.Delay(350);
            if (!active || disposed || bridgeFailed || request != generation || !fellBack ||
                surface is not null || !ReferenceEquals(shell, current) || !CanReenter(current)) return;
            fellBack = false;
            await TryOpenAsync(++generation);
        }
        finally
        {
            reentryScheduled = false;
        }
    }

    private void Fallback()
    {
        if (disposed || fellBack) return;
        fellBack = true;
        var request = ++generation;
        // Ne pas détruire le moteur pendant une réponse JNI en cours.
        activity.RunOnUiThread(() =>
        {
            if (request != generation || disposed) return;
            if (view is not null) view.Visibility = ViewStates.Invisible;
            activity.Window?.DecorView?.Post(new Java.Lang.Runnable(() =>
            {
                if (request == generation && !disposed) Close(keepShell: !bridgeFailed);
            }));
        });
    }

    internal void Pause()
    {
        active = false;
        generation++;
        Close();
    }

    private void Close(bool keepShell = false)
    {
        if (!keepShell)
        {
            if (shell is not null) shell.PropertyChanged -= ShellChanged;
            shell = null;
        }
        if (view is not null) (view.Parent as ViewGroup)?.RemoveView(view);
        view = null;
        try { connection?.Dispose(); }
        finally
        {
            connection = null;
            try { surface?.Close(); }
            finally { surface?.Dispose(); surface = null; }
        }
    }

    public void Dispose()
    {
        disposed = true;
        Pause();
    }
}
#endif
