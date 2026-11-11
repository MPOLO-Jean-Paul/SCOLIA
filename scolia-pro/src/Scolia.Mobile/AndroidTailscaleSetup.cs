using Android.App;
using Android.Content;
using Android.Content.PM;
using Scolia.Bureau.Infrastructure;
using AndroidUri = Android.Net.Uri;

namespace Scolia.Mobile;

/// <summary>
/// Installation de Tailscale depuis SCOLIA, à partir de l'APK officiel déjà téléchargé et vérifié par son empreinte.
/// Le fichier doit bien être l'application com.tailscale.ipn ; Android garde la main : il demande l'autorisation
/// « installer des applications inconnues » la première fois, puis affiche sa propre confirmation d'installation.
/// Si Tailscale est déjà présent, Android refuse toute mise à jour qui ne porte pas la même signature.
/// </summary>
public static class AndroidTailscaleSetup
{
    public const string TailscalePackage = "com.tailscale.ipn";
    internal const string ResultAction = "com.jpservices.scolia.TAILSCALE_INSTALL_RESULT";
    private static TaskCompletionSource<TailscaleInstallResult>? pending;

    public static async Task<TailscaleInstallResult> InstallAsync(Context ctx, string path)
    {
        if (ctx.PackageManager is not { } pm)
            return new(TailscaleInstallOutcome.Failed, "Android n'est pas prêt pour l'installation.");

        // 1. Le fichier est-il bien l'application Tailscale ?
        var archive = pm.GetPackageArchiveInfo(path, 0);
        if (archive?.PackageName != TailscalePackage)
        {
            TryDelete(path);
            return new(TailscaleInstallOutcome.Refused, "Installation refusée : le fichier téléchargé n'est pas l'application Tailscale. Il a été supprimé.");
        }

        // 2. Première fois : Android demande d'autoriser SCOLIA à installer une application.
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !pm.CanRequestPackageInstalls())
        {
            try
            {
                var settings = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                    AndroidUri.Parse("package:" + ctx.PackageName));
                settings.AddFlags(ActivityFlags.NewTask);
                ctx.StartActivity(settings);
                return new(TailscaleInstallOutcome.NeedsPermission,
                    "Android demande une autorisation : activez « Autoriser cette source » pour SCOLIA, puis revenez. L'installation reprendra toute seule.");
            }
            catch (ActivityNotFoundException)
            {
                return new(TailscaleInstallOutcome.NeedsPermission,
                    "Autorisez SCOLIA à installer des applications dans Paramètres, Applications, Accès spécial, puis revenez.");
            }
        }

        // 3. Session d'installation Android : le système affiche sa confirmation.
        pending?.TrySetResult(new(TailscaleInstallOutcome.WaitingForUser, "Une nouvelle installation a été lancée."));
        var done = new TaskCompletionSource<TailscaleInstallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = done;
        try
        {
            var installer = pm.PackageInstaller;
            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            parameters.SetAppPackageName(TailscalePackage);
            var sessionId = installer.CreateSession(parameters);
            using (var session = installer.OpenSession(sessionId))
            {
                var length = new FileInfo(path).Length;
                await using (var input = File.OpenRead(path))
                using (var output = session.OpenWrite("tailscale.apk", 0, length))
                {
                    await input.CopyToAsync(output).ConfigureAwait(false);
                    session.Fsync(output);
                }
                var callback = new Intent(ResultAction);
                callback.SetPackage(ctx.PackageName);
                callback.SetClass(ctx, Java.Lang.Class.FromType(typeof(TailscaleInstallReceiver)));
                var flags = PendingIntentFlags.UpdateCurrent;
                if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;
                var sender = PendingIntent.GetBroadcast(ctx, sessionId, callback, flags)
                    ?? throw new InvalidOperationException("Android n'a pas préparé le retour de l'installation.");
                session.Commit(sender.IntentSender);
            }
        }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.IO.IOException or IOException or InvalidOperationException or Java.Lang.IllegalArgumentException)
        {
            pending = null;
            return new(TailscaleInstallOutcome.Failed, $"Android n'a pas pu préparer l'installation : {ex.Message}");
        }

        // La confirmation peut rester ouverte : au-delà, l'installation reprend au retour dans SCOLIA.
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(10))).ConfigureAwait(false);
        if (finished == done.Task) return await done.Task.ConfigureAwait(false);
        pending = null;
        return new(TailscaleInstallOutcome.WaitingForUser, "Android attend encore votre confirmation pour installer Tailscale.");
    }

    internal static void Complete(Context ctx, Intent intent)
    {
        var status = (PackageInstallStatus)intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        var message = intent.GetStringExtra(PackageInstaller.ExtraStatusMessage);
        switch (status)
        {
            case PackageInstallStatus.PendingUserAction:
                // Android demande la confirmation de l'utilisateur : on ouvre son écran, la suite revient ici.
                var confirm = ConfirmIntent(intent);
                if (confirm is null)
                {
                    pending?.TrySetResult(new(TailscaleInstallOutcome.Failed, "Android n'a pas transmis son écran de confirmation."));
                    return;
                }
                confirm.AddFlags(ActivityFlags.NewTask);
                try { ctx.StartActivity(confirm); }
                catch (ActivityNotFoundException)
                {
                    pending?.TrySetResult(new(TailscaleInstallOutcome.Failed, "Android n'a pas ouvert son écran de confirmation."));
                }
                return;
            case PackageInstallStatus.Success:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Installed, "Tailscale est installé sur ce téléphone."));
                break;
            case PackageInstallStatus.FailureAborted:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Refused, "Installation annulée sur l'écran d'Android. Vous pouvez la relancer."));
                break;
            case PackageInstallStatus.FailureConflict:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Failed,
                    "Android refuse : une autre version de Tailscale est déjà présente. Utilisez le magasin officiel pour la mettre à jour."));
                break;
            case PackageInstallStatus.FailureStorage:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Failed, "Espace insuffisant sur le téléphone pour installer Tailscale."));
                break;
            case PackageInstallStatus.FailureIncompatible:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Failed, "Cette version de Tailscale n'est pas compatible avec ce téléphone. Passez par le magasin officiel."));
                break;
            default:
                pending?.TrySetResult(new(TailscaleInstallOutcome.Failed,
                    string.IsNullOrWhiteSpace(message) ? "Android n'a pas installé Tailscale." : $"Android n'a pas installé Tailscale : {message}"));
                break;
        }
        pending = null;
    }

    private static Intent? ConfirmIntent(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))) as Intent;
#pragma warning disable CA1422, CS0618 // Android 7 à 12 : seule la forme non typée existe.
        return intent.GetParcelableExtra(Intent.ExtraIntent) as Intent;
#pragma warning restore CA1422, CS0618
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Retour de la session d'installation Android : confirmation à afficher, succès, annulation ou échec.</summary>
[BroadcastReceiver(Exported = false, Name = "com.jpservices.scolia.TailscaleInstallReceiver")]
public sealed class TailscaleInstallReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        AndroidTailscaleSetup.Complete(context, intent);
    }
}
