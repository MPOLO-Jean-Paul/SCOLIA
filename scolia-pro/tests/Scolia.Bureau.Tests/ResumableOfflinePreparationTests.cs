using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class ResumableOfflinePreparationTests
{
    [Fact]
    public void Progression_reste_sur_disque_apres_fermeture_et_redemarrage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-resume-" + Guid.NewGuid().ToString("N"));
        try
        {
            var server = new Uri("https://establishment.example:5080/");
            const string user = "comptabilite";
            const string revision = "epoch-2026:17|finances";
            const string account = "api/inscriptions/00a00000-0000-4000-8000-000000000001/compte";
            var store = new OfflineStore(server, folder) { User = user };
            store.SavePage(account, "{\"id\":\"local-copy\"}");
            store.BeginPreparationFor(user, revision);
            Assert.False(store.WasPreparedFor(user, revision, account));
            store.MarkPreparedFor(user, revision, account);
            Assert.True(store.WasPreparedFor(user, revision, account));

            // Nouvelle instance, même dossier chiffré : l'étape ne doit pas repartir de zéro.
            var reopened = new OfflineStore(server, folder) { User = user };
            Assert.True(reopened.WasPreparedFor(user, revision, account));
            Assert.Contains("local-copy", reopened.ReadPage(account)!.Value.Json);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Compte_et_serveur_restreignent_chacun_leur_propre_curseur()
    {
        var root = Path.Combine(Path.GetTempPath(), "scolia-scope-" + Guid.NewGuid().ToString("N"));
        try
        {
            const string rev = "epoch:7|dossiers";
            const string resource = "api/apprenants?page=1&taille=50";
            var a = new OfflineStore(new Uri("https://school-a.example:5080/"), root)
                { User = "secretariat" };
            a.BeginPreparationFor("secretariat", rev);
            a.MarkPreparedFor("secretariat", rev, resource);
            Assert.True(a.WasPreparedFor("secretariat", rev, resource));
            Assert.False(a.WasPreparedFor("comptabilite", rev, resource));

            var b = new OfflineStore(new Uri("https://school-b.example:5080/"), root)
                { User = "secretariat" };
            b.BeginPreparationFor("secretariat", rev);
            Assert.False(b.WasPreparedFor("secretariat", rev, resource));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Nouvelle_revision_revalide_les_pages_sans_effacer_les_donnees_locales_ni_la_file_d_envoi()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-nextrev-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder)
                { User = "secretaire" };
            const string path = "api/apprenants?page=1&taille=50";
            store.SavePage(path, "{\"items\":[]}");
            store.Enqueue(new OutboxItem { User = "secretaire", Path = "api/apprenants",
                Method = "POST", Key = "TEST_KEY", Label = "Dossier hors ligne" });
            store.BeginPreparationFor("secretaire", "epoch:17|dossiers");
            store.MarkPreparedFor("secretaire", "epoch:17|dossiers", path);
            Assert.True(store.WasPreparedFor("secretaire", "epoch:17|dossiers", path));

            store.BeginPreparationFor("secretaire", "epoch:18|dossiers");
            Assert.False(store.WasPreparedFor("secretaire", "epoch:18|dossiers", path));
            Assert.NotNull(store.ReadPage(path)); // Données conservées pour ETag/304
            Assert.Single(store.Outbox()); // Les changements en attente sont conservés
            Assert.False(store.WasPreparedFor("secretaire", "epoch:17|dossiers", path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Une_marque_d_ancienne_revision_ou_d_un_autre_mode_ne_valide_pas_un_telechargement()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-access-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.example:5080/"), folder)
                { User = "lecteur" };
            const string path = "api/apprenants?page=1&taille=50";
            store.BeginPreparationFor("lecteur", "e:1|dossiers");
            store.MarkPreparedFor("lecteur", "e:1|dossiers", path);
            Assert.False(store.WasPreparedFor("lecteur", "e:1|finances", path));
            store.BeginPreparationFor("lecteur", "e:1|finances");
            Assert.False(store.WasPreparedFor("lecteur", "e:1|finances", path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
