using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Relier un appareil par code QR : ce que le serveur montre, ce que l'autre appareil accepte.</summary>
public sealed class PairingCodeTests
{
    [Fact]
    public void Le_code_du_serveur_se_relit_a_l_identique_wifi_avant_tailscale()
    {
        var code = PairingCode.ForThisServer("Complexe Scolaire Les Étoiles", "SECRETARIAT",
            ["http://100.101.2.3:5080", "http://192.168.1.10:5080", "http://8.8.8.8:5080"], invitation: null);
        Assert.True(PairingCode.TryParse(code.Text(), out var read, out _));
        Assert.Equal("Complexe Scolaire Les Étoiles", read!.School);
        Assert.Equal("SECRETARIAT", read.Machine);
        // Le Wi-Fi d'abord, Tailscale ensuite ; l'adresse publique n'est jamais mise dans le code.
        Assert.Equal(["192.168.1.10", "100.101.2.3"], read.Addresses.Select(a => a.Host));
        Assert.Single(read.LanAddresses);
        Assert.Single(read.TailnetAddresses);
    }

    [Theory]
    [InlineData("scolia://relier?v=1&e=X&a=8.8.8.8:5080")]
    [InlineData("scolia://relier?v=1&e=X&a=exemple.com:5080")]
    [InlineData("https://exemple.com/scolia")]
    [InlineData("scolia://relier?v=2&e=X&a=192.168.1.10:5080")]
    [InlineData("")]
    public void Un_code_etranger_ou_vers_internet_est_refuse(string text)
    {
        Assert.False(PairingCode.TryParse(text, out var code, out var error));
        Assert.Null(code);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Une_invitation_de_compte_est_preservee_sur_wifi_et_tailscale()
    {
        var ticket = new string('A', 48);
        var sent = PairingCode.ForThisServer("ITM", "SERVEUR",
            ["http://100.101.2.3:5080", "http://192.168.2.9:5080"], null) with { AccessTicket = ticket };
        Assert.True(PairingCode.TryParse(sent.Text(), out var read, out _));
        Assert.Equal(ticket, read!.AccessTicket);
        Assert.Single(read.LanAddresses);
        Assert.Single(read.TailnetAddresses);
    }

    [Fact]
    public void Un_jeton_d_invitation_malforme_est_refuse_plutot_que_devenir_un_simple_lien()
    {
        Assert.False(PairingCode.TryParse("scolia://relier?v=1&e=ITM&a=192.168.1.9:5080&p=malforme",
            out _, out var message));
        Assert.Contains("invalide", message);
    }

    [Fact]
    public void Le_port_par_defaut_et_les_noms_tailscale_sont_compris()
    {
        Assert.True(PairingCode.TryParse("scolia://relier?v=1&e=%C3%89cole&a=10.0.0.5,machine.tailnet.ts.net:5099", out var code, out _));
        Assert.Equal("École", code!.School);
        Assert.Equal(PosteAdmin.DefaultPort, code.Addresses[0].Port);
        Assert.Equal(5099, code.Addresses[1].Port);
        Assert.True(TailscaleEndpoint.IsTailnetAddress(code.Addresses[1]));
    }

    [Fact]
    public void Le_qr_dessine_se_relit_dans_l_image()
    {
        var text = PairingCode.ForThisServer("CSE", "POSTE-1", ["http://192.168.1.10:5080"], null).Text();
        Assert.Equal(text, QrCodes.Read(QrCodes.Png(text)));
    }

    [Fact]
    public void Une_image_sans_code_ne_donne_rien()
    {
        Assert.Null(QrCodes.Read(new byte[QrCodes.Png("x").Length]));
    }
}
