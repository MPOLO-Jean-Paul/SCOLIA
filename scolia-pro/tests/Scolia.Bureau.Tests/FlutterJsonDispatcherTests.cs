using System.Text.Json;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class FlutterJsonDispatcherTests
{
    [Fact]
    public async Task Le_canal_transmet_les_filtres_et_serialize_en_camelCase()
    {
        var dispatcher = new FlutterJsonDispatcher((operation, args) =>
        {
            Assert.Equal("listMonthly", operation);
            Assert.Equal("INF1", args["promotion"]);
            Assert.Equal("paye", args["status"]);
            return Task.FromResult(new FlutterCommandAdapter.Snapshot(1, "home", "Établissement", "Compte",
                "2026", ["monthly"], ""));
        });
        var result = await dispatcher.DispatchAsync("listMonthly", """{"promotion":"INF1","status":"paye"}""");
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(1, doc.RootElement.GetProperty("protocol").GetInt32());
        Assert.Equal("home", doc.RootElement.GetProperty("stage").GetString());
        Assert.Equal("monthly", doc.RootElement.GetProperty("modules")[0].GetString());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"page\":1}")]
    [InlineData("{\"page\":\"1\",\"page\":\"2\"}")]
    public async Task Les_arguments_invalides_ne_declenchent_aucune_commande(string json)
    {
        var called = false;
        var dispatcher = new FlutterJsonDispatcher((_, _) =>
        {
            called = true;
            throw new InvalidOperationException();
        });
        await Assert.ThrowsAsync<ArgumentException>(() => dispatcher.DispatchAsync("snapshot", json));
        Assert.False(called);
    }
}
