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
    public void Retirer_un_compte_efface_son_acces_hors_ligne_et_conserve_celui_des_autres()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-forget-" + Guid.NewGuid().ToString("N"));
        try
        {
            var server = new Uri("https://example-school.test:5080/");
            var store = new OfflineStore(server, folder);
            var a = new SessionDto(Guid.NewGuid(), "prof", "Enseignant", "Enseignant", "Professeur",
                [], false, "École test", null, null, "FC", "243");
            var b = a with { UserName = "secretariat" };
            store.SaveCredential("prof", "secret-1", a);
            store.RememberAccount("prof");
            store.SaveCredential("secretariat", "secret-2", b);
            store.RememberAccount("secretariat");

            Assert.True(store.ForgetKnownAccount("prof"));
            Assert.DoesNotContain("prof", store.KnownAccounts);
            Assert.True(store.WasForgottenAccount("prof"));
            Assert.Null(store.OfflineLogin("prof", "secret-1"));
            Assert.Equal("secretariat", store.OfflineLogin("secretariat", "secret-2")?.UserName);

            // Même après le redémarrage, le compte retiré ne réapparaît pas.
            var reopened = new OfflineStore(server, folder);
            Assert.DoesNotContain("prof", reopened.KnownAccounts);
            Assert.True(reopened.WasForgottenAccount("prof"));
            reopened.RememberAccount("prof"); // après reconnexion authentifiée
            Assert.False(reopened.WasForgottenAccount("prof"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Un_compte_avec_operations_non_envoyees_ne_peut_pas_etre_oublie()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-forget-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://school.test:5080/"), folder) { User = "prof" };
            store.RememberAccount("prof");
            store.Enqueue(new OutboxItem { User = "prof", Method = "POST",
                Path = "api/deliberation/cotes-par-code", Key = "mark-1", Label = "Note" });
            Assert.False(store.ForgetKnownAccount("prof"));
            Assert.Contains("prof", store.KnownAccounts);
            Assert.Single(store.Outbox());
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
