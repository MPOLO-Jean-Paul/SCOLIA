using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using AndroidX.Work;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using AndroidUri = Android.Net.Uri;

namespace Scolia.Mobile;

/// <summary>
/// Mise à jour des APK SCOLIA publiés sur GitHub Releases. Le contrôle peut être différé par Android.
/// Aucun téléchargement sans consentement : l'utilisateur approuve le téléchargement (taille affichée),
/// puis Android demande son autorisation pour installer la mise à jour.
/// </summary>
public static class AndroidAppUpdates
{
    private const string LatestRelease = "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases/latest";
    private const string ReleasesPath = "/MPOLO-Jean-Paul/SCOLIA/releases/download/";
    private const string Store = "scolia-updates";
    private const string ReleaseKey = "release";
    private const string LastCheckKey = "last-check-utc";
    private const string DownloadIdKey = "download-id";
    private const string FileNameKey = "file-name";
    private const string DigestKey = "sha256";
    private const string DownloadVersionKey = "download-version";
    private const string NotifiedKey = "notified-version";
    private const string Channel = "scolia-mises-a-jour";
    private const int NotificationId = 39124;
    private const int DownloadSuccessStatus = 8; // DownloadManager.STATUS_SUCCESSFUL
    private const int DownloadFailureStatus = 16; // DownloadManager.STATUS_FAILED
    private const long MaxApkBytes = 350L * 1024 * 1024;
    private const long CheckSeconds = 6 * 60 * 60;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static readonly SemaphoreSlim VerificationGate = new(1, 1);
    private static string? promptedVersion;
    private static bool waitingForInstallPermission;

    internal sealed record Release(string Version, string DownloadUrl, string Sha256, long Size);

    private static ISharedPreferences Preferences(Context context) =>
        context.GetSharedPreferences(Store, FileCreationMode.Private)!;

