using Avalonia;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Saisie à la voix des recherches : le même micro dans chaque champ de recherche, comme dans Google. Le texte dicté
/// remplace la saisie et la recherche part aussitôt.
/// </summary>
public static class VoiceInput
{
    public static bool Available => Platform.RecognizeSpeechAsync is not null && (Platform.CanRecognizeSpeech?.Invoke() ?? true);

    /// <summary>Écoute une phrase ; rien si la personne annule ou si l'appareil n'a pas de reconnaissance vocale.</summary>
    public static async Task<string?> ListenAsync()
    {
        if (!Available || Platform.RecognizeSpeechAsync is not { } recognize) return null;
        try
        {
            var text = await recognize();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim().TrimEnd('.', '!', '?').TrimEnd();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException or IOException) { return null; }
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
            mic.Click += async (_, _) =>
            {
                mic.Classes.Add("listening");
                try
                {
                    if (await VoiceInput.ListenAsync() is { } text && box.IsEnabled)
                    {
                        box.Text = text;
                        box.CaretIndex = text.Length;
                    }
                }
                finally { mic.Classes.Remove("listening"); }
            };
            box.InnerRightContent = mic;
        });
    }

    public static bool GetEnabled(Avalonia.Controls.TextBox box) => box.GetValue(EnabledProperty);
    public static void SetEnabled(Avalonia.Controls.TextBox box, bool value) => box.SetValue(EnabledProperty, value);
}
