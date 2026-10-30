using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;

namespace Scolia.Mobile.Security;

/// <summary>
/// Android BiometricPrompt (API 29+). SCOLIA ne collecte, ne stocke et ne
/// transmet aucune empreinte ; le résultat natif est oui/non.
/// </summary>
public static class AndroidBiometrics
{
    private static WeakReference<Activity>? current;
    private static int inProgress;

    public static void Attach(Activity activity) => current = new WeakReference<Activity>(activity);

    public static void Detach(Activity activity)
    {
        if (current?.TryGetTarget(out var active) == true && ReferenceEquals(active, activity))
            current = null;
    }

    private static Activity? Active => current?.TryGetTarget(out var a) == true
        && !a.IsFinishing && !a.IsDestroyed ? a : null;

    public static bool IsAvailable()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q || Active is not { } activity)
            return false;
        try
        {
            return activity.GetSystemService(Context.BiometricService)
                is BiometricManager manager && (int)manager.CanAuthenticate() == 0;
        }
        catch (Java.Lang.Exception) { return false; }
    }

    public static async Task<bool> VerifyAsync()
    {
        if (!IsAvailable() || Active is not { } activity
            || Interlocked.Exchange(ref inProgress, 1) != 0)
            return false;
        try
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var prompt = new BiometricPrompt.Builder(activity)
                        .SetTitle("SCOLIA Phone")
                        .SetSubtitle("Confirmez votre identité avec l'empreinte de ce téléphone")
                        .SetNegativeButton("Utiliser le mot de passe", activity.MainExecutor,
                            new CancelListener(done))
                        .Build();
                    prompt.Authenticate(new CancellationSignal(), activity.MainExecutor, new AuthCallback(done));
                }
                catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
                {
                    done.TrySetResult(false);
                }
            });
            return await done.Task.ConfigureAwait(false);
        }
        finally { Interlocked.Exchange(ref inProgress, 0); }
    }

    private sealed class CancelListener(TaskCompletionSource<bool> done)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => done.TrySetResult(false);
    }

    private sealed class AuthCallback(TaskCompletionSource<bool> done) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
            => done.TrySetResult(true);
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? text)
            => done.TrySetResult(false);
    }
}
