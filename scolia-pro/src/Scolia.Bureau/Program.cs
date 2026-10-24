using System.Globalization;
using Avalonia;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Relance élevée d'une opération du poste (service, réseau local) : pas d'interface, le résultat part dans un fichier.
        if (args is ["--poste", var action, "--resultat", var resultFile]) return PosteAdmin.RunElevatedCommand(action, resultFile);

        // Dates et mois en français, partout : calendriers, libellés et montants.
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentCulture = fr;
        CultureInfo.CurrentUICulture = fr;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
