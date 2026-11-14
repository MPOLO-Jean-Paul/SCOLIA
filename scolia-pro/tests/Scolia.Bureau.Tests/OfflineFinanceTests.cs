using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Les versements ne doivent ni disparaître ni être doublement imputés hors réseau.</summary>
public sealed class OfflineFinanceTests
{
    private static (OfflineStore Store, string Directory, AccountDto Account) Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "scolia-finance", Guid.NewGuid().ToString("N"));
        var store = new OfflineStore(new Uri("http://127.0.0.1:1/"), dir) { User = "caisse" };
        var student = Guid.NewGuid();
        var enrollment = Guid.NewGuid();
        var month1 = new AccountLineDto(Guid.NewGuid(), "Septembre", new DateOnly(2026, 9, 6),
            100, 0, 100, 0, 100, "En retard", true, "Mensualite");
        var month2 = new AccountLineDto(Guid.NewGuid(), "Octobre", new DateOnly(2026, 10, 6),
            100, 0, 100, 0, 100, "En retard", true, "Mensualite");
        var account = new AccountDto(student, enrollment, "INF293", "SHIMBA MAKITA",
            "INF3", "2026-2027", "Actif", [month1, month2],
            200, 0, 200, 0, 200, 200, 2, "Retard", [], null, null);
        store.SavePage($"api/inscriptions/{enrollment}/compte",
            JsonSerializer.Serialize(account, Json.Options));
        return (store, dir, account);
    }

    private static OutboxItem Queue(OfflineStore store, AccountDto account, decimal amount, params Guid[] fees)
    {
        var item = new OutboxItem
        {
            User = store.User, Method = "POST", Path = "api/paiements",
            Key = Guid.NewGuid().ToString("N"), Label = "Encaissement",
            Body = JsonSerializer.Serialize(new {
                enrollmentId = account.EnrollmentId, amount,
                date = new DateOnly(2026, 11, 6), method = "Especes",
                feeItemIds = fees, reference = "TR-LOCAL",
            }, Json.Options)
        };
        store.Enqueue(item);
        return item;
    }

    [Fact]
    public void Deux_paiements_partiels_consecutifs_affectent_le_solde_sans_doublon()
    {
        var (store, directory, account) = Setup();
        try
        {
            var sept = account.Lines[0].FeeItemId;
            var first = Queue(store, account, 60, sept);
            var one = OfflineFinance.ProjectAccount(account, store);
            Assert.Equal(140, one.Balance);
            Assert.Equal(40, one.Lines[0].Remaining);
            var second = Queue(store, account, 40, sept);
            var two = OfflineFinance.ProjectAccount(account, store);
            Assert.Equal(100, two.Balance);
            Assert.Equal(0, two.Lines[0].Remaining);
            Assert.Equal(100, two.Lines[1].Remaining);
            Assert.Equal(2, OfflineFinance.Pending(store).Count);
            Assert.Equal(2, two.Receipts.Count);
            Assert.StartsWith("LOCAL-", OfflineFinance.PendingReceipt(store, first.IdAsGuid())!.Number);
            Assert.NotNull(OfflineFinance.PendingReceipt(store, second.IdAsGuid()));
            Assert.Throws<ArgumentException>(() => OfflineFinance.Preview(two, [sept], 1));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Le_journal_est_isole_par_compte_et_identifie_le_paiement_local()
    {
        var (store, directory, account) = Setup();
        try
        {
            var item = Queue(store, account, 100, account.Lines[0].FeeItemId);
            var rows = OfflineFinance.PendingRows(store);
            var line = Assert.Single(rows);
            Assert.Equal(account.Matricule, line.Matricule);
            Assert.Equal("En attente", line.Status);
            Assert.Equal(100, line.Amount);
            Assert.Equal(item.IdAsGuid(), line.Id);
            store.User = "direction";
            Assert.Empty(OfflineFinance.Pending(store));
            Assert.Empty(OfflineFinance.PendingRows(store));
            Assert.Null(OfflineFinance.CachedAccount(store, account.EnrollmentId));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Montant_superieur_aux_mois_et_mois_inconnu_sont_refuses()
    {
        var (_, directory, account) = Setup();
        try
        {
            Assert.Throws<ArgumentException>(() => OfflineFinance.Preview(account, [account.Lines[0].FeeItemId], 101));
            Assert.Throws<ArgumentException>(() => OfflineFinance.Preview(account, [Guid.NewGuid()], 10));
            Assert.Throws<ArgumentException>(() => OfflineFinance.Preview(account, [account.Lines[0].FeeItemId, account.Lines[0].FeeItemId], 10));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}

internal static class OfflineFinanceTestIds
{
    public static Guid IdAsGuid(this OutboxItem item) => Guid.Parse(item.Id);
}
