using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;

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
            if (started) return;
            AppBuilder.Configure<OwnerApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            started = true;
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
        var button = All<Button>(root).First(b => b.Content as string == label);
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    private static string Texts(Control root) =>
        string.Join("\n", All<TextBlock>(root).Select(t => t.Text));

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
            foreach (var (width, height, suffix) in new[] { (1280, 820, "ordinateur"), (390, 844, "telephone") })
            {
                var shell = new OwnerShell();
                var window = new Window { Width = width, Height = height, Content = shell };
                window.Show();
                Pump(800);
                Assert.Contains("Établissements", Texts(shell), StringComparison.Ordinal);
                Assert.Contains("3", All<TextBlock>(shell).Select(t => t.Text));
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

                // Détail d'une école et action en deux temps (aucune écriture au premier appui).
                Navigate(shell, "Établissements");
                All<Button>(shell).First(b => All<TextBlock>(b).Any(t => t.Text == "Complexe scolaire La Référence"))
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Pump(800);
                Assert.Contains("PC-SECRETARIAT", Texts(shell), StringComparison.Ordinal);
                Save(window, output, $"04-ecole-{suffix}.png");
                var patches = service.Requests.Count(r => r.Method == HttpMethod.Patch);
                Click(shell, "Désactiver la facturation");
                Assert.Contains("Confirmer : licence gratuite", All<Button>(shell).Select(b => b.Content as string));
                Assert.Equal(patches, service.Requests.Count(r => r.Method == HttpMethod.Patch));
                window.Close();
            }

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
            Assert.Contains("Configurer l'accès", All<Button>(shell).Select(b => b.Content as string));
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
        var button = All<Button>(shell).First(b => b.IsEffectivelyVisible && b.Content is string text &&
            (text.EndsWith("   " + section, StringComparison.Ordinal) || text.EndsWith("\n" + section, StringComparison.Ordinal)));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(600);
    }

    private static void Save(Window window, string? output, string name)
    {
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        frame!.Save(Path.Combine(output, name));
    }
}