    public static void Schedule(Context context)
    {
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected)!.Build();
        var periodic = new PeriodicWorkRequest.Builder(
                typeof(AndroidAppUpdateWorker), 6, Java.Util.Concurrent.TimeUnit.Hours!)
            .SetConstraints(constraints)!.Build();
        WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
            "scolia-apk-updates", ExistingPeriodicWorkPolicy.Keep!, periodic);
    }

    public static void OnResume(Activity activity)
    {
        _ = ResumeAsync(activity);
    }

    private static async Task ResumeAsync(Activity activity)
    {
        try
        {
            // Une installation en attente a priorité sur une nouvelle recherche réseau.
            if (await CheckDownloadAsync(activity)) return;
            var prefs = Preferences(activity);
            var cached = Deserialize(prefs.GetString(ReleaseKey, null));
            if (cached is not null && IsNewer(activity, cached.Version))
            {
                OfferUpdate(activity, cached);
                return;
            }
            await CheckLatestAsync(activity, showDialog: true);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException
            or TaskCanceledException or Java.Lang.Exception or InvalidOperationException)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Vérification reportée : {ex.GetType().Name} {ex.Message}");
        }
    }

    /// <summary>Peut être appelée par l'interface ou le worker. Ne télécharge jamais un APK seule.</summary>
    internal static async Task<bool> CheckLatestAsync(Context context, bool showDialog)
    {
        if (!await CheckGate.WaitAsync(0)) return false;
        try
        {
            var prefs = Preferences(context);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now - prefs.GetLong(LastCheckKey, 0) < CheckSeconds)
            {
                var cached = Deserialize(prefs.GetString(ReleaseKey, null));
                if (cached is null || !IsNewer(context, cached.Version)) return false;
                if (showDialog && context is Activity shown) OfferUpdate(shown, cached);
                return true;
            }

            var release = await ReadReleaseAsync(context);
            // Ne pas saturer GitHub à chaque reprise d'activité ; en cas d'erreur réseau on réessaie.
            prefs.Edit()!.PutLong(LastCheckKey, now)!.Apply();
            if (release is null)
            {
                prefs.Edit()!.Remove(ReleaseKey)!.Apply();
                return false;
            }

            prefs.Edit()!.PutString(ReleaseKey, JsonSerializer.Serialize(release))!.Apply();
            if (showDialog && context is Activity screen) OfferUpdate(screen, release);
            else NotifyOnce(context, release);
            return true;
        }
        finally { CheckGate.Release(); }
    }

    private static async Task<Release?> ReadReleaseAsync(Context context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.ParseAdd("SCOLIA-Pro-Android/1.2");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagValue)) return null;
        var tag = tagValue.GetString() ?? "";
        var version = Regex.Match(tag, @"(?<!\d)(\d+)\.(\d+)\.(\d+)(?!\d)");
        if (!version.Success || !IsNewer(context, version.Value)) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!Regex.IsMatch(name, @"^SCOLIA_Pro_\d+\.\d+\.\d+_Android\.apk$")) continue;
            var digest = asset.TryGetProperty("digest", out var hashValue) ? hashValue.GetString() ?? "" : "";
            if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) continue; // refuser un fichier invérifiable
            long size = asset.GetProperty("size").GetInt64();
            if (size < 1024 * 1024 || size > MaxApkBytes) continue;
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!TrustedDownload(url)) continue;
            return new Release(version.Value, url, digest[7..].ToUpperInvariant(), size);
        }
        return null;
    }

    private static bool TrustedDownload(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith(ReleasesPath, StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith(".apk", StringComparison.Ordinal);

    private static bool IsNewer(Context context, string candidate)
    {
        var v = Version.TryParse(candidate, out var parsed) ? parsed : null;
        var installedName = context.PackageManager?.GetPackageInfo(context.PackageName!, 0)?.VersionName;
        var installed = Version.TryParse(installedName, out var current) ? current : null;
        return v is not null && installed is not null && v.CompareTo(installed) > 0;
    }

    private static Release? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var release = JsonSerializer.Deserialize<Release>(json);
            return release is not null && TrustedDownload(release.DownloadUrl)
                && Regex.IsMatch(release.Sha256, "^[a-fA-F0-9]{64}$")
                && release.Size is > 1048576 and < MaxApkBytes ? release : null;
        }
        catch (JsonException) { return null; }
    }

    private static void OfferUpdate(Activity activity, Release release)
    {
        if (activity.IsFinishing || activity.IsDestroyed || promptedVersion == release.Version) return;
        promptedVersion = release.Version;
        activity.RunOnUiThread(() =>
        {
            if (activity.IsFinishing || activity.IsDestroyed) return;
            var mb = release.Size / (1024.0 * 1024.0);
            new AlertDialog.Builder(activity)
                .SetTitle($"SCOLIA Pro {release.Version} disponible")
                .SetMessage($"Une mise à jour est disponible.\nTaille : {mb:F1} Mo.\n\nTélécharger maintenant avec Android ? Les données scolaires existantes seront conservées si la signature de l'application est identique.")
                .SetPositiveButton("Télécharger", (_, _) => StartDownload(activity, release))
                .SetNegativeButton("Plus tard", (_, _) => { })
                .Show();
        });
    }

    private static string UpdateFileName(string version) => $"SCOLIA-MiseAJour-{version}.apk";

    private static string? UpdatePath(Context context, string filename)
    {
        if (!Regex.IsMatch(filename, @"^SCOLIA-MiseAJour-\d+\.\d+\.\d+\.apk$")) return null;
        var folder = context.GetExternalFilesDir(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
        return folder is null ? null : Path.Combine(folder, filename);
    }

    private static void StartDownload(Activity activity, Release release)
    {
        try
        {
            var filename = UpdateFileName(release.Version);
            var path = UpdatePath(activity, filename)
                ?? throw new IOException("Dossier de téléchargement Android introuvable.");
            if (File.Exists(path)) File.Delete(path);
            var downloadManager = (DownloadManager?)activity.GetSystemService(Context.DownloadService)
                ?? throw new IOException("Le gestionnaire de téléchargement n'est pas disponible.");
            var request = new DownloadManager.Request(AndroidUri.Parse(release.DownloadUrl));
            request.SetTitle($"SCOLIA Pro {release.Version}");
            request.SetDescription("Mise à jour officielle — installation après vérification");
            request.SetMimeType("application/vnd.android.package-archive");
            request.SetNotificationVisibility(DownloadVisibility.VisibleNotifyCompleted);
            request.SetAllowedOverMetered(true);
            request.SetDestinationInExternalFilesDir(activity, Android.OS.Environment.DirectoryDownloads, filename);
            var id = downloadManager.Enqueue(request);
            Preferences(activity).Edit()!
                .PutLong(DownloadIdKey, id)!
                .PutString(FileNameKey, filename)!
                .PutString(DigestKey, release.Sha256)!
                .PutString(DownloadVersionKey, release.Version)!
                .Apply();
            Toast.MakeText(activity, "Téléchargement commencé. Android affiche sa progression dans les notifications.", ToastLength.Long)?.Show();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or InvalidOperationException)
        {
            Toast.MakeText(activity, $"Téléchargement impossible : {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private static async Task<bool> CheckDownloadAsync(Activity activity)
    {
        var pref = Preferences(activity);
        var id = pref.GetLong(DownloadIdKey, 0);
        if (id <= 0) return false;
        var downloadManager = activity.GetSystemService(Context.DownloadService) as DownloadManager;
        if (downloadManager is null) return false;
        var query = new DownloadManager.Query().SetFilterById(id);
        using var cursor = downloadManager.InvokeQuery(query);
        if (cursor is null || !cursor.MoveToFirst()) return false;
        var statusIndex = cursor.GetColumnIndex(DownloadManager.ColumnStatus);
        if (statusIndex < 0) return false;
        var status = cursor.GetInt(statusIndex);
        if (status == DownloadFailureStatus)
        {
            pref.Edit()!.Remove(DownloadIdKey)!.Apply();
            Toast.MakeText(activity, "La mise à jour a échoué. Vérifiez la connexion ou l'espace libre et réessayez.", ToastLength.Long)?.Show();
            return false;
        }
        if (status != DownloadSuccessStatus) return true;

        if (!await VerificationGate.WaitAsync(0)) return true;
        try
        {
            var file = pref.GetString(FileNameKey, null) ?? "";
            var path = UpdatePath(activity, file);
            var hash = pref.GetString(DigestKey, "") ?? "";
            var version = pref.GetString(DownloadVersionKey, "") ?? "";
            if (path is null || !File.Exists(path) || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"))
            {
                ResetPending(activity);
                return false;
            }
            var valid = await Task.Run(() => VerifyDownloadedApk(activity, path, hash));
            if (!valid)
            {
                try { File.Delete(path); } catch (IOException) { }
                ResetPending(activity);
                Toast.MakeText(activity, "Mise à jour refusée : APK incorrect, non signé comme attendu ou version obsolète.", ToastLength.Long)?.Show();
                return false;
            }
            if (promptedVersion == "installer-" + version && !waitingForInstallPermission) return true;
            promptedVersion = "installer-" + version;
            activity.RunOnUiThread(() =>
                new AlertDialog.Builder(activity)
                    .SetTitle("Mise à jour téléchargée et vérifiée")
                    .SetMessage($"SCOLIA Pro {version} est prêt. Android va demander votre accord pour installer l'APK.")
                    .SetPositiveButton("Installer", (_, _) => OpenSystemInstaller(activity, path))
                    .SetNegativeButton("Plus tard", (_, _) => { })
                    .Show());
            return true;
        }
        finally { VerificationGate.Release(); }
    }

    private static bool VerifyDownloadedApk(Context context, string path, string digest)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 1024 * 1024 || fs.Length > MaxApkBytes) return false;
            var actual = Convert.ToHexString(SHA256.HashData(fs));
            if (!actual.Equals(digest, StringComparison.OrdinalIgnoreCase)) return false;
            var packageManager = context.PackageManager;
            var archive = packageManager?.GetPackageArchiveInfo(path, 0);
            var current = packageManager?.GetPackageInfo(context.PackageName!, 0);
            if (archive?.PackageName != context.PackageName || current is null) return false;
            long candidateCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? archive.LongVersionCode : archive.VersionCode;
            long currentCode = OperatingSystem.IsAndroidVersionAtLeast(28) ? current.LongVersionCode : current.VersionCode;
            return candidateCode > currentCode; // Android vérifie également que le certificat de signature reste identique.
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.Exception or UnauthorizedAccessException)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Vérification APK : {ex.GetType().Name}");
            return false;
        }
    }

    private static void OpenSystemInstaller(Activity activity, string path)
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26) &&
                activity.PackageManager?.CanRequestPackageInstalls() == false)
            {
                waitingForInstallPermission = true;
                var permission = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                    AndroidUri.Parse($"package:{activity.PackageName}"));
                activity.StartActivity(permission);
                return;
            }
            waitingForInstallPermission = false;
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity,
                $"{activity.PackageName}.fichiers", new Java.IO.File(path));
            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            activity.StartActivity(intent); // confirmation explicite Android, jamais installation invisible
        }
        catch (Exception ex) when (ex is ActivityNotFoundException or Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
        {
            Toast.MakeText(activity, $"Android n'a pas pu ouvrir l'installateur : {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private static void ResetPending(Context ctx) =>
        Preferences(ctx).Edit()!.Remove(DownloadIdKey)!.Remove(FileNameKey)!
            .Remove(DigestKey)!.Remove(DownloadVersionKey)!.Apply();

    private static void NotifyOnce(Context context, Release release)
    {
        var prefs = Preferences(context);
        if (prefs.GetString(NotifiedKey, null) == release.Version) return;
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(
                Channel, "Mises à jour SCOLIA Pro", NotificationImportance.Default));
        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? "");
        if (open is null) return;
        open.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        var action = PendingIntent.GetActivity(context, 0, open,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, Channel) : new Notification.Builder(context);
        builder.SetSmallIcon(Resource.Mipmap.icon)
            .SetContentTitle($"SCOLIA Pro {release.Version} disponible")
            .SetContentText("Touchez pour télécharger la mise à jour depuis SCOLIA.")
            .SetContentIntent(action)
            .SetAutoCancel(true);
        manager.Notify(NotificationId, builder.Build());
        prefs.Edit()!.PutString(NotifiedKey, release.Version)!.Apply();
    }
}

/// <summary>Vérifie les mises à jour à intervalles espacés lorsque le réseau est disponible.</summary>
[Android.Runtime.Register("com/jpservices/scolia/AppUpdateWorker")]
public sealed class AndroidAppUpdateWorker : Worker
{
    public AndroidAppUpdateWorker(Context context, WorkerParameters parameters) : base(context, parameters) { }

    public override Result DoWork()
    {
        try
        {
            AndroidAppUpdates.CheckLatestAsync(ApplicationContext, showDialog: false)
                .GetAwaiter().GetResult();
            return Result.InvokeSuccess();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        { return Result.InvokeRetry(); }
        catch (Exception ex) when (ex is Java.Lang.Exception or JsonException or InvalidOperationException)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Mises à jour différées : {ex.Message}");
            return Result.InvokeFailure();
        }
    }
}
