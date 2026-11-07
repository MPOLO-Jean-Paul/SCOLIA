using System.Text.Json;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Tests;

public sealed class OfflinePreparationTests
{
    [Fact]
    public void Preparation_uses_exact_student_pages_expected_by_offline_search()
    {
        Assert.Equal("api/apprenants?page=1&taille=50", OfflinePreparation.PagePath(1));
        Assert.Equal("api/apprenants?page=200&taille=50", OfflinePreparation.PagePath(200));
        Assert.Equal(10000, OfflinePreparation.MaximumStudents);
        Assert.Throws<ArgumentOutOfRangeException>(() => OfflinePreparation.PagePath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OfflinePreparation.PagePath(201));
    }

    [Fact]
    public void Background_cache_refresh_rotates_oldest_pages_and_respects_user_isolation()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-offline-refresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder)
            {
                User = "secretariat"
            };
            var oldPaths = Enumerable.Range(1, 12).Select(n => $"api/apprenants/ancien-{n}").ToArray();
            var laterPaths = Enumerable.Range(13, 48).Select(n => $"api/apprenants/recent-{n}").ToArray();
            foreach (var path in oldPaths) store.SavePage(path, "{}");
            Thread.Sleep(100); // Horodatages nettement séparés pour un test déterministe.
            foreach (var path in laterPaths) store.SavePage(path, "{}");

            var initial = store.CachedPaths(limit: 12);
            Assert.Equal(12, initial.Count);
            Assert.All(initial, p => Assert.Contains(p, oldPaths));

            Thread.Sleep(100);
            foreach (var path in initial) store.SavePage(path, "{\"updated\":true}");
            var next = store.CachedPaths(limit: 12);
            Assert.Equal(12, next.Count);
            Assert.DoesNotContain(next, p => initial.Contains(p));
            Assert.All(next, p => Assert.Contains(p, laterPaths));

            store.User = "comptabilite";
            Assert.Empty(store.CachedPaths());
            store.User = "secretariat";
            Assert.Equal(12, store.CachedPaths(limit: 12).Count);
            Assert.Empty(store.CachedPaths(limit: 0));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Revoked_cached_resource_is_removed_only_for_the_original_user()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-offline-revoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder);
            const string resource = "api/apprenants/74599756-ecfa-48ee-b37c-cef33bcc773a";
            store.User = "secretariat";
            store.SavePage(resource, "{\"account\":\"secretariat\"}");
            store.User = "comptabilite";
            store.SavePage(resource, "{\"account\":\"comptabilite\"}");

            // Un refus du serveur pour le secrétariat ne doit pas effacer la copie comptabilité.
            store.RemovePageFor("secretariat", resource);
            Assert.NotNull(store.ReadPage(resource));
            Assert.Contains("comptabilite", store.ReadPage(resource)!.Value.Json);

            store.User = "secretariat";
            Assert.Null(store.ReadPage(resource));
            Assert.DoesNotContain(resource, store.CachedPaths());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Index_en_memoire_se_met_a_jour_apres_saisie_locale_et_copie_serveur()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-index-fast-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder)
            {
                User = "secretariat"
            };
            var id = Guid.NewGuid();
            var pupil = new StudentListItem(id, Guid.NewGuid(), "INF293",
                "ANCIEN", "KABEYA", "PAUL", "M", "INF1", "Actif", null, 0m, 0m);
            var path = OfflinePreparation.PagePath(1);
            store.SavePage(path, JsonSerializer.Serialize(
                new PagedResult<StudentListItem>([pupil], 1, 1, 50, null), Json.Options));

            Assert.NotNull(OfflineStudentSearch.Search(store, "ANCIEN", null, null, 1));
            Assert.Single(OfflineStudentSearch.Search(store, "ANCIEN", null, null, 1)!.Page.Items);

            var queued = new OutboxItem
            {
                User = store.User, Method = "PUT", Path = $"api/apprenants/{id}",
                Key = Guid.NewGuid().ToString("N"),
                Body = "{\"lastName\":\"NOUVEAU\"}"
            };
            store.Enqueue(queued);
            Assert.Single(OfflineStudentSearch.Search(store, "NOUVEAU", null, null, 1)!.Page.Items);
            Assert.Empty(OfflineStudentSearch.Search(store, "ANCIEN", null, null, 1)!.Page.Items);

            store.Sent(queued.Id);
            Assert.Single(OfflineStudentSearch.Search(store, "ANCIEN", null, null, 1)!.Page.Items);

            var changed = pupil with { LastName = "RETABLI" };
            store.SavePage(path, JsonSerializer.Serialize(
                new PagedResult<StudentListItem>([changed], 1, 1, 50, null), Json.Options));
            Assert.Single(OfflineStudentSearch.Search(store, "RETABLI", null, null, 1)!.Page.Items);
            Assert.Empty(OfflineStudentSearch.Search(store, "ANCIEN", null, null, 1)!.Page.Items);

            store.User = "comptabilite";
            Assert.Null(OfflineStudentSearch.Search(store, "RETABLI", null, null, 1));
            store.User = "secretariat";
            Assert.Single(OfflineStudentSearch.Search(store, "RETABLI", null, null, 1)!.Page.Items);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Pages_chiffrees_sont_lues_en_lot_sans_fuite_entre_utilisateurs()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-pages-bulk-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder) { User = "secretariat" };
            var first = OfflinePreparation.PagePath(1);
            var second = OfflinePreparation.PagePath(2);
            store.SavePage(first, "{\"total\":2}");
            store.SavePage(second, "{\"total\":3}");
            var pages = store.ReadPages([first, second]);
            Assert.Equal(2, pages.Count);
            Assert.Contains("\"total\":2", pages[first], StringComparison.Ordinal);
            store.User = "comptabilite";
            Assert.Empty(store.ReadPages([first, second]));
            store.User = "secretariat";
            Assert.Equal(2, store.ReadPages([first, second]).Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Cached_index_remains_private_to_the_authenticated_account()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-offline-prepare-" + Guid.NewGuid().ToString("N"));
        try
        {
            var server = new Uri("https://school.example:5080/");
            var store = new OfflineStore(server, folder) { User = "secretariat" };
            var pupil = new StudentListItem(Guid.NewGuid(), Guid.NewGuid(), "INF293",
                "EXEMPLE", "ELEVE", "TEST", "F", "INF1", "Actif", null, 2500m, 500m);
            var page = new PagedResult<StudentListItem>([pupil], 1, 1,
                OfflinePreparation.PageSize, null);
            store.SavePage(OfflinePreparation.PagePath(1), JsonSerializer.Serialize(page, Json.Options));

            var found = OfflineStudentSearch.Search(store, "EXEMPLE", null, null, 1);
            Assert.NotNull(found);
            Assert.True(found.Complete);
            Assert.Single(found.Page.Items);

            store.User = "comptabilite";
            Assert.Null(OfflineStudentSearch.Search(store, "EXEMPLE", null, null, 1));
            Assert.Null(store.ReadPage(OfflinePreparation.PagePath(1)));

            store.User = "secretariat";
            Assert.NotNull(store.ReadPage(OfflinePreparation.PagePath(1)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
