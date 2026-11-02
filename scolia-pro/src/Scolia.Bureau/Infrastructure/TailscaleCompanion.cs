using System.ComponentModel;
using System.Diagnostics;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Parcours d'activation explicite du client VPN Tailscale.
/// L'utilisateur conserve le choix de l'installation, de la connexion au compte
/// et de l'autorisation VPN : SCOLIA ne modifie jamais ces paramètres silencieusement.
/// </summary>
public static class TailscaleCompanion
{
    public const string WindowsInstallerRelativePath = "Outils/Tailscale-Setup.exe";
    public const string OfficialWindowsDownload = "https://tailscale.com/download/windows";

    public static bool IsInstalled()
    {
        if (Platform.IsTailscaleInstalled is { } check) return check();
        if (!OperatingSystem.IsWindows()) return false;
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Any(folder => File.Exists(Path.Combine(folder, "Tailscale", "tailscale-ipn.exe")));
    }

    /// <summary>La progression réelle appartient au magasin d'applications, pas à SCOLIA.</summary>
    public static string OpenInstallPage()
    {
        if (Platform.OpenTailscaleInstallPage is { } open) return open();
        return Open();
    }


    /// <summary>Demande de connexion discrète, après la première configuration Tailscale.</summary>
    public static async Task<string> ConnectInBackgroundAsync()
    {
        if (Platform.ConnectTailscaleInBackground is { } native)
            return await native().ConfigureAwait(false);
        return "L'activation discrète du VPN n'est pas disponible sur cet appareil.";
    }
    public static string Open()
    {
        if (Platform.OpenTailscale is { } native) return native();
        if (!OperatingSystem.IsWindows())
            return "Ouvrez Tailscale sur cet appareil et rejoignez le réseau privé de l'établissement.";

        try
        {
            foreach (var folder in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale"),
            })
            {
                var gui = Path.Combine(folder, "tailscale-ipn.exe");
                if (!File.Exists(gui)) continue;
                Process.Start(new ProcessStartInfo(gui) { UseShellExecute = true });
                return "Tailscale est ouvert. Connectez-vous au réseau privé, puis revenez dans SCOLIA.";
            }

            // Le paquet Windows officiel de SCOLIA peut contenir l'installateur
            // Tailscale vérifié ; il demeure facultatif et demande l'accord Windows.
            var installer = Path.Combine(AppContext.BaseDirectory,
                WindowsInstallerRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(installer))
            {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                return "Installation de Tailscale ouverte depuis le kit SCOLIA. Autorisez l'installation et la connexion VPN, puis revenez ici.";
            }
            Process.Start(new ProcessStartInfo(OfficialWindowsDownload) { UseShellExecute = true });
            return "Page officielle Tailscale ouverte. Installez-le, connectez votre appareil, puis revenez dans SCOLIA.";
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return "Tailscale ne peut pas être ouvert. Vérifiez l'autorisation de Windows ou ouvrez le dossier Outils du kit SCOLIA.";
        }
    }
}
