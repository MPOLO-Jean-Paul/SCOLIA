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
    private const string DownloadStartedAtKey = "download-started-at";
    private const string LastStatusKey = "last-check-result";
    private const string NotifiedKey = "notified-version";
    private const string Channel = "scolia-mises-a-jour";
    private const string InstallChannel = "scolia-installation";
    private const int NotificationId = 39124;
    private const int InstallNotificationId = 39125;
    private const int DownloadSuccessStatus = UpdateCompletionPolicy.Successful;
    private const int DownloadFailureStatus = UpdateCompletionPolicy.Failed;
    private const long MaxApkBytes = 350L * 1024 * 1024;
    // Le contrôle en ouvrant SCOLIA doit voir une publication récente ; WorkManager
    // reste à 6 h pour économiser la batterie hors de l'application.
    // Les ouvertures contrôlent une publication fraîche sans attendre quinze minutes.
    // WorkManager conserve un intervalle de six heures pour la batterie.
    private const long CheckSeconds = 2 * 60;
    private const string DeferredVersionKey = "deferred-version";
    private const string DeferredUntilKey = "deferred-until";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly SemaphoreSlim CheckGate = new(1, 1);
    private static readonly SemaphoreSlim VerificationGate = new(1, 1);
    private static string? promptedVersion;
    private static bool waitingForInstallPermission;
    private static bool installDialogOpen;
    private static WeakReference<Activity>? foregroundActivity;
    private static CancellationTokenSource? foregroundDownloadWatch;

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
        foregroundActivity = new WeakReference<Activity>(activity);
        // Le broadcast Android est utile, mais n'est pas la seule source de vérité :
        // en cas de signal perdu, un contrôle léger du statut déclenche la fenêtre Installer.
        if (Preferences(activity).GetLong(DownloadIdKey, 0) > 0)
            StartForegroundDownloadWatch(activity);
        _ = ResumeAsync(activity);
    }

    public static void OnPause(Activity activity)
    {
        if (foregroundActivity?.TryGetTarget(out var shown) == true && ReferenceEquals(shown, activity))
        {
            foregroundActivity = null;
            StopForegroundDownloadWatch();
        }
        // Un dialogue disparu avec l'activité ne doit jamais bloquer
        // les prochaines propositions de téléchargement ou d'installation.
        installDialogOpen = false;
        promptedVersion = null;
    }

    private static bool IsForeground(Activity activity) =>
        foregroundActivity?.TryGetTarget(out var shown) == true
        && ReferenceEquals(shown, activity)
        && !activity.IsFinishing && !activity.IsDestroyed;

    private static void StopForegroundDownloadWatch()
    {
        var previous = foregroundDownloadWatch;
        foregroundDownloadWatch = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private static void StartForegroundDownloadWatch(Activity activity)
    {
        StopForegroundDownloadWatch();
        var source = new CancellationTokenSource();
        foregroundDownloadWatch = source;
        _ = WatchDownloadAsync(activity, source.Token);
    }

    private static int? DownloadStatus(Context context, long id)
    {
        var manager = context.GetSystemService(Context.DownloadService) as DownloadManager;
        if (manager is null) return null;
        using var cursor = manager.InvokeQuery(new DownloadManager.Query().SetFilterById(id));
        if (cursor is null || !cursor.MoveToFirst()) return null;
        var column = cursor.GetColumnIndex(DownloadManager.ColumnStatus);
        return column < 0 ? null : cursor.GetInt(column);
    }

    // Actif uniquement pendant le téléchargement et uniquement lorsque SCOLIA
    // est visible. Une fois terminé, vérification SHA-256 puis boîte de dialogue.
    private static async Task WatchDownloadAsync(Activity activity, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && IsForeground(activity))
            {
                var id = Preferences(activity).GetLong(DownloadIdKey, 0);
                if (id <= 0) return;
                var status = DownloadStatus(activity, id);
                if (UpdateCompletionPolicy.NeedsForegroundCheck(
                    IsForeground(activity), id, status))
                {
                    await CheckDownloadSafelyAsync(activity);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellation);
            }
        }
        catch (System.OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Suivi du téléchargement différé : {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Notification reçue au MOMENT précis où Android finit le téléchargement.
    /// Si SCOLIA est visible, vérifier l'APK et afficher « Installer » immédiatement.
    /// En arrière-plan, Android interdit d'ouvrir arbitrairement une activité :
    /// une notification persistante ramène l'utilisateur à la même proposition.
    /// </summary>
    public static void DownloadCompleted(Context context, long downloadId)
    {
        if (downloadId <= 0 || Preferences(context).GetLong(DownloadIdKey, 0) != downloadId) return;
        // DownloadManager notifie aussi les échecs. Seul STATUS_SUCCESSFUL
        // peut déclencher l'installation ; hors premier plan, notification.
        var status = DownloadStatus(context, downloadId);
        Activity? screen = null;
        if (foregroundActivity?.TryGetTarget(out var visible) == true && IsForeground(visible))
            screen = visible;
        var action = UpdateCompletionPolicy.Decide(status, screen is not null);
        switch (action)
        {
            case UpdateCompletionAction.DownloadFailed:
                Report(context, "Échec du téléchargement de la mise à jour.");
                if (screen is not null) _ = CheckDownloadSafelyAsync(screen);
                break;
            case UpdateCompletionAction.PresentInstall:
                Report(context, "Téléchargement terminé. Vérification de l'APK avant installation.");
                if (screen is not null) _ = CheckDownloadSafelyAsync(screen);
                break;
            case UpdateCompletionAction.NotifyReady:
                Report(context, "Téléchargement terminé. Installation proposée à la reprise de SCOLIA.");
                NotifyInstallReady(context);
                break;
        }
    }

    private static async Task CheckDownloadSafelyAsync(Activity activity)
    {
        try { await CheckDownloadAsync(activity); }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Ouverture de l'installation différée : {ex}");
            Report(activity, "L'installation est disponible depuis la notification ou au prochain retour dans SCOLIA.");
            NotifyInstallReady(activity);
        }
    }

    /// <summary>Etat lisible de la vérification, même si Android a désactivé les notifications.</summary>
    public static string LastStatus(Context context) =>
        Preferences(context).GetString(LastStatusKey, null) ??
        "Aucune recherche effectuée. Ouvrez Mon compte puis Rechercher les mises à jour.";

    private static void Report(Context context, string message)
    {
        Preferences(context).Edit()!.PutString(LastStatusKey,
            $"{DateTime.Now:HH:mm} · {message}")!.Apply();
        Android.Util.Log.Info("SCOLIA-MAJ", message);
    }

    /// <summary>Commande visible « Vérifier les mises à jour » : interroge GitHub sans cache.</summary>
    public static async Task CheckNowAsync(Activity activity)
    {
        try
        {
            // Toujours consulter GitHub, MEME si une ancienne version attend
            // deja son installation. L'APK courant reste disponible jusqu'a ce
            // que l'utilisateur approuve le telechargement de sa remplaçante.
            bool found = await CheckLatestAsync(activity, showDialog: true,
                forceRefresh: true, manual: true);
            var prefs = Preferences(activity);
            var latest = Deserialize(prefs.GetString(ReleaseKey, null));
            var pending = prefs.GetString(DownloadVersionKey, null);
            bool superseded = latest is not null && !string.IsNullOrEmpty(pending)
                && UpdateCompletionPolicy.IsNewerThanPending(latest.Version, pending);
            bool awaiting = await CheckDownloadAsync(activity, showInstallDialog: !superseded);
            if (!found && !awaiting)
                activity.RunOnUiThread(() => new AlertDialog.Builder(activity)
                    .SetTitle("Mises à jour SCOLIA")
                    .SetMessage("Aucune nouvelle version Android officielle n'est disponible pour cette installation.")
                    .SetPositiveButton("Fermer", (_, _) => { })
                    .Show());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
            or TaskCanceledException or Java.Lang.Exception or InvalidOperationException)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Contrôle manuel impossible : {ex.GetType().Name}");
            Report(activity, $"Recherche impossible : {ex.Message}");
            // Une coupure Internet ne doit pas masquer l'installation d'un APK
            // deja disponible et verifie localement.
            try
            {
                if (await CheckDownloadAsync(activity))
                {
                    Report(activity, "Recherche réseau indisponible ; APK déjà téléchargé toujours installable.");
                    return;
                }
            }
            catch (Exception pendingError) when (pendingError is IOException or Java.Lang.Exception
                or InvalidOperationException or UnauthorizedAccessException)
            {
                Android.Util.Log.Warn("SCOLIA-MAJ", $"Vérification locale différée : {pendingError.GetType().Name}");
            }
            activity.RunOnUiThread(() => new AlertDialog.Builder(activity)
                .SetTitle("Vérification indisponible")
                .SetMessage("Impossible de vérifier la publication officielle. Vérifiez Internet ou le VPN, puis réessayez dans Mon compte. Vos données scolaires sont conservées.")
                .SetPositiveButton("Fermer", (_, _) => { })
                .Show());
        }
    }

    private static async Task ResumeAsync(Activity activity)
    {
        try
        {
            // Ne pas afficher les alertes pendant l'écran de lancement natif.
            // L'utilisateur ne les voyait pas lorsque le splash couvrait l'activité.
            if (Scolia.Bureau.Infrastructure.Platform.StartupTask is { } startup)
            {
                try { await startup; }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Android.Util.Log.Warn("SCOLIA-MAJ", $"Interface non prête : {ex.GetType().Name}");
                    return;
                }
            }
            // Le splash attend encore le dessin de la première page, l'animation du
            // logo, les points et leur fondu après StartupTask. Un délai fixe de
            // 700 ms pouvait donc encore afficher la fenêtre derrière cet écran.
            if (activity is MainActivity main)
            {
                while (main.IsStartupOverlayVisible && IsForeground(activity))
                    await Task.Delay(140);
            }
            else await Task.Delay(250);
            if (!IsForeground(activity)) return;
            // Après l'autorisation « Installer des applications inconnues », reprendre
            // l'installation déjà consentie sans obliger à rechercher l'APK.
            if (waitingForInstallPermission)
            {
                waitingForInstallPermission = false;
                if (!OperatingSystem.IsAndroidVersionAtLeast(26)
                    || activity.PackageManager?.CanRequestPackageInstalls() == true)
                {
                    var prefs = Preferences(activity);
                    var path = UpdatePath(activity, prefs.GetString(FileNameKey, null) ?? "");
                    var digest = prefs.GetString(DigestKey, "") ?? "";
                    if (path is not null && File.Exists(path)
                        && Regex.IsMatch(digest, "^[a-fA-F0-9]{64}$")
                        && await Task.Run(() => VerifyDownloadedApk(activity, path, digest)))
                    {
                        if (IsForeground(activity)) OpenSystemInstaller(activity, path);
                        else NotifyInstallReady(activity);
                        return;
                    }
                }
                NotifyInstallReady(activity);
                return;
            }
            // Une installation déjà prête est affichée immédiatement, sans
            // attendre les délais d'Internet. Mais la recherche ne s'arrête
            // JAMAIS là : on consulte aussi GitHub lorsque l'APK est en attente.
            bool pending = await CheckDownloadAsync(activity);
            try { await CheckLatestAsync(activity, showDialog: !pending); }
            catch (Exception ex) when (ex is IOException or HttpRequestException or
                JsonException or TaskCanceledException or Java.Lang.Exception or InvalidOperationException)
            {
                Android.Util.Log.Warn("SCOLIA-MAJ", $"Consultation GitHub reportée : {ex.GetType().Name}");
                Report(activity, "Vérification réseau reportée, mise à jour déjà téléchargée conservée.");
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException
            or TaskCanceledException or Java.Lang.Exception or InvalidOperationException)
        {
            Android.Util.Log.Warn("SCOLIA-MAJ", $"Vérification reportée : {ex.GetType().Name} {ex.Message}");
            Report(activity, $"Connexion au service de mises à jour impossible ({ex.GetType().Name}).");
        }
    }

    /// <summary>Peut être appelée par l'interface ou le worker. Ne télécharge jamais un APK seule.</summary>
    internal static async Task<bool> CheckLatestAsync(Context context, bool showDialog,
        bool forceRefresh = false, bool manual = false)
    {
        // Pour une demande explicite, attendre le contrôle en cours au lieu
        // d'abandonner silencieusement pendant qu'un worker réseau travaille.
        if (manual) await CheckGate.WaitAsync();
        else if (!await CheckGate.WaitAsync(0)) return false;
        try
        {
            var prefs = Preferences(context);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!forceRefresh && now - prefs.GetLong(LastCheckKey, 0) < CheckSeconds)
            {
                var cached = Deserialize(prefs.GetString(ReleaseKey, null));
                if (cached is null || !IsNewer(context, cached.Version))
                {
                    ClearAvailableNotification(context);
                    return false;
                }
                HandleKnownRelease(context, cached, showDialog, manual);
                return true;
            }

            var release = await ReadReleaseAsync(context);
            // Ne pas saturer GitHub à chaque reprise d'activité ; en cas d'erreur réseau on réessaie.
            prefs.Edit()!.PutLong(LastCheckKey, now)!.Apply();
            if (release is null)
            {
                prefs.Edit()!.Remove(ReleaseKey)!.Apply();
                ClearAvailableNotification(context);
                Report(context, "Aucune version plus récente trouvée sur GitHub.");
                return false;
            }

            prefs.Edit()!.PutString(ReleaseKey, JsonSerializer.Serialize(release))!.Apply();
            HandleKnownRelease(context, release, showDialog, manual);
            return true;
        }
        finally { CheckGate.Release(); }
    }

    /// <summary>
    /// Ne pas re-proposer le telechargement d'une version deja presente dans
    /// DownloadManager. Continuer de consulter GitHub et signaler toute version
    /// qui devient superieure a celle qui attend l'installation.
    /// </summary>
    private static void HandleKnownRelease(Context context, Release release,
        bool showDialog, bool manual)
    {
        var prefs = Preferences(context);
        var pending = prefs.GetString(DownloadVersionKey, null);
        if (!UpdateCompletionPolicy.IsNewerThanPending(release.Version, pending))
        {
            Report(context, $"Version {release.Version} suivie : installation ou téléchargement encore en attente.");
            var id = prefs.GetLong(DownloadIdKey, 0);
            if (id > 0 && DownloadStatus(context, id) == DownloadSuccessStatus
                && context is not Activity)
                NotifyInstallReady(context);
            return;
        }
        Report(context, pending is null
            ? $"Version {release.Version} disponible sur GitHub."
            : $"Version {release.Version} plus récente que l'APK {pending} en attente.");
        if (showDialog && context is Activity screen) OfferUpdate(screen, release, manual);
        else NotifyOnce(context, release);
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
        if (!root.TryGetProperty("tag_name", out var tagValue))
            throw new InvalidOperationException("La publication officielle n'indique pas sa version.");
        var tag = tagValue.GetString() ?? "";
        var version = Regex.Match(tag, @"(?<!\d)(\d+)\.(\d+)\.(\d+)(?!\d)");
        if (!version.Success)
            throw new InvalidOperationException("La version GitHub officielle est invalide.");
        if (!IsNewer(context, version.Value)) return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("La nouvelle publication ne contient aucun APK vérifiable.");

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
        // Une release existe mais l'APK ou son empreinte n'est pas encore validé.
        // Un faux message « déjà à jour » masquerait cette anomalie.
        throw new InvalidOperationException($"La version {version.Value} n'a pas d'APK Android vérifiable.");
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
        // Quelques constructeurs renvoient un VersionName non standard :
        // repli sur la version embarquée dans SCOLIA, pas sur une valeur inconnue.
        var installed = Version.TryParse(installedName, out var current) ? current
            : Version.TryParse(Scolia.Bureau.Infrastructure.AboutInfo.Version, out var bundled) ? bundled : null;
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

    private static void OfferUpdate(Activity activity, Release release, bool manual = false)
    {
        if (activity.IsFinishing || activity.IsDestroyed) return;
        var prefs = Preferences(activity);
        if (!manual && prefs.GetString(DeferredVersionKey, null) == release.Version &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() < prefs.GetLong(DeferredUntilKey, 0))
        {
            // « Plus tard » masque le dialogue pendant 12 h, pas la recherche
            // ni l'indicateur de mise à jour dans Android.
            NotifyOnce(activity, release);
            return;
        }
        if (!manual && promptedVersion == release.Version) return;
        activity.RunOnUiThread(() =>
        {
            if (activity.IsFinishing || activity.IsDestroyed)
            {
                NotifyOnce(activity, release);
                return;
            }
            if (!manual && promptedVersion == release.Version) return;
            promptedVersion = release.Version;
            var mb = release.Size / (1024.0 * 1024.0);
            new AlertDialog.Builder(activity)
                .SetTitle($"SCOLIA Pro {release.Version} disponible")
                .SetMessage($"Une mise à jour est disponible.\nTaille : {mb:F1} Mo.\n\nTélécharger maintenant avec Android ? Les données scolaires existantes seront conservées si la signature de l'application est identique.")
                .SetPositiveButton("Télécharger", (_, _) =>
                {
                    promptedVersion = null;
                    prefs.Edit()!.Remove(DeferredVersionKey)!.Remove(DeferredUntilKey)!.Apply();
                    StartDownload(activity, release);
                })
                .SetNegativeButton("Plus tard", (_, _) =>
                {
                    promptedVersion = null;
                    // Ne pas proposer la même version à chaque changement de page.
                    prefs.Edit()!
                        .PutString(DeferredVersionKey, release.Version)!
                        .PutLong(DeferredUntilKey, DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds())!
                        .Apply();
                    NotifyOnce(activity, release);
                })
                .SetCancelable(false) // « Plus tard » memorise correctement le rappel, sans blocage du dialogue
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
            var prefs = Preferences(activity);
            var previousId = prefs.GetLong(DownloadIdKey, 0);
            var previousFile = UpdatePath(activity, prefs.GetString(FileNameKey, "") ?? "");
            prefs.Edit()!
                .PutLong(DownloadIdKey, id)!
                .PutString(FileNameKey, filename)!
                .PutString(DigestKey, release.Sha256)!
                .PutString(DownloadVersionKey, release.Version)!
                .PutLong(DownloadStartedAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds())!
                .Apply();
            // Remplacer un ancien APK uniquement apres le consentement explicite
            // et l'enregistrement du nouveau téléchargement. Ne pas accumuler
            // d'anciens APK ni conserver une notification d'installation obsolète.
            if (previousId > 0 && previousId != id)
                downloadManager.Remove(previousId);
            if (previousFile is not null && previousFile != path)
            {
                try { if (File.Exists(previousFile)) File.Delete(previousFile); }
                catch (IOException) { /* Le gestionnaire Android peut encore libérer le fichier. */ }
            }
            (activity.GetSystemService(Context.NotificationService) as NotificationManager)?
                .Cancel(InstallNotificationId);
            ClearAvailableNotification(activity);
            // Ne pas dépendre exclusivement du broadcast DOWNLOAD_COMPLETE.
            if (IsForeground(activity)) StartForegroundDownloadWatch(activity);
            Report(activity, $"Téléchargement de la mise à jour {release.Version} lancé.");
            Toast.MakeText(activity, "Téléchargement commencé. Android affiche sa progression dans les notifications.", ToastLength.Long)?.Show();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException or InvalidOperationException)
        {
            Toast.MakeText(activity, $"Téléchargement impossible : {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    private static async Task<bool> CheckDownloadAsync(Activity activity, bool showInstallDialog = true)
    {
        var pref = Preferences(activity);
        var id = pref.GetLong(DownloadIdKey, 0);
        if (id <= 0) return false;
        var pendingVersion = pref.GetString(DownloadVersionKey, null);
        var installedVersion = activity.PackageManager?.GetPackageInfo(activity.PackageName!, 0)?.VersionName;
        if (UpdateCompletionPolicy.AlreadyInstalled(pendingVersion, installedVersion))
        {
            var cachedFile = UpdatePath(activity, pref.GetString(FileNameKey, "") ?? "");
            if (cachedFile is not null)
            {
                try { if (File.Exists(cachedFile)) File.Delete(cachedFile); }
                catch (IOException) { /* Un cache supprimable au prochain nettoyage. */ }
            }
            ResetPending(activity);
            ClearAvailableNotification(activity);
            pref.Edit()!.Remove(DeferredVersionKey)!.Remove(DeferredUntilKey)!.Apply();
            Report(activity, "Mise à jour déjà installée. Cache de téléchargement nettoyé.");
            return false;
        }
        var downloadManager = activity.GetSystemService(Context.DownloadService) as DownloadManager;
        if (downloadManager is null) return false;
        var state = DownloadStatus(activity, id);
        if (state is null)
        {
            ResetPending(activity);
            return false;
        }
        var status = state.Value;
        if (status == DownloadFailureStatus)
        {
            ResetPending(activity);
            Toast.MakeText(activity, "La mise à jour a échoué. Vérifiez la connexion ou l'espace libre et réessayez.", ToastLength.Long)?.Show();
            return false;
        }
        if (status != DownloadSuccessStatus)
        {
            long started = pref.GetLong(DownloadStartedAtKey, 0);
            // Le reseau mobile peut etre interrompu plusieurs jours. Ne pas
            // detruire le téléchargement apres six heures : Android sait le
            // reprendre et la recherche de nouvelles versions continue quand meme.
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (UpdateCompletionPolicy.IsStalePendingDownload(started, now))
            {
                downloadManager.Remove(id);
                ResetPending(activity);
                Report(activity, "Un ancien téléchargement interrompu a été nettoyé.");
                return false;
            }
            return true;
        }

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
            if (!IsForeground(activity) || !showInstallDialog)
            {
                NotifyInstallReady(activity);
                return true;
            }
            if (installDialogOpen) return true;
            // Marquer AVANT RunOnUiThread : broadcast et surveillance ne doivent
            // pas programmer plusieurs fenêtres « Installer » simultanément.
            installDialogOpen = true;
            activity.RunOnUiThread(() =>
            {
                if (!IsForeground(activity))
                {
                    installDialogOpen = false;
                    NotifyInstallReady(activity);
                    return;
                }
                try
                {
                    Report(activity, $"SCOLIA Pro {version} est vérifié et prêt à installer.");
                    new AlertDialog.Builder(activity)
                        .SetTitle($"SCOLIA Pro {version} — prêt à installer")
                        .SetMessage("Le téléchargement est terminé et le fichier APK a été vérifié.\n\n"
                            + "Appuyez sur Installer : Android affichera son écran de confirmation. "
                            + "Les données de SCOLIA ne seront pas supprimées.")
                        .SetPositiveButton("Installer", (_, _) =>
                        {
                            installDialogOpen = false;
                            OpenSystemInstaller(activity, path);
                        })
                        .SetNegativeButton("Plus tard", (_, _) =>
                        {
                            installDialogOpen = false;
                            NotifyInstallReady(activity);
                        })
                        .SetCancelable(false)
                        .Show();
                }
                catch (Java.Lang.Exception ex)
                {
                    installDialogOpen = false;
                    Android.Util.Log.Warn("SCOLIA-MAJ", $"Boîte d'installation reportée : {ex.Message}");
                    NotifyInstallReady(activity);
                }
            });
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

    private static void ResetPending(Context ctx)
    {
        Preferences(ctx).Edit()!.Remove(DownloadIdKey)!.Remove(FileNameKey)!
            .Remove(DigestKey)!.Remove(DownloadVersionKey)!
            .Remove(DownloadStartedAtKey)!.Apply();
        installDialogOpen = false;
        (ctx.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(InstallNotificationId);
    }

    /// <summary>Message Android utilisable même quand l'activité SCOLIA n'est plus affichée.</summary>
    private static void NotifyInstallReady(Context context)
    {
        var version = Preferences(context).GetString(DownloadVersionKey, null);
        if (string.IsNullOrWhiteSpace(version)) return;
        if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(
                InstallChannel, "Installation des mises à jour SCOLIA", NotificationImportance.High));
        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? "");
        if (open is null) return;
        open.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        var action = PendingIntent.GetActivity(context, 1, open,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, InstallChannel)
            : new Notification.Builder(context);
        builder.SetSmallIcon(Resource.Mipmap.icon)
            .SetContentTitle($"SCOLIA Pro {version} — téléchargement terminé")
            .SetContentText("Touchez pour vérifier l'APK et afficher le bouton Installer.")
            .SetContentIntent(action)
            .SetOnlyAlertOnce(true)
            .SetOngoing(true)
            .SetAutoCancel(false);
        manager.Notify(InstallNotificationId, builder.Build());
    }

    private static void ClearAvailableNotification(Context context)
    {
        (context.GetSystemService(Context.NotificationService) as NotificationManager)?.Cancel(NotificationId);
        Preferences(context).Edit()!.Remove(NotifiedKey)!.Apply();
    }

    private static void NotifyOnce(Context context, Release release)
    {
        // Une notification supprimée par l'utilisateur peut réapparaître au
        // prochain contrôle planifié, jusqu'à installation effective. Pas
        // d'alerte sonore à chaque passage : SetOnlyAlertOnce.
        var prefs = Preferences(context);
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
            .SetOnlyAlertOnce(true)
            .SetAutoCancel(false);
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
