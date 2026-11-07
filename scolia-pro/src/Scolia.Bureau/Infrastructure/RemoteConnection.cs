using System.Text.Json;
using Scolia.Transport;

namespace Scolia.Bureau.Infrastructure;

public sealed record RemoteConfiguration(RemoteLink Link, bool Host, string School, bool Enabled = true);

/// <summary>Configuration privée, séparée des paramètres LAN. Désactiver Internet ne coupe pas le Wi-Fi.</summary>
public static class RemoteConnection
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "liaison-distante.enc");
    private static RemoteHost? host;
    private static readonly object HostGate = new();
    public static bool Hosting { get { lock (HostGate) return host?.Connected == true; } }
    public static RemoteConfiguration? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var saved = JsonSerializer.Deserialize<RemoteConfiguration>(SecretStore.Unprotect(File.ReadAllBytes(FilePath)));
        if (saved is { Host: false, Enabled: true } && saved.Link.ExpiresUtc <= DateTime.UtcNow &&
            DevicePairingStore.Read(saved.Link.VirtualServer) is not null)
        {
            // L'ancien lien a une date limite, contrairement au poste déjà
            // autorisé. Réutiliser exactement la même salle et les mêmes clés.
            saved = saved with { Link = saved.Link with { ExpiresUtc = DateTime.MaxValue } };
            Save(saved);
        }
        return saved;
    }

    public static void Save(RemoteConfiguration configuration) => SecretStore.Save(FilePath, JsonSerializer.SerializeToUtf8Bytes(configuration));

    public static void StartHost(Uri localServer)
    {
        lock (HostGate)
        {
            StopHost();
            StartHostIfMissing(localServer);
        }
    }

    /// <summary>Restaure le relais sortant après un redémarrage, sans interrompre
    /// une connexion déjà en cours de reprise.</summary>
    public static void StartHostIfMissing(Uri localServer)
    {
        lock (HostGate)
        {
            if (host is not null) return;
            if (Load() is { Host: true, Enabled: true } configuration)
            {
                var link = configuration.Link.ExpiresUtc <= DateTime.UtcNow
                    ? configuration.Link.Renew(configuration.Link.Relay) : configuration.Link;
                if (link != configuration.Link) Save(configuration with { Link = link });
                host = new RemoteHost(link, localServer);
            }
        }
    }

    public static void StopHost()
    {
        lock (HostGate)
        {
            host?.Dispose();
            host = null;
        }
    }
    public static void Disable()
    {
        StopHost();
        if (Load() is { } current) Save(current with { Enabled = false });
    }
}
