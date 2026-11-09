using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace JPServices.Owner;

/// <summary>
/// Une vraie interface native Avalonia : aucune WebView. Sans délégation d'identité
/// Cloudflare Access auditable, les écritures restent dans le navigateur protégé.
/// </summary>
public sealed class OwnerShell : UserControl
{
    private const string ConsoleUrl =
        "https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console";
    private const string ReleasesApi =
        "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases?per_page=50";
    private const string Channel = "scolia-owner-v";
    private const string Version = "1.1.0";
    private static readonly HttpClient Client = NewClient();

    private enum Page { Home, Schools, Licenses, Servers, Activity, Backups, Account }
    private static readonly (Page Page, string Icon, string Name)[] Sections =
    [
        (Page.Home, "⌂", "Accueil"),
        (Page.Schools, "É", "Établissements"),
        (Page.Licenses, "L", "Licences"),
        (Page.Servers, "S", "Serveurs"),
        (Page.Activity, "≋", "Activité"),
        (Page.Backups, "↺", "Sauvegardes"),
        (Page.Account, "⚙", "Compte")
    ];

    private readonly Grid _layout = new()
    {
        RowDefinitions = new RowDefinitions("72,*,70"),
        ColumnDefinitions = new ColumnDefinitions("238,*")
    };
    private readonly Border _sidebar = new();
    private readonly Border _bottomNav = new();
    private readonly StackPanel _sidebarButtons = new() { Spacing = 5 };
    private readonly StackPanel _bottomButtons = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly TextBlock _heading = new()
    {
        FontSize = 19,
        FontWeight = FontWeight.Bold,
        Foreground = OwnerPalette.Ink,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _message = new()
    {
        Foreground = OwnerPalette.Muted,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };
    private Page _page = Page.Home;
    private bool _compact;
    private bool _narrow;

    public OwnerShell()
    {
        Background = OwnerPalette.Background;
        Content = _layout;

        _sidebar.Background = OwnerPalette.Navy;
        _sidebar.Child = BuildSidebar();
        Grid.SetRowSpan(_sidebar, 3);
        _layout.Children.Add(_sidebar);

        var top = BuildHeader();
        Grid.SetColumn(top, 1);
        _layout.Children.Add(top);

        _scroll.Content = new StackPanel();
        Grid.SetColumn(_scroll, 1);
        Grid.SetRow(_scroll, 1);
        _layout.Children.Add(_scroll);

        _bottomNav.Background = OwnerPalette.Paper;
        _bottomNav.BorderBrush = OwnerPalette.Border;
        _bottomNav.BorderThickness = new Thickness(0, 1, 0, 0);
        _bottomNav.Child = _bottomButtons;
        Grid.SetColumn(_bottomNav, 1);
        Grid.SetRow(_bottomNav, 2);
        _layout.Children.Add(_bottomNav);

        SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width < 1) return;
            bool compact = args.NewSize.Width < 780;
            bool narrow = args.NewSize.Width < 375;
            if (compact == _compact && narrow == _narrow) return;
            _compact = compact;
            _narrow = narrow;
            ApplyResponsive();
            ShowPage();
        };
        ApplyResponsive();
        ShowPage();
    }

    private static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SCOLIA-Admin-Avalonia/1.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static TextBlock Text(string text, double size = 14, bool bold = false,
        IBrush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = color ?? OwnerPalette.Ink,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border Box(Control child, IBrush? fill = null, int corner = 18,
        Thickness? padding = null) => new()
    {
        Background = fill ?? OwnerPalette.Paper,
        BorderBrush = OwnerPalette.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(corner),
        Padding = padding ?? new Thickness(17),
        Child = child
    };

    private Button Button(string name, Action action, bool primary = false, bool subtle = false)
    {
        var button = new Button
        {
            Content = name,
            Background = primary ? OwnerPalette.Blue :
                subtle ? OwnerPalette.Background : OwnerPalette.BlueWash,
            Foreground = primary ? OwnerPalette.Paper : OwnerPalette.NavyLight,
            BorderBrush = primary ? OwnerPalette.Blue : OwnerPalette.Border,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Padding = new Thickness(15, 11),
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 42
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Control BuildSidebar()
    {
        var stack = new StackPanel
        {
            Spacing = 21,
            Margin = new Thickness(15, 22, 15, 22)
        };
        var mark = new Border
        {
            Background = OwnerPalette.Blue,
            CornerRadius = new CornerRadius(13),
            Width = 46,
            Height = 46,
            Child = Text("S", 26, true, OwnerPalette.Paper)
        };
        var brand = new StackPanel { Spacing = 4 };
        brand.Children.Add(Text("JP-SERVICES", 16, true, OwnerPalette.Paper));
        brand.Children.Add(Text("SCOLIA · ADMINISTRATION", 10, false, OwnerPalette.BlueWash));

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 11
        };
        heading.Children.Add(mark);
        heading.Children.Add(brand);
        stack.Children.Add(heading);

        stack.Children.Add(Text("ESPACE PROPRIÉTAIRE", 10, true, OwnerPalette.BlueWash));
        stack.Children.Add(_sidebarButtons);

        stack.Children.Add(Box(new StackPanel
        {
            Spacing = 7,
            Children =
            {
                Text("ACCÈS PROTÉGÉ", 11, true, OwnerPalette.BlueWash),
                Text("Cloudflare Access + MFA", 11, false, OwnerPalette.Paper),
                Text("Aucune donnée scolaire conservée", 10, false, OwnerPalette.BlueWash)
            }
        }, OwnerPalette.NavyLight, 14, new Thickness(12)));
        return new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private Control BuildHeader()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = OwnerPalette.Paper,
            Margin = new Thickness(0)
        };
        var left = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(19, 7)
        };
        left.Children.Add(Text("SCOLIA PRO • JP-SERVICES", 10, true, OwnerPalette.Blue));
        left.Children.Add(_heading);
        row.Children.Add(left);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };
        actions.Children.Add(Button("↻", () => _ = CheckUpdatesAsync(true), subtle: true));
        actions.Children.Add(Button("Ouvrir l’accès", () => _ = OpenConsoleAsync(), primary: true));
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private void ApplyResponsive()
    {
        _layout.ColumnDefinitions[0].Width = new GridLength(_compact ? 0 : 238);
        _layout.RowDefinitions[2].Height = new GridLength(_compact ? 72 : 0);
        _sidebar.IsVisible = !_compact;
        _bottomNav.IsVisible = _compact;
        _sidebarButtons.Children.Clear();
        foreach (var item in Sections)
        {
            var button = Button(item.Icon + "   " + item.Name,
                () => Navigate(item.Page), subtle: item.Page != _page);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Foreground = OwnerPalette.Paper;
            button.Background = item.Page == _page ?
                OwnerPalette.NavyLight : OwnerPalette.Navy;
            button.BorderThickness = new Thickness(0);
            _sidebarButtons.Children.Add(button);
        }

        _bottomButtons.Children.Clear();
        foreach (var item in Sections.Where(x =>
            x.Page is Page.Home or Page.Schools or Page.Licenses or Page.Activity or Page.Account))
        {
            var button = Button(item.Icon + "\n" + item.Name, () => Navigate(item.Page),
                subtle: item.Page != _page);
            button.FontSize = 10;
            button.Padding = new Thickness(3, 7);
            button.MinHeight = 64;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Background = item.Page == _page ? OwnerPalette.BlueWash : OwnerPalette.Paper;
            button.BorderThickness = new Thickness(0);
            button.Width = _narrow ? 66 : 74;
            _bottomButtons.Children.Add(button);
        }
        _bottomButtons.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private void Navigate(Page page)
    {
        _page = page;
        ApplyResponsive();
        ShowPage();
        _scroll.Offset = default;
    }

    private void ShowPage()
    {
        var item = Sections.First(x => x.Page == _page);
        _heading.Text = item.Name;
        _scroll.Content = _page switch
        {
            Page.Home => HomePage(),
            Page.Schools => DetailsPage("Établissements",
                "Invitations et gestion des établissements sous votre contrôle.",
                "Écoles enregistrées", "Les établissements apparaîtront ici après la liaison sécurisée.",
                [("Créer une invitation", "Une invitation à usage limité est créée depuis la console."),
                ("Établissements autorisés", "Accès, statut et dernière activité des serveurs."),
                ("Suivi des comptes", "Chaque école conserve sa propre base et ses utilisateurs.")]),
            Page.Licenses => DetailsPage("Licences et abonnements",
                "Suivez les accès commerciaux sans modifier les données scolaires.",
                "Licences actives", "Les licences ne sont pas encore synchronisées dans cette version native.",
                [("Activer la facturation", "Uniquement après la décision explicite du propriétaire."),
                ("Définir un essai", "Durée configurable et statut détaillé."),
                ("Renouvellement", "Prochaine échéance et jours restants."),
                ("Suspension manuelle", "Accès en lecture conservé ; action confirmée dans la console.")]),
            Page.Servers => DetailsPage("Serveurs et connexions",
                "État des appareils serveurs enregistrés par les établissements.",
                "Serveurs enregistrés", "La liste nécessite une session native Cloudflare Access.",
                [("Disponibilité", "Dernier signal reçu et état réseau."),
                ("Relais Internet", "Fournisseurs approuvés par les établissements."),
                ("Sécurité réseau", "Contrôler les jumelages et la continuité.")]),
            Page.Activity => DetailsPage("Journal d’activité",
                "Historique des opérations commerciales autorisées.",
                "Événements récents", "Aucune donnée n'est inventée ou téléchargée sans autorisation.",
                [("Modifications de licences", "Consulter les opérations horodatées."),
                ("Connexions serveurs", "Observer les derniers événements techniques."),
                ("Actions d'administration", "Suivre les interventions dans le journal d'audit.")]),
            Page.Backups => DetailsPage("Sauvegardes Cloudflare",
                "Suivi des métadonnées de sauvegarde ; contenu chiffré par chaque école.",
                "Stockage", "Aucune clé R2 ou sauvegarde scolaire n'est enregistrée dans cette application.",
                [("État R2", "Service, bucket et disponibilité."),
                ("Sauvegardes chiffrées", "Dates, taille et statuts uniquement."),
                ("Opérations en attente", "Continuité en cas d'interruption du serveur.")]),
            _ => AccountPage()
        };
    }

    private StackPanel PageContainer()
    {
        return new StackPanel
        {
            Margin = new Thickness(_compact ? 13 : 26, 22,
                _compact ? 13 : 26, 33),
            Spacing = 17,
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Control WelcomePanel()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Text("CONSOLE PROPRIÉTAIRE", 11, true, OwnerPalette.BlueWash));
        body.Children.Add(Text("Pilotez SCOLIA Pro\navec précision.", _compact ? 25 : 32,
            true, OwnerPalette.Paper));
        body.Children.Add(Text("Espace réservé à JP-Services. Gestion des licences, des serveurs " +
            "et des sauvegardes — sans accès aux dossiers des apprenants.",
            13, false, OwnerPalette.BlueWash));
        var button = Button("Ouvrir la session sécurisée  ↗",
            () => _ = OpenConsoleAsync(), primary: true);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(button);
        return Box(body, OwnerPalette.Navy, 22,
            new Thickness(_compact ? 20 : 27));
    }

    private Control HomePage()
    {
        var stack = PageContainer();
        stack.Children.Add(WelcomePanel());
        stack.Children.Add(Text("Vue d’ensemble", 21, true));
        stack.Children.Add(Text("Indicateurs disponibles après connexion autorisée.",
            12, false, OwnerPalette.Muted));

        var metrics = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var stat in new[]
        {
            ("É", "Établissements", Page.Schools, OwnerPalette.BlueWash, OwnerPalette.Blue),
            ("L", "Licences", Page.Licenses, OwnerPalette.GreenWash, OwnerPalette.Teal),
            ("S", "Serveurs", Page.Servers, OwnerPalette.AmberWash, OwnerPalette.Amber),
            ("≋", "Activité", Page.Activity, OwnerPalette.BlueWash, OwnerPalette.Blue)
        })
            metrics.Children.Add(FeatureTile(stat.Item1, stat.Item2, stat.Item3,
                stat.Item4, stat.Item5));
        stack.Children.Add(metrics);

        stack.Children.Add(Text("Outils d’administration", 20, true));
        var tools = new StackPanel { Spacing = 9 };
        tools.Children.Add(CompactAction("Gérer les établissements",
            "Créer des invitations et consulter la liste", Page.Schools));
        tools.Children.Add(CompactAction("Licences et renouvellements",
            "Facturation, essais et suspension sous contrôle", Page.Licenses));
        tools.Children.Add(CompactAction("Sauvegardes Cloudflare",
            "Contrôler les métadonnées et la disponibilité R2", Page.Backups));
        stack.Children.Add(tools);
        stack.Children.Add(SafetyNotice());
        return stack;
    }

    private Control FeatureTile(string icon, string label, Page page,
        IBrush wash, IBrush foreground)
    {
        var pane = new StackPanel { Spacing = 12 };
        pane.Children.Add(Box(Text(icon, 19, true, foreground),
            wash, 12, new Thickness(12)));
        pane.Children.Add(Text(label, 15, true));
        pane.Children.Add(Text("Non connecté  →", 11, false, OwnerPalette.Muted));
        var button = new Button
        {
            Width = _compact ? (_narrow ? 290 : 151) : 205,
            Height = 160,
            Margin = new Thickness(0, 0, 10, 10),
            Background = OwnerPalette.Paper,
            BorderBrush = OwnerPalette.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Content = pane,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => Navigate(page);
        return button;
    }

    private Control CompactAction(string title, string detail, Page next)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 4 };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text(detail, 11, false, OwnerPalette.Muted));
        row.Children.Add(info);
        var arrow = Button("›", () => Navigate(next), subtle: true);
        Grid.SetColumn(arrow, 1);
        row.Children.Add(arrow);
        return Box(row, corner: 15, padding: new Thickness(15));
    }

    private Control DetailsPage(string title, string subtitle, string metric,
        string emptyText, IReadOnlyList<(string Name, string Description)> entries)
    {
        var stack = PageContainer();
        stack.Children.Add(Text(title, _compact ? 24 : 29, true));
        stack.Children.Add(Text(subtitle, 13, false, OwnerPalette.Muted));
        var heading = new StackPanel { Spacing = 12 };
        heading.Children.Add(Text(metric, 12, true, OwnerPalette.Muted));
        heading.Children.Add(Text("—", 37, true));
        heading.Children.Add(Text(emptyText, 12, false, OwnerPalette.Muted));
        stack.Children.Add(Box(heading));
        stack.Children.Add(Text("Fonctionnalités", 20, true));
        foreach (var item in entries)
        {
            var row = new StackPanel { Spacing = 5 };
            row.Children.Add(Text(item.Name, 14, true));
            row.Children.Add(Text(item.Description, 12, false, OwnerPalette.Muted));
            stack.Children.Add(Box(row, corner: 15));
        }
        var go = Button("Ouvrir cette fonction dans la console sécurisée  ↗",
            () => _ = OpenConsoleAsync(), primary: true);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(go);
        stack.Children.Add(SafetyNotice());
        return stack;
    }

    private Control AccountPage()
    {
        var stack = PageContainer();
        stack.Children.Add(Text("Compte et sécurité", 26, true));
        stack.Children.Add(Text("Version native Avalonia • JP-Services",
            13, false, OwnerPalette.Muted));
        var security = new StackPanel { Spacing = 12 };
        security.Children.Add(Text("Cloudflare Access + MFA", 16, true));
        security.Children.Add(Text("Authentification forte requise. Les jetons et cookies " +
            "du navigateur ne sont pas copiés vers l’application native.",
            12, false, OwnerPalette.Muted));
        security.Children.Add(Button("Se connecter avec le navigateur sécurisé",
            () => _ = OpenConsoleAsync(), primary: true));
        stack.Children.Add(Box(security));
        var updates = new StackPanel { Spacing = 10 };
        updates.Children.Add(Text("Mises à jour JP-Services", 16, true));
        updates.Children.Add(Text("Canal indépendant de SCOLIA Client • version " + Version,
            12, false, OwnerPalette.Muted));
        updates.Children.Add(Button("Rechercher les mises à jour",
            () => _ = CheckUpdatesAsync(true)));
        updates.Children.Add(_message);
        stack.Children.Add(Box(updates));
        stack.Children.Add(SafetyNotice());
        return stack;
    }

    private Control SafetyNotice()
    {
        var text = new StackPanel { Spacing = 7 };
        text.Children.Add(Text("Sécurité et confidentialité", 14, true, OwnerPalette.NavyLight));
        text.Children.Add(Text(
            "Cette version utilise une interface Avalonia entièrement native. " +
            "L’API propriétaire reste protégée par Cloudflare Access et la MFA. " +
            "En attendant une authentification native validée, toute opération sensible " +
            "s’effectue dans le navigateur sécurisé.",
            12, false, OwnerPalette.Muted));
        return Box(text, OwnerPalette.BlueWash, 15);
    }

    private async Task OpenConsoleAsync()
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(new Uri(ConsoleUrl)))
                _message.Text = "Impossible d’ouvrir le navigateur sur cet appareil.";
        }
        catch (Exception)
        {
            _message.Text = "Ouverture du navigateur indisponible.";
        }
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        try
        {
            using var response = await Client.GetAsync(ReleasesApi);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            System.Version? best = null;
            string? link = null;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.GetProperty("draft").GetBoolean() ||
                    entry.GetProperty("prerelease").GetBoolean()) continue;
                var tag = entry.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith(Channel, StringComparison.Ordinal)) continue;
                if (!System.Version.TryParse(tag[Channel.Length..], out var candidate))
                    continue;
                if (best is null || candidate > best)
                {
                    best = candidate;
                    link = entry.GetProperty("html_url").GetString();
                }
            }
            string message = best is null ?
                "Aucune version officielle JP-Services Admin n’est encore publiée." :
                best > new System.Version(Version) ?
                    "Mise à jour administrative " + best + " disponible." :
                    "La version administrateur est à jour sur le canal officiel.";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _message.Text = message;
                if (best > new System.Version(Version) && link is not null && interactive)
                    _ = OpenReleaseAsync(link);
                else if (interactive && _page != Page.Account)
                    Navigate(Page.Account);
            });
        }
        catch (Exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _message.Text = "Recherche indisponible : vérifier la connexion Internet.";
                if (interactive && _page != Page.Account) Navigate(Page.Account);
            });
        }
    }

    private async Task OpenReleaseAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
            return;
        try { await (TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri) ??
            Task.FromResult(false)); }
        catch (Exception) { _message.Text = "Lien de mise à jour indisponible."; }
    }
}
