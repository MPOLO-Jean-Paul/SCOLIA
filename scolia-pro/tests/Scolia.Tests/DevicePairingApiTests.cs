using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Scolia.Api.Security;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Le QR n'accorde qu'une session d'appareil autorisée, révocable et indépendante du mot de passe.</summary>
public sealed class DevicePairingApiTests
{
    [Fact]
    public async Task Invitation_a_usage_unique_et_appareil_revoque()
    {
        using var server = new ScoliaApp();
        using var owner = await server.InstalledAdmin();
        var registry = server.Services.GetRequiredService<DevicePairingRegistry>();
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var account = await db.Users.AsNoTracking().FirstAsync();
        var invitation = registry.Invite(account.Id, account.SecurityStamp);

        using var device = server.Client();
        device.DefaultRequestHeaders.Add("X-Scolia-Poste", "device-unique-001");
        var accepted = await device.Post("/api/jumelage/accepter", new
        {
            invitationToken = invitation.Token, deviceId = "device-unique-001", deviceName = "Poste infirmier"
        }).Ok();
        var grantId = accepted.GetProperty("grantId").GetGuid();
        var secret = accepted.GetProperty("deviceSecret").GetString();
        Assert.False(string.IsNullOrEmpty(secret));
        Assert.Equal("admin", accepted.GetProperty("session").GetProperty("userName").GetString());

        var reused = await server.Client().Post("/api/jumelage/accepter", new
        {
            invitationToken = invitation.Token, deviceId = "device-unique-002", deviceName = "Autre poste"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);

        using var restart = server.Client();
        restart.DefaultRequestHeaders.Add("X-Scolia-Poste", "device-unique-001");
        var loggedIn = await restart.Post("/api/session/appareil", new
        {
            grantId, deviceId = "device-unique-001", secret
        }).Ok();
        Assert.Equal("admin", loggedIn.GetProperty("userName").GetString());

        // Une session copiée avec un autre identifiant de poste ne suffit pas.
        restart.DefaultRequestHeaders.Remove("X-Scolia-Poste");
        restart.DefaultRequestHeaders.Add("X-Scolia-Poste", "device-non-autorise");
        Assert.Equal(HttpStatusCode.Unauthorized, (await restart.GetAsync("/api/session")).StatusCode);

        await owner.Post("/api/jumelage/appareils/" + grantId + "/revoquer", new { }).Ok();
        var refused = await device.Post("/api/session/appareil", new
        {
            grantId, deviceId = "device-unique-001", secret
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
    }

    [Fact]
    public async Task Une_adresse_ou_un_code_errone_ne_donne_jamais_une_session()
    {
        using var server = new ScoliaApp();
        using var owner = await server.InstalledAdmin();
        using var device = server.Client();
        device.DefaultRequestHeaders.Add("X-Scolia-Poste", "device-xyz");
        var without = await device.Post("/api/session/appareil", new
        {
            grantId = Guid.NewGuid(), deviceId = "device-xyz", secret = "copie-d-adresse-tailscale"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, without.Status);
        var withoutInvitation = await device.Post("/api/jumelage/accepter", new
        {
            invitationToken = "LIAISON-SIMPLE-SANS-CODE", deviceId = "device-xyz", deviceName = "Poste"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, withoutInvitation.Status);
    }
}
