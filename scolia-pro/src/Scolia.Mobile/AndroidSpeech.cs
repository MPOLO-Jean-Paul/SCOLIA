using Android.App;
using Android.Content;
using Android.Speech;

namespace Scolia.Mobile;

/// <summary>
/// Recherche à la voix sur Android : la fenêtre « Parlez maintenant » de Google s'ouvre, écoute en français, puis rend
/// le texte au champ de recherche. SCOLIA n'enregistre rien et n'a pas besoin de la permission du micro : c'est le
/// service vocal du téléphone qui écoute.
/// </summary>
public static class AndroidSpeech
{
    private const int RequestCode = 4717;
    private static TaskCompletionSource<string?>? pending;
    private static WeakReference<Activity>? current;

    public static void Attach(Activity activity) => current = new WeakReference<Activity>(activity);

    private static Intent Intent() => new Intent(RecognizerIntent.ActionRecognizeSpeech)
        .PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm)
        .PutExtra(RecognizerIntent.ExtraLanguage, "fr-FR")
        .PutExtra(RecognizerIntent.ExtraLanguagePreference, "fr-FR")
        .PutExtra(RecognizerIntent.ExtraPrompt, "Dites ce que vous cherchez")
        .PutExtra(RecognizerIntent.ExtraMaxResults, 1);

    /// <summary>Vrai si un service vocal (Google ou celui du fabricant) peut répondre.</summary>
    public static bool IsAvailable()
    {
        if (current?.TryGetTarget(out var activity) != true) return false;
        try { return Intent().ResolveActivity(activity!.PackageManager!) is not null; }
        catch (Exception) { return false; }
    }

    public static Task<string?> ListenAsync()
    {
        if (current?.TryGetTarget(out var activity) != true) return Task.FromResult<string?>(null);
        pending?.TrySetResult(null);
        var waiting = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = waiting;
        try { activity!.StartActivityForResult(Intent(), RequestCode); }
        catch (ActivityNotFoundException) { pending = null; return Task.FromResult<string?>(null); }
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
