using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Contrôles de non-régression de l'assistant Windows et du nouvel accueil compact.</summary>
public sealed class InstallerAndHomePolishTests
{
    private static string Source(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln"),
            path.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void L_assistance_de_l_installeur_est_un_lien_mailto_cliquable_sur_toutes_les_pages()
    {
        var iss = File.ReadAllText(Source("scripts/windows/SCOLIA-Pro.iss"));
        Assert.Contains("#define AppContactEmail \"jeanpaulmpolo242@gmail.com\"", iss);
        Assert.Contains("#define AppSupportLink \"mailto:jeanpaulmpolo242@gmail.com\"", iss);
        Assert.Contains("AppSupportURL={#AppSupportLink}", iss);
        Assert.Contains("LienAssistance.Parent := WizardForm", iss);
        Assert.Contains("LienAssistance.OnClick := @OuvrirAssistance", iss);
        Assert.Contains("LienAssistance.Cursor := crHand", iss);
        Assert.Contains("ShellExecAsOriginalUser('open', '{#AppSupportLink}'", iss);
        Assert.Contains("procedure InitializeWizard", iss);
        Assert.DoesNotContain("AppSupportURL=https://github.com", iss);
    }

    [Fact]
    public void Accueils_Windows_et_Android_montrent_une_coupure_sans_dialogue_repetitif()
    {
        foreach (var view in new[] { "Views/HomeView.axaml", "Views/Phone/HomeView.axaml" })
        {
            var doc = XDocument.Load(Source("src/Scolia.Bureau/" + view));
            var elements = doc.Descendants().ToArray();
            // Sur Windows aussi, la ligne réseau doit être visible même en ligne,
            // exactement comme sur l'accueil du téléphone ; un clic ouvre Réseau et postes.
            Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
                e.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("Network")) &&
                !e.Attributes().Any(a => a.Name.LocalName == "IsVisible" && a.Value.Contains("Sync.Offline")));
            Assert.Contains(elements, e => e.Name.LocalName == "TextBlock" &&
                e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains("Sync.StatusText")));
        }

        // Un cycle indépendant vérifie le serveur même lorsque l'utilisateur
        // ne consulte aucune rubrique. La reprise automatique conserve l'outbox.
        var sync = File.ReadAllText(Source("src/Scolia.Bureau/ViewModels/Sync.cs"));
        Assert.Contains("_ = HealthLoopAsync(token)", sync, StringComparison.Ordinal);
        Assert.Contains("await api.IsRunningAsync(ct)", sync, StringComparison.Ordinal);
        Assert.Contains("_ = ReconnectLoopAsync(token)", sync, StringComparison.Ordinal);
        Assert.Contains("await SyncAsync(manual: false)", sync, StringComparison.Ordinal);
        Assert.Contains("TryReconnectPairedLanAsync(ct)", sync, StringComparison.Ordinal);

        var client = File.ReadAllText(Source("src/Scolia.Bureau/Api/ScoliaClient.cs"));
        Assert.Contains("if (sent > 0 && !connectionLost) SetOnline(true)", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Accueil_desktop_conserve_des_cartes_claires_et_responsives()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var elements = doc.Descendants().ToArray();

        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "desktopwelcome"));
        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Height" && a.Value == "3") &&
            e.Attributes().Any(a => a.Name.LocalName == "Background" && a.Value.Contains("AccentBrush")));
        // Les boutons doivent demeurer à droite du séparateur VERTICAL sur petits
        // et grands écrans, et non sous le message d'accueil.
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "WelcomeLayout") &&
            e.Attributes().Any(a => a.Name.LocalName == "ColumnDefinitions" && a.Value == "*,Auto,Auto"));
        Assert.Contains(elements, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Grid.Column" && a.Value == "1") &&
            e.Attributes().Any(a => a.Name.LocalName == "Width" && a.Value == "1"));
        Assert.Contains(elements, e => e.Name.LocalName == "ItemsControl" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "WelcomeQuickActions") &&
            e.Attributes().Any(a => a.Name.LocalName == "Grid.Column" && a.Value == "2"));
        Assert.Contains(elements, e => e.Name.LocalName == "UniformGrid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal) && a.Value == "112"));
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "410"));
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "760"));
        // Les informations essentielles sont désormais sur une seule ligne.
        foreach (var property in new[] { "Greeting", "AccountContextLine", "ShortTitle",
            "Value", "Hint" })
        {
            Assert.Contains(elements, e => e.Name.LocalName == "TextBlock" &&
                e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains(property)) &&
                e.Attributes().Any(a => a.Name.LocalName == "MaxLines" && a.Value == "1") &&
                e.Attributes().Any(a => a.Name.LocalName == "TextTrimming" && a.Value == "CharacterEllipsis"));
        }
        // Le bandeau garde ses deux boutons à droite, sans retour sous le salut.
        var logic = File.ReadAllText(Source("src/Scolia.Bureau/Views/HomeView.axaml.cs"));
        Assert.Contains("Math.Min(244, Math.Max(98, available * 0.34))", logic, StringComparison.Ordinal);
        Assert.Contains("WelcomeQuickActions.Width = rightWidth", logic, StringComparison.Ordinal);
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("ShowAllTools")));
    }
    [Fact]
    public void Accueil_Windows_reprend_les_groupes_et_quatre_raccourcis_du_mobile()
    {
        var desktop = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var phone = XDocument.Load(Source("src/Scolia.Bureau/Views/Phone/HomeView.axaml"));
        var desktopNodes = desktop.Descendants().ToArray();
        var phoneNodes = phone.Descendants().ToArray();

        // Ne jamais créer une liste de fonctionnalités Windows différente du téléphone.
        foreach (var nodes in new[] { desktopNodes, phoneNodes })
            Assert.Contains(nodes, e => e.Name.LocalName == "ItemsControl" &&
                e.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value == "{Binding PhoneTools}"));

        // Les raccourcis sont des lignes GROUPÉES, et non des tuiles isolées sans contexte.
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "Border" &&
            e.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "desktopgroup"));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "desktopentry") &&
            e.Descendants().Any(d => d.Name.LocalName == "TextBlock" &&
                d.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "{Binding Description}")) &&
            e.Descendants().Any(d => d.Name.LocalName == "MaterialIcon" &&
                d.Attributes().Any(a => a.Name.LocalName == "Kind" && a.Value == "ChevronRight")));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "RACCOURCIS"));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "TextBlock" &&
            e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "APERÇU"));

        // L'état de la connexion est un vrai raccourci permanent, avec explication.
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "Button" &&
            e.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("Network")) &&
            e.Descendants().Any(d => d.Name.LocalName == "TextBlock" &&
                d.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains("Sync.StatusHint"))));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "ItemsControl" &&
            e.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("Kpis")));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "ItemsControl" &&
            e.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("Receipts")));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "ItemsControl" &&
            e.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("Tasks")));

        var home = File.ReadAllText(Source("src/Scolia.Bureau/ViewModels/Home.cs"));
        Assert.Contains("public IReadOnlyList<ToolCardVm> PhoneTools => Tools.Take(4).ToList()", home);
        Assert.Contains("if (perm is null || Shell.Can(perm))", home);
    }

    [Fact]
    public void L_installateur_met_a_jour_et_redemarre_le_vrai_serveur_sans_supprimer_la_base()
    {
        var install = File.ReadAllText(Source("scripts/windows/SCOLIA-Pro.iss"));
        var updater = File.ReadAllText(Source("src/Scolia.Bureau/Infrastructure/DesktopAppUpdates.cs"));
        Assert.Contains("function PrepareToInstall(var NeedsRestart: Boolean)", install, StringComparison.Ordinal);
        Assert.Contains("CurrentControlSet\\Services\\SCOLIA", install, StringComparison.Ordinal);
        Assert.Contains("Pos(Lowercase(ExpectedPath), Lowercase(OriginalPath)) = 0", install, StringComparison.Ordinal);
        Assert.Contains("Stop-Service -Name ''SCOLIA''", install, StringComparison.Ordinal);
        Assert.Contains("WaitForStatus(''Stopped''", install, StringComparison.Ordinal);
        Assert.Contains("procedure CurStepChanged(CurStep: TSetupStep)", install, StringComparison.Ordinal);
        Assert.Contains("'start SCOLIA'", install, StringComparison.Ordinal);
        Assert.Contains("uninsneveruninstall", install, StringComparison.Ordinal);
        Assert.Contains("l'assistant demandera les droits Windows", updater, StringComparison.Ordinal);
        Assert.DoesNotContain("retirez temporairement le service", updater, StringComparison.Ordinal);
    }

}
