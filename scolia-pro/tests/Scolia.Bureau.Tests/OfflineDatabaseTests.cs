using System.Text;
using System.Text.Json;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class OfflineDatabaseTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "scolia-journal", Guid.NewGuid().ToString("N"));
    private readonly Uri server = new("http://journal-test:5080/");
    private OfflineStore Store(string user = "secretariat") => new(server, folder) { User = user };

    [Fact]
    public void Queue_conflict_and_retry_survive_reopening_and_remain_private_to_user()
    {
        var store = Store();
        var item = new OutboxItem { Key = Guid.NewGuid().ToString("N"), Path = "api/paiements", Body = "{\"payeur\":\"DONNEES-PRIVEES-782\"}" };
        store.Enqueue(item);
        store.Refused(item.Id, "Mois déjà payé");
        Assert.Empty(Store().Outbox());
        Assert.Single(Store().Conflicts());
        Assert.Empty(Store("comptabilite").Conflicts());
        Store("comptabilite").Discard(item.Id);
        Assert.Single(Store().Conflicts());
        Store().Retry(item.Id);
        Assert.Empty(Store().Conflicts());
        Assert.Equal(item.Key, Assert.Single(Store().Outbox()).Key);
        Assert.Empty(Store("comptabilite").Outbox());
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("DONNEES-PRIVEES-782", Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_instances_do_not_overwrite_each_others_operations()
    {
        var one = Store();
        var two = Store();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(() =>
            (i % 2 == 0 ? one : two).Enqueue(new OutboxItem { Key = i.ToString(System.Globalization.CultureInfo.InvariantCulture), Path = "api/apprenants" }))));
        Assert.Equal(30, Store().Outbox().Count);
    }

    [Fact]
    public void Legacy_queue_is_migrated_once_and_original_is_backed_up_encrypted()
    {
        var root = Path.Combine(folder, "journal-test_5080");
        Directory.CreateDirectory(root);
        var item = new OutboxItem { Key = "ancienne-cle", User = "secretariat", Body = "PAIEMENT-ANCIEN-991" };
        File.WriteAllText(Path.Combine(root, "envois.json"), JsonSerializer.Serialize(new[] { item }));
        Assert.Equal("ancienne-cle", Assert.Single(Store().Outbox()).Key);
        Assert.Single(Store().Outbox());
        Assert.False(File.Exists(Path.Combine(root, "envois.json")));
        Assert.True(File.Exists(Path.Combine(root, "envois.json.avant-sqlite.enc")));
        var backup = SecretStore.Unprotect(File.ReadAllBytes(Path.Combine(root, "envois.json.avant-sqlite.enc")));
        Assert.Contains("PAIEMENT-ANCIEN-991", Encoding.UTF8.GetString(backup), StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupted_legacy_queue_is_never_silently_replaced_by_empty_queue()
    {
        var root = Path.Combine(folder, "journal-test_5080");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "envois.json"), "[broken");
        Assert.Throws<JsonException>(() => Store());
        Assert.Equal("[broken", File.ReadAllText(Path.Combine(root, "envois.json")));
    }

    [Fact]
    public void Cached_pages_are_encrypted_and_separate_for_each_account()
    {
        Store().SavePage("api/apprenants", "PRIVE-APPRENANT-928");
        Assert.Equal("PRIVE-APPRENANT-928", Store().ReadPage("api/apprenants")?.Json);
        Assert.Null(Store("comptabilite").ReadPage("api/apprenants"));
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain("PRIVE-APPRENANT-928", Encoding.UTF8.GetString(File.ReadAllBytes(file)), StringComparison.Ordinal);
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
