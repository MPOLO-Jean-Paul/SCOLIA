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
    private readonly LogoAssemblyView logo;
    private readonly List<AndroidView> intro = [];
    private readonly TaskCompletionSource introDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool introStarted;
    private ObjectAnimator? fade;
    private bool closed;
    private bool active = true;

    /// <summary>Thème choisi dans SCOLIA ; « Système » suit le mode sombre du téléphone.</summary>
    private static bool IsDark(Activity activity)
    {
        var theme = "system";
        try { theme = Scolia.Bureau.Infrastructure.Preferences.Load().Theme; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        if (theme == "dark") return true;
        if (theme == "light") return false;
        var mode = activity.Resources?.Configuration?.UiMode ?? Android.Content.Res.UiMode.NightNo;
        return (mode & Android.Content.Res.UiMode.NightMask) == Android.Content.Res.UiMode.NightYes;
    }

    public StartupScreen(Activity activity) : base(activity)
    {
        this.activity = activity;
        animationScale = ReadAnimationScale(activity);
        // Même ambiance que l'application : sombre si le thème choisi (ou celui du téléphone) est sombre.
        var dark = IsDark(activity);
        SetBackgroundColor(dark ? Color.Rgb(19, 19, 22) : Color.White);
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
        markBackground.SetColor(dark ? Color.Rgb(33, 33, 38) : Color.Rgb(241, 246, 252));
        // Le logo est dessiné en quatre pièces qui s'assemblent en tournant au lancement.
        logo = new LogoAssemblyView(activity, dark) { Background = markBackground };
        logo.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
        content.AddView(logo, new LinearLayout.LayoutParams(Dp(112), Dp(112))
        {
            BottomMargin = Dp(20),
            Gravity = GravityFlags.CenterHorizontal,
        });

        var title = new TextView(activity) { Text = "SCOLIA Pro", TextSize = 23, Gravity = GravityFlags.Center };
        title.SetTextColor(dark ? Color.Rgb(230, 230, 235) : Color.Rgb(15, 43, 107));
        title.SetTypeface(null, TypefaceStyle.Bold);
        content.AddView(title, new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));
        intro.Add(title);

        status = new TextView(activity)
        {
            Text = "Préparation de votre espace…",
            TextSize = 13,
            Gravity = GravityFlags.Center,
            AccessibilityLiveRegion = AccessibilityLiveRegion.Polite,
        };
        status.SetTextColor(dark ? Color.Rgb(161, 161, 169) : Color.Rgb(91, 105, 126));
        content.AddView(status, new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent)
        {
            TopMargin = Dp(8),
        });
        intro.Add(status);

        dots = new LinearLayout(activity) { Orientation = Orientation.Horizontal, ImportantForAccessibility = ImportantForAccessibility.No };
        dots.SetGravity(GravityFlags.Center);
        content.AddView(dots, new LinearLayout.LayoutParams(LayoutParams.WrapContent, Dp(24)) { TopMargin = Dp(18) });
        intro.Add(dots);
        for (var i = 0; i < 3; i++)
        {
            var shape = new GradientDrawable();
            shape.SetShape(ShapeType.Oval);
            shape.SetColor(i == 1 ? Color.Rgb(20, 159, 72) : dark ? Color.Rgb(120, 185, 255) : Color.Rgb(15, 43, 107));
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
        PlayIntro();
        _ = OpenAsync(startup, animationScale > 0 ? minimum : TimeSpan.Zero);
    }

    /// <summary>
    /// Ouverture : les pièces du logo arrivent en tournant et s'emboîtent, le logo descend à sa place, puis le nom,
    /// le message et les trois points prennent la relève. Sans animations sur le téléphone, tout s'affiche aussitôt.
    /// </summary>
    private void PlayIntro()
    {
        if (introStarted) { StartPulses(); return; }
        introStarted = true;
        if (animationScale <= 0)
        {
            logo.Progress = 1f;
            StartPulses();
            introDone.TrySetResult();
            return;
        }
        logo.Progress = 0f;
        logo.ScaleX = logo.ScaleY = 1.3f;
        logo.TranslationY = -Dp(40);
        foreach (var view in intro)
        {
            view.Alpha = 0f;
            view.TranslationY = Dp(10);
        }
        var assemble = ValueAnimator.OfFloat(0f, 1f)!;
        assemble.SetDuration(1100);
        assemble.SetInterpolator(new Android.Views.Animations.LinearInterpolator());
        assemble.Update += (_, e) => logo.Progress = (float)(e.Animation.AnimatedValue ?? 1f);
        assemble.AnimationEnd += (_, _) =>
        {
            if (closed) return;
            // Le logo descend à sa place, en douceur.
            logo.Animate()?.TranslationY(0f)?.ScaleX(1f)?.ScaleY(1f)?.SetDuration(380)
                ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(1.6f))?.Start();
            // Le nom, le message et les trois points apparaissent l'un après l'autre.
            for (var i = 0; i < intro.Count; i++)
                intro[i].Animate()?.Alpha(1f)?.TranslationY(0f)?.SetStartDelay(200 + i * 90)?.SetDuration(260)
                    ?.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator())?.Start();
            PostDelayed(() =>
            {
                if (closed) return;
                StartPulses();
                introDone.TrySetResult();
            }, (long)((200 + intro.Count * 90 + 260) * animationScale));
        };
        assemble.Start();
        if (!active) assemble.Pause();
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
            // L'ouverture du logo va jusqu'au bout : la page suivante ne coupe jamais l'animation en plein vol.
            await introDone.Task.WaitAsync(TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
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
