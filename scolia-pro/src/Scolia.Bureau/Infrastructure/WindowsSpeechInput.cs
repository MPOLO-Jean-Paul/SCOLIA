using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Dictée vocale locale sous Windows, déclenchée uniquement par le bouton micro.
/// Utilise le moteur de dictée du système, sans service cloud ni nouvelle dépendance NuGet.
/// Aucun texte de recherche ne devient une commande système.
/// </summary>
public static class WindowsSpeechInput
{
    // Processus système, chemin fixe : aucune commande et aucun paramètre ne vient de l'utilisateur.
    private const string SpeechScript = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        Add-Type -AssemblyName System.Speech
        $recognizers = [System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers()
        $recognizer = $recognizers | Where-Object { $_.Culture.Name -eq 'fr-FR' } | Select-Object -First 1
        if ($null -eq $recognizer) { exit 3 }
        $engine = [System.Speech.Recognition.SpeechRecognitionEngine]::new($recognizer)
        try {
            $engine.LoadGrammar([System.Speech.Recognition.DictationGrammar]::new())
            $engine.SetInputToDefaultAudioDevice()
            $result = $engine.Recognize([TimeSpan]::FromSeconds(10))
            if ($null -ne $result -and $result.Confidence -ge 0.30) {
                [Console]::WriteLine($result.Text)
            }
        }
        finally { $engine.Dispose() }
        """;

    private static readonly SemaphoreSlim MicrophoneGate = new(1, 1);

    private static string PowerShellPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

    public static bool IsAvailable() =>
        OperatingSystem.IsWindows() && File.Exists(PowerShellPath);

    public static Task<string?> ListenAsync() => ListenAsync(CancellationToken.None);

    public static async Task<string?> ListenAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable() || cancellationToken.IsCancellationRequested) return null;
        // Après « Arrêter » puis « Recommencer », attendre que Windows ait rendu
        // le micro au lieu d'abandonner silencieusement la nouvelle dictée.
        // L'attente elle-même reste annulable par un autre clic.
        bool entered;
        try { entered = await MicrophoneGate.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        if (!entered) return null;
        try
        {
            return await RecognizeOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { MicrophoneGate.Release(); }
    }

    private static async Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(SpeechScript)));
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(19));
        var started = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            started = process.Start();
            if (!started) return null;
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var phrase = (await stdout.ConfigureAwait(false)).Trim();
            _ = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0 || phrase.Length == 0 || phrase.Length > 300) return null;
            // Dictée uniquement : pas de lancement de commandes, pas de modification des données.
            return phrase;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or
            Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            if (started)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                // Ne libérer le microphone que lorsque le processus PowerShell est réellement arrêté.
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(stopTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
            return null;
        }
    }
}
