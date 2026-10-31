using Android.Animation;
using Android.App;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using AndroidView = Android.Views.View;

namespace Scolia.Mobile;

/// <summary>
/// Garde une continuité entre le logo du système et la première page Avalonia réellement prête.
/// Les trois points ne changent que d'opacité : aucune mesure ni mise en page à chaque image.
/// </summary>
internal sealed class StartupScreen : FrameLayout
{
    private readonly Activity activity;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<ObjectAnimator> pulses = [];
    private readonly TextView status;
    private readonly LinearLayout dots;
    private readonly Button retry;
    private readonly float animationScale;
    private ObjectAnimator? fade;
    private bool closed;
    private bool active = true;

    public StartupScreen(Activity activity) : base(activity)
    {
        this.activity = activity;
        animationScale = ReadAnimationScale(activity);
        SetBackgroundColor(Color.White);
        Clickable = true;
        Focusable = true;

        // Le paysage et les grands caractères gardent un contenu accessible, sans débordement coupé.
        var scroll = new ScrollView(activity) { FillViewport = true };
        AddView(scroll, new FrameLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetGravity(GravityFlags.Center);
        content.SetPadding(Dp(24), Dp(24), Dp(24), Dp(24));
        scroll.AddView(content, new ScrollView.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));

        // Le dessin vectoriel reprend exactement le symbole SCOLIA utilisé dans Avalonia.
        // Il n'appartient à aucun établissement et ne remplace jamais son logo configuré.
        var markBackground = new GradientDrawable();
        markBackground.SetShape(ShapeType.Rectangle);
        markBackground.SetCornerRadius(Dp(26));
        markBackground.SetColor(Color.Rgb(241, 246, 252));
        var mark = new ImageView(activity) { ImportantForAccessibility = ImportantForAccessibility.No };
        mark.SetImageResource(Resource.Drawable.scolia_mark);
        mark.SetScaleType(ImageView.ScaleType.FitCenter);
        mark.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        mark.Background = markBackground;
        content.AddView(mark, new LinearLayout.LayoutParams(Dp(112), Dp(112))
        {
            BottomMargin = Dp(20),
            Gravity = GravityFlags.CenterHorizontal,
        });
        // Une seule entrée discrète, désactivée si l'appareil réduit les animations.
        if (animationScale > 0)
        {
            mark.Alpha = 0f;
            mark.Animate()?.Alpha(1f)?.SetDuration((long)(240 * animationScale))?.Start();
        }

        var title = new TextView(activity) { Text = "SCOLIA Pro", TextSize = 23, Gravity = GravityFlags.Center };
        title.SetTextColor(Color.Rgb(15, 43, 107));
        title.SetTypeface(null, TypefaceStyle.Bold);
        content.AddView(title, new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));

