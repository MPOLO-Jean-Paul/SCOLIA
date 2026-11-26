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
        Assert.Contains("#define AppContactEmail \"contact@jp-services.org\"", iss);
        Assert.Contains("#define AppSupportLink \"mailto:contact@jp-services.org\"", iss);
        Assert.Contains("#define AppWebsite \"https://www.jp-services.org\"", iss);
        Assert.Contains("AppPublisherURL={#AppWebsite}", iss);
        Assert.Contains("ShellExecAsOriginalUser('open', '{#AppWebsite}'", iss);
        Assert.DoesNotContain("gmail.com", iss);
        // Les textes affichés par l'assistant (licence, avant et après installation) portent la même adresse.
        foreach (var text in new[] { "CONDITIONS_UTILISATION.txt", "AVANT_INSTALLATION.txt", "APRES_INSTALLATION.txt" })
        {
            var content = File.ReadAllText(Source("scripts/windows/" + text));
            Assert.DoesNotContain("gmail.com", content);
            Assert.Contains("contact@jp-services.org", content);
            Assert.Contains("www.jp-services.org", content);
        }
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
    public void Accueil_desktop_limite_la_largeur_du_message_de_bienvenue()
    {
        var nodes = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml")).Descendants().ToArray();
        var greeting = Assert.Single(nodes, e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "WelcomeGreeting"));
        var avatar = Assert.Single(nodes, e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "WelcomeAvatar"));
        var grid = greeting.Parent!.Parent!;
        Assert.Equal("Grid", grid.Name.LocalName);
        Assert.Equal("*,Auto", (string?)grid.Attribute("ColumnDefinitions"));
        Assert.Equal("320", (string?)grid.Attribute("MaxWidth"));
        Assert.Equal("CharacterEllipsis", (string?)greeting.Attribute("TextTrimming"));
        Assert.Same(grid, avatar.Parent);
        Assert.Equal("1", (string?)avatar.Attribute("Grid.Column"));
    }

    [Fact]
    public void Accueil_Windows_Settings_conserve_les_actions_dans_deux_panneaux_adaptatifs()
    {
        var home = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var panels = Assert.Single(home.Descendants(), e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "HomeSettingsPanels"));
        Assert.Equal("*,*", (string?)panels.Attribute("ColumnDefinitions"));
        Assert.Contains(panels.Attributes(), a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "820");
        Assert.Equal(2, panels.Elements().Count(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Classes") == "pbiPanel"));
        foreach (var binding in new[] { "QuickActions", "MoreTools" })
        {
            var items = Assert.Single(panels.Descendants(), e => e.Name.LocalName == "ItemsControl" &&
                (string?)e.Attribute("ItemsSource") == "{Binding " + binding + "}");
            Assert.Contains(items.Descendants(), e => e.Name.LocalName == "Button" && (string?)e.Attribute("Command") == "{Binding Open}");
            Assert.Contains(items.Descendants(), e => e.Name.LocalName == "PathIcon" &&
                e.Attributes().Any(a => a.Value.Contains("HomeGlyphs.Converter")));
        }
        var shell = XDocument.Load(Source("src/Scolia.Bureau/Views/MainView.axaml"));
        Assert.Contains(shell.Descendants(), e => e.Attributes().Any(a => a.Name.LocalName == "Classes.homepane" && a.Value == "{Binding IsHomePage}"));
    }

    [Fact]
    public void Accueil_desktop_PowerBI_adopte_des_panneaux_Fluent_responsives()
    {
        var doc = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var elements = doc.Descendants().ToArray();

        Assert.True(elements.Count(e => e.Name.LocalName == "Border" &&
            (string?)e.Attribute("Classes") == "pbiPanel") >= 4);
        Assert.Contains(elements, e => e.Name.LocalName == "Grid" &&
            (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value == "WelcomeLayout" &&
            (string?)e.Attribute("ColumnDefinitions") == "*,Auto" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "610"));
        Assert.True(elements.Count(e => e.Name.LocalName == "Grid" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "820")) >= 2);
        Assert.Contains(elements, e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding QuickActions}");
        Assert.Contains(elements, e => e.Name.LocalName == "AdaptiveTiles" &&
            e.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal) && a.Value == "190"));
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Command") == "{Binding Network}");
        Assert.Contains(elements, e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Command") == "{Binding ShowAllTools}");
        foreach (var value in new[] { "Greeting", "AccountContextLine", "ShortTitle", "Value", "Hint" })
            Assert.Contains(elements, e => e.Name.LocalName == "TextBlock" &&
                e.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains(value)));
        var logic = File.ReadAllText(Source("src/Scolia.Bureau/Views/HomeView.axaml.cs"));
        Assert.Contains("InitializeComponent()", logic, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeQuickActions.Width =", logic, StringComparison.Ordinal);
    }

    [Fact]
    public void Accueil_PowerBI_reutilise_les_fonctions_de_la_vue_mobile_et_les_indicateurs_reels()
    {
        var desktop = XDocument.Load(Source("src/Scolia.Bureau/Views/HomeView.axaml"));
        var phone = XDocument.Load(Source("src/Scolia.Bureau/Views/Phone/HomeView.axaml"));
        var desktopNodes = desktop.Descendants().ToArray();
        var phoneNodes = phone.Descendants().ToArray();

        Assert.Contains(desktopNodes, e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding MoreTools}" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "FeatureCards"));
        Assert.Contains(phoneNodes, e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding MoreTools}" &&
            e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "FeatureCards"));
        Assert.Contains(phoneNodes, e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding QuickActions}");
        Assert.Contains(phoneNodes, e => e.Name.LocalName == "MaterialIcon" &&
            (string?)e.Attribute("Kind") == "{Binding Icon}");
        Assert.Contains(phoneNodes, e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") == "{Binding AllToolGroups}");
        foreach (var nodes in new[] { desktopNodes, phoneNodes })
        {
            Assert.Contains(nodes, e => (string?)e.Attribute("IsVisible") == "{Binding OverviewUnavailable}");
            Assert.Contains(nodes, e => (string?)e.Attribute("IsVisible") == "{Binding OverviewAvailable}");
        }
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "mi:MaterialIcon" ||
            (e.Name.LocalName == "MaterialIcon" && (string?)e.Attribute("Kind") == "{Binding Icon}"));
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "TextBlock" &&
            (string?)e.Attribute("Text") == "Recommandé pour vous");

        // La connexion est toujours cliquable, même sans Internet, et les indicateurs
        // ouvrent les vraies pages de travail. Aucun chiffre de démonstration n'est codé.
        Assert.Contains(desktopNodes, e => e.Name.LocalName == "Button" &&
            (string?)e.Attribute("Command") == "{Binding Network}" &&
            e.Descendants().Any(d => d.Name.LocalName == "TextBlock" &&
                (string?)d.Attribute("Text") == "{Binding Sync.StatusHint}"));
        foreach (var source in new[] { "Kpis", "Receipts", "Tasks" })
            Assert.Contains(desktopNodes, e => e.Name.LocalName == "ItemsControl" &&
                (string?)e.Attribute("ItemsSource") == "{Binding " + source + "}");

        var home = File.ReadAllText(Source("src/Scolia.Bureau/ViewModels/Home.cs"));
        Assert.Contains("public IReadOnlyList<ToolCardVm> PhoneTools => Tools.Take(4).ToList()", home);
        Assert.Contains("public IReadOnlyList<ToolCardVm> MoreTools => Tools.Skip(2).Take(4).ToList()", home);
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
