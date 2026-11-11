using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class TailnetDiagnosticsTests
{
    [Fact]
    public void Vpn_actif_ne_prouve_pas_Tailscale_connecte()
    {
        var result = TailnetDiagnostics.DescribeServer(true, true, true);
        Assert.Contains("identité non confirmée", result);
        Assert.Contains("localement", result);
        Assert.DoesNotContain("Tailscale connecté", result);
    }

    [Fact]
    public void Panne_du_serveur_est_visible()
    {
        var result = TailnetDiagnostics.DescribeServer(true, false, false);
        Assert.Contains("aucun VPN actif", result);
        Assert.Contains("indisponible", result);
    }

    [Fact]
    public void Tailscale_absent_ne_devient_pas_un_succes()
    {
        var result = TailnetDiagnostics.DescribeServer(false, null, true);
        Assert.Contains("non installé", result);
        Assert.Contains("non disponible", result);
        Assert.DoesNotContain("Tailscale connecté", result);
    }
}