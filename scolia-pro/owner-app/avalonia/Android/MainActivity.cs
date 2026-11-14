using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia.Android;

namespace JPServices.Owner.Android;

[Application(Label = "SCOLIA Console", Icon = "@mipmap/icon", RoundIcon = "@mipmap/icon_round", AllowBackup = false, UsesCleartextTraffic = false)]
public sealed class MainApplication : AvaloniaAndroidApplication<JPServices.Owner.OwnerApp>
{
    public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) { }
}

[Activity(Label = "SCOLIA Console", Icon = "@mipmap/icon", RoundIcon = "@mipmap/icon_round", MainLauncher = true, Exported = true,
    Theme = "@style/ScoliaConsoleTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
