using System.Runtime.InteropServices;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Événements qui peuvent faire un son, chacun réglable dans Paramètres, Sons.</summary>
public enum SoundKind { Success, Error, Notification, Opening }

/// <summary>
/// Sons de SCOLIA : de courtes notes générées par le logiciel, au volume choisi, ou les sons de Windows.
/// Rien ne joue si les sons sont coupés, ou hors de Windows. Deux sons trop rapprochés n'en font qu'un.
/// </summary>
public static class Sounds
{
    private const int Rate = 22050;
    private static readonly Dictionary<(SoundKind, string, int), GCHandle> Cache = [];
    private static DateTime last = DateTime.MinValue;

    public static void Play(Preferences prefs, SoundKind kind, bool force = false)
    {
        if (!force && (!prefs.Sounds || !prefs.SoundFor(kind))) return;
        if (prefs.SoundVolume <= 0) return;
        // Téléphone : les mêmes notes, jouées par le haut-parleur d'Android ; les sons de Windows n'y existent pas.
        if (Platform.PlayWave is { } playWave)
        {
            var at = DateTime.UtcNow;
            if (!force && at - last < TimeSpan.FromMilliseconds(500)) return;
            last = at;
            try { playWave(Wave(kind, prefs.SoundStyle == "systeme" ? "doux" : prefs.SoundStyle, prefs.SoundVolume / 100.0)); } catch (Exception) { }
            return;
        }
        if (!OperatingSystem.IsWindows()) return;
        var now = DateTime.UtcNow;
        if (!force && now - last < TimeSpan.FromMilliseconds(500)) return;
        last = now;
        try
        {
            if (prefs.SoundStyle == "systeme")
            {
                MessageBeep(kind switch { SoundKind.Error => 0x10u, SoundKind.Notification => 0x40u, _ => 0u });
                return;
            }
            var key = (kind, prefs.SoundStyle, prefs.SoundVolume);
            if (!Cache.TryGetValue(key, out var handle))
            {
                // Le son reste épinglé en mémoire : Windows le lit pendant que SCOLIA continue.
                handle = GCHandle.Alloc(Wave(kind, prefs.SoundStyle, prefs.SoundVolume / 100.0), GCHandleType.Pinned);
                Cache[key] = handle;
            }
            PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, 0x0004 | 0x0001 | 0x0002);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException) { }
    }

    /// <summary>Notes de chaque son : montante pour une réussite, descendante pour une erreur, une seule pour un message.</summary>
    private static (double Hz, double Seconds)[] Notes(SoundKind kind) => kind switch
    {
        SoundKind.Success => [(659.3, 0.09), (880.0, 0.16)],
        SoundKind.Error => [(440.0, 0.12), (329.6, 0.2)],
        SoundKind.Opening => [(523.3, 0.08), (659.3, 0.08), (784.0, 0.2)],
        _ => [(1046.5, 0.14)],
    };

    /// <summary>Fichier WAV en mémoire, 16 bits mono. Le style « clair » ajoute une harmonique pour un timbre de clochette.</summary>
    public static byte[] Wave(SoundKind kind, string style, double volume)
    {
        var samples = new List<short>();
        foreach (var (hz, seconds) in Notes(kind))
        {
            var count = (int)(Rate * seconds);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / Rate;
                var envelope = Math.Min(1, i / (Rate * 0.004)) * Math.Exp(-t * (style == "clair" ? 14 : 9));
                var wave = Math.Sin(2 * Math.PI * hz * t);
                if (style == "clair") wave = 0.7 * wave + 0.3 * Math.Sin(2 * Math.PI * hz * 2.01 * t);
                samples.Add((short)(wave * envelope * volume * 0.5 * short.MaxValue));
            }
        }
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        var data = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return stream.ToArray();
    }

    [DllImport("winmm.dll")]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);
}
