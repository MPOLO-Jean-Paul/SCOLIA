using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Réglages du poste propres à SCOLIA sous Windows : démarrage automatique (service) et accès depuis le réseau local.
/// Tout est fait par SCOLIA lui-même, sans script ni PowerShell. Les opérations système demandent les droits d'administrateur :
/// SCOLIA se relance alors avec l'élévation Windows (UAC) et rend compte du résultat.
/// </summary>
public static class PosteAdmin
{
    public const string ServiceName = "SCOLIA";
    public const string ServiceTitle = "SCOLIA Pro";
    public const string ServiceDescription = "Serveur de gestion scolaire SCOLIA Pro (secrétariat, caisse, scolarité).";
    public const string FirewallRule = "SCOLIA Pro (réseau local)";
    private const string ServiceAccount = @"NT SERVICE\SCOLIA";
    private const int DefaultPort = 5080;

    /// <summary>Serveur de SCOLIA, livré à côté de l'interface.</summary>
    public static string ServerExecutable => Path.Combine(AppContext.BaseDirectory, "SCOLIA.exe");

    private static string ConfigFile => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    /// <summary>Lit le réglage d'accès réseau local dans la configuration du serveur.</summary>
    public static bool LanEnabled() => ReadServerSetting("AccesReseauLocal") is bool enabled && enabled;

    /// <summary>Port d'écoute du serveur (5080 par défaut).</summary>
    public static int Port() => ReadServerSetting("Port") is int port && port > 0 ? port : DefaultPort;

