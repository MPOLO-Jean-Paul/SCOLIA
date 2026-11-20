using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Relier un appareil par code QR : ce que le serveur montre, ce que l'autre appareil accepte.</summary>
public sealed class PairingCodeTests
{
    [Fact]
    public void Le_code_du_serveur_se_relit_avec_wifi_et_sans_vpn()
    {
        var code = PairingCode.ForThisServer("Complexe Scolaire Les Étoiles", "SECRETARIAT",
            ["http://100.101.2.3:5080", "http://192.168.1.10:5080", "http://8.8.8.8:5080"], invitation: null);
        Assert.True(PairingCode.TryParse(code.Text(), out var read, out _));
        Assert.Equal("Complexe Scolaire Les Étoiles", read!.School);
        Assert.Equal("SECRETARIAT", read.Machine);
        // Un nouveau QR ne contient ni l'adresse VPN ni l'adresse publique.
        Assert.Equal(["192.168.1.10"], read.Addresses.Select(a => a.Host));
        Assert.Single(read.LanAddresses);
        Assert.Empty(read.TailnetAddresses);
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
    public void Une_invitation_de_compte_est_preservee_sur_wifi_sans_vpn()
    {
        var ticket = new string('A', 48);
        var sent = PairingCode.ForThisServer("ITM", "SERVEUR",
            ["http://100.101.2.3:5080", "http://192.168.2.9:5080"], null) with { AccessTicket = ticket };
        Assert.True(PairingCode.TryParse(sent.Text(), out var read, out _));
        Assert.Equal(ticket, read!.AccessTicket);
        Assert.Single(read.LanAddresses);
        Assert.Empty(read.TailnetAddresses);
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
    public void Un_ancien_QR_VPN_reste_lisible_mais_n_est_plus_genere()
    {
        var ancien = "scolia://relier?v=1&e=ITM&a=192.168.1.10:5080,100.101.2.3:5080";
        Assert.True(PairingCode.TryParse(ancien, out var lu, out _));
        Assert.Single(lu!.LanAddresses);
        Assert.Single(lu.TailnetAddresses);
        var nouveau = PairingCode.ForThisServer("ITM", "PC", [
            "http://192.168.1.10:5080", "http://100.101.2.3:5080"], null);
        Assert.Single(nouveau.LanAddresses);
        Assert.Empty(nouveau.TailnetAddresses);
    }

    [Fact]
    public void Le_qr_dessine_se_relit_dans_l_image()
    {
        var text = PairingCode.ForThisServer("CSE", "POSTE-1", ["http://192.168.1.10:5080"], null).Text();
        Assert.Equal(text, QrCodes.Read(QrCodes.Png(text)));
    }

    [Fact]
    public void Une_photo_decran_importee_recupere_le_QR_du_serveur()
    {
        var code = PairingCode.ForThisServer("ITM Coeur de Dieu", "SCOLIA-BUREAU",
            ["http://192.168.1.16:5080", "http://100.102.45.61:5080"], null)
            with { AccessTicket = new string('F', 48) };
        using var qr = SkiaSharp.SKBitmap.Decode(QrCodes.Png(code.Text(), 10));
        Assert.NotNull(qr);
        using var scene = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(1600, 900,
            SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
        using (var canvas = new SkiaSharp.SKCanvas(scene))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.DrawBitmap(qr, new SkiaSharp.SKRect(550, 160, 970, 580));
            canvas.Flush();
        }
        using var preview = SkiaSharp.SKImage.FromBitmap(scene);
        using var jpeg = preview.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 93);
        var text = QrCodes.Read(jpeg.ToArray());
        Assert.Equal(code.Text(), text);
        Assert.True(PairingCode.TryParse(text, out var parsed, out _));
        Assert.Single(parsed!.LanAddresses);
        Assert.Empty(parsed.TailnetAddresses);
    }

    [Fact]
    public void QR_dense_d_un_vrai_jumelage_avec_trois_adresses_et_ticket_est_lisible()
    {
        var code = PairingCode.ForThisServer("Institut Technique Médical Cœur de Dieu", "ORDINATEUR PRINCIPAL",
            ["http://192.168.1.15:5080", "http://192.168.43.1:5080",
             "http://100.101.102.103:5080", "http://10.0.0.20:5080"], null)
            with { AccessTicket = new string('C', 48) };
        Assert.Equal(3, code.Addresses.Count);
        var text = code.Text();
        Assert.InRange(text.Length, 100, 950);
        Assert.Equal(text, QrCodes.Read(QrCodes.Png(text, 10)));
        Assert.True(PairingCode.TryParse(text, out var parsed, out _));
        Assert.Equal(code.AccessTicket, parsed!.AccessTicket);
    }

    [Fact]
    public void Une_image_sans_code_ne_donne_rien()
    {
        Assert.Null(QrCodes.Read(new byte[QrCodes.Png("x").Length]));
    }
}
