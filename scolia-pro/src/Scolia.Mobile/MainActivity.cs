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
        // Le fond du thème couvre aussi la préparation d'Avalonia dans base.OnCreate.
        base.OnCreate(savedInstanceState);
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
        startup?.SetActive(true);
        // La recherche du serveur sur le Wi-Fi a besoin de recevoir les réponses en diffusion.
        MobilePlatform.HoldMulticast(true);
        ScoliaPlatform.NotifyAppResumed();
        // Une erreur de WorkManager ne doit pas empêcher de revenir sur l'écran SCOLIA.
        try { Synchronization.SyncWorker.ScheduleOnResume(this); }
        catch (Exception ex)
        { Android.Util.Log.Warn("SCOLIA-Sync", $"Reprise différée indisponible : {ex.GetType().Name}"); }
    }

    protected override void OnPause()
    {
        startup?.SetActive(false);
        MobilePlatform.HoldMulticast(false);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        startup?.Close();
        startup = null;
        base.OnDestroy();
    }
}
