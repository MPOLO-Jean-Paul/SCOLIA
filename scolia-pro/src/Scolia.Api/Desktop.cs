using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Serilog;

namespace Scolia.Api;

/// <summary>Ouvre SCOLIA dans une fenêtre d'application (Edge ou Chrome en mode application), sinon dans le navigateur par défaut.</summary>
internal static class DesktopLauncher
{
    public static void Open(string url)
    {
        if (!OperatingSystem.IsWindows()) return;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        try
        {
            var browser = candidates.FirstOrDefault(File.Exists);
            if (browser is not null) Process.Start(new ProcessStartInfo(browser, $"--app={url} --window-size=1366,860") { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning(ex, "Ouverture automatique impossible : ouvrez {Url} dans votre navigateur.", url);
        }
    }

    /// <summary>Adresse IPv4 du poste sur le réseau local, pour connecter les autres postes.</summary>
    public static string? LocalAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork) return ip.Address.ToString();
        }
        return null;
    }

    /// <summary>Si SCOLIA tourne déjà sur ce poste (fenêtre ou service), on ouvre simplement la fenêtre.</summary>
    public static bool AlreadyRunning(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
            return client.GetAsync(new Uri($"http://localhost:{port}/api/sante")).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}

#if SCOLIA_DESKTOP
/// <summary>Icône dans la zone de notification de Windows : ouvrir, accès réseau, dossier des données, quitter.</summary>
internal static class TrayHost
{
    public static void Start(WebApplication app, string url, string dataDirectory, bool lan, int port)
    {
        var thread = new Thread(() =>
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            using var context = new System.Windows.Forms.ApplicationContext();
            var sync = new System.Windows.Forms.WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(sync);
            using var icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
                Text = "SCOLIA — gestion scolaire",
                Visible = true,
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Ouvrir SCOLIA", null, (_, _) => DesktopLauncher.Open(url)).Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add("Ouvrir dans le navigateur", null, (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
            if (lan && DesktopLauncher.LocalAddress() is { } ip)
                menu.Items.Add($"Adresse pour les autres postes : http://{ip}:{port}", null, (_, _) => System.Windows.Forms.Clipboard.SetText($"http://{ip}:{port}"));
            menu.Items.Add("Ouvrir le dossier des données", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dataDirectory}\"") { UseShellExecute = true }));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Quitter SCOLIA", null, (_, _) => app.Lifetime.StopApplication());
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (_, _) => DesktopLauncher.Open(url);
            app.Lifetime.ApplicationStopping.Register(() => sync.Post(_ => { icon.Visible = false; context.ExitThread(); }, null));
            icon.ShowBalloonTip(4000, "SCOLIA est prêt", lan ? $"Accessible depuis les autres postes du réseau (port {port})." : "Double-cliquez sur cette icône pour ouvrir SCOLIA.", System.Windows.Forms.ToolTipIcon.Info);
            System.Windows.Forms.Application.Run(context);
        })
        { IsBackground = true, Name = "SCOLIA.Notification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
#endif
