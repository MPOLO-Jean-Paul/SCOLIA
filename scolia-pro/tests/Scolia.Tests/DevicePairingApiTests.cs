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
    public async Task Invitation_a_usage_unique_ne_cree_pas_de_session_utilisateur()
    {
        using var server = new ScoliaApp();
        using var owner = await server.InstalledAdmin();
        var registry = server.Services.GetRequiredService<DevicePairingRegistry>();
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var account = await db.Users.AsNoTracking().FirstAsync();
        var invitation = registry.Invite(account.Id, account.SecurityStamp);

        using var device = server.Client();
        const string deviceId = "device-unique-001";
        device.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var accepted = await device.Post("/api/jumelage/accepter", new
        {
            invitationToken = invitation.Token, deviceId, deviceName = "Poste infirmier"
        }).Ok();
        var grantId = accepted.GetProperty("grantId").GetGuid();
        var secret = accepted.GetProperty("deviceSecret").GetString()!;
        Assert.Equal("Institut de test", accepted.GetProperty("schoolName").GetString());
        Assert.False(accepted.TryGetProperty("session", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/session")).StatusCode);

        var reused = await server.Client().Post("/api/jumelage/accepter", new
        {
            invitationToken = invitation.Token, deviceId = "device-unique-002", deviceName = "Autre poste"
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, reused.Status);

        using var restart = server.Client();
        restart.DefaultRequestHeaders.Add("X-Scolia-Poste", deviceId);
        var linked = await restart.Post("/api/jumelage/appareil/verifier", new
        {
            grantId, deviceId, secret
        }).Ok();
        Assert.True(linked.GetProperty("expiresUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restart.GetAsync("/api/session")).StatusCode);

        // Même avec la clé correcte, les anciens endpoints de connexion restent fermés.
        Assert.Equal(HttpStatusCode.Unauthorized, (await restart.Post("/api/session/appareil/renouveler", new
        {
            grantId, deviceId, secret
        })).StatusCode);

        await owner.Post($"/api/jumelage/appareils/{grantId}/revoquer", new { }).Ok();
        var refused = await restart.Post("/api/jumelage/appareil/verifier", new
        {
            grantId, deviceId, secret
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
