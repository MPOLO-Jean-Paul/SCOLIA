using System.Text.Json;
using Scolia.Transport;

namespace Scolia.Bureau.Infrastructure;

public sealed record RemoteConfiguration(RemoteLink Link, bool Host, string School, bool Enabled = true);

/// <summary>Configuration privée, séparée des paramètres LAN. Désactiver Internet ne coupe pas le Wi-Fi.</summary>
public static class RemoteConnection
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCOLIA", "liaison-distante.enc");
    private static RemoteHost? host;
    public static bool Hosting => host?.Connected == true;
    public static RemoteConfiguration? Load()
    {
        if (!File.Exists(FilePath)) return null;
        return JsonSerializer.Deserialize<RemoteConfiguration>(SecretStore.Unprotect(File.ReadAllBytes(FilePath)));
    }

    public static void Save(RemoteConfiguration configuration) => SecretStore.Save(FilePath, JsonSerializer.SerializeToUtf8Bytes(configuration));

    public static void StartHost(Uri localServer)
    {
        StopHost();
        if (Load() is { Host: true, Enabled: true } configuration && configuration.Link.ExpiresUtc > DateTime.UtcNow)
            host = new RemoteHost(configuration.Link, localServer);
    }

    public static void StopHost() { host?.Dispose(); host = null; }
    public static void Disable()
    {
        StopHost();
        if (Load() is { } current) Save(current with { Enabled = false });
    }
}
