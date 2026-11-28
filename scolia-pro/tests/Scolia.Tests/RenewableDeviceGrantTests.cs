using System.Text.Json.Nodes;
using Scolia.Api.Security;

namespace Scolia.Tests;

public sealed class RenewableDeviceGrantTests
{
    [Fact]
    public void La_liste_d_appareils_survit_au_redemarrage_et_le_retrait_est_definitif()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-persistent-devices", Guid.NewGuid().ToString("N"));
        try
        {
            var owner = Guid.NewGuid();
            var original = new DevicePairingRegistry(folder);
            var first = original.Invite(owner, "stamp");
            var linked = original.Redeem(first.Token, "poste-internet-123", "Android du laboratoire")!.Value;
            Assert.Equal(DateTime.MaxValue, linked.ExpiresUtc);

            var restarted = new DevicePairingRegistry(folder);
            Assert.Equal(linked.GrantId, Assert.Single(restarted.ListAll()).Id);
            Assert.NotNull(restarted.AuthorizeAndRefresh(linked.GrantId, "poste-internet-123", linked.Secret));

            // Réautoriser le même téléphone remplace son ancienne empreinte au lieu d'en ajouter une.
            var second = restarted.Invite(owner, "stamp");
            var renewed = restarted.Redeem(second.Token, "poste-internet-123", "Android du laboratoire")!.Value;
            Assert.Single(restarted.ListAll());
            Assert.Null(restarted.AuthorizeAndRefresh(linked.GrantId, "poste-internet-123", linked.Secret));

            Assert.True(restarted.RevokeAny(renewed.GrantId));
            Assert.Empty(restarted.ListAll());
            var afterRestart = new DevicePairingRegistry(folder);
            Assert.Empty(afterRestart.ListAll());
            Assert.Null(afterRestart.AuthorizeAndRefresh(renewed.GrantId, "poste-internet-123", renewed.Secret));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

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
