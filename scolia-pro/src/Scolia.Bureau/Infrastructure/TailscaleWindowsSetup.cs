using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Installation de Tailscale sur Windows à partir du fichier téléchargé par SCOLIA : Windows vérifie d'abord la
/// signature numérique (intacte, de confiance, éditeur Tailscale), puis l'installateur officiel s'ouvre ; Windows
/// demande alors l'accord de l'administrateur. SCOLIA attend la fin et vérifie que Tailscale est bien installé.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TailscaleWindowsSetup
{
    public static async Task<TailscaleInstallResult> InstallAsync(string path)
    {
        if (!IsTrustedTailscaleSignature(path, out var why))
            return new(TailscaleInstallOutcome.Refused, $"Installation refusée : {why} Le fichier a été supprimé.");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
            if (process is null)
                return new(TailscaleInstallOutcome.Failed, "Windows n'a pas ouvert l'installateur de Tailscale.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            return TailscaleCompanion.IsInstalled()
                ? new(TailscaleInstallOutcome.Installed, "Tailscale est installé sur cet ordinateur.")
                : new(TailscaleInstallOutcome.WaitingForUser, "L'installateur est fermé, mais Tailscale n'est pas encore détecté. Terminez l'installation ou relancez-la.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED : l'utilisateur a refusé l'accord administrateur.
            return new(TailscaleInstallOutcome.Refused, "Installation annulée : Windows a besoin de votre accord d'administrateur pour installer Tailscale.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new(TailscaleInstallOutcome.Failed, $"Windows n'a pas pu lancer l'installateur : {ex.Message}");
        }
    }

    /// <summary>Signature Authenticode valide, chaîne de confiance Windows, éditeur « Tailscale ».</summary>
    public static bool IsTrustedTailscaleSignature(string path, out string reason)
    {
        if (!VerifyAuthenticode(path))
        {
            reason = "la signature numérique du fichier n'est pas valide.";
            return false;
        }
        try
        {
#pragma warning disable SYSLIB0057 // Lecture du seul certificat signataire d'un exécutable déjà vérifié par WinVerifyTrust.
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            var publisher = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            if (!publisher.Contains("Tailscale", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"le fichier est signé par « {publisher} », pas par Tailscale.";
                return false;
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            reason = "le fichier ne porte pas de signature lisible.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool VerifyAuthenticode(string path)
    {
        var fileInfo = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = Marshal.StringToCoTaskMemUni(path) };
        var fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        var dataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var data = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,           // WTD_UI_NONE
                RevocationChecks = 0,   // WTD_REVOKE_NONE : ne bloque pas un poste hors ligne
                UnionChoice = 1,        // WTD_CHOICE_FILE
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProvFlags = 0x10,       // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            Marshal.StructureToPtr(data, dataPointer, false);
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, dataPointer) == 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(fileInfo.FilePath);
            Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(dataPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, IntPtr data);
}
