using System.Diagnostics;

namespace Scolia.Bureau;

/// <summary>Serveur SCOLIA livré dans le même dossier que l'application native. Démarré seulement s'il n'est pas déjà actif.</summary>
public sealed class ServerProcess(int port)
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

    /// <summary>Arrête le serveur seulement s'il a été lancé par cette application.</summary>
    public void Stop()
    {
        if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
    }
}
