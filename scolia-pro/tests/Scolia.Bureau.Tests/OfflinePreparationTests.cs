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
        Assert.Equal("api/apprenants?page=20&taille=50", OfflinePreparation.PagePath(20));
        Assert.Equal(1000, OfflinePreparation.MaximumStudents);
        Assert.Throws<ArgumentOutOfRangeException>(() => OfflinePreparation.PagePath(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OfflinePreparation.PagePath(21));
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
