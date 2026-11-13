using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Première connexion nominative, puis compte reconnu sans conserver un droit de session.</summary>
public sealed class RememberedAccountsTests
{
    [Fact]
    public void Plusieurs_comptes_restent_reconnus_sur_le_meme_poste_et_leurs_secrets_sont_isoles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-known-accounts", Guid.NewGuid().ToString("N"));
        try
        {
            var uri = new Uri("http://scolia-ecole-test:5080/");
            var school = new OfflineStore(uri, folder);
            Assert.Empty(school.KnownAccounts);

            var secretaria = new SessionDto(Guid.NewGuid(), "secretariat", "Secrétariat",
                "Secretariat", "Secrétariat", [], false, "Institut test", null, null, "FC", "243");
            var comptabilite = new SessionDto(Guid.NewGuid(), "comptabilite", "Comptabilité",
                "Comptabilite", "Comptabilité", [], false, "Institut test", null, null, "FC", "243");

            school.SaveCredential(secretaria.UserName, "code-secret-01", secretaria);
            school.RememberAccount(secretaria.UserName);
            school.SaveCredential(comptabilite.UserName, "code-comptable-02", comptabilite);
            school.RememberAccount(comptabilite.UserName);

            var reopened = new OfflineStore(uri, folder);
            Assert.Equal("comptabilite", reopened.LastAccount);
            Assert.Equal(["comptabilite", "secretariat"], reopened.KnownAccounts);
            Assert.Equal("comptabilite", reopened.OfflineLogin("comptabilite", "code-comptable-02")?.UserName);
            Assert.Equal("secretariat", reopened.OfflineLogin("secretariat", "code-secret-01")?.UserName);
            Assert.Null(reopened.OfflineLogin("secretariat", "code-comptable-02"));
            Assert.Null(reopened.OfflineLogin("comptabilite", "code-secret-01"));

            reopened.ForgetCredential("secretariat");
            Assert.Null(reopened.OfflineLogin("secretariat", "code-secret-01"));
            Assert.NotNull(reopened.OfflineLogin("comptabilite", "code-comptable-02"));
            Assert.Contains("secretariat", reopened.KnownAccounts);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Retirer_un_compte_ne_retire_ni_les_autres_comptes_ni_le_compte_sur_le_serveur()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-forget-account", Guid.NewGuid().ToString("N"));
        try
        {
            var uri = new Uri("http://test-forget:5080/");
            var store = new OfflineStore(uri, folder);
            var one = new SessionDto(Guid.NewGuid(), "enseignant", "Enseignant",
                "Enseignant", "Enseignant", [], false, "École test", null, null, "FC", "243");
            var two = new SessionDto(Guid.NewGuid(), "secretariat", "Secrétariat",
                "Secretariat", "Secrétariat", [], false, "École test", null, null, "FC", "243");
            store.SaveCredential("enseignant", "Professeur1", one);
            store.RememberAccount("enseignant");
            store.SaveCredential("secretariat", "Secretariat1", two);
            store.RememberAccount("secretariat");

            store.ForgetAccount("enseignant");
            var reopened = new OfflineStore(uri, folder);
            Assert.True(reopened.IsAccountForgotten("enseignant"));
            Assert.DoesNotContain("enseignant", reopened.KnownAccounts);
            Assert.Equal("secretariat", reopened.LastAccount);
            Assert.Null(reopened.OfflineLogin("enseignant", "Professeur1"));
            Assert.Equal("secretariat", reopened.OfflineLogin("secretariat", "Secretariat1")?.UserName);

            // Après une nouvelle authentification explicite, la mémorisation redevient possible.
            reopened.RememberAccount("enseignant");
            Assert.False(reopened.IsAccountForgotten("enseignant"));
            Assert.Contains("enseignant", reopened.KnownAccounts);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Retrait_interdit_si_une_operation_hors_ligne_attend_encore()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-pending-account", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("http://test-pending:5080/"), folder) { User = "enseignant" };
            store.RememberAccount("enseignant");
            store.Enqueue(new OutboxItem { Key = Guid.NewGuid().ToString("N"),
                User = "enseignant", Path = "api/deliberation/cotes-par-code" });
            Assert.Single(store.OutboxFor("ENSEIGNANT"));
            Assert.Throws<InvalidOperationException>(() => store.ForgetAccount("enseignant"));
            Assert.Contains("enseignant", store.KnownAccounts);
            Assert.Equal(1, store.PendingOperations);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Reconnaitre_un_compte_n_accorde_ni_mot_de_passe_ni_acces_dans_l_interface()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var root = dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable");
        var xml = XDocument.Load(Path.Combine(root, "src", "Scolia.Bureau", "Views", "LoginView.axaml"));
        Assert.Contains(xml.Descendants(), node => node.Name.LocalName == "ItemsControl"
            && node.Attributes().Any(a => a.Name.LocalName == "ItemsSource"
                && a.Value.Contains("KnownAccounts", StringComparison.Ordinal)));
        Assert.Contains(xml.Descendants(), node => node.Name.LocalName == "Button"
            && node.Attributes().Any(a => a.Name.LocalName == "Command"
                && a.Value.Contains("ChooseRememberedAccount", StringComparison.Ordinal)));
        Assert.Contains(xml.Descendants(), node => node.Name.LocalName == "TextBox"
            && node.Attributes().Any(a => a.Name.LocalName == "Text"
                && a.Value.Contains("Binding Password", StringComparison.Ordinal)));
    }
}
