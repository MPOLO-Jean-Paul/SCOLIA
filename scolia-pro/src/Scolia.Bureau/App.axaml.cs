using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Scolia.Bureau.Api;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau;

public partial class App : Application
{
    /// <summary>Adresse du serveur SCOLIA : celle du poste, ou celle indiquée par la variable d'environnement.</summary>
    public static Uri ServerAddress { get; } = new(Environment.GetEnvironmentVariable("SCOLIA_SERVEUR") ?? "http://localhost:5080/");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var api = new ScoliaClient(ServerAddress);
            var server = new ServerProcess(ServerAddress.Port);
            var shell = new ShellViewModel(api);
            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.ShutdownRequested += (_, _) => server.Stop();
            _ = shell.StartAsync(server);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
