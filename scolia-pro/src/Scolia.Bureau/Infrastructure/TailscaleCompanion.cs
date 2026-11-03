using System.ComponentModel;
using System.Diagnostics;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Ouverture explicite de l'assistant officiel Tailscale. Sous Windows, tailscale-ipn.exe
/// n'affiche normalement qu'une icône dans la zone de notification : le lancer seul donnait
/// l'impression que « Configurer » ne faisait rien. La CLI officielle ouvre, elle, le
/// navigateur d'authentification et garde un terminal visible en cas d'erreur.
/// </summary>
public static class TailscaleCompanion
{
    public const string WindowsInstallerRelativePath = "Outils/Tailscale-Setup.exe";
    public const string OfficialWindowsDownload = "https://tailscale.com/download/windows";

    private static IEnumerable<string> InstallDirectories()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            yield return Path.Combine(folder, "Tailscale");
    }

    public static string? FindWindowsExecutable(string fileName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        return InstallDirectories().Select(folder => Path.Combine(folder, fileName)).FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled()
    {
        if (Platform.IsTailscaleInstalled is { } check) return check();
        return FindWindowsExecutable("tailscale-ipn.exe") is not null
            || FindWindowsExecutable("tailscale.exe") is not null;
    }

    public static string OpenInstallPage()
    {
        if (Platform.OpenTailscaleInstallPage is { } open) return open();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                Process.Start(new ProcessStartInfo(OfficialWindowsDownload) { UseShellExecute = true });
                return "Page officielle de téléchargement Tailscale ouverte.";
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
            { return $"Impossible d'ouvrir le site officiel : {ex.Message}"; }
        }
        return Open();
    }

    public static async Task<string> ConnectInBackgroundAsync()
    {
        if (Platform.ConnectTailscaleInBackground is { } native)
            return await native().ConfigureAwait(false);
        return "L'activation discrète du VPN n'est pas disponible sur cet appareil.";
    }

    /// <summary>Commande PowerShell visible. Apostrophes échappées pour empêcher l'interprétation du chemin.</summary>
    public static string WindowsLoginCommand(string executablePath) =>
        $"& '{executablePath.Replace("'", "''", StringComparison.Ordinal)}' login";

    public static string Open()
    {
        if (Platform.OpenTailscale is { } native) return native();
        if (!OperatingSystem.IsWindows())
            return "Ouvrez Tailscale sur cet appareil, connectez-vous et rejoignez le réseau privé de l'établissement.";

        try
        {
            var cli = FindWindowsExecutable("tailscale.exe");
            var gui = FindWindowsExecutable("tailscale-ipn.exe");
            if (cli is not null)
            {
                // Le composant graphique Windows est une application de la zone de notification,
                // pas une fenêtre principale. On le lance, puis on démarre une commande de
                // connexion explicite et visible qui ouvre le navigateur d'identification.
                if (gui is not null)
                    Process.Start(new ProcessStartInfo(gui) { UseShellExecute = true });

                var login = new ProcessStartInfo("powershell.exe") { UseShellExecute = true };
                login.ArgumentList.Add("-NoProfile");
                login.ArgumentList.Add("-NoExit");
                login.ArgumentList.Add("-Command");
                login.ArgumentList.Add(WindowsLoginCommand(cli));
                if (Process.Start(login) is null)
                    return "Tailscale est installé, mais Windows n'a pas ouvert l'assistant. Cliquez sur l'icône Tailscale près de l'horloge, puis « Log in ».";
                return "Fenêtre de connexion Tailscale ouverte. Autorisez l'accès dans le navigateur, puis revenez dans SCOLIA et appuyez sur « Vérifier à nouveau ». La fenêtre de diagnostic peut ensuite être fermée.";
            }

            if (gui is not null)
            {
                Process.Start(new ProcessStartInfo(gui) { UseShellExecute = true });
                return "Tailscale est lancé dans la zone de notification de Windows, à côté de l'horloge (flèche ^ si masqué). Cliquez sur son icône, puis sur « Log in » pour ouvrir la connexion dans le navigateur.";
            }

            var installer = Path.Combine(AppContext.BaseDirectory,
                WindowsInstallerRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(installer))
            {
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                return "Installation officielle de Tailscale ouverte depuis le kit SCOLIA. Après installation, cliquez à nouveau sur « Configurer Tailscale ».";
            }

            Process.Start(new ProcessStartInfo(OfficialWindowsDownload) { UseShellExecute = true });
            return "La page officielle de Tailscale s'est ouverte. Installez le logiciel puis relancez la configuration dans SCOLIA.";
        }
        catch (Win32Exception ex)
        {
            return ex.NativeErrorCode == 1223
                ? "Windows a refusé l'ouverture de Tailscale. Vérifiez l'autorisation demandée et réessayez."
                : $"Windows n'a pas pu ouvrir la configuration de Tailscale ({ex.Message}). Ouvrez son icône près de l'horloge et sélectionnez « Log in ».";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"La fenêtre de Tailscale n'a pas pu être ouverte : {ex.Message}. Essayez son icône près de l'horloge.";
        }
    }
}
