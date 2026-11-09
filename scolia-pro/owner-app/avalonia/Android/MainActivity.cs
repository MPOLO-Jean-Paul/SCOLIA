using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia.Android;

namespace JPServices.Owner.Android;

[Application(Label = "SCOLIA Admin", AllowBackup = false, UsesCleartextTraffic = false)]
public sealed class MainApplication : AvaloniaAndroidApplication<JPServices.Owner.OwnerApp>
{
    public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer) { }
}

[Activity(Label = "SCOLIA Admin", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
        ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
        ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden,
    WindowSoftInputMode = global::Android.Views.SoftInput.AdjustResize)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
