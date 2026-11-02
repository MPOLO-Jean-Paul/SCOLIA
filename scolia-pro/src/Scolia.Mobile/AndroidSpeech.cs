using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;

namespace Scolia.Mobile;

/// <summary>
/// Recherche à la voix sur Android, avec ou sans Internet.
/// <list type="number">
/// <item>Android 12 et plus, si le téléphone sait reconnaître la voix sur l'appareil : l'écoute se fait dans le
/// téléphone, sans réseau. Le champ affiche « Parlez maintenant… ». Si le français n'est pas encore installé,
/// SCOLIA en demande le téléchargement (Android 13 et plus) pour les fois suivantes.</item>
/// <item>Sinon : la fenêtre « Parlez maintenant » du service vocal, réglée pour préférer le modèle hors ligne quand il
/// est présent.</item>
/// </list>
/// SCOLIA n'enregistre rien. Le micro n'est demandé que pour l'écoute sur l'appareil.
/// </summary>
public static class AndroidSpeech
{
    private const int RequestCode = 4717;
    private const int MicrophoneRequest = 4718;
    private static TaskCompletionSource<string?>? pending;
    private static TaskCompletionSource<bool>? microphone;
    private static WeakReference<Activity>? current;

    public static void Attach(Activity activity) => current = new WeakReference<Activity>(activity);

    private static Activity? Active => current?.TryGetTarget(out var activity) == true ? activity : null;

    private static Intent Intent(bool preferOffline) => new Intent(RecognizerIntent.ActionRecognizeSpeech)
        .PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm)
        .PutExtra(RecognizerIntent.ExtraLanguage, "fr-FR")
        .PutExtra(RecognizerIntent.ExtraLanguagePreference, "fr-FR")
        .PutExtra(RecognizerIntent.ExtraPrompt, "Dites ce que vous cherchez")
        .PutExtra(RecognizerIntent.ExtraPreferOffline, preferOffline)
        .PutExtra(RecognizerIntent.ExtraMaxResults, 1);

    /// <summary>Reconnaissance dans le téléphone lui-même : Android 12 et plus, selon le modèle du téléphone.</summary>
    private static bool OnDevice(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return false;
        try { return SpeechRecognizer.IsOnDeviceRecognitionAvailable(context); }
        catch (Exception) { return false; }
    }

    private static Context? application;

    /// <summary>Contexte de l'application, connu dès le lancement, avant même que l'écran soit prêt.</summary>
    public static void Initialize(Context context) => application = context.ApplicationContext ?? context;

    /// <summary>
    /// Vrai si le téléphone peut écouter : sur l'appareil, par un service vocal (Google, fabricant) ou par sa fenêtre
    /// vocale. Le contrôle se fait sur l'application, pas sur l'écran : le micro apparaît même quand la recherche
    /// est préparée avant l'affichage.
    /// </summary>
    public static bool IsAvailable()
    {
        if ((Active as Context ?? application) is not { } context) return true;
        if (OnDevice(context)) return true;
        try
        {
            if (SpeechRecognizer.IsRecognitionAvailable(context)) return true;
            return Intent(true).ResolveActivity(context.PackageManager!) is not null;
        }
        catch (Exception) { return true; }
    }

    public static async Task<string?> ListenAsync()
    {
        if (Active is not { } activity) return null;
        if (OnDevice(activity) && await MicrophoneAsync(activity))
        {
            var (heard, fallback) = await ListenOnDeviceAsync(activity);
            if (!fallback) return heard;
        }
        return await ListenWithServiceAsync(activity);
    }

    // ---------- Écoute dans le téléphone (hors ligne) ----------

    private static async Task<(string? Text, bool Fallback)> ListenOnDeviceAsync(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return (null, true);
        SpeechRecognizer? recognizer = null;
        var listener = new Listener();
        var intent = Intent(true);
        try
        {
            recognizer = SpeechRecognizer.CreateOnDeviceSpeechRecognizer(activity);
            recognizer.SetRecognitionListener(listener);
            recognizer.StartListening(intent);
            var outcome = await listener.Done.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (outcome.Error is SpeechRecognizerError.LanguageNotSupported or SpeechRecognizerError.LanguageUnavailable)
            {
                // Français absent du téléphone : il se télécharge pour la prochaine fois, et cette fois passe par le service.
                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                    try { recognizer.TriggerModelDownload(intent); } catch (Exception) { }
                return (null, true);
            }
            if (outcome.Error is SpeechRecognizerError.InsufficientPermissions or SpeechRecognizerError.Client
                or SpeechRecognizerError.RecognizerBusy) return (null, true);
            return (outcome.Text, false);
        }
        catch (TimeoutException) { return (listener.Last, false); }
        catch (Exception) { return (null, true); }
        finally
        {
            try { recognizer?.Cancel(); recognizer?.Destroy(); } catch (Exception) { }
        }
    }

    /// <summary>Le micro se demande une fois, au premier toucher du micro d'une recherche.</summary>
    private static Task<bool> MicrophoneAsync(Activity activity)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23)) return Task.FromResult(true);
        if (activity.CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted) return Task.FromResult(true);
        microphone?.TrySetResult(false);
        microphone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { activity.RequestPermissions([Manifest.Permission.RecordAudio], MicrophoneRequest); }
        catch (Java.Lang.Exception) { microphone.TrySetResult(false); }
        return microphone.Task;
    }

    /// <summary>Réponse à la demande du micro ; faux si elle concerne une autre permission.</summary>
    public static bool CompletePermission(int requestCode, Permission[] results)
    {
        if (requestCode != MicrophoneRequest) return false;
        microphone?.TrySetResult(results.Length > 0 && results[0] == Permission.Granted);
        microphone = null;
        return true;
    }

    private sealed class Listener : Java.Lang.Object, IRecognitionListener
    {
        public TaskCompletionSource<(string? Text, SpeechRecognizerError? Error)> Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Last { get; private set; }

        public void OnResults(Bundle? results) =>
            Done.TrySetResult((results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() ?? Last, null));
        public void OnPartialResults(Bundle? partialResults)
        {
            if (partialResults?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault() is { Length: > 0 } text) Last = text;
        }
        public void OnError(SpeechRecognizerError error) => Done.TrySetResult((null, error));
        public void OnBeginningOfSpeech() { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnEvent(int eventType, Bundle? @params) { }
        public void OnReadyForSpeech(Bundle? @params) { }
        public void OnRmsChanged(float rmsdB) { }
    }

    // ---------- Fenêtre du service vocal ----------

    private static Task<string?> ListenWithServiceAsync(Activity activity)
    {
        pending?.TrySetResult(null);
        var waiting = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = waiting;
        try { activity.StartActivityForResult(Intent(true), RequestCode); }
        catch (ActivityNotFoundException)
        {
            pending = null;
            // Aucun service vocal : le dire, plutôt qu'un micro sans effet.
            Android.Widget.Toast.MakeText(activity, "Saisie vocale indisponible : activez « Saisie vocale Google » ou installez l'application Google.",
                Android.Widget.ToastLength.Long)?.Show();
            return Task.FromResult<string?>(null);
        }
        return waiting.Task;
    }

    /// <summary>Réponse de la fenêtre vocale ; faux si le résultat concerne une autre demande de l'activité.</summary>
    public static bool Complete(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return false;
        var text = resultCode == Result.Ok
            ? data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault()
            : null;
        var waiting = pending;
        pending = null;
        waiting?.TrySetResult(text);
        return true;
    }
}
