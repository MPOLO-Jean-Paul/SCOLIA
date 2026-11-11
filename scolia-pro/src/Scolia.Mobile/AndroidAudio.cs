using Android.Media;
using Android.Util;

namespace Scolia.Mobile;

/// <summary>
/// Sons des événements SCOLIA sur le volume multimédia Android.
/// La lecture PCM s'effectue hors du fil graphique et le lecteur reste vivant
/// jusqu'à la fin du son (l'ancienne lecture statique libérait parfois la piste trop tôt).
/// </summary>
internal static class AndroidAudio
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void PlayWave(byte[] wav)
    {
        if (wav.Length < 46 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return;
        var sampleRate = BitConverter.ToInt32(wav, 24);
        if (sampleRate is < 8000 or > 48000 || BitConverter.ToInt16(wav, 22) != 1
            || BitConverter.ToInt16(wav, 34) != 16) return;
        var pcm = wav.AsSpan(44).ToArray();
        if ((pcm.Length & 1) != 0 || pcm.Length == 0) return;

        // Ne jamais bloquer le clavier, les menus ni les transitions d'écran.
        _ = Task.Run(async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try { await PlayPcmAsync(pcm, sampleRate).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn("SCOLIA-Audio", $"Lecture impossible : {ex.Message}"); }
            finally { Gate.Release(); }
        });
    }

    private static async Task PlayPcmAsync(byte[] pcm, int sampleRate)
    {
        var minimum = AudioTrack.GetMinBufferSize(sampleRate, ChannelOut.Mono, Encoding.Pcm16bit);
        if (minimum <= 0)
        {
            Log.Warn("SCOLIA-Audio", "Format de sortie audio non disponible sur cet appareil.");
            return;
        }
        using var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build();
        using var format = new AudioFormat.Builder()
            .SetSampleRate(sampleRate)!.SetEncoding(Encoding.Pcm16bit)!
            .SetChannelMask(ChannelOut.Mono)!.Build();
        using var track = new AudioTrack.Builder()
            .SetAudioAttributes(attributes)!.SetAudioFormat(format)!
            .SetBufferSizeInBytes(Math.Max(minimum, 4096))!
            .SetTransferMode(AudioTrackMode.Stream)!.Build();

        if (track is null) return;
        try
        {
            track.Play();
            var index = 0;
            while (index < pcm.Length)
            {
                var written = track.Write(pcm, index, pcm.Length - index);
                if (written <= 0) throw new InvalidOperationException($"Écriture PCM Android : {written}");
                index += written;
            }
            // Conserver la piste pendant la lecture du tampon système.
            await Task.Delay(TimeSpan.FromSeconds(pcm.Length / (2.0 * sampleRate))
                + TimeSpan.FromMilliseconds(90)).ConfigureAwait(false);
        }
        finally
        {
            try { track.Stop(); } catch (Java.Lang.IllegalStateException) { }
            track.Release();
        }
    }
}
