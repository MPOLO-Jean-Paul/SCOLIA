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
