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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
        base.OnFrameworkInitializationCompleted();
    }
}
