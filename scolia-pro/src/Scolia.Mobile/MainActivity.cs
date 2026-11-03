using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using ScoliaPlatform = Scolia.Bureau.Infrastructure.Platform;

namespace Scolia.Mobile;

/// <summary>Écran de SCOLIA sur le téléphone. Le clavier pousse le formulaire vers le haut au lieu de le cacher.</summary>
[Activity(Label = "SCOLIA Phone", Theme = "@style/ScoliaLaunchTheme", Icon = "@mipmap/icon", RoundIcon = "@mipmap/icon_round", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private static bool launchPresented;
    private StartupScreen? startup;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        MobilePlatform.Attach(this);
        AndroidSpeech.Attach(this);
        AndroidCamera.Attach(this);
        // Le fond du thème couvre aussi la préparation d'Avalonia dans base.OnCreate.
        base.OnCreate(savedInstanceState);
        // Android 13+ : permission des notifications (encaissements et sauvegardes des autres postes).
        AndroidAlerts.AskPermission(this);
        // Une reprise d'activité déjà prête ne rejoue pas le lancement. Une rotation pendant la préparation le garde.
        if (!launchPresented || ScoliaPlatform.StartupTask is { IsCompletedSuccessfully: false })
        {
            var minimum = launchPresented ? TimeSpan.Zero : TimeSpan.FromMilliseconds(900);
            launchPresented = true;
            startup = new StartupScreen(this);
            AddContentView(startup, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            // App.OnFrameworkInitializationCompleted est exécuté par AndroidApp.OnCreate avant l'activité.
            // Si cet invariant est rompu, afficher une erreur plutôt que retirer le logo prématurément.
            startup.Begin(ScoliaPlatform.StartupTask ?? Task.FromException(new InvalidOperationException("L'initialisation SCOLIA n'est pas disponible.")), minimum);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidAlerts.SetForeground(true);
        startup?.SetActive(true);
        // La recherche du serveur sur le Wi-Fi a besoin de recevoir les réponses en diffusion.
        MobilePlatform.HoldMulticast(true);
        Security.AndroidBiometrics.Attach(this);
        AndroidSpeech.Attach(this);
        AndroidCamera.Attach(this);
        ScoliaPlatform.NotifyAppResumed();
        // Le retour au premier plan est un moment autorisé pour rétablir un service
        // Android interrompu. On n'attend pas la connexion du secrétariat ni
        // l'ouverture d'une page : les autres appareils dépendent de ce serveur.
        // Aucune action pour un appareil configuré comme client.
        try { Server.PhoneServer.Get(this).EnsureServingOnResume(); }
        catch (Exception ex) when (ex is Java.Lang.SecurityException or InvalidOperationException
            or System.IO.IOException or UnauthorizedAccessException)
        { Android.Util.Log.Warn("SCOLIA", $"Reprise du serveur différée : {ex.Message}"); }
        // Téléphone serveur encore freiné par les économies de batterie : Android le demande, une fois par lancement.
        try { AndroidBackground.AskOnceIfServer(this); }
        catch (Exception ex) { Android.Util.Log.Warn("SCOLIA", $"Demande batterie impossible : {ex.GetType().Name}"); }
        // Une erreur de WorkManager ne doit pas empêcher de revenir sur l'écran SCOLIA.
        try { Synchronization.SyncWorker.ScheduleOnResume(this); }
        catch (Exception ex)
        { Android.Util.Log.Warn("SCOLIA-Sync", $"Reprise différée indisponible : {ex.GetType().Name}"); }
    }

    protected override void OnPause()
    {
        AndroidAlerts.SetForeground(false);
        startup?.SetActive(false);
        MobilePlatform.HoldMulticast(false);
        // Ne jamais arrêter PhoneServer ni ServeurScolia ici : OnPause est déclenché
        // quand on affiche le bureau Android, le VPN, une notification ou un dialogue.
        Security.AndroidBiometrics.Detach(this);
        base.OnPause();
    }

    /// <summary>Accord du micro pour la recherche à la voix dans le téléphone, sans Internet.</summary>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (AndroidSpeech.CompletePermission(requestCode, grantResults)) return;
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    /// <summary>Le texte dicté revient ici, puis part dans le champ de recherche.</summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        if (AndroidSpeech.Complete(requestCode, resultCode, data)) return;
        if (AndroidCamera.Complete(requestCode, resultCode)) return;
        base.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnDestroy()
    {
        startup?.Close();
        startup = null;
        base.OnDestroy();
    }
}
