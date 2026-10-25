using System.Globalization;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace Scolia.Mobile;

/// <summary>Application Android de SCOLIA : la même application Avalonia que sur l'ordinateur, en vue unique.</summary>
[Application(Label = "SCOLIA", Icon = "@mipmap/icon", RoundIcon = "@mipmap/icon_round", UsesCleartextTraffic = true, AllowBackup = false)]
public sealed class AndroidApp : AvaloniaAndroidApplication<Scolia.Bureau.App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer) { }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // Dates, mois et montants en français, comme sur l'ordinateur.
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentCulture = fr;
        CultureInfo.CurrentUICulture = fr;
        MobilePlatform.Prepare(this);
        return base.CustomizeAppBuilder(builder);
    }
}
