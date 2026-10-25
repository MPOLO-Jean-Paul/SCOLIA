using System.Diagnostics;

namespace Scolia.Bureau;

/// <summary>Serveur SCOLIA livré dans le même dossier que l'application native. Démarré seulement s'il n'est pas déjà actif.</summary>
public sealed class ServerProcess(int port) : Infrastructure.ILocalServer
{
    private Process? process;

    public void Start()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "SCOLIA-Serveur.exe");
        if (!File.Exists(exe) || process is { HasExited: false }) return;
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
        if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
    }
}
