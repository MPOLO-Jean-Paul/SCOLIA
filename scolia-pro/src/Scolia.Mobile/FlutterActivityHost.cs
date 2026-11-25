#if SCOLIA_FLUTTER_EXPERIMENT
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
    private bool disposed;
    private int generation;

    internal void Resume()
    {
        active = true;
        if (fellBack || disposed || surface is not null) return;
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
            shell = current;
            if (RequiresNative(current)) { shell = null; return; }
            shell.PropertyChanged += ShellChanged;
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
            Fallback();
        }
    }

    private static bool RequiresNative(ShellViewModel value) =>
        value.IsLocked || value.SwitchingAccount || value.HasDialog || value.HasSheet ||
        value.Session is { MustChangePassword: true };

    private void ShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (shell is { } current && RequiresNative(current)) Fallback();
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
                if (request == generation && !disposed) Close();
            }));
        });
    }

    internal void Pause()
    {
        active = false;
        generation++;
        Close();
    }

    private void Close()
    {
        if (shell is not null) shell.PropertyChanged -= ShellChanged;
        shell = null;
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
