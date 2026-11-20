using System.Net;
using System.Text;
using System.Text.Json;

namespace JPServices.Owner.Tests;

public sealed class OwnerApiTests
{
    private static readonly OwnerCredentials Credentials =
        new("abcd1234.access", "secret-de-service-0123456789", "jeton-proprietaire-0123456789");

    private static OwnerApi Api(FakeOwnerService service) =>
        new(Credentials, new Uri("https://console.test/"), service);

    [Fact]
    public async Task Chaque_appel_porte_le_jeton_de_service_Access_et_le_jeton_proprietaire()
    {
        var service = new FakeOwnerService();
        using var api = Api(service);
        var summary = await api.SummaryAsync();
        Assert.Equal(3, OwnerText.Number(summary, "schools"));
        var (_, path, headers, _) = Assert.Single(service.Requests);
        Assert.Equal("/api/v1/owner/summary", path);
        Assert.Equal("abcd1234.access", headers["CF-Access-Client-Id"]);
        Assert.Equal("secret-de-service-0123456789", headers["CF-Access-Client-Secret"]);
        Assert.Equal("Bearer jeton-proprietaire-0123456789", headers["Authorization"]);
    }

    [Fact]
    public async Task Une_redirection_vers_la_connexion_Access_est_un_refus_sans_suivre_le_lien()
    {
        var service = new FakeOwnerService
        {
            Override = _ => new HttpResponseMessage(HttpStatusCode.Found)
                { Headers = { Location = new Uri("https://jpservices.cloudflareaccess.com/cdn-cgi/access/login") } }
        };
        using var api = Api(service);
        var error = await Assert.ThrowsAsync<OwnerApiException>(() => api.SchoolsAsync());
        Assert.Contains("Service Auth", error.Message, StringComparison.Ordinal);
        Assert.Single(service.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Jeton propriétaire refusé")]
    [InlineData(HttpStatusCode.Forbidden, "Cloudflare Access a refusé")]
    [InlineData(HttpStatusCode.TooManyRequests, "Trop de demandes")]
    public async Task Les_refus_sont_expliques_sans_citer_de_secret(HttpStatusCode status, string expected)
    {
        var service = new FakeOwnerService { Override = _ => new HttpResponseMessage(status) { Content = new StringContent("<html>refus</html>") } };
        using var api = Api(service);
        var error = await Assert.ThrowsAsync<OwnerApiException>(() => api.AuditAsync());
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-de-service", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jeton-proprietaire", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Le_message_d_erreur_du_Worker_est_repris()
    {
        var service = new FakeOwnerService
        {
            Override = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("""{"error":"Paramètres de licence invalides."}""", Encoding.UTF8, "application/json") }
        };
        using var api = Api(service);
        var error = await Assert.ThrowsAsync<OwnerApiException>(() =>
            api.UpdateLicenseAsync(FakeOwnerService.SchoolId, new Dictionary<string, object?> { ["trial_days"] = 30 }));
        Assert.Equal("Paramètres de licence invalides.", error.Message);
    }

    [Fact]
    public async Task La_licence_n_envoie_que_les_champs_modifies()
    {
        var service = new FakeOwnerService();
        using var api = Api(service);
        await api.UpdateLicenseAsync(FakeOwnerService.SchoolId, new Dictionary<string, object?>
            { ["manual_override"] = "none", ["override_until"] = null });
        var (method, path, _, body) = Assert.Single(service.Requests);
        Assert.Equal(HttpMethod.Patch, method);
        Assert.Equal($"/api/v1/owner/schools/{FakeOwnerService.SchoolId}/license", path);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal(["manual_override", "override_until"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("override_until").ValueKind);
    }

    [Fact]
    public async Task Un_identifiant_invalide_n_atteint_jamais_le_service()
    {
        var service = new FakeOwnerService();
        using var api = Api(service);
        await Assert.ThrowsAsync<OwnerApiException>(() => api.SchoolAsync("../summary"));
        Assert.Empty(service.Requests);
    }

    [Fact]
    public async Task Sans_identifiants_complets_aucune_requete_n_est_envoyee()
    {
        var service = new FakeOwnerService();
        using var api = new OwnerApi(new OwnerCredentials("id", "", ""), new Uri("https://console.test/"), service);
        await Assert.ThrowsAsync<OwnerApiException>(() => api.SummaryAsync());
        Assert.Empty(service.Requests);
    }
}

public sealed class OwnerVaultTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "scolia-console-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Les_identifiants_sont_relus_et_jamais_ecrits_en_clair()
    {
        var dir = TempDir();
        var vault = new OwnerVault(new FakeProtector(), dir);
        var credentials = new OwnerCredentials("abcd1234.access", "secret-de-service-0123456789", "jeton-proprietaire-0123456789");
        vault.Save(credentials);
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(dir).Single()));
        Assert.DoesNotContain("secret-de-service", raw, StringComparison.Ordinal);
        Assert.Equal(credentials, vault.Load());
        vault.Clear();
        Assert.Null(vault.Load());
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Sans_protection_de_la_plateforme_rien_n_est_enregistre()
    {
        var vault = new OwnerVault(new FakeProtector(available: false), TempDir());
        Assert.False(vault.CanStore);
        Assert.Throws<OwnerApiException>(() => vault.Save(new OwnerCredentials("a", "b", "c")));
        Assert.Null(vault.Load());
    }

    [Fact]
    public void Un_fichier_abime_est_ignore()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "console-acces.bin"), [1, 2, 3]);
        Assert.Null(new OwnerVault(new FakeProtector(), dir).Load());
        Directory.Delete(dir, true);
    }
}

public sealed class OwnerTextTests
{
    [Theory]
    [InlineData("active", "Payée", LicenseTone.Good)]
    [InlineData("expired", "Échue · lecture seule", LicenseTone.Bad)]
    [InlineData("suspended", "Bloquée · lecture seule", LicenseTone.Bad)]
    [InlineData("grace", "Délai supplémentaire", LicenseTone.Warning)]
    public void Le_statut_effectif_de_licence_est_libelle(string status, string label, LicenseTone tone) =>
        Assert.Equal((label, tone), OwnerText.License(status));

    [Theory]
    [InlineData("365", 1100, 365)]
    [InlineData(" 15 ", 120, 15)]
    [InlineData("0", 120, null)]
    [InlineData("121", 120, null)]
    [InlineData("-3", 120, null)]
    [InlineData("2,5", 120, null)]
    public void Les_jours_saisis_sont_bornes(string text, int max, int? expected) =>
        Assert.Equal(expected, OwnerText.Days(text, max));

    [Fact]
    public void Les_booleens_D1_en_0_1_sont_lus()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":0,"c":true}""");
        Assert.True(OwnerText.Flag(doc.RootElement, "a"));
        Assert.False(OwnerText.Flag(doc.RootElement, "b"));
        Assert.True(OwnerText.Flag(doc.RootElement, "c"));
        Assert.False(OwnerText.Flag(doc.RootElement, "absent"));
    }

    [Fact]
    public void Les_durees_et_tailles_sont_lisibles()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("il y a 3 h", OwnerText.Ago(now.AddHours(-3).ToUnixTimeMilliseconds(), now));
        Assert.Equal("jamais", OwnerText.Ago(null, now));
        Assert.Equal("700 Mo", OwnerText.Bytes(734_003_200));
        Assert.Equal("Licence modifiée", OwnerText.Action("license_policy_changed"));
    }
}