    /// <summary>Adresses IPv4 du poste sur le réseau local, à donner aux autres postes.</summary>
    public static IReadOnlyList<string> Addresses()
    {
        var found = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = address.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                if (ip.GetAddressBytes() is [169, 254, ..]) continue;
                found.Add($"http://{ip}:{Port()}");
            }
        }
        return found;
    }

    /// <summary>Vrai si ce processus a les droits d'administrateur.</summary>
    [SupportedOSPlatform("windows")]
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Vrai si le service Windows de SCOLIA est installé sur ce poste (aucun droit particulier requis).</summary>
    [SupportedOSPlatform("windows")]
    public static bool ServiceInstalled()
    {
        var manager = OpenScm(ScmConnect);
        try
        {
            var service = OpenService(manager, ServiceName, ServiceQueryStatus);
            if (service != IntPtr.Zero)
            {
                CloseServiceHandle(service);
                return true;
            }
            if (Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist) return false;
            throw Failure("Lecture du service");
        }
        finally { CloseServiceHandle(manager); }
    }

    /// <summary>
    /// Demande une opération d'administration. Si SCOLIA n'est pas élevé, il se relance avec l'élévation Windows,
    /// fait l'opération, puis rend le résultat à cette fenêtre.
    /// </summary>
    public static async Task<string> RunAsync(string action, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Cette action est disponible sous Windows uniquement.");
        if (IsElevated()) return await Task.Run(() => Execute(action), ct);

        var result = Path.Combine(Path.GetTempPath(), $"scolia-poste-{Guid.NewGuid():N}.txt");
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!, $"--poste {action} --resultat \"{result}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var process = Process.Start(info) ?? throw new InvalidOperationException("SCOLIA n'a pas pu demander les droits d'administrateur.");
            await process.WaitForExitAsync(ct);
            var lines = File.Exists(result) ? await File.ReadAllLinesAsync(result, ct) : [];
            if (lines is ["OK", ..]) return string.Join(Environment.NewLine, lines.Skip(1)).Trim();
            throw new InvalidOperationException(lines.Length > 1 ? string.Join(Environment.NewLine, lines.Skip(1)) : "L'opération n'a pas abouti.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Opération annulée : les droits d'administrateur n'ont pas été accordés.", ex);
        }
        finally { try { File.Delete(result); } catch (IOException) { } }
    }

    /// <summary>Point d'entrée du processus élevé : exécute l'action et écrit le résultat pour le processus appelant.</summary>
    public static int RunElevatedCommand(string action, string resultFile)
    {
        try
        {
            var message = Execute(action);
            File.WriteAllText(resultFile, "OK" + Environment.NewLine + message);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            File.WriteAllText(resultFile, "ERREUR" + Environment.NewLine + ex.Message);
            return 1;
        }
    }

    private static string Execute(string action)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Cette action est disponible sous Windows uniquement.");
        return action switch
        {
            "service-installer" => InstallService(),
            "service-desinstaller" => UninstallService(),
            "reseau-on" => SetLanAccess(true),
            "reseau-off" => SetLanAccess(false),
            _ => throw new ArgumentException($"Action inconnue : {action}"),
        };
    }

    [SupportedOSPlatform("windows")]
    private static string InstallService()
    {
        if (ServiceInstalled()) return "Le service SCOLIA est déjà installé.";
        if (IsServerRunningFromHere()) throw new InvalidOperationException("SCOLIA est ouvert sur ce poste. Fermez-le depuis son icône de notification, puis relancez cette installation.");

        var dataFolder = DataFolder();
        Directory.CreateDirectory(dataFolder);

        var manager = OpenScm(ScmAllAccess);
        try
        {
            // Compte virtuel dédié au service : il ne reçoit que les droits nécessaires (moindre privilège).
            var service = CreateService(manager, ServiceName, ServiceTitle, ServiceAllAccess, ServiceWin32OwnProcess, ServiceAutoStart,
                ServiceErrorNormal, $"\"{ServerExecutable}\"", null, IntPtr.Zero, null, ServiceAccount, null);
            if (service == IntPtr.Zero) throw Failure("Création du service");
            try
            {
                SetDescription(service, ServiceDescription);
                SetRestartOnFailure(service);
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }

        GrantAccess(AppContext.BaseDirectory, FileSystemRights.ReadAndExecute);
        GrantAccess(dataFolder, FileSystemRights.Modify);
        StartServiceNow();
        return $"Service SCOLIA installé et démarré. SCOLIA démarre avec Windows, même sans session ouverte.{Environment.NewLine}Adresse sur ce poste : http://localhost:{Port()}";
    }

    [SupportedOSPlatform("windows")]
    private static string UninstallService()
    {
        if (!ServiceInstalled()) return "Le service SCOLIA n'est pas installé.";
        var manager = OpenScm(ScmAllAccess);
        try
        {
            var service = OpenService(manager, ServiceName, ServiceStop | ServiceQueryStatus | Delete);
            if (service == IntPtr.Zero) throw Failure("Ouverture du service");
            try
            {
                var status = Marshal.AllocHGlobal(ServiceStatusSize);
                try { ControlService(service, ServiceControlStop, status); }
                finally { Marshal.FreeHGlobal(status); }
                if (!DeleteService(service)) throw Failure("Suppression du service");
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
        return "Service SCOLIA supprimé. Les données sont conservées.";
    }

    private static string SetLanAccess(bool enabled)
    {
        var root = JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject
            ?? throw new InvalidOperationException("La configuration de SCOLIA est illisible.");
        var reseau = root["Scolia"]?["Reseau"] as JsonObject
            ?? throw new InvalidOperationException("La configuration de SCOLIA ne contient pas la rubrique réseau.");
        reseau["AccesReseauLocal"] = enabled;
        File.WriteAllText(ConfigFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Règle de pare-feu limitée au port de SCOLIA et aux réseaux privés et de domaine (jamais aux réseaux publics).
        DeleteFirewallRule();
        if (enabled) AddFirewallRule();

        var restart = OperatingSystem.IsWindows() && ServiceInstalled() ? " Le service SCOLIA prend le réglage au prochain démarrage." : " Le réglage prendra effet au prochain démarrage de SCOLIA.";
        return enabled
            ? $"Accès depuis le réseau local activé (réseaux privés et de domaine).{restart}"
            : $"Accès depuis le réseau local désactivé : SCOLIA ne répond plus que sur ce poste.{restart}";
    }

    private static void AddFirewallRule() =>
        RunNetsh($"advfirewall firewall add rule name=\"{FirewallRule}\" dir=in action=allow protocol=TCP localport={Port()} profile=private,domain");

    private static void DeleteFirewallRule() =>
        RunNetsh($"advfirewall firewall delete rule name=\"{FirewallRule}\"", ignoreFailure: true);

    private static void RunNetsh(string arguments, bool ignoreFailure = false)
    {
        // Netsh est l'outil réseau intégré à Windows : la ligne de commande est passée telle quelle pour garder les guillemets.
        using var process = Process.Start(new ProcessStartInfo("netsh.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Le pare-feu Windows est inaccessible.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException($"Pare-feu Windows : {output.Trim()}");
    }

    [SupportedOSPlatform("windows")]
    private static void StartServiceNow()
    {
        var manager = OpenScm(ScmConnect);
        try
        {
            var service = OpenService(manager, ServiceName, ServiceStart);
            if (service == IntPtr.Zero) throw Failure("Ouverture du service");
            try
            {
                // Déjà démarré : l'erreur 1056 n'empêche pas l'installation.
                if (!StartService(service, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != ErrorServiceAlreadyRunning)
                    throw Failure("Démarrage du service");
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [SupportedOSPlatform("windows")]
    private static void GrantAccess(string folder, FileSystemRights rights)
    {
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new NTAccount(ServiceAccount), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void SetDescription(IntPtr service, string description)
    {
        var text = Marshal.StringToHGlobalUni(description);
        var info = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(info, text);
            if (!ChangeServiceConfig2(service, ServiceConfigDescription, info)) throw Failure("Description du service");
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            Marshal.FreeHGlobal(text);
        }
    }

    /// <summary>Redémarrage automatique en cas d'arrêt inattendu : 5 s, 15 s, puis 60 s, compteur remis à zéro chaque jour.</summary>
    [SupportedOSPlatform("windows")]
    private static void SetRestartOnFailure(IntPtr service)
    {
        uint[] delays = [5000, 15000, 60000];
        var actionSize = Marshal.SizeOf<ScAction>();
        var actions = Marshal.AllocHGlobal(actionSize * delays.Length);
        var failure = Marshal.AllocHGlobal(Marshal.SizeOf<ServiceFailureActions>());
        try
        {
            for (var i = 0; i < delays.Length; i++)
                Marshal.StructureToPtr(new ScAction { Type = ScActionRestart, Delay = delays[i] }, IntPtr.Add(actions, i * actionSize), false);
            Marshal.StructureToPtr(new ServiceFailureActions
            {
                ResetPeriod = 86400,
                Count = (uint)delays.Length,
                Actions = actions,
            }, failure, false);
            if (!ChangeServiceConfig2(service, ServiceConfigFailureActions, failure)) throw Failure("Redémarrage automatique");
        }
        finally
        {
            Marshal.FreeHGlobal(failure);
            Marshal.FreeHGlobal(actions);
        }
    }

    private static bool IsServerRunningFromHere()
    {
        foreach (var process in Process.GetProcessesByName("SCOLIA"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, ServerExecutable, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Win32Exception) { }
            finally { process.Dispose(); }
        }
        return false;
    }

    private static string DataFolder()
    {
        var configured = ReadServerSetting("DataDirectory") as string;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "donnees")
            : Environment.ExpandEnvironmentVariables(configured);
    }

    /// <summary>Lit un réglage de la rubrique Scolia:Reseau, ou Scolia:Stockage pour DataDirectory.</summary>
    private static object? ReadServerSetting(string key)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(ConfigFile)) as JsonObject;
            var scolia = root?["Scolia"] as JsonObject;
            var section = key == "DataDirectory" ? scolia?["Stockage"] : scolia?["Reseau"];
            var node = section?[key];
            if (node is null) return null;
            return key switch
            {
                "AccesReseauLocal" => node.GetValue<bool>(),
                "Port" => node.GetValue<int>(),
                _ => node.GetValue<string>(),
            };
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static Win32Exception Failure(string step)
    {
        var code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{step} impossible : {new Win32Exception(code).Message}");
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr OpenScm(uint access)
    {
        var manager = OpenSCManager(null, null, access);
        return manager != IntPtr.Zero ? manager : throw Failure("Gestionnaire de services");
    }

    // Constantes de l'API Windows (advapi32).
    private const uint ScmConnect = 0x0001;
    private const uint ScmAllAccess = 0xF003F;
    private const uint ServiceAllAccess = 0xF01FF;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceStart = 0x0010;
    private const uint ServiceStop = 0x0020;
    private const uint Delete = 0x10000;
    private const uint ServiceWin32OwnProcess = 0x10;
    private const uint ServiceAutoStart = 0x2;
    private const uint ServiceErrorNormal = 0x1;
    private const uint ServiceConfigDescription = 1;
    private const uint ServiceConfigFailureActions = 2;
    private const uint ServiceControlStop = 1;
    private const uint ScActionRestart = 1;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ServiceStatusSize = 28;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction
    {
        public uint Type;
        public uint Delay;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceFailureActions
    {
        public uint ResetPeriod;
        public IntPtr RebootMessage;
        public IntPtr Command;
        public uint Count;
        public IntPtr Actions;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint access);

    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateService(IntPtr manager, string name, string displayName, uint access, uint type, uint start,
        uint errorControl, string binaryPath, string? loadOrderGroup, IntPtr tagId, string? dependencies, string? account, string? password);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, IntPtr info);

    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartService(IntPtr service, uint argc, IntPtr argv);

    [DllImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, IntPtr status);

    [DllImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
