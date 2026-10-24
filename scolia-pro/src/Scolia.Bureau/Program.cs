using System.Globalization;
using Avalonia;

namespace Scolia.Bureau;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Dates et mois en français, partout : calendriers, libellés et montants.
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentCulture = fr;
        CultureInfo.CurrentUICulture = fr;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
