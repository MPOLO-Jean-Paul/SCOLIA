using System.Globalization;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using ObjCRuntime;
using Scolia.Bureau.Infrastructure;
using UIKit;

namespace Scolia.Phone.iOS;

/// <summary>Réutilise les écrans SCOLIA natifs ; iPhone en mode client.</summary>
[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<Scolia.Bureau.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        IosPlatform.Prepare();
        return base.CustomizeAppBuilder(builder);
    }

}
