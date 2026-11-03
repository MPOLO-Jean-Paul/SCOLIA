using System.Diagnostics;

namespace Scolia.Bureau;

/// <summary>Serveur SCOLIA livré dans le même dossier que l'application native. Démarré seulement s'il n'est pas déjà actif.</summary>
public sealed class ServerProcess(int port) : Infrastructure.ILocalServer
{
    private readonly object gate = new();
    private Process? process;
    private System.Threading.Timer? watchdog;
    private bool keepServing;

    /// <summary>La disponibilité du serveur ne dépend pas de l'état visuel de la fenêtre.</summary>
    public static bool ShouldRestore(bool configuredAsServer, bool intentionalStop, bool processAlive)
        => configuredAsServer && !intentionalStop && !processAlive;

    public void Start()
    {
        lock (gate)
        {
            keepServing = true;
            LaunchIfMissing();
            // Le contrôleur est indépendant de la fenêtre : réduire SCOLIA ne suspend
            // pas la supervision du serveur. Un service Windows installé assure lui-même
            // sa reprise via le gestionnaire de services.
            watchdog ??= new System.Threading.Timer(_ => CheckInBackground(), null,
                TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }
    }

    private void LaunchIfMissing()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "SCOLIA-Serveur.exe");
        if (!File.Exists(exe) || process is { HasExited: false }) return;
        process?.Dispose();
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        info.Environment["Scolia__Reseau__Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["Scolia__OuvrirNavigateur"] = "false";
        info.Environment["Scolia__Icone"] = "false";
        process = Process.Start(info);
    }

    private void CheckInBackground()
    {
        lock (gate)
        {
            var saved = Infrastructure.ConnectionStore.Load();
            var locallyConfigured = saved?.Mode == Infrastructure.ConnectionSettings.Local
                || (saved is null && Infrastructure.ConnectionStore.HasLocalData());
            if (!ShouldRestore(locallyConfigured, !keepServing, process is { HasExited: false })) return;
            try
            {
                // Ne pas démarrer un second hébergeur si Windows supervise le service.
                if (OperatingSystem.IsWindows() && Infrastructure.PosteAdmin.ServiceInstalled()) return;
                LaunchIfMissing();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Trace.WriteLine($"SCOLIA : serveur Windows non rétabli : {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Relance le serveur de ce dossier pour qu'il prenne un nouveau réglage réseau tout de suite.
    /// Un serveur lancé par une autre session de SCOLIA sur ce poste est relancé aussi.
    /// </summary>
    public void Restart()
    {
        Stop();
        var exe = Path.Combine(AppContext.BaseDirectory, "SCOLIA-Serveur.exe");
        foreach (var other in Process.GetProcessesByName("SCOLIA-Serveur"))
        {
            try
            {
                if (string.Equals(other.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                {
                    other.Kill(entireProcessTree: true);
                    other.WaitForExit(10000);
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            finally { other.Dispose(); }
        }
        process?.WaitForExit(10000);
        process = null;
        Start();
    }

    /// <summary>Arrête le serveur seulement s'il a été lancé par cette application.</summary>
    public void Stop()
    {
        lock (gate)
        {
            // Seul l'arrêt explicite de SCOLIA ou la sortie du mode serveur coupe
            // le processus. La réduction ou le verrouillage de Windows ne l'appelle pas.
            keepServing = false;
            watchdog?.Dispose();
            watchdog = null;
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
    }
}
