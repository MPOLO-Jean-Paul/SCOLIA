using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Scénarios de non-régression : bureau étroit, catalogue, recherche et encaissement par lot.</summary>
public sealed class HomeAndBatchUsabilityTests
{
    private static string ViewFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."),
            "src", "Scolia.Bureau", relative);
    }

    [Theory]
    [InlineData(1280, 820, 72)]
    [InlineData(760, 520, 110)]
    [InlineData(608, 416, 110)]
    [InlineData(507, 347, 290)]
    [InlineData(320, 200, 190)]
    [InlineData(10, 5, 50)]
    public void Le_menu_desktop_reste_dans_la_fenetre(double width, double height, double anchorBottom)
    {
        var rect = Scolia.Bureau.Views.MainView.CommandMenuBounds(new Avalonia.Size(width, height), anchorBottom);
        Assert.InRange(rect.X, 0, width);
        Assert.InRange(rect.Y, 0, height);
        Assert.InRange(rect.Width, 0, Math.Min(420, width));
        Assert.InRange(rect.Height, 0, height);
        Assert.True(rect.Right <= width);
        Assert.True(rect.Bottom <= height);
        var source = File.ReadAllText(ViewFile("Views/MainView.axaml.cs"));
        Assert.Contains("DesktopArea.Children.Add(overlay)", source);
        Assert.Contains("DesktopArea.SizeChanged += (_, _) => PlaceCommandMenu()", source);
        Assert.Contains("VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto", source);
        Assert.DoesNotContain("var menu = new MenuFlyout", source);
    }

    [Fact]
    public void L_accueil_PowerBI_garde_indicateurs_et_actions_adaptatifs()
    {
        var doc = XDocument.Load(ViewFile("Views/HomeView.axaml"));
        var nodes = doc.Descendants().ToArray();
        Assert.Contains(nodes, x => x.Name.LocalName == "AdaptiveTiles"
            && x.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal) && a.Value == "190"));
        Assert.Contains(nodes, x => x.Name.LocalName == "ItemsControl"
            && (string?)x.Attribute("ItemsSource") == "{Binding QuickActions}");
        Assert.Contains(nodes, x => x.Name.LocalName == "ItemsControl"
            && (string?)x.Attribute("ItemsSource") == "{Binding MoreTools}");
        Assert.Contains(nodes, x => x.Name.LocalName == "ItemsControl"
            && (string?)x.Attribute("ItemsSource") == "{Binding AllToolGroups}");
        Assert.Contains(nodes, x => x.Name.LocalName == "Grid"
            && x.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal) && a.Value == "700"));
        Assert.Contains(nodes, x => x.Name.LocalName == "Button"
            && (string?)x.Attribute("Command") == "{Binding Network}");
        Assert.Contains(nodes, x => x.Name.LocalName == "TextBlock"
            && (string?)x.Attribute("Text") == "Indicateurs de l'établissement");
    }

    [Fact]
    public void Accueil_et_demarrer_utilisent_des_gabarits_identiques()
    {
        var home = XDocument.Load(ViewFile("Views/HomeView.axaml")).Descendants().ToArray();
        var lists = home.Where(e => e.Name.LocalName == "ItemsControl" &&
            (string?)e.Attribute("ItemsSource") is "{Binding QuickActions}" or "{Binding MoreTools}").ToArray();
        Assert.Equal(2, lists.Length);
        foreach (var list in lists)
        {
            var panel = Assert.Single(list.Descendants(), e => e.Name.LocalName == "ItemsPanelTemplate");
            Assert.Equal("StackPanel", Assert.Single(panel.Elements()).Name.LocalName);
            Assert.Contains(list.Descendants(), e => e.Name.LocalName == "Button" && (string?)e.Attribute("Classes") == "pbiAction");
        }
        var main = XDocument.Load(ViewFile("Views/MainView.axaml")).Descendants().ToArray();
        var style = Assert.Single(main, e => e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == "Button.startentry");
        Assert.Contains(style.Elements(), e => (string?)e.Attribute("Property") == "MinHeight" && (string?)e.Attribute("Value") == "54");
        Assert.DoesNotContain(style.Elements(), e => (string?)e.Attribute("Property") == "Height");
    }

    [Fact]
    public void Une_recherche_differee_ne_peut_pas_arreter_un_minuteur_plus_recent()
    {
        var students = File.ReadAllText(ViewFile("ViewModels/Students.cs"));
        var cashier = File.ReadAllText(ViewFile("ViewModels/Cashier.cs"));
        Assert.Contains("if (!ReferenceEquals(debounce, scheduled)) return;", students);
        foreach (var field in new[] { "searchTimer", "previewTimer", "timer" })
            Assert.Contains($"if (!ReferenceEquals({field}, scheduled)) return;", cashier);
    }

    [Fact]
    public void Les_parametres_mobile_gardent_les_selections_et_les_curseurs_adaptatifs()
    {
        var nodes = XDocument.Load(ViewFile("Views/SettingsView.axaml")).Descendants().ToArray();
        foreach (var (source, selection) in new[] { ("Years", "SelectedYear"), ("Categories", "SelectedCategory") })
        {
            Assert.Contains(nodes, x => x.Name.LocalName == "ListBox" &&
                (string?)x.Attribute("ItemsSource") == "{Binding " + source + "}" &&
                (string?)x.Attribute("SelectedItem") == "{Binding " + selection + "}" &&
                (string?)x.Attribute("IsVisible") == "{Binding Phone}");
            Assert.Contains(nodes, x => x.Name.LocalName == "DataGrid" &&
                (string?)x.Attribute("ItemsSource") == "{Binding " + source + "}" &&
                (string?)x.Attribute("SelectedItem") == "{Binding " + selection + "}" &&
                (string?)x.Attribute("IsVisible") == "{Binding NotPhone}");
        }
        Assert.All(nodes.Where(x => x.Name.LocalName == "Slider"), x => Assert.Null(x.Attribute("Width")));
    }

    [Fact]
    public void Les_listes_metier_mobile_evitent_les_tableaux_larges()
    {
        foreach (var (view, command) in new[]
        {
            ("Views/StudentsView.axaml", "OpenStudentOnPhone"),
            ("Views/ReceiptsView.axaml", "OpenReceiptOnPhone")
        })
        {
            var nodes = XDocument.Load(ViewFile(view)).Descendants().ToArray();
            Assert.Contains(nodes, x => x.Name.LocalName == "Expander" && (string?)x.Attribute("IsVisible") == "{Binding Phone}");
            Assert.Contains(nodes, x => x.Name.LocalName == "DataGrid" && (string?)x.Attribute("IsVisible") == "{Binding NotPhone}");
            Assert.Contains(nodes, x => x.Name.LocalName == "Button" && ((string?)x.Attribute("Command"))?.Contains(command) == true);
        }
        var cash = XDocument.Load(ViewFile("Views/CashierView.axaml")).Descendants();
        Assert.Contains(cash, x => x.Name.LocalName == "ItemsControl" && (string?)x.Attribute("ItemsSource") == "{Binding Allocations}" &&
            (string?)x.Attribute("IsVisible") == "{Binding Phone}");
    }

    [Fact]
    public void Les_rapports_adaptent_filtres_et_formats_a_la_largeur()
    {
        foreach (var view in new[] { "Views/ReportsView.axaml", "Views/Phone/ReportsView.axaml" })
        {
            var nodes = XDocument.Load(ViewFile(view)).Descendants().ToArray();
            Assert.Contains(nodes, x => x.Name.LocalName == "Grid" &&
                x.Attributes().Any(a => a.Name.LocalName.EndsWith("StackBelow", StringComparison.Ordinal)));
            Assert.Contains(nodes, x => x.Name.LocalName == "Button" &&
                (string?)x.Attribute("Command") == "{Binding Run}");
        }
        var phone = XDocument.Load(ViewFile("Views/Phone/ReportsView.axaml"));
        Assert.All(phone.Descendants().Where(x => x.Name.LocalName is "CalendarDatePicker" or "ComboBox"),
            x => Assert.Null(x.Attribute("Width")));
        Assert.Contains(phone.Descendants(), x => x.Name.LocalName == "UniformGrid" &&
            x.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal)));
    }

    [Fact]
    public void Afficher_tous_les_outils_est_une_vraie_action_interactive()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        var home = new HomePage(shell);
        Assert.False(home.AllToolsVisible);
        home.ShowAllTools.Execute(null);
        Assert.True(home.AllToolsVisible);
        Assert.Equal("Réduire les outils", home.AllToolsLabel);
        home.ShowAllTools.Execute(null);
        Assert.False(home.AllToolsVisible);
    }

    [Fact]
    public void Recherche_rapide_desktop_adapte_les_tuiles_a_sa_largeur()
    {
        var doc = XDocument.Load(ViewFile("Views/SearchResultsView.axaml"));
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "UniformGrid"
            && x.Attributes().Any(a => a.Name.LocalName.EndsWith("MinItemWidth", StringComparison.Ordinal) && a.Value == "230"));
        Assert.Contains(doc.Descendants(), x => x.Name.LocalName == "Button"
            && x.Attributes().Any(a => a.Name.LocalName == "Classes" && a.Value == "searchtile"));
    }

    [Fact]
    public void Un_lot_de_plusieurs_promotions_survit_aux_filtres_et_compte_les_lignes_masquees()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var shell = new ShellViewModel(api, new Preferences());
        var page = new BatchPaymentPage(shell);
        BatchRowVm Candidate(string promotion, string matricule)
        {
            var row = new BatchRowVm(new BatchCandidateDto(Guid.NewGuid(), matricule,
                "Élève de " + promotion, promotion, 100, 100,
                [new BatchMonthDto(Guid.NewGuid(), "Novembre", new DateOnly(2026, 11, 5), 100, true)]),
                "100", "100", page.SelectionChanged);
            row.ApplyMonths(row.Candidate.Months, amount => amount.ToString("0"));
            return row;
        }
        var inf = Candidate("INF1", "INF001");
        var acc = Candidate("ACC2", "ACC002");
        page.Items.Add(inf);
        inf.Selected = true;
        page.Items.Clear(); // La recherche a remplacé uniquement les lignes à l'écran.
        page.Items.Add(acc);
        acc.Selected = true;
        Assert.Equal(2, page.SelectedCount);
        Assert.Contains("2 apprenants cochés", page.SelectionSummary);
        Assert.Contains("200", page.SelectionSummary);
        Assert.True(inf.Selected);
        Assert.True(acc.Selected);
    }

    [Fact]
    public void Les_modes_du_lot_ciblent_le_premier_le_deuxieme_et_tous_les_retards()
    {
        BatchMonthDto Month(string name, int day, bool late) =>
            new(Guid.NewGuid(), name, new DateOnly(2026, 11, day), 5000m, late);

        var oldest = Month("Septembre", 2, true);
        var second = Month("Octobre", 6, true);
        var third = Month("Novembre", 10, true);
        var upcoming = Month("Décembre", 18, false);
        // L'API peut fournir les échéances dans un ordre différent de leur date.
        var candidate = new BatchCandidateDto(Guid.NewGuid(), "INF293", "KASONGO KABEYA",
            "INF3", 20000m, 15000m, [upcoming, third, oldest, second]);

        static Guid[] Ids(BatchCandidateDto item, string mode) =>
            BatchPaymentPage.MonthsForMode(item, mode).Select(m => m.FeeItemId).ToArray();

        Assert.Equal(new[] { oldest.FeeItemId }, Ids(candidate, "first_late"));
        Assert.Equal(new[] { second.FeeItemId }, Ids(candidate, "next"));
        Assert.Equal(new[] { oldest.FeeItemId, second.FeeItemId, third.FeeItemId }, Ids(candidate, "late"));
        Assert.Equal(new[] { oldest.FeeItemId, second.FeeItemId, third.FeeItemId, upcoming.FeeItemId },
            Ids(candidate, "all"));
        Assert.Empty(Ids(candidate with { Months = [oldest, upcoming] }, "next"));
        Assert.Empty(Ids(candidate with { Months = [upcoming] }, "first_late"));
    }

    [Fact]
    public void Les_choix_de_mois_expliquent_clairement_leur_portee()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var page = new BatchPaymentPage(new ShellViewModel(api, new Preferences()));
        Assert.Equal(new[] { "first_late", "next", "late", "all" },
            page.Modes.Select(m => m.Value));
        Assert.Contains("Premier mois en retard", page.Modes[0].Label);
        Assert.Contains("2e retard", page.Modes[1].Label);
        Assert.Contains("Tous les mois en retard", page.Modes[2].Label);
        Assert.Contains("premier mois en retard", page.MonthModeHint);
    }

    [Fact]
    public void Apercu_et_paiement_du_meme_lot_partagent_une_reference_inchangee()
    {
        using var api = new ScoliaClient(new Uri("http://localhost:5080/"));
        var page = new BatchPaymentPage(new ShellViewModel(api, new Preferences()));
        var eid = Guid.NewGuid();
        var row = new BatchRowVm(new BatchCandidateDto(eid, "INF293", "SHIMBA MAKITA ELIE",
            "INF3", 100, 100, [new BatchMonthDto(Guid.NewGuid(), "Novembre",
                new DateOnly(2026, 11, 5), 100, true)]), "100", "100", _ => { });
        row.ApplyMonths(row.Candidate.Months, x => x.ToString("0"));
        var method = typeof(BatchPaymentPage).GetMethod("BuildRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var first = method!.Invoke(page, [new List<BatchRowVm> { row }, null]);
        var second = method.Invoke(page, [new List<BatchRowVm> { row }, Guid.NewGuid().ToString("N")]);
        using var preview = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(first));
        using var payment = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(second));
        string Reference(System.Text.Json.JsonDocument doc) =>
            doc.RootElement.GetProperty("items")[0].GetProperty("reference").GetString()!;
        Assert.StartsWith("TR", Reference(preview));
        Assert.Equal(Reference(preview), Reference(payment));
    }

    [Fact]
    public void Une_erreur_de_paiement_ne_recoche_pas_un_dossier_retiré()
    {
        var eid = Guid.NewGuid();
        var line = new BatchRowVm(
            new BatchCandidateDto(eid, "INF293", "SHIMBA MAKITA ELIE", "INF3", 100, 100,
                [new BatchMonthDto(Guid.NewGuid(), "Novembre", new DateOnly(2026, 11, 5), 100, true)]),
            "100", "100", _ => { });
        line.ApplyMonths(line.Candidate.Months, x => x.ToString("0"));
        line.Selected = false;
        line.ShowResult(new BatchPaymentLineDto(eid, "INF293", "SHIMBA MAKITA ELIE",
            false, null, null, 0, "Le mois est déjà réglé"));
        Assert.False(line.Selected);
        line.Selected = true;
        line.ShowResult(new BatchPaymentLineDto(eid, "INF293", "SHIMBA MAKITA ELIE",
            true, Guid.NewGuid(), "REC-2026-01", 100, null));
        Assert.False(line.Selected);
        Assert.Contains("REC-2026-01", line.Result);
    }
}
