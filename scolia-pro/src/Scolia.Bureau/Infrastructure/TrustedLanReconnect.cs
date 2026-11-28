using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Retrouve un serveur deja jumelé apres un changement de Wi-Fi/IP.
/// Les annonces UDP ne sont PAS fiables : chaque candidat doit prouver
/// la possession de la cle de jumelage de CET appareil (defi aleatoire HMAC).
/// Aucune autre ecole ne peut recuperer une session en copiant son nom/ID.
/// </summary>
public static class TrustedLanReconnect
{
    public static async Task<DiscoveredServer?> FindAsync(
        ConnectionSettings? saved, TimeSpan? wait = null, CancellationToken ct = default)
    {
        if (saved is not { IsRemote: true, Url: { Length: > 0 } url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var previous) ||
            TailscaleEndpoint.IsTailnetAddress(previous))
            return null;
        var credential = DevicePairingStore.Read(previous);
        if (credential is null) return null;

        var candidates = await LanDiscovery.FindAsync(wait, ct);
        // Le SchoolId n'est qu'une optimisation ; l'identite reelle vient du HMAC.
        var hasSchoolId = Guid.TryParse(saved.SchoolId, out var schoolId);
        foreach (var server in candidates.Where(server => server.Address != previous)
                     .Where(server => !hasSchoolId ||
                         Guid.TryParse(server.SchoolId, out var candidateId) && candidateId == schoolId))
        {
            ct.ThrowIfCancellationRequested();
            using var probe = new ScoliaClient(server.Address);
            if (!await probe.IsRunningAsync(ct)) continue;
            if (await probe.ProvesPairedServerAsync(credential, ct)) return server;
        }
        return null;
    }

    /// <summary>Copie le droit preexistant vers la nouvelle adresse, sans generer un nouveau droit.</summary>
    public static void PreserveGrant(Uri oldAddress, Uri newAddress)
    {
        if (DevicePairingStore.Read(oldAddress) is { } valid)
            DevicePairingStore.Save(newAddress, valid);
    }
}
