using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class CloudOfflineContinuityTests
{
    [Fact]
    public void Only_accounts_with_pending_operations_and_preissued_cloud_access_are_selected()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-cloud-pending", Guid.NewGuid().ToString("N"));
        try
        {
            var server = new Uri("http://cloud-continuity-test:5080/");
            var store = new OfflineStore(server, folder) { User = "secretariat" };
            var first = new OutboxItem { Key = "operation-secretariat", Path = "api/paiements", Body = "{\"note\":\"CHIFFREMENT-TEST\"}" };
            store.Enqueue(first);
            store.SaveCloudAccessFor("secretariat", "{\"enabled\":true}");
            store.User = "comptabilite";
            var second = new OutboxItem { Key = "operation-comptabilite", Path = "api/paiements" };
            store.Enqueue(second);

            Assert.Equal(new[] { "secretariat" }, store.AccountsAwaitingCloudDeposit());

            store.SaveCloudAccessFor("comptabilite", "{\"enabled\":true}");
            var reopened = new OfflineStore(server, folder);
            Assert.Equal(new[] { "secretariat", "comptabilite" }, reopened.AccountsAwaitingCloudDeposit());

            // Un simple dépôt cloud ne doit jamais faire disparaître une écriture :
            // seul le serveur scolaire confirme définitivement son application.
            reopened.MarkDepositedFor("secretariat", first.Id);
            Assert.Contains(reopened.OutboxFor("secretariat"), x => x.Id == first.Id && x.Deposited);
            Assert.Equal(new[] { "comptabilite" }, reopened.AccountsAwaitingCloudDeposit());
            reopened.SentFor("secretariat", first.Id);
            Assert.Empty(reopened.OutboxFor("secretariat"));
            Assert.Single(reopened.OutboxFor("comptabilite"));

            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                Assert.DoesNotContain("CHIFFREMENT-TEST", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
