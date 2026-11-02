using System.Net;
using System.Net.Http.Json;

namespace Scolia.Tests;

/// <summary>Code d'accès au choix : un code PIN remplace le mot de passe et sert ensuite à se connecter.</summary>
public sealed class AccessCodeApiTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Un_code_pin_choisi_sert_ensuite_a_se_connecter()
    {
        var admin = await app.InstalledAdmin();
        var changed = await admin.PostAsJsonAsync("/api/session/mot-de-passe", new { currentPassword = "Scolia2026!", newPassword = "482915" });
        Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());

        var withPin = await app.Login("admin", "482915");
        var session = await withPin.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        // Retour à un mot de passe alphanumérique, toujours au choix de la personne.
        var back = await withPin.PostAsJsonAsync("/api/session/mot-de-passe", new { currentPassword = "482915", newPassword = "ma devise ecole" });
        Assert.True(back.IsSuccessStatusCode, await back.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    [InlineData("1234")]
    public async Task Un_code_pin_trop_facile_est_refuse(string pin)
    {
        var admin = await app.InstalledAdmin();
        var refused = await admin.PostAsJsonAsync("/api/session/mot-de-passe", new { currentPassword = "Scolia2026!", newPassword = pin });
        Assert.False(refused.IsSuccessStatusCode);
    }
}
