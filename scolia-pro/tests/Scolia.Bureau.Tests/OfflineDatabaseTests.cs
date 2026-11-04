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

    [Fact]
    public void Registre_local_recherche_sans_serveur_et_signale_si_la_liste_est_incomplete()
    {
        var school = Store();
        var records = new[]
        {
            new Scolia.Bureau.Api.StudentListItem(Guid.NewGuid(), null, "INF001", "MPOLO", "LONO",
                "Jean", "M", "INF1", "Actif", "0977000001", 15000m, 0m),
            new Scolia.Bureau.Api.StudentListItem(Guid.NewGuid(), null, "INF002", "KABUYA", "LUNGU",
                "Alice", "F", "INF1", "Actif", "0977000002", 25000m, 5000m),
            new Scolia.Bureau.Api.StudentListItem(Guid.NewGuid(), null, "ACC003", "BANDA", "KASI",
                "Ruth", "F", "ACC1", "Sorti", "0977000003", 0m, 0m)
        };
        var page = new Scolia.Bureau.Api.PagedResult<Scolia.Bureau.Api.StudentListItem>(records, 4, 1, 50, null);
        school.SavePage("api/apprenants?page=1&taille=50",
            JsonSerializer.Serialize(page, Scolia.Bureau.Api.Json.Options));

        var result = OfflineStudentSearch.Search(school, "mPoLo", "INF1", "Actif", 1);
        Assert.NotNull(result);
        Assert.Equal("INF001", Assert.Single(result.Page.Items).Matricule);
        Assert.Equal(1, result.Page.Total);
        Assert.Equal(3, result.Loaded);
        Assert.Equal(4, result.Registered);
        Assert.False(result.Complete);

        var byPhone = OfflineStudentSearch.Search(school, "0977000002", null, null, 20);
        Assert.NotNull(byPhone);
        Assert.Equal("INF002", Assert.Single(byPhone.Page.Items).Matricule);
        Assert.Equal(1, byPhone.Page.Page); // la pagination reste utilisable même hors ligne

        // Les autres comptes n'ont pas accès à l'index de l'utilisateur.
        Assert.Null(OfflineStudentSearch.Search(Store("comptabilite"), "MPOLO", null, null, 1));
    }

    [Fact]
    public void Accuse_de_reception_d_un_ancien_compte_ne_supprime_pas_les_operations_d_un_autre()
    {
        var store = Store("secretariat");
        var a = new OutboxItem { Key = "secretariat-operation", User = "secretariat", Path = "api/paiements" };
        store.Enqueue(a);
        store.User = "comptabilite";
        var b = new OutboxItem { Key = "comptabilite-operation", User = "comptabilite", Path = "api/paiements" };
        store.Enqueue(b);
        store.SentFor("secretariat", a.Id);

        Assert.Equal(b.Id, Assert.Single(store.Outbox()).Id);
        store.User = "secretariat";
        Assert.Empty(store.Outbox());
        store.User = "comptabilite";
        store.RefusedFor("secretariat", b.Id, "Ne doit pas modifier ce compte");
        Assert.Single(store.Outbox());
        Assert.Empty(store.Conflicts());
    }

    [Fact]
    public void Reponse_en_retard_est_enregistree_uniquement_pour_le_compte_a_l_origine()
    {
        var store = Store("secretariat");
        const string path = "api/rapports/tableau-de-bord";
        store.User = "comptabilite"; // Un autre compte ouvre sa session avant la fin de la requête.
        store.SavePageFor("secretariat", path, """{"nombre":131}""");

        Assert.Null(store.ReadPage(path));
        store.User = "secretariat";
        Assert.Contains("131", store.ReadPage(path)?.Json);
        store.User = "comptabilite";
        store.SavePage(path, """{"nombre":42}""");
        Assert.Contains("42", store.ReadPage(path)?.Json);
        store.User = "secretariat";
        Assert.Contains("131", store.ReadPage(path)?.Json);
    }

    [Fact]
    public void Logging_out_clears_background_session_but_preserves_pending_operations()
    {
        var store = Store();
        var session = new Scolia.Bureau.Api.SessionDto(Guid.NewGuid(), "secretariat", "Secrétariat", "Secretariat", "Secrétariat", [], false, "Institut", null, null, "FC", "243");
        store.SaveCredential("SECRETARIAT", "Secretariat1", session, "scolia.session=private-cookie");
        store.Enqueue(new OutboxItem { Key = "pending-logout", Path = "api/apprenants" });
        Assert.Equal("scolia.session=private-cookie", store.SavedCredential?.SessionCookie);
        store.ClearBackgroundSession();
        Assert.Equal(string.Empty, Store().SavedCredential?.SessionCookie);
        Assert.Single(Store().Outbox());
        Assert.NotNull(Store().OfflineLogin("SECRETARIAT", "Secretariat1"));
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
