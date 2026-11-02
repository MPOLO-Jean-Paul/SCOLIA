namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// État public accessible sur Android, sans lecture des comptes ni de la base privée
/// de Tailscale. Une IP du CGNAT peut aussi appartenir à un autre VPN.
/// </summary>
public sealed record TailnetSnapshot(bool Installed, bool? VpnActive, string? CandidateIp)
{
    public bool HasCandidateIp => IsCandidateTailscaleIp(CandidateIp);
    public bool LikelyConnected => Installed && VpnActive == true && HasCandidateIp;

    public static bool IsCandidateTailscaleIp(string? raw)
    {
        if (!System.Net.IPAddress.TryParse(raw, out var ip)) return false;
        var octets = ip.GetAddressBytes();
        return octets.Length == 4 && octets[0] == 100 && octets[1] is >= 64 and <= 127
            && raw != "100.100.100.100"; // DNS local Tailscale, pas IP d'un téléphone.
    }

    public string Summary => this switch
    {
        { Installed: true, VpnActive: true, HasCandidateIp: true } =>
            $"VPN privé détecté : {CandidateIp} (adresse probable Tailscale, non certifiée).",
        { Installed: true, VpnActive: true } =>
            "Tailscale installé, VPN actif ; adresse privée non disponible. Vérifiez la liaison.",
        { Installed: true, VpnActive: false } =>
            "Tailscale installé, mais aucun VPN actif n'est détecté.",
        { Installed: true } =>
            "Tailscale installé. Statut de la connexion non disponible sur ce téléphone.",
        { Installed: false, VpnActive: true, HasCandidateIp: true } =>
            $"VPN privé présent : {CandidateIp}. Le paquet Tailscale n'a pas pu être confirmé.",
        _ => "Tailscale non détecté. Installez-le depuis sa source officielle si nécessaire."
    };
}
