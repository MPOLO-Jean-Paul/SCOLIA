using AVFoundation;
using Foundation;
using Speech;

namespace Scolia.Phone.iOS;

/// <summary>
/// Recherche à la voix sur iPhone : la reconnaissance vocale d'Apple écoute en français pendant que le champ affiche
/// « Parlez maintenant… ». L'écoute s'arrête d'elle-même après un court silence, puis le texte part dans la recherche.
/// Quand l'iPhone sait reconnaître le français sur l'appareil, l'écoute se fait sans Internet.
/// Rien n'est enregistré par SCOLIA.
/// </summary>
public static class IosSpeech
{
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan NoSpeech = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(12);
    private static bool busy;

    private static SFSpeechRecognizer? Recognizer()
    {
        try { return new SFSpeechRecognizer(NSLocale.FromLocaleIdentifier("fr-FR")); }
        catch (Exception) { return null; }
    }

    public static bool IsAvailable()
    {
        if (SFSpeechRecognizer.AuthorizationStatus is SFSpeechRecognizerAuthorizationStatus.Denied
            or SFSpeechRecognizerAuthorizationStatus.Restricted) return false;
        return Recognizer() is { Available: true };
    }

    private static Task<bool> AuthorizeAsync()
    {
        var speech = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SFSpeechRecognizer.RequestAuthorization(status => speech.TrySetResult(status == SFSpeechRecognizerAuthorizationStatus.Authorized));
        return speech.Task.ContinueWith(t =>
        {
            if (!t.Result) return Task.FromResult(false);
            var microphone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable CA1422, CS0618
            AVAudioSession.SharedInstance().RequestRecordPermission(granted => microphone.TrySetResult(granted));
#pragma warning restore CA1422, CS0618
            return microphone.Task;
        }, TaskScheduler.Default).Unwrap();
    }

    public static async Task<string?> ListenAsync()
    {
        if (busy) return null;
        busy = true;
        AVAudioEngine? engine = null;
        SFSpeechRecognitionTask? recognition = null;
        var session = AVAudioSession.SharedInstance();
        try
        {
            if (!await AuthorizeAsync()) return null;
            var recognizer = Recognizer();
            if (recognizer is not { Available: true }) return null;

            session.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DuckOthers);
            session.SetActive(true);

            // Hors ligne : l'iPhone reconnaît le français sur l'appareil quand il le sait ; rien ne part sur Internet.
            var request = new SFSpeechAudioBufferRecognitionRequest
            {
                ShouldReportPartialResults = true,
                RequiresOnDeviceRecognition = recognizer.SupportsOnDeviceRecognition,
            };
            engine = new AVAudioEngine();
            var input = engine.InputNode;
            input.InstallTapOnBus(0, 1024, input.GetBusOutputFormat(0), (buffer, _) => request.Append(buffer));
            engine.Prepare();
            if (!engine.StartAndReturnError(out _)) return null;

            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? heard = null;
            var started = DateTime.UtcNow;
            var lastChange = started;
            recognition = recognizer.GetRecognitionTask(request, (result, error) =>
            {
                if (result is not null)
                {
                    var text = result.BestTranscription.FormattedString;
                    if (!string.Equals(text, heard, StringComparison.Ordinal)) { heard = text; lastChange = DateTime.UtcNow; }
                    if (result.Final) done.TrySetResult(true);
                }
                if (error is not null) done.TrySetResult(false);
            });

            // Fin de phrase : un court silence après des mots, rien d'entendu pendant six secondes, ou douze secondes au plus.
            while (!done.Task.IsCompleted)
            {
                await Task.Delay(150);
                var now = DateTime.UtcNow;
                if (heard is { Length: > 0 } && now - lastChange > Silence) break;
                if (heard is null && now - started > NoSpeech) break;
                if (now - started > Longest) break;
            }
            engine.Stop();
            input.RemoveTapOnBus(0);
            request.EndAudio();
            // Le dernier mot arrive parfois juste après l'arrêt du micro.
            await Task.WhenAny(done.Task, Task.Delay(1200));
            return string.IsNullOrWhiteSpace(heard) ? null : heard;
        }
        catch (Exception) { return null; }
        finally
        {
            try
            {
                recognition?.Cancel();
                if (engine is { Running: true }) engine.Stop();
                session.SetCategory(AVAudioSessionCategory.Ambient);
                session.SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation);
            }
            catch (Exception) { }
            busy = false;
        }
    }
}
