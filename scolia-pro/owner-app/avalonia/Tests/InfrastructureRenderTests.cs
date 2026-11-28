using System.Net;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;

namespace JPServices.Owner.Tests;

public sealed partial class ShellRenderTests
{
    private static TextBox NamedField(Control root, string name) =>
        All<TextBox>(root).Single(t => AutomationProperties.GetName(t) == name);

    [Fact]
    public void Les_reglages_natifs_modifient_le_vrai_Worker_avec_confirmation_et_effacement_des_secrets()
    {
        Start();
        using var service = new LocalOwnerService();
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-infrastructure-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerVault.Current.Save(LocalOwnerService.Credentials);
        OwnerShell.ApiFactory = c => new OwnerApi(c, service.Address);
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        var shell = new OwnerShell();
        var window = new Window { Width = 320, Height = 640, Content = shell };
        try
        {
            window.Show(); Pump(600);
            Navigate(shell, "Stockage cloud");
            Until(shell, () => Texts(shell).Contains("Identifiants Cloudflare R2", StringComparison.Ordinal));
            NamedField(shell, "Compte Cloudflare R2").Text = InfrastructureApiTests.Account;
            NamedField(shell, "Nom du bucket R2").Text = "scolia-tests";
            NamedField(shell, "Clé d'accès R2").Text = InfrastructureApiTests.AccessKey;
            NamedField(shell, "Clé secrète R2").Text = InfrastructureApiTests.Secret;
            Assert.NotEqual('\0', NamedField(shell, "Clé secrète R2").PasswordChar);
            Click(shell, "Enregistrer et vérifier le stockage");
            Assert.False(OwnerText.Flag(Read(api.StorageAsync()), "ready"));
            Click(shell, "Confirmer la vérification et l'enregistrement");
            Until(shell, () => Texts(shell).Contains("Stockage vérifié et disponible", StringComparison.Ordinal));
            Assert.True(OwnerText.Flag(Read(api.StorageAsync()), "ready"));
            Assert.True(string.IsNullOrEmpty(NamedField(shell, "Clé secrète R2").Text));
            Assert.True(string.IsNullOrEmpty(NamedField(shell, "Clé d'accès R2").Text));
            Click(shell, "Proposer la sauvegarde à toutes les écoles");
            Assert.Contains(OwnerText.Items(Read(api.SchoolsAsync()), "schools"), school => !OwnerText.Flag(school, "cloud_requested"));
            Click(shell, "Confirmer la proposition à toutes les écoles");
            Until(shell, () => Texts(shell).Contains("Toutes les écoles", StringComparison.Ordinal));
            Assert.All(OwnerText.Items(Read(api.SchoolsAsync()), "schools"), school => Assert.True(OwnerText.Flag(school, "cloud_requested")));
            Click(shell, "Revenir");
            Assert.Contains("le contenu est chiffré par chaque école", Texts(shell), StringComparison.Ordinal);

            Navigate(shell, "Secours HTTPS");
            NamedField(shell, "Nom du secours HTTPS").Text = "Secours natif vérifié";
            NamedField(shell, "Adresse du site Netlify").Text = "https://scolia-demo.netlify.app/";
            Click(shell, "Enregistrer le site de secours");
            Assert.False(OwnerText.Flag(Read(api.NetlifyAsync()), "configured"));
            Click(shell, "Confirmer : conserver le pilote désactivé");
            Until(shell, () => All<Button>(shell).Any(b => AutomationProperties.GetName(b) == "Tester le site de secours"));
            Assert.False(OwnerText.Flag(Read(api.NetlifyAsync()), "can_activate"));
            Click(shell, "Tester le site de secours");
            Until(shell, () => Texts(shell).Contains("Netlify répond via HTTPS", StringComparison.Ordinal));
            Click(shell, "Retirer le site de secours");
            Assert.True(OwnerText.Flag(Read(api.NetlifyAsync()), "configured"));
            Click(shell, "Confirmer le retrait du site");
            Until(shell, () => !All<Button>(shell).Any(b => AutomationProperties.GetName(b) == "Tester le site de secours"));
            Assert.False(OwnerText.Flag(Read(api.NetlifyAsync()), "configured"));
            Click(shell, "Revenir");
            Assert.Contains("Catalogue WSS", Texts(shell), StringComparison.Ordinal);
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); Directory.Delete(directory, true); }
    }

    [Fact]
    public void Un_refus_de_stockage_conserve_le_formulaire_masque_sans_fausse_confirmation()
    {
        Start();
        var service = new FakeOwnerService { Override = request => request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("""{"error":"Test du stockage échoué."}""", Encoding.UTF8, "application/json") } : null };
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-r2-refus-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerVault.Current.Save(LocalOwnerService.Credentials);
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        var shell = new OwnerShell();
        var window = new Window { Width = 390, Height = 844, Content = shell };
        try
        {
            window.Show(); Pump(600); Navigate(shell, "Stockage cloud");
            var secret = NamedField(shell, "Clé secrète R2");
            secret.Text = InfrastructureApiTests.Secret;
            Click(shell, "Enregistrer et vérifier le stockage");
            Click(shell, "Confirmer la vérification et l'enregistrement");
            Until(shell, () => Texts(shell).Contains("Test du stockage échoué", StringComparison.Ordinal));
            Assert.Same(secret, NamedField(shell, "Clé secrète R2"));
            Assert.Equal(InfrastructureApiTests.Secret, secret.Text);
            Assert.NotEqual('\0', secret.PasswordChar);
            Assert.DoesNotContain("Opération enregistrée.", Texts(shell), StringComparison.Ordinal);
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); Directory.Delete(directory, true); }
    }
}
