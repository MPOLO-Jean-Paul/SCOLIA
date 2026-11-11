using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Serilog;
#if SCOLIA_DESKTOP
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
#endif

namespace Scolia.Api;

/// <summary>
/// Ouverture de SCOLIA sur le poste. Sous Windows, SCOLIA vit dans sa propre fenêtre (moteur WebView2 intégré à Windows) :
/// aucun navigateur n'est nécessaire. Le navigateur par défaut n'est qu'une solution de secours.
/// </summary>
internal static class DesktopLauncher
{
    private static SynchronizationContext? uiContext;
    private static string? dataDirectory;

    /// <summary>Rattache le thread d'interface (icône de notification) : c'est là que la fenêtre est créée.</summary>
    internal static void Attach(SynchronizationContext context, string data)
    {
        uiContext = context;
        dataDirectory = data;
    }

    /// <summary>Affiche SCOLIA : fenêtre native si elle est disponible, sinon navigateur.</summary>
    public static void Open(string url)
    {
        if (!OperatingSystem.IsWindows()) return;
#if SCOLIA_DESKTOP
        if (uiContext is not null && dataDirectory is not null)
        {
            uiContext.Post(_ => NativeWindow.Show(url, dataDirectory), null);
            return;
        }
#endif
        OpenInBrowser(url);
    }

    /// <summary>Ouvre une adresse dans le navigateur par défaut du poste (liens externes, secours).</summary>
    public static void OpenInBrowser(string url)
    {
        if (!OperatingSystem.IsWindows()) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning(ex, "Ouverture automatique impossible : ouvrez {Url} dans votre navigateur.", url);
        }
    }

    /// <summary>Second lancement : demande à l'instance déjà ouverte d'afficher sa fenêtre.</summary>
    public static bool ShowRunningInstance(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return client.GetAsync(new Uri($"http://localhost:{port}/api/bureau/afficher")).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
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
}

#if SCOLIA_DESKTOP

/// <summary>
/// Fenêtre native de SCOLIA. Elle n'est jamais détruite à la fermeture : elle se cache, et l'application reste dans la zone de notification.
/// Seule l'adresse de SCOLIA est affichée. Tout autre lien s'ouvre dans le navigateur du poste.
/// </summary>
internal static class NativeWindow
{
    private static Form? window;
    private static WebView2? view;
    private static string? appUrl;
    private static string? dataFolder;
    private static Task? starting;
    private static bool fallback;

    /// <summary>Affiche la fenêtre (appelé sur le thread d'interface).</summary>
    public static void Show(string url, string data)
    {
        appUrl = url;
        dataFolder = data;
        if (fallback)
        {
            DesktopLauncher.OpenInBrowser(url);
            return;
        }
        window ??= CreateWindow();
        if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Maximized;
        window.Show();
        window.Activate();
        starting ??= StartAsync();
    }

    private static Form CreateWindow()
    {
        var form = new Form
        {
            Text = "SCOLIA",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            StartPosition = FormStartPosition.CenterScreen,
            WindowState = FormWindowState.Maximized,
            MinimumSize = new Size(960, 600),
            Size = new Size(1366, 860),
            BackColor = Color.FromArgb(248, 250, 253),
        };
        view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(248, 250, 253) };
        form.Controls.Add(view);
        form.FormClosing += (_, e) =>
        {
            // Fermer la fenêtre ne ferme pas SCOLIA : l'application continue dans la zone de notification.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                form.Hide();
            }
        };
        return form;
    }

    private static async Task StartAsync()
    {
        try
        {
            // Données du moteur (cache, cookies) rangées dans le dossier de SCOLIA, séparées de tout navigateur.
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataFolder!, "navigateur"));
            await view!.EnsureCoreWebView2Async(env);
            var core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            var home = new Uri(appUrl!);
            core.NavigationStarting += (_, e) =>
            {
                if (new Uri(e.Uri).Host == home.Host) return;
                e.Cancel = true;
                DesktopLauncher.OpenInBrowser(e.Uri);
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                DesktopLauncher.OpenInBrowser(e.Uri);
            };
            core.DownloadStarting += (_, e) =>
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                e.ResultFilePath = Path.Combine(downloads, Path.GetFileName(e.ResultFilePath));
                e.Handled = true;
            };
            core.Navigate(appUrl!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Moteur WebView2 indisponible : l'interface s'ouvre dans le navigateur du poste.
            Log.Warning(ex, "Fenêtre native indisponible : ouverture dans le navigateur.");
            fallback = true;
            window?.Hide();
            DesktopLauncher.OpenInBrowser(appUrl!);
        }
    }
}

/// <summary>Icône dans la zone de notification de Windows : ouvrir, accès réseau, dossier des données, quitter.</summary>
internal static class TrayHost
{
    public static void Start(WebApplication app, string url, string dataDirectory, bool lan, int port)
    {
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            // Une défaillance de l'icône ne doit jamais arrêter le serveur ni les autres postes.
            try { Run(app, url, dataDirectory, lan, port, ready); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Icône de notification indisponible : SCOLIA reste accessible à {Url}", url);
                ready.Set();
            }
        })
        { IsBackground = true, Name = "SCOLIA.Notification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Le lancement de la fenêtre attend que le thread d'interface soit prêt.
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    private static void Run(WebApplication app, string url, string dataDirectory, bool lan, int port, ManualResetEventSlim ready)
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        using var context = new System.Windows.Forms.ApplicationContext();
        var sync = new System.Windows.Forms.WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(sync);
        DesktopLauncher.Attach(sync, dataDirectory);
        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Text = "SCOLIA",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir SCOLIA", null, (_, _) => DesktopLauncher.Open(url)).Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add("Ouvrir dans le navigateur", null, (_, _) => DesktopLauncher.OpenInBrowser(url));
        if (lan && DesktopLauncher.LocalAddress() is { } ip)
            menu.Items.Add($"Adresse pour les autres postes : http://{ip}:{port}", null, (_, _) => System.Windows.Forms.Clipboard.SetText($"http://{ip}:{port}"));
        menu.Items.Add("Ouvrir le dossier des données", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dataDirectory}\"") { UseShellExecute = true }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quitter SCOLIA", null, (_, _) => app.Lifetime.StopApplication());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => DesktopLauncher.Open(url);
        app.Lifetime.ApplicationStopping.Register(() => sync.Post(_ => { icon.Visible = false; context.ExitThread(); }, null));
        icon.ShowBalloonTip(4000, "SCOLIA est prêt", lan ? $"Accessible depuis les autres postes du réseau (port {port})." : "Double-cliquez sur cette icône pour ouvrir SCOLIA.", System.Windows.Forms.ToolTipIcon.Info);
        ready.Set();
        System.Windows.Forms.Application.Run(context);
    }
}
#endif
