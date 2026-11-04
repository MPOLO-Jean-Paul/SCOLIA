using Avalonia;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Saisie à la voix des recherches : le même micro dans chaque champ de recherche, comme dans Google. Le texte dicté
/// remplace la saisie et la recherche part aussitôt.
/// </summary>
public static class VoiceInput
{
    public static bool Available =>
        (Platform.RecognizeSpeechAsync is not null || Platform.RecognizeSpeechWithCancellationAsync is not null)
        && (Platform.CanRecognizeSpeech?.Invoke() ?? true);

    /// <summary>Retourne uniquement une dictée non annulée. Sous Windows, le jeton arrête le vrai processus vocal.</summary>
    public static async Task<string?> ListenAsync(CancellationToken cancellationToken = default)
    {
        if (!Available || cancellationToken.IsCancellationRequested) return null;
        try
        {
            var text = Platform.RecognizeSpeechWithCancellationAsync is { } recognizeCancelable
                ? await recognizeCancelable(cancellationToken)
                : Platform.RecognizeSpeechAsync is { } recognize
                    ? await recognize().WaitAsync(cancellationToken)
                    : null;
            return cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(text)
                ? null : text.Trim().TrimEnd('.', '!', '?').TrimEnd();
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

}

/// <summary>
/// Cycle de vie d'un microphone lié à un champ : un clic démarre, un second annule.
/// Aucune ancienne transcription ne doit modifier la recherche après l'arrêt.
/// </summary>
public sealed class VoiceCaptureSession
{
    private CancellationTokenSource? active;

    public bool IsListening => active is not null;

    /// <summary>
    /// Le clic d'arrêt rend le bouton immédiatement réutilisable, sans attendre que
    /// PowerShell termine la libération du microphone. Un ancien moteur en retard
    /// ne peut jamais remplir la nouvelle recherche ni modifier son état.
    /// </summary>
    public Task ToggleAsync(Action<string> accept, Action<bool>? listeningChanged = null)
    {
        if (active is not null)
        {
            Cancel(listeningChanged);
            return Task.CompletedTask;
        }
        if (!VoiceInput.Available) return Task.CompletedTask;

        var source = new CancellationTokenSource();
        active = source;
        listeningChanged?.Invoke(true);
        return CaptureAsync(source, accept, listeningChanged);
    }

    private async Task CaptureAsync(CancellationTokenSource source, Action<string> accept,
        Action<bool>? listeningChanged)
    {
        try
        {
            var text = await VoiceInput.ListenAsync(source.Token);
            if (ReferenceEquals(active, source) && !source.IsCancellationRequested && text is not null)
                accept(text);
        }
        finally
        {
            // Si une autre écoute a démarré entre-temps, la précédente ne touche
            // pas au micro, au tooltip ou au texte de la nouvelle session.
            if (ReferenceEquals(active, source))
            {
                active = null;
                listeningChanged?.Invoke(false);
            }
            source.Dispose();
        }
    }

    public void Cancel(Action<bool>? listeningChanged = null)
    {
        var source = active;
        if (source is null) return;
        active = null;
        listeningChanged?.Invoke(false);
        // L'arrêt du véritable processus Windows se fait hors du fil UI.
        // Le champ est immédiatement libéré, même si le système met un instant à fermer le micro.
        _ = CancelSourceAsync(source);
    }

    private static async Task CancelSourceAsync(CancellationTokenSource source)
    {
        try { await source.CancelAsync(); }
        catch (ObjectDisposedException) { } // La dictée vient naturellement de se terminer.
    }
}

/// <summary>
/// Micro ajouté à tout champ de recherche en pilule (classe « search ») : un toucher dicte le texte, comme la barre
/// de l'accueil. Rien n'est ajouté sur un appareil sans reconnaissance vocale, donc rien sur l'ordinateur.
/// </summary>
public sealed class VoiceField : Avalonia.AvaloniaObject
{
    public static readonly Avalonia.AttachedProperty<bool> EnabledProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<VoiceField, Avalonia.Controls.TextBox, bool>("Enabled");

    static VoiceField()
    {
        EnabledProperty.Changed.AddClassHandler<Avalonia.Controls.TextBox>((box, e) =>
        {
            if (e.NewValue is not true || !VoiceInput.Available || box.InnerRightContent is not null) return;
            var mic = new Avalonia.Controls.Button
            {
                Content = new Material.Icons.Avalonia.MaterialIcon { Kind = Material.Icons.MaterialIconKind.Microphone },
                Width = 38,
                Height = 38,
                Margin = new Avalonia.Thickness(0, 0, 2, 0),
                Focusable = false,
            };
            mic.Classes.Add("baricon");
            mic.Classes.Add("mic");
            Avalonia.Controls.ToolTip.SetTip(mic, "Dicter la recherche");
            var dictation = new VoiceCaptureSession();
            mic.Click += (_, _) => _ = dictation.ToggleAsync(text =>
            {
                if (box.IsEnabled)
                {
                    box.Text = text;
                    box.CaretIndex = text.Length;
                }
            }, isListening =>
            {
                if (isListening) mic.Classes.Add("listening");
                else mic.Classes.Remove("listening");
                if (mic.Content is Material.Icons.Avalonia.MaterialIcon icon)
                    icon.Kind = isListening ? Material.Icons.MaterialIconKind.Stop : Material.Icons.MaterialIconKind.Microphone;
                Avalonia.Controls.ToolTip.SetTip(mic, isListening ? "Arrêter l'écoute" : "Dicter la recherche");
            });
            box.InnerRightContent = mic;
        });
    }

    public static bool GetEnabled(Avalonia.Controls.TextBox box) => box.GetValue(EnabledProperty);
    public static void SetEnabled(Avalonia.Controls.TextBox box, bool value) => box.SetValue(EnabledProperty, value);
}
