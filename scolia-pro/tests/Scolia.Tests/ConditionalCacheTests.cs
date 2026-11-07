using System.Net;
using System.Net.Http.Headers;
using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>Le serveur refuse de retransférer les mêmes données à une copie locale déjà à jour.</summary>
public sealed class ConditionalCacheTests
{
    [Fact]
    public async Task Une_ressource_inchangee_repond_304_et_une_modification_repond_200()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        var path = "/api/etablissement/promotions";
        using var first = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(first.Headers.ETag);
        var original = await first.Content.ReadAsStringAsync();

        using var unchangedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        unchangedRequest.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var unchanged = await admin.SendAsync(unchangedRequest);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal(string.Empty, await unchanged.Content.ReadAsStringAsync());

        await admin.Post("/api/etablissement/promotions", new
        {
            code = "NEW1", name = "Première", level = 1, isActive = true,
            monthlyFee = 50000m, oneTimeFees = Array.Empty<object>()
        }).Ok();
        using var changedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        changedRequest.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var changed = await admin.SendAsync(changedRequest);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(original, await changed.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Un_redemarrage_change_la_generation_du_serveur()
    {
        var earlier = new DataRevision();
        var later = new DataRevision();
        Assert.NotEqual(earlier.Epoch, later.Epoch);
    }
}
