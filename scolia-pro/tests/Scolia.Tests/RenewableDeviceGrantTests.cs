using System.Text.Json.Nodes;
using Scolia.Api.Security;

namespace Scolia.Tests;

public sealed class RenewableDeviceGrantTests
{
    [Fact]
    public void Le_jumelage_se_renouvelle_sans_qr_mais_reste_revocable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-render-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var grants = new DevicePairingRegistry(folder);
            var owner = Guid.NewGuid();
            var token = grants.Invite(owner, "security-stamp");
            var accepted = grants.Redeem(token.Token, "telephone-123", "Android SCOLIA");
            Assert.NotNull(accepted);
            var original = accepted!.Value;
            Assert.True(original.ExpiresUtc > DateTime.UtcNow.AddDays(170));
            Assert.Null(grants.Redeem(token.Token, "autre-telephone", "Copie du QR"));
            Assert.Null(grants.AuthorizeAndRefresh(original.GrantId, "intrus", original.Secret));
            Assert.Null(grants.AuthorizeAndRefresh(original.GrantId, "telephone-123", "secret-faux"));

            // Simuler l'approche de l'échéance : une authentification valide
            // prolonge la clé locale et la date renvoyée au client.
            var file = Path.Combine(folder, "cles", "appareils-autorises.json");
            var stored = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            stored[0]!["ExpiresUtc"] = DateTime.UtcNow.AddDays(3);
            File.WriteAllText(file, stored.ToJsonString());
            var restarted = new DevicePairingRegistry(folder);
            var renewed = restarted.AuthorizeAndRefresh(original.GrantId, "telephone-123", original.Secret);
            Assert.NotNull(renewed);
            Assert.True(renewed!.Value.ExpiresUtc > DateTime.UtcNow.AddDays(170));
            Assert.True(restarted.Revoke(original.GrantId, owner));
            Assert.Null(restarted.AuthorizeAndRefresh(original.GrantId, "telephone-123", original.Secret));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
