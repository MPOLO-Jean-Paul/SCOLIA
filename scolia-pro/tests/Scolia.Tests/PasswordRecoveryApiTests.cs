using System.Net;

namespace Scolia.Tests;

public sealed class PasswordRecoveryApiTests
{
    [Fact]
    public async Task Le_code_de_secours_permet_de_restaurer_sans_ouvrir_une_session()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        var path = Path.Combine(app.DataDirectory, "cles", "code-de-secours.txt");
        var originalCode = File.ReadAllText(path).Trim();
        using var anonymous = app.Client();

        var bad = await anonymous.Post("/api/session/recuperation", new
        {
            userName = "admin", recoveryCode = "FAUX-CODE",
            newPassword = "MotDePasseRenouvele2026!", confirmation = "MotDePasseRenouvele2026!",
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Status);

        await anonymous.Post("/api/session/recuperation", new
        {
            userName = "admin", recoveryCode = originalCode,
            newPassword = "MotDePasseRenouvele2026!", confirmation = "MotDePasseRenouvele2026!",
        }).Ok();

        Assert.NotEqual(originalCode, File.ReadAllText(path).Trim());
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/session")).StatusCode);

        using var restored = await app.Login("admin", "MotDePasseRenouvele2026!");
        Assert.Equal(HttpStatusCode.OK, (await restored.GetAsync("/api/session")).StatusCode);

        var reuse = await anonymous.Post("/api/session/recuperation", new
        {
            userName = "admin", recoveryCode = originalCode,
            newPassword = "EncoreUnMotDePasse2026!", confirmation = "EncoreUnMotDePasse2026!",
        }).Fail();
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.Status);
    }

    [Fact]
    public async Task Une_confirmation_incorrecte_n_utilise_pas_le_code_de_secours()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        var path = Path.Combine(app.DataDirectory, "cles", "code-de-secours.txt");
        var originalCode = File.ReadAllText(path).Trim();
        using var anonymous = app.Client();

        var refused = await anonymous.Post("/api/session/recuperation", new
        {
            userName = "admin", recoveryCode = originalCode,
            newPassword = "MotDePasseRenouvele2026!", confirmation = "CodeDifferent2026!",
        }).Fail();
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal(originalCode, File.ReadAllText(path).Trim());

        using var unchanged = await app.Login("admin", "Scolia2026!");
        Assert.Equal(HttpStatusCode.OK, (await unchanged.GetAsync("/api/session")).StatusCode);
    }
}