        status = new TextView(activity)
        {
            Text = "Préparation de votre espace…",
            TextSize = 13,
            Gravity = GravityFlags.Center,
            AccessibilityLiveRegion = AccessibilityLiveRegion.Polite,
        };
        status.SetTextColor(Color.Rgb(91, 105, 126));
        content.AddView(status, new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)
        {
            TopMargin = Dp(8),
        });

        dots = new LinearLayout(activity) { Orientation = Orientation.Horizontal, ImportantForAccessibility = ImportantForAccessibility.No };
        dots.SetGravity(GravityFlags.Center);
        content.AddView(dots, new LinearLayout.LayoutParams(LayoutParams.WrapContent, Dp(24)) { TopMargin = Dp(18) });
        for (var i = 0; i < 3; i++)
        {
            var shape = new GradientDrawable();
            shape.SetShape(ShapeType.Oval);
            shape.SetColor(i == 1 ? Color.Rgb(20, 159, 72) : Color.Rgb(15, 43, 107));
            var dot = new AndroidView(activity) { Background = shape, Alpha = animationScale > 0 ? 0.25f : 1f };
            dots.AddView(dot, new LinearLayout.LayoutParams(Dp(6), Dp(6)) { LeftMargin = Dp(4), RightMargin = Dp(4) });
            if (animationScale <= 0) continue;
            var pulse = ObjectAnimator.OfFloat(dot, "alpha", 0.25f, 1f, 0.25f)!;
            pulse.SetDuration(900);
            pulse.StartDelay = i * 150;
            pulse.RepeatCount = ValueAnimator.Infinite;
            pulse.RepeatMode = ValueAnimatorRepeatMode.Restart;
            pulses.Add(pulse);
        }

        retry = new Button(activity) { Text = "Réessayer", Visibility = ViewStates.Gone };
        retry.SetMinHeight(Dp(48));
        content.AddView(retry, new LinearLayout.LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent) { TopMargin = Dp(16) });
        retry.Click += (_, _) =>
        {
            if (closed) return;
            if (Platform.RetryStartup is not { } restart) { activity.Finish(); return; }
            retry.Visibility = ViewStates.Gone;
            dots.Visibility = ViewStates.Visible;
            status.Text = "Préparation de votre espace…";
            Begin(restart(), TimeSpan.Zero);
        };
    }

    public void Begin(Task startup, TimeSpan minimum)
    {
        StartPulses();
        _ = OpenAsync(startup, animationScale > 0 ? minimum : TimeSpan.Zero);
    }

    public void SetActive(bool value)
    {
        active = value;
        foreach (var pulse in pulses)
        {
            if (!pulse.IsStarted) continue;
            if (value) pulse.Resume();
            else pulse.Pause();
        }
    }

    private void StartPulses()
    {
        foreach (var pulse in pulses)
        {
            pulse.Start();
            if (!active) pulse.Pause();
        }
    }

    private async Task OpenAsync(Task startup, TimeSpan minimum)
    {
        var token = lifetime.Token;
        try
        {
            // Une première préparation de serveur peut durer : le logo reste visible, l'attente est expliquée.
            // Ce délai ne relance pas un second serveur pendant que le premier démarre.
            var ready = StartupReadiness.WaitAsync(startup, minimum, WaitForPageFrameAsync, token);
            var patience = Task.Delay(TimeSpan.FromSeconds(12), token);
            if (await Task.WhenAny(ready, patience).ConfigureAwait(false) != ready)
                await OnUiAsync(() => { if (!closed) status.Text = "La préparation continue. Merci de patienter…"; }).ConfigureAwait(false);
            await ready.ConfigureAwait(false);
            if (animationScale > 0)
            {
                await OnUiAsync(() =>
                {
                    fade = ObjectAnimator.OfFloat(this, "alpha", 1f, 0f);
                    fade!.SetDuration(180);
                    fade.Start();
                }).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(180 * animationScale), token).ConfigureAwait(false);
            }
            await OnUiAsync(Close).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SCOLIA", $"Démarrage de l'application : {ex}");
            await OnUiAsync(() =>
            {
                if (closed) return;
                foreach (var pulse in pulses) pulse.Cancel();
                dots.Visibility = ViewStates.Gone;
                Alpha = 1;
                status.Text = Platform.RetryStartup is null
                    ? "SCOLIA n'a pas pu démarrer. Fermez puis rouvrez l'application."
                    : "SCOLIA n'a pas pu ouvrir votre espace. Vous pouvez réessayer.";
                retry.Text = Platform.RetryStartup is null ? "Fermer" : "Réessayer";
                retry.Visibility = ViewStates.Visible;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>Attend la mise en page et une image Avalonia avant d'enlever le fond natif.</summary>
    private Task WaitForPageFrameAsync(CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = token.Register(() => ready.TrySetCanceled(token));
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (closed) { ready.TrySetCanceled(token); return; }
            void NextFrame() => PostOnAnimation(new Java.Lang.Runnable(() => ready.TrySetResult()));
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime phone
                && phone.MainView is { } view && Avalonia.Controls.TopLevel.GetTopLevel(view) is { } top)
                top.RequestAnimationFrame(_ => NextFrame());
            else NextFrame();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        return DisposeRegistrationAsync(ready.Task, registration);
    }

    private static async Task DisposeRegistrationAsync(Task task, CancellationTokenRegistration registration)
    {
        using (registration) await task.ConfigureAwait(false);
    }

    private Task OnUiAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try { action(); done.TrySetResult(); }
            catch (Exception ex) { done.TrySetException(ex); }
        });
        return done.Task;
    }

    private static float ReadAnimationScale(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !ValueAnimator.AreAnimatorsEnabled()) return 0;
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime phone
            && phone.MainView?.DataContext is ShellViewModel { Prefs.Animations: false }) return 0;
        try { return Math.Clamp(Settings.Global.GetFloat(activity.ContentResolver, Settings.Global.AnimatorDurationScale, 1f), 0, 10); }
        catch (Java.Lang.SecurityException) { return 1; }
    }

    private int Dp(int value) => (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1));

    public void Close()
    {
        if (closed) return;
        closed = true;
        lifetime.Cancel();
        foreach (var pulse in pulses) { pulse.Cancel(); pulse.Dispose(); }
        pulses.Clear();
        fade?.Cancel();
        fade?.Dispose();
        fade = null;
        (Parent as ViewGroup)?.RemoveView(this);
        lifetime.Dispose();
    }
}
