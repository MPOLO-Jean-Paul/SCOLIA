using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.Automation;
using Avalonia.VisualTree;

namespace JPServices.Owner.Tests;

/// <summary>
/// Rendu hors écran de chaque page de la console sur le service simulé. Les captures sont écrites dans
/// le dossier donné par SCOLIA_CONSOLE_CAPTURES, sinon seulement vérifiées.
/// </summary>
[Collection("Avalonia")]
public sealed class ShellRenderTests
{
    private static readonly object Gate = new();
    private static bool started;

    private static void Start()
    {
        lock (Gate)
        {
            if (!started)
            {
                AppBuilder.Configure<OwnerApp>()
                    .UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
                started = true;
            }
            // xUnit remet son contexte entre les tests ; les réponses HTTP doivent revenir sur l'UI.
            AvaloniaSynchronizationContext.InstallIfNeeded();
        }
    }

    private static void Pump(int milliseconds = 400)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        }
    }

    private static IEnumerable<T> All<T>(Control root) where T : Control =>
        root.GetLogicalDescendants().OfType<T>();

    private static void Click(Control root, string label)
    {
        var button = All<Button>(root).First(b => AutomationProperties.GetName(b) == label);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    private static string Texts(Control root) =>
        string.Join("\n", All<TextBlock>(root).Select(t => t.Text));

    private static void Until(Control root, Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ready() && DateTime.UtcNow < deadline) Pump(20);
        Assert.True(ready(), Texts(root));
    }

    [Fact]
    public void Chaque_page_s_affiche_avec_les_donnees_du_service()
    {
        Start();
        var service = new FakeOwnerService();
        var dir = Path.Combine(Path.GetTempPath(), "scolia-console-ui-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), dir);
        OwnerVault.Current.Save(new OwnerCredentials("abcd1234.access", "secret-de-service-0123456789", "jeton-proprietaire-0123456789"));
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        var output = Environment.GetEnvironmentVariable("SCOLIA_CONSOLE_CAPTURES");
        try
        {
            foreach (var (width, height, suffix, dark) in new[]
            {
                (1280, 820, "ordinateur", false), (1024, 768, "ordinateur-etroit", false),
                (1280, 820, "ordinateur-sombre", true), (390, 844, "telephone", false),
                (320, 640, "petit-telephone", false), (390, 844, "telephone-sombre", true),
                (640, 360, "paysage", false)
            })
            {
                Application.Current!.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
                var shell = new OwnerShell();
                var window = new Window { Width = width, Height = height, Content = shell };
                window.Show();
                Pump(800);
                Assert.Contains("Établissements", Texts(shell), StringComparison.Ordinal);
                Assert.Contains("3", All<TextBlock>(shell).Select(t => t.Text));
                if (width < 780)
                    Assert.All(All<TextBlock>(shell).Where(t => t.Classes.Contains("ownerbody")), t => Assert.Equal(15, t.FontSize));
                Save(window, output, $"01-accueil-{suffix}.png");

                foreach (var (name, expected, file) in new[]
                {
                    ("Établissements", "Complexe scolaire La Référence", "02-etablissements"),
                    ("Licences", "Échue · lecture seule", "03-licences"),
                    ("Activité", "Licence modifiée", "06-activite"),
                    ("Compte", "Configuré · jeton de service", "07-compte")
                })
                {
                    Navigate(shell, name);
                    Assert.Contains(expected, Texts(shell), StringComparison.Ordinal);
                    Save(window, output, $"{file}-{suffix}.png");
                }

                foreach (var (name, expected, file) in new[]
                {
                    ("Serveurs", "Sans signal récent", "09-serveurs"),
                    ("Relais", "Cloudflare principal", "10-relais"),
                    ("Sauvegardes", "En retard", "11-sauvegardes")
                })
                {
                    Navigate(shell, name);
                    Assert.Contains(expected, Texts(shell), StringComparison.Ordinal);
                    Save(window, output, $"{file}-{suffix}.png");
                }

                // Détail d'une école et action en deux temps (aucune écriture au premier appui).
                Navigate(shell, "Établissements");
                All<Button>(shell).First(b => All<TextBlock>(b).Any(t => t.Text == "Complexe scolaire La Référence"))
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(800);
                Assert.Contains("PC-SECRETARIAT", Texts(shell), StringComparison.Ordinal);
                Save(window, output, $"04-ecole-{suffix}.png");
                var patches = service.Requests.Count(r => r.Method == HttpMethod.Patch);
                Click(shell, "Désactiver la facturation");
                Assert.Contains("Confirmer : licence gratuite", All<Button>(shell).Select(AutomationProperties.GetName));
                Assert.Equal(patches, service.Requests.Count(r => r.Method == HttpMethod.Patch));
                Save(window, output, $"12-confirmation-{suffix}.png");
                Click(shell, "Invitation de reprise (nouveau serveur)");
                Save(window, output, $"13-confirmation-longue-{suffix}.png");
                window.Close();
            }
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;

            // Sur ordinateur : invitation créée et affichée une seule fois.
            var desk = new OwnerShell();
            var w = new Window { Width = 1280, Height = 820, Content = desk };
            w.Show();
            Pump(600);
            Navigate(desk, "Établissements");
            All<TextBox>(desk).First().Text = "École Les Étoiles";
            Click(desk, "Créer l'invitation");
            Pump(600);
            Assert.Contains(All<SelectableTextBlock>(desk), t => t.Text == "SCO-7Q4M-XK2P-91TA");
            Save(w, output, "05-invitation-ordinateur.png");
            Navigate(desk, "Serveurs");
            Assert.Contains("Sans signal récent", Texts(desk), StringComparison.Ordinal);
            Navigate(desk, "Relais");
            Assert.Contains("Relais Cloudflare principal", Texts(desk), StringComparison.Ordinal);
            Navigate(desk, "Sauvegardes");
            Assert.Contains("En retard", Texts(desk), StringComparison.Ordinal);
            Save(w, output, "08-sauvegardes-ordinateur.png");
            w.Close();
        }
        finally
        {
            OwnerShell.ApiFactory = c => new OwnerApi(c);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Sans_acces_configure_la_console_invite_a_le_configurer_sans_appel_reseau()
    {
        Start();
        var service = new FakeOwnerService();
        OwnerVault.Current = new OwnerVault(new FakeProtector(), Path.Combine(Path.GetTempPath(), "vide-" + Guid.NewGuid().ToString("N")));
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        try
        {
            var shell = new OwnerShell();
            var window = new Window { Width = 390, Height = 844, Content = shell };
            window.Show();
            Pump();
            Assert.Contains("Configurer l'accès", All<Button>(shell).Select(AutomationProperties.GetName));
            Navigate(shell, "Licences");
            Assert.Contains("Accès non configuré sur cet appareil", Texts(shell), StringComparison.Ordinal);
            Navigate(shell, "Compte");
            Assert.Equal(2, All<TextBox>(shell).Count(t => t.PasswordChar == '•'));
            Save(window, Environment.GetEnvironmentVariable("SCOLIA_CONSOLE_CAPTURES"), "00-premier-lancement-telephone.png");
            Assert.Empty(service.Requests);
            window.Close();
        }
        finally { OwnerShell.ApiFactory = c => new OwnerApi(c); }
    }

    private static void Navigate(OwnerShell shell, string section)
    {
        // Barre latérale sur ordinateur (« É   Établissements »), barre du bas sur téléphone (« É\nÉtablissements »).
        var button = All<Button>(shell).FirstOrDefault(b => b.IsEffectivelyVisible && b.Classes.Contains("ownernav") &&
            AutomationProperties.GetName(b) == section);
        if (button is null)
        {
            var menu = All<Button>(shell).First(b => b.IsEffectivelyVisible && b.Classes.Contains("ownernav") && AutomationProperties.GetName(b) == "Menu");
            menu.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump();
            button = All<Button>(shell).First(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == section);
        }
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(600);
    }

    [Fact]
    public void La_navigation_ne_reste_pas_bloquee_derriere_une_lecture_lente()
    {
        Start();
        var pending = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeOwnerService
        {
            BeforeResponseAsync = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/summary", StringComparison.Ordinal)
                ? pending.Task : Task.FromResult<HttpResponseMessage?>(null)
        };
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-navigation-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerVault.Current.Save(new OwnerCredentials("abcd1234.access", "service-secret-fictif-2026", "proprietaire-fictif-2026"));
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        var window = new Window { Width = 320, Height = 640, Content = new OwnerShell() };
        try
        {
            window.Show();
            Pump(100);
            var shell = (OwnerShell)window.Content;
            Navigate(shell, "Établissements");
            Assert.Contains("Complexe scolaire La Référence", Texts(shell), StringComparison.Ordinal);
            Assert.False(pending.Task.IsCompleted);
            pending.SetResult(null);
            Pump();
            Assert.Contains("Complexe scolaire La Référence", Texts(shell), StringComparison.Ordinal);
            Assert.DoesNotContain("Lecture impossible", Texts(shell), StringComparison.Ordinal);
        }
        finally
        {
            pending.TrySetResult(null);
            window.Close();
            OwnerShell.ApiFactory = c => new OwnerApi(c);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Un_refus_de_modification_conserve_le_motif_et_permet_de_reessayer()
    {
        Start();
        var service = new FakeOwnerService
        {
            Override = request => request.Method == HttpMethod.Patch ? new HttpResponseMessage(System.Net.HttpStatusCode.Conflict)
                { Content = new StringContent("""{"error":"Modification refusée par le service."}""") } : null
        };
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-refus-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerVault.Current.Save(new OwnerCredentials("abcd1234.access", "service-secret-fictif-2026", "proprietaire-fictif-2026"));
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        var window = new Window { Width = 390, Height = 844, Content = new OwnerShell() };
        try
        {
            window.Show(); Pump(500);
            var shell = (OwnerShell)window.Content;
            Navigate(shell, "Établissements");
            Click(shell, "Complexe scolaire La Référence");
            var reason = All<TextBox>(shell).Single(t => AutomationProperties.GetName(t) == "Motif · Bloquer en lecture seule");
            reason.Text = "Demande administrative de test";
            var card = reason.GetLogicalAncestors().OfType<Border>().First(b => b.Classes.Contains("ownercard"));
            Click(card, "Appliquer");
            Assert.DoesNotContain(service.Requests, r => r.Method == HttpMethod.Patch);
            Click(card, "Confirmer");
            Assert.Contains("Modification refusée par le service.", Texts(shell), StringComparison.Ordinal);
            Assert.Equal("Demande administrative de test", reason.Text);
            Assert.Contains(All<TextBox>(shell), t => ReferenceEquals(t, reason));
            Assert.True(All<Button>(card).Single(b => AutomationProperties.GetName(b) == "Appliquer").IsEnabled);
            Assert.Single(service.Requests, r => r.Method == HttpMethod.Patch);
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); Directory.Delete(directory, true); }
    }

    [Fact]
    public void Un_identifiant_refuse_et_une_rotation_ne_videront_pas_le_formulaire()
    {
        Start();
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-acces-refuse-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        var service = new FakeOwnerService
        {
            Override = _ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                { Content = new StringContent("""{"error":"refus"}""") }
        };
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), service);
        var window = new Window { Width = 1280, Height = 820, Content = new OwnerShell() };
        try
        {
            window.Show(); Pump();
            var shell = (OwnerShell)window.Content;
            Navigate(shell, "Compte");
            var inputs = All<TextBox>(shell).ToArray();
            inputs[0].Text = "abcd1234.access";
            inputs[1].Text = "service-secret-fictif-2026";
            inputs[2].Text = "proprietaire-fictif-2026";
            window.Width = 320; window.Height = 300; Pump();
            Assert.Contains(All<TextBox>(shell), t => ReferenceEquals(t, inputs[0]));
            Click(shell, "Enregistrer et vérifier");
            Assert.Contains("Jeton propriétaire refusé", Texts(shell), StringComparison.Ordinal);
            Assert.Equal("service-secret-fictif-2026", inputs[1].Text);
            Assert.Null(OwnerVault.Current.Load());
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static T Read<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump(20);
        return task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
    }

    [Fact]
    public void Un_acces_valide_est_enregistre_et_peut_etre_efface_depuis_le_meme_ecran()
    {
        Start();
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-configuration-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerShell.ApiFactory = c => new OwnerApi(c, new Uri("https://console.test/"), new FakeOwnerService());
        var shell = new OwnerShell();
        var window = new Window { Width = 320, Height = 640, Content = shell };
        try
        {
            window.Show(); Pump(); Navigate(shell, "Compte");
            var inputs = All<TextBox>(shell).ToArray();
            inputs[0].Text = "abcd1234.access";
            inputs[1].Text = "service-secret-fictif-2026";
            inputs[2].Text = "proprietaire-fictif-2026";
            Click(shell, "Enregistrer et vérifier");
            Assert.NotNull(OwnerVault.Current.Load());
            Assert.Contains("Accès vérifié et enregistré", Texts(shell), StringComparison.Ordinal);
            Assert.Contains("Configuré · jeton de service", Texts(shell), StringComparison.Ordinal);
            Click(shell, "Effacer l'accès de cet appareil");
            Assert.NotNull(OwnerVault.Current.Load());
            Click(shell, "Confirmer l'effacement");
            Assert.Null(OwnerVault.Current.Load());
            Assert.Equal(3, All<TextBox>(shell).Count());
            Assert.Contains("Accès effacé de cet appareil.", Texts(shell), StringComparison.Ordinal);
            Assert.True(shell.Back());
            Assert.Contains("Votre espace administrateur", Texts(shell), StringComparison.Ordinal);
            Assert.False(shell.Back());
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Le_renouvellement_et_les_relais_modifient_le_vrai_service_et_sa_base()
    {
        Start();
        using var service = new LocalOwnerService();
        var directory = Path.Combine(Path.GetTempPath(), "scolia-owner-worker-reel-" + Guid.NewGuid().ToString("N"));
        OwnerVault.Current = new OwnerVault(new FakeProtector(), directory);
        OwnerVault.Current.Save(LocalOwnerService.Credentials);
        OwnerShell.ApiFactory = c => new OwnerApi(c, service.Address);
        using var api = new OwnerApi(LocalOwnerService.Credentials, service.Address);
        var listing = Read(api.SchoolsAsync());
        var school = OwnerText.Items(listing, "schools").Single(s => OwnerText.Str(s, "name") == "École Démonstration JP-Services");
        var id = OwnerText.Str(school, "id");
        Assert.True(OwnerText.Flag(OwnerText.Child(school, "license"), "readonly"));
        var window = new Window { Width = 390, Height = 844, Content = new OwnerShell() };
        try
        {
            window.Show(); Pump(600);
            var shell = (OwnerShell)window.Content;
            Navigate(shell, "Établissements");
            Click(shell, "École Démonstration JP-Services");
            Until(shell, () => All<TextBox>(shell).Any(t => AutomationProperties.GetName(t) == "Nombre de jours · Enregistrer un paiement"));
            var days = All<TextBox>(shell).Single(t => AutomationProperties.GetName(t) == "Nombre de jours · Enregistrer un paiement");
            days.Text = "365";
            var card = days.GetLogicalAncestors().OfType<Border>().First(b => b.Classes.Contains("ownercard"));
            Click(card, "Appliquer");
            Assert.Contains(All<TextBox>(shell), t => ReferenceEquals(t, days));
            Assert.Contains("Confirmer le paiement et lever le contrôle manuel", All<Button>(card).Select(AutomationProperties.GetName));
            Click(card, "Confirmer le paiement et lever le contrôle manuel");
            Until(shell, () => Texts(shell).Contains("Payée · 365 j restants", StringComparison.Ordinal));
            var paid = Read(api.SchoolAsync(id));
            Assert.False(OwnerText.Flag(OwnerText.Child(paid, "license"), "readonly"));
            Assert.True(OwnerText.Number(OwnerText.Child(paid, "school"), "paid_until") > DateTimeOffset.UtcNow.AddDays(364).ToUnixTimeMilliseconds());

            Navigate(shell, "Relais");
            Until(shell, () => Texts(shell).Contains("Relais de démonstration", StringComparison.Ordinal));
            Click(shell, "Utiliser Cloudflare comme relais principal");
            Assert.DoesNotContain(OwnerText.Items(Read(api.RelaysAsync()), "relays"), r => OwnerText.Str(r, "state") == "active");
            Click(shell, "Confirmer le choix Cloudflare");
            Until(shell, () => Texts(shell).Contains("SCOLIA Cloudflare (principal)", StringComparison.Ordinal));
            var relays = OwnerText.Items(Read(api.RelaysAsync()), "relays");
            var active = Assert.Single(relays, r => OwnerText.Str(r, "state") == "active");
            Assert.Equal("wss://relay.jp-services.org/", OwnerText.Str(active, "url"));
            var revision = OwnerText.Number(active, "revision");
            Read(api.UseCloudflarePrimaryAsync());
            Assert.Equal(revision, OwnerText.Number(OwnerText.Items(Read(api.RelaysAsync()), "relays").Single(r => OwnerText.Str(r, "state") == "active"), "revision"));
        }
        finally { window.Close(); OwnerShell.ApiFactory = c => new OwnerApi(c); Directory.Delete(directory, true); }
    }

    private static void Save(Window window, string? output, string name)
    {
        foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible &&
                     (c is Button or TextBox || c is Border border && border.Classes.Contains("ownercard"))))
        {
            if (control.Bounds.Width <= 0 || control.TransformToVisual(window) is not { } transform) continue;
            var bounds = new Rect(control.Bounds.Size).TransformToAABB(transform);
            Assert.True(bounds.Left >= -1 && bounds.Right <= window.Bounds.Width + 1,
                $"{name} : {control.GetType().Name} sort horizontalement ({bounds.Left:0} à {bounds.Right:0}).");
        }
        foreach (var illustration in window.GetVisualDescendants().OfType<Image>().Where(i => i.IsEffectivelyVisible))
            Assert.True(illustration.Source is { Size.Width: > 0, Size.Height: > 0 }, $"{name} : illustration absente.");
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        frame!.Save(Path.Combine(output, name));
    }
}
