using Android.Content;
using Android.Media;
using Android.Net.Wifi;
using Android.Widget;
using Scolia.Bureau.Infrastructure;
using AndroidUri = Android.Net.Uri;

namespace Scolia.Mobile;

/// <summary>
/// Services d'Android fournis à SCOLIA : dossier des documents, ouverture d'un PDF ou d'un classeur avec l'application
/// du téléphone, liens WhatsApp, SMS et e-mail, sons, et recherche du serveur sur le Wi-Fi.
/// </summary>
public static class MobilePlatform
{
    private static Context? context;
    private static WifiManager.MulticastLock? multicast;

    public static void Prepare(Context app)
    {
        context = app;
        Platform.IsMobile = true;
        Preferences.MobileLayout = true;
        Platform.DeviceName = $"{Android.OS.Build.Manufacturer} {Android.OS.Build.Model}".Trim();
        Platform.DocumentsFolder = app.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments)?.AbsolutePath
            ?? Path.Combine(app.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "Documents");
        Platform.OpenFile = OpenFile;
        Platform.OpenUri = OpenUri;
        Platform.PlayWave = PlayWave;
        // Sans ordinateur dans l'établissement, ce téléphone peut en être le serveur.
        Platform.LocalServer = new Server.PhoneServer(app);
    }

    public static void Attach(Context activity) => context ??= activity.ApplicationContext ?? activity;

    /// <summary>Garde la réception des diffusions du Wi-Fi pendant que SCOLIA est à l'écran, et la rend ensuite.</summary>
    public static void HoldMulticast(bool hold)
    {
        try
        {
            if (hold)
            {
                if (multicast is null && context?.GetSystemService(Context.WifiService) is WifiManager wifi)
                {
                    multicast = wifi.CreateMulticastLock("scolia-reseau");
                    multicast?.SetReferenceCounted(false);
                }
                multicast?.Acquire();
            }
            else if (multicast?.IsHeld == true) multicast.Release();
        }
        catch (Java.Lang.SecurityException) { }
    }

    private static void OpenFile(string path)
    {
        if (context is not { } ctx) return;
        try
        {
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, $"{ctx.PackageName}.fichiers", new Java.IO.File(path));
            var view = new Intent(Intent.ActionView);
            view.SetDataAndType(uri, MimeOf(path));
            view.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            var chooser = Intent.CreateChooser(view, "Ouvrir avec");
            chooser!.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
            ctx.StartActivity(chooser);
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.IllegalArgumentException)
        {
            Toast.MakeText(ctx, "Aucune application du téléphone ne sait ouvrir ce document.", ToastLength.Long)?.Show();
        }
    }

    private static void OpenUri(string address)
    {
        if (context is not { } ctx) return;
        try
        {
            var intent = new Intent(Intent.ActionView, AndroidUri.Parse(address));
            intent.AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            Toast.MakeText(ctx, "Aucune application du téléphone ne sait ouvrir ce lien.", ToastLength.Long)?.Show();
        }
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".csv" => "text/csv",
        ".png" => "image/png",
        ".zip" => "application/zip",
        _ => "*/*",
    };

    /// <summary>Joue les notes de SCOLIA : un fichier WAV 16 bits mono, lu directement par le haut-parleur.</summary>
    private static void PlayWave(byte[] wav)
    {
        if (wav.Length <= 44) return;
        var rate = BitConverter.ToInt32(wav, 24);
        var pcm = wav.AsSpan(44).ToArray();
        var track = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.AssistanceSonification)!.SetContentType(AudioContentType.Sonification)!.Build()!)
            .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit)!.SetSampleRate(rate)!.SetChannelMask(ChannelOut.Mono)!.Build()!)
            .SetBufferSizeInBytes(pcm.Length)
            .SetTransferMode(AudioTrackMode.Static)
            .Build();
        track.Write(pcm, 0, pcm.Length);
        track.Play();
        var duration = TimeSpan.FromSeconds(pcm.Length / 2.0 / rate) + TimeSpan.FromMilliseconds(300);
        _ = Task.Delay(duration).ContinueWith(_ => { track.Stop(); track.Release(); }, TaskScheduler.Default);
    }
}
