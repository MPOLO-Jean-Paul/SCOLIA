using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Scolia.Tests;

/// <summary>L'invitation ouvre un compte sans copier le mot de passe ; l'appareil garde une autorisation révocable.</summary>
public sealed class DevicePairingTests
{
    [Fact]
    public async Task Un_qr_approuve_l_appareil_mais_ne_connecte_jamais_un_compte()
    {
        using var app = new ScoliaApp();
        using var host = await app.InstalledAdmin();
        var invite = await host.Post("/api/jumelage/inviter", new { }).Ok();
        var token = invite.GetProperty("token").GetString()!;

        using var guest = app.Client();
        const string deviceId = "appareil-client-test-01";
        guest.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var joined = await guest.Post("/api/jumelage/accepter", new {
            invitationToken = token, deviceId, deviceName = "Appareil test"
        }).Ok();
        Assert.Equal("Institut de test", joined.GetProperty("schoolName").GetString());
        Assert.False(joined.TryGetProperty("session", out _));
        var grantId = joined.GetProperty("grantId").GetGuid();
        var secret = joined.GetProperty("deviceSecret").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/session")).StatusCode);

        using var second = app.Client();
        second.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var verified = await second.Post("/api/jumelage/appareil/verifier", new {
            grantId, deviceId, secret
        }).Ok();
        Assert.True(verified.GetProperty("expiresUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Post("/api/session/appareil", new {
            grantId, deviceId, secret
        })).StatusCode);

        // Un compte doit s'authentifier avec son PROPRE mot de passe.
        var login = await second.Post("/api/session", new { userName = "admin", password = "Scolia2026!" }).Ok();
        Assert.Equal("admin", login.GetProperty("userName").GetString());

        using var other = app.Client();
        const string otherId = "autre-appareil-test-02";
        other.DefaultRequestHeaders.Add("X-Scolia-Poste", otherId);
        var denied = await other.Post("/api/jumelage/appareil/verifier", new {
            grantId, deviceId = otherId, secret
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        var reused = await other.Post("/api/jumelage/accepter", new {
            invitationToken = token, deviceId = otherId, deviceName = "Autre appareil"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);

        await host.Post($"/api/jumelage/appareils/{grantId}/revoquer", new { }).Ok();
        var expired = await second.Post("/api/jumelage/appareil/verifier", new {
            grantId, deviceId, secret
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, expired.Status);
    }

    [Fact]
    public async Task Un_serveur_qui_change_d_adresse_prouve_son_identite_sans_recevoir_la_cle()
    {
        using var app = new ScoliaApp();
        using var host = await app.InstalledAdmin();
        var invitation = await host.Post("/api/jumelage/inviter", new { }).Ok();

        using var poste = app.Client();
        const string deviceId = "telephone-promoteur-reseau";
        poste.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var paired = await poste.Post("/api/jumelage/accepter", new {
            invitationToken = invitation.GetProperty("token").GetString(),
            deviceId, deviceName = "Téléphone promoteur"
        }).Ok();
        var grantId = paired.GetProperty("grantId").GetGuid();
        var secret = paired.GetProperty("deviceSecret").GetString()!;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        // Le client envoie seulement le nonce, jamais la clé de jumelage.
        var reply = await poste.Post("/api/jumelage/preuve-serveur",
            new { grantId, deviceId, nonce }).Ok();
        var received = Convert.FromHexString(reply.GetProperty("proof").GetString()!);
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var expected = HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(nonce));
        Assert.True(CryptographicOperations.FixedTimeEquals(expected, received));

        using var intrus = app.Client();
        intrus.DefaultRequestHeaders.Add("X-Scolia-Poste", "telephone-different");
        var denied = await intrus.Post("/api/jumelage/preuve-serveur",
            new { grantId, deviceId, nonce }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        await host.Post($"/api/jumelage/appareils/{grantId}/revoquer", new { }).Ok();
        var revoked = await poste.Post("/api/jumelage/preuve-serveur",
            new { grantId, deviceId, nonce }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.Status);
    }

}
