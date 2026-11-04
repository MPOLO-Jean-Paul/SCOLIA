using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Bureau.Views;

namespace Scolia.Bureau;

public partial class App : Avalonia.Application
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
            // Le poste principal héberge le serveur dès l'ouverture, avant les
            // appels réseau et la page de connexion. Minimiser la fenêtre ne
            // suspend ni le processus SCOLIA-Serveur ni son superviseur.
            if (OperatingSystem.IsWindows() &&
                (ConnectionStore.Load()?.Mode == ConnectionSettings.Local || ConnectionStore.HasLocalData()))
            {
                try
                {
                    if (!PosteAdmin.ServiceInstalled()) server.Start();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                    or IOException or UnauthorizedAccessException or InvalidOperationException)
                { System.Diagnostics.Trace.WriteLine($"Démarrage anticipé du serveur reporté : {ex.Message}"); }
            }
            var shell = new ShellViewModel(api, Preferences.Load());
            var mainWindow = new MainWindow { DataContext = shell };
            DesktopAppUpdates.Attach(mainWindow);
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) =>
            {
                shell.Prefs.Save();
                server.Stop();
            };
            _ = shell.StartAsync(server);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime phone)
        {
            // Téléphone : une seule vue, en plein écran. Il rejoint le serveur de l'établissement sur le Wi-Fi, ou l'est lui-même.
            DataTemplates.Add(new ViewLocator());
            var saved = ConnectionStore.Load();
            var api = new ScoliaClient(saved?.Url is { } url ? new Uri(url) : ConnectionStore.LocalAddress);
            var shell = new ShellViewModel(api, Preferences.Load());
            phone.MainView = new MainView { DataContext = shell };
            // Android observe cette tâche : aucun écran blanc entre le logo et la configuration ou la connexion.
            // Une erreur reste visible et peut être réessayée, au lieu de perdre l'exception d'une tâche détachée.
            Platform.RetryStartup = () => Platform.StartupTask = shell.StartAsync(Platform.LocalServer);
            _ = Platform.RetryStartup();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
