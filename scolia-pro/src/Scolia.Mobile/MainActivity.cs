using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;

namespace Scolia.Mobile;

/// <summary>Écran de SCOLIA sur le téléphone. Le clavier pousse le formulaire vers le haut au lieu de le cacher.</summary>
[Activity(Label = "SCOLIA", Theme = "@style/ScoliaTheme", Icon = "@mipmap/icon", RoundIcon = "@mipmap/icon_round", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        MobilePlatform.Attach(this);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        base.OnResume();
        // La recherche du serveur sur le Wi-Fi a besoin de recevoir les réponses en diffusion.
        MobilePlatform.HoldMulticast(true);
    }

    protected override void OnPause()
    {
        MobilePlatform.HoldMulticast(false);
        base.OnPause();
    }
}
