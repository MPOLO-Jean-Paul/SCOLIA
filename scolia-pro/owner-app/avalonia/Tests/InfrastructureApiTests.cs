using System.Net;

namespace JPServices.Owner.Tests;

public sealed class InfrastructureApiTests
{
    internal const string Account = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string AccessKey = "R2AccessKeyFictif2026";
    internal const string Secret = "secret-r2-fictif-local-2026-0123456789";

    [Fact]
    public async Task Le_stockage_est_verifie_sans_restituer_les_secrets_et_la_proposition_globale_est_persistante()
    {
        using var service = new LocalOwnerService();
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        Assert.False(OwnerText.Flag(await api.StorageAsync(), "ready"));
        var configured = await api.ConfigureStorageAsync(Account, "scolia-tests", AccessKey, Secret);
        Assert.True(OwnerText.Flag(configured, "ready"));
        var stored = await api.StorageAsync();
        Assert.Equal("credentials", OwnerText.Str(stored, "mode"));
        Assert.Equal("scolia-tests", OwnerText.Str(stored, "bucket"));
        Assert.DoesNotContain(Secret, stored.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(AccessKey, stored.GetRawText(), StringComparison.Ordinal);
        await api.RequestCloudForAllAsync();
        Assert.All(OwnerText.Items(await api.SchoolsAsync(), "schools"), school => Assert.True(OwnerText.Flag(school, "cloud_requested")));
        await api.RemoveStorageAsync();
        Assert.False(OwnerText.Flag(await api.StorageAsync(), "ready"));
        var refused = await Assert.ThrowsAsync<OwnerApiException>(() => api.RequestCloudForAllAsync());
        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
    }

    [Fact]
    public async Task Une_configuration_R2_invalide_ne_remplace_pas_le_stockage_verifie()
    {
        using var service = new LocalOwnerService();
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        await api.ConfigureStorageAsync(Account, "scolia-tests", AccessKey, Secret);
        var error = await Assert.ThrowsAsync<OwnerApiException>(() => api.ConfigureStorageAsync(Account, "autre-bucket", AccessKey, "court"));
        Assert.Equal(HttpStatusCode.BadRequest, error.Status);
        Assert.Equal("scolia-tests", OwnerText.Str(await api.StorageAsync(), "bucket"));
    }

    [Fact]
    public async Task Le_catalogue_TURN_reste_distinct_du_relais_WSS_et_ne_divulgue_pas_de_credentials()
    {
        using var service = new LocalOwnerService();
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        var before = await api.RelaysAsync();
        var catalogue = await api.DiscoverTurnAsync();
        Assert.Equal("available", OwnerText.Str(catalogue, "status"));
        var relay = Assert.Single(OwnerText.Items(catalogue, "relays"));
        Assert.Equal("TURN Démonstration JP-Services", OwnerText.Str(relay, "label"));
        Assert.DoesNotContain("turn-decouverte-fictif", catalogue.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(before.GetProperty("relays").GetRawText(), (await api.RelaysAsync()).GetProperty("relays").GetRawText());
    }

    [Fact]
    public async Task Le_site_Netlify_se_configure_se_teste_et_se_retire_sans_activer_un_transport()
    {
        using var service = new LocalOwnerService();
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        await api.ConfigureNetlifyAsync("Secours natif", "https://scolia-demo.netlify.app/");
        var configured = await api.NetlifyAsync();
        Assert.True(OwnerText.Flag(configured, "configured"));
        Assert.Equal("disabled", OwnerText.Str(configured, "state"));
        Assert.False(OwnerText.Flag(configured, "can_activate"));
        Assert.True(OwnerText.Flag(await api.TestNetlifyAsync(), "reachable"));
        await Assert.ThrowsAsync<OwnerApiException>(() => api.ConfigureNetlifyAsync("Invalide", "http://localhost/"));
        Assert.Equal("https://scolia-demo.netlify.app", OwnerText.Str(await api.NetlifyAsync(), "base_url"));
        await api.RemoveNetlifyAsync();
        Assert.False(OwnerText.Flag(await api.NetlifyAsync(), "configured"));
    }
}
