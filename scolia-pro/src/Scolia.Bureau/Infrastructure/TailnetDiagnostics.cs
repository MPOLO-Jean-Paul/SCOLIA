namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Le VPN actif d'Android ne prouve pas que Tailscale est connecté ;
/// un serveur qui répond en local ne garantit pas non plus sa joignabilité distante.
/// </summary>
public static class TailnetDiagnostics
{
    public static string DescribeServer(bool installed, bool? androidVpnActive, bool localServerReady)
    {
        var installation = installed ? "Tailscale installé" : "Tailscale non installé";
        var vpn = androidVpnActive switch
        {
            true => "un VPN Android actif est détecté (identité non confirmée)",
            false => "aucun VPN actif détecté sur le réseau principal",
            null => "état VPN non disponible sur cet appareil",
        };
        var server = localServerReady
            ? "serveur SCOLIA répond localement"
            : "serveur SCOLIA indisponible localement";
        return $"{installation} · {vpn} · {server}. Vérifiez la liaison depuis un autre appareil autorisé avant de considérer le partage comme opérationnel.";
    }
}