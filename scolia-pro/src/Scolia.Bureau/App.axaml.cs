using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Bureau.Views;

namespace Scolia.Bureau;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        PhoneFit.Install();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DataTemplates.Add(new ViewLocator());
            // Le serveur réel (ce poste ou celui de l'établissement) est choisi au démarrage du shell.
            var api = new ScoliaClient(ConnectionStore.LocalAddress);
            var server = new ServerProcess(ConnectionStore.LocalAddress.Port);
            var shell = new ShellViewModel(api, Preferences.Load());
            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.ShutdownRequested += (_, _) =>
            {
                shell.Prefs.Save();
                server.Stop();
            };
            _ = shell.StartAsync(server);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime phone)
        {
            // Téléphone : une seule vue, en plein écran ; il se connecte au serveur de l'établissement sur le Wi-Fi.
            DataTemplates.Add(new ViewLocator());
            var saved = ConnectionStore.Load();
            var api = new ScoliaClient(saved?.Url is { } url ? new Uri(url) : ConnectionStore.LocalAddress);
            var shell = new ShellViewModel(api, Preferences.Load());
            phone.MainView = new MainView { DataContext = shell };
            _ = shell.StartAsync(null);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
