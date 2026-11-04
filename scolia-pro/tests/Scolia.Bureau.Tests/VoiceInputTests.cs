using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Recherche à la voix : le micro n'apparaît qu'avec un service vocal, et le texte dicté arrive propre.</summary>
[Collection("Plateforme")]
public sealed class VoiceInputTests
{
    [Fact]
    public async Task Sans_service_vocal_pas_de_micro_et_rien_d_entendu()
    {
        var (recognize, can) = (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech);
        try
        {
            Platform.RecognizeSpeechAsync = null;
            Platform.CanRecognizeSpeech = null;
            Assert.False(VoiceInput.Available);
            Assert.Null(await VoiceInput.ListenAsync());

            Platform.RecognizeSpeechAsync = () => Task.FromResult<string?>("Kabeya");
            Platform.CanRecognizeSpeech = () => false;
            Assert.False(VoiceInput.Available);
        }
        finally { (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech) = (recognize, can); }
    }

    [Theory]
    [InlineData("  Kabeya Ruth. ", "Kabeya Ruth")]
    [InlineData("reçu 345 ?", "reçu 345")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task Le_texte_dicte_arrive_sans_ponctuation_finale(string? heard, string? expected)
    {
        var (recognize, can) = (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech);
        try
        {
            Platform.RecognizeSpeechAsync = () => Task.FromResult(heard);
            Platform.CanRecognizeSpeech = () => true;
            Assert.True(VoiceInput.Available);
            Assert.Equal(expected, await VoiceInput.ListenAsync());
        }
        finally { (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech) = (recognize, can); }
    }

    [Fact]
    public async Task Deuxieme_clic_arrete_le_micro_et_un_nouveau_clic_redemarre()
    {
        var oldRecognition = Platform.RecognizeSpeechAsync;
        var oldCancelable = Platform.RecognizeSpeechWithCancellationAsync;
        var oldAvailable = Platform.CanRecognizeSpeech;
        try
        {
            var launched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Platform.RecognizeSpeechAsync = null;
            Platform.CanRecognizeSpeech = () => true;
            Platform.RecognizeSpeechWithCancellationAsync = async token =>
            {
                launched.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "Ne doit pas apparaître";
            };
            var mic = new VoiceCaptureSession();
            var query = "Nom déjà recherché";
            var changes = new List<bool>();
            var firstClick = mic.ToggleAsync(text => query = text, isListening => changes.Add(isListening));

            await launched.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(mic.IsListening);
            await mic.ToggleAsync(text => query = text, isListening => changes.Add(isListening));

            Assert.False(mic.IsListening);
            await firstClick.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Nom déjà recherché", query);
            Assert.Equal(true, changes[0]);
            Assert.Contains(false, changes);

            Platform.RecognizeSpeechWithCancellationAsync = _ => Task.FromResult<string?>("Deuxième recherche");
            await mic.ToggleAsync(text => query = text);
            Assert.Equal("Deuxième recherche", query);
            Assert.False(mic.IsListening);
        }
        finally
        {
            Platform.RecognizeSpeechAsync = oldRecognition;
            Platform.RecognizeSpeechWithCancellationAsync = oldCancelable;
            Platform.CanRecognizeSpeech = oldAvailable;
        }
    }

    [Fact]
    public async Task Un_resultat_arrive_apres_arret_ne_modifie_jamais_le_champ()
    {
        var oldRecognition = Platform.RecognizeSpeechAsync;
        var oldCancelable = Platform.RecognizeSpeechWithCancellationAsync;
        var oldAvailable = Platform.CanRecognizeSpeech;
        try
        {
            var heardLater = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Platform.RecognizeSpeechAsync = null;
            Platform.CanRecognizeSpeech = () => true;
            // Simule un moteur qui rend une dernière phrase après que l'utilisateur a cliqué sur Arrêter.
            Platform.RecognizeSpeechWithCancellationAsync = _ => heardLater.Task;
            var mic = new VoiceCaptureSession();
            var query = "Recherche existante";
            var work = mic.ToggleAsync(text => query = text);
            Assert.True(mic.IsListening);

            await mic.ToggleAsync(text => query = text);
            Assert.False(mic.IsListening);
            heardLater.SetResult("Ancienne dictée");
            await work.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Recherche existante", query);
        }
        finally
        {
            Platform.RecognizeSpeechAsync = oldRecognition;
            Platform.RecognizeSpeechWithCancellationAsync = oldCancelable;
            Platform.CanRecognizeSpeech = oldAvailable;
        }
    }

    [Fact]
    public async Task Le_service_windows_ne_demarre_jamais_sur_un_autre_systeme()
    {
        if (OperatingSystem.IsWindows()) return; // Exige un microphone et un moteur fr-FR pour le vrai test Windows.
        Assert.False(WindowsSpeechInput.IsAvailable());
        Assert.Null(await WindowsSpeechInput.ListenAsync());
    }

    [Fact]
    public async Task Une_panne_du_service_vocal_ne_casse_pas_la_recherche()
    {
        var (recognize, can) = (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech);
        try
        {
            Platform.RecognizeSpeechAsync = () => Task.FromException<string?>(new InvalidOperationException("micro occupé"));
            Platform.CanRecognizeSpeech = () => true;
            Assert.Null(await VoiceInput.ListenAsync());
        }
        finally { (Platform.RecognizeSpeechAsync, Platform.CanRecognizeSpeech) = (recognize, can); }
    }
}
