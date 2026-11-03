using System.Net;

namespace Scolia.Tests;

/// <summary>L'invitation ouvre un compte sans copier le mot de passe ; l'appareil garde une autorisation révocable.</summary>
public sealed class DevicePairingTests
{
    [Fact]
    public async Task Un_qr_approuve_ouvre_la_session_une_fois_et_l_appareil_peut_revenir_sans_code()
    {
        using var app = new ScoliaApp();
        using var host = await app.InstalledAdmin();
        var invite = await host.Post("/api/jumelage/inviter", new { }).Ok();
        var token = invite.GetProperty("token").GetString()!;

        using var guest = app.Client();
        guest.DefaultRequestHeaders.Add("X-Scolia-Poste", "appareil-client-test-01");
        var joined = await guest.Post("/api/jumelage/accepter", new {
            invitationToken = token, deviceId = "appareil-client-test-01", deviceName = "Appareil test"
        }).Ok();
        Assert.Equal("admin", joined.GetProperty("session").GetProperty("userName").GetString());
        var grantId = joined.GetProperty("grantId").GetGuid();
        var secret = joined.GetProperty("deviceSecret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/api/session")).StatusCode);

        using var second = app.Client();
        second.DefaultRequestHeaders.Add("X-Scolia-Poste", "appareil-client-test-01");
        var renewed = await second.Post("/api/session/appareil", new {
            grantId, deviceId = "appareil-client-test-01", secret
        }).Ok();
        Assert.Equal("admin", renewed.GetProperty("userName").GetString());

        using var other = app.Client();
        other.DefaultRequestHeaders.Add("X-Scolia-Poste", "autre-appareil-test-02");
        var denied = await other.Post("/api/session/appareil", new {
            grantId, deviceId = "autre-appareil-test-02", secret
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, denied.Status);

        var reused = await other.Post("/api/jumelage/accepter", new {
            invitationToken = token, deviceId = "autre-appareil-test-02", deviceName = "Autre appareil"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);

        await host.Post($"/api/jumelage/appareils/{grantId}/revoquer", new { }).Ok();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/session")).StatusCode);
        var expired = await second.Post("/api/session/appareil", new {
            grantId, deviceId = "appareil-client-test-01", secret
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, expired.Status);
    }
}
