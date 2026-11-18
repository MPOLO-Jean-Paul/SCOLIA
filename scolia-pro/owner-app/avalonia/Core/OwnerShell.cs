using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace JPServices.Owner;

/// <summary>
/// Console native Avalonia (aucune WebView). Chaque appel porte le jeton de service Cloudflare Access
/// et le jeton propriétaire, chiffrés sur l'appareil : mêmes protections que la console web.
/// </summary>
public sealed class OwnerShell : UserControl
{
    private const string ConsoleUrl =
        "https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console";
    private const string ReleasesApi =
        "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases?per_page=50";
    private const string Channel = "scolia-owner-v";
    private static readonly HttpClient Client = NewClient();

    private enum Page { Home, Schools, Licenses, Servers, Relays, Activity, Backups, Account, SchoolDetail }
    private static readonly (Page Page, string Icon, string Name)[] Sections =
    [
        (Page.Home, "⌂", "Accueil"),
        (Page.Schools, "É", "Établissements"),
        (Page.Licenses, "L", "Licences"),
        (Page.Servers, "S", "Serveurs"),
        (Page.Relays, "◉", "Relais"),
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
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1
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

    private readonly OwnerVault? _vault = OwnerVault.Current;
    private OwnerCredentials? _credentials;
    private OwnerApi? _api;
    private JsonElement? _summary, _schools, _servers, _relays, _audit, _school;
    private string? _selectedSchool;
    private (string Code, string School, long? Expires)? _invitation;
    private bool _loading;
    private string? _error, _notice;
    private DateTimeOffset? _loadedAt;

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
        _credentials = _vault?.Load();
        ApplyResponsive();
        ShowPage();
        _ = LoadAsync(Page.Home);
    }

    private static HttpClient NewClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SCOLIA-Console/" + OwnerInfo.Version);
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

    private static Control BrandMark()
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri("avares://Scolia.Owner.Core/Assets/scolia.png"));
            return new Image
            {
                Source = new Bitmap(stream),
                Stretch = Stretch.Uniform,
                Width = 42,
                Height = 42
            };
        }
        catch (Exception)
        {
            return Text("S", 26, true, OwnerPalette.Paper);
        }
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
            Child = BrandMark()
        };
        var brand = new StackPanel { Spacing = 4 };
        brand.Children.Add(Text("SCOLIA CONSOLE", 16, true, OwnerPalette.Paper));
        brand.Children.Add(Text("JP-SERVICES · ADMINISTRATION", 10, false, OwnerPalette.BlueWash));

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
                Text("Cloudflare Access + jeton propriétaire", 11, false, OwnerPalette.Paper),
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
        left.Children.Add(Text("SCOLIA CONSOLE • JP-SERVICES", 10, true, OwnerPalette.Blue));
        left.Children.Add(_heading);
        row.Children.Add(left);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };
        actions.Children.Add(Button("↻", () => _ = LoadAsync(_page, force: true), subtle: true));
        actions.Children.Add(Button("Console web  ↗", () => _ = OpenConsoleAsync(), subtle: true));
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private Page CurrentSection => _page == Page.SchoolDetail ? Page.Schools : _page;

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
                () => Navigate(item.Page), subtle: item.Page != CurrentSection);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Foreground = OwnerPalette.Paper;
            button.Background = item.Page == CurrentSection ?
                OwnerPalette.NavyLight : OwnerPalette.Navy;
            button.BorderThickness = new Thickness(0);
            _sidebarButtons.Children.Add(button);
        }

        _bottomButtons.Children.Clear();
        foreach (var item in Sections.Where(x =>
            x.Page is Page.Home or Page.Schools or Page.Licenses or Page.Activity or Page.Account))
        {
            var button = Button(item.Icon + "\n" + item.Name, () => Navigate(item.Page),
                subtle: item.Page != CurrentSection);
            button.FontSize = 10;
            button.Padding = new Thickness(3, 7);
            button.MinHeight = 64;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Background = item.Page == CurrentSection ? OwnerPalette.BlueWash : OwnerPalette.Paper;
            button.BorderThickness = new Thickness(0);
            button.Width = _narrow ? 66 : 74;
            _bottomButtons.Children.Add(button);
        }
        _bottomButtons.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private void Navigate(Page page)
    {
        _page = page;
        _error = null;
        _notice = null;
        if (page != Page.Schools && page != Page.SchoolDetail) _invitation = null;
        ApplyResponsive();
        _scroll.Offset = default;
        _ = LoadAsync(page);
    }

    // ───────────────────────── Données ─────────────────────────

    /// <summary>Fabrique du client ; remplacée seulement par les tests (serveur simulé).</summary>
    internal static Func<OwnerCredentials, OwnerApi> ApiFactory { get; set; } = credentials => new OwnerApi(credentials);

    private OwnerApi? Api()
    {
        if (_credentials is not { IsComplete: true } credentials) return null;
        return _api ??= ApiFactory(credentials);
    }

    /// <summary>Charge les données d'une page, puis l'affiche. Une erreur reste visible en tête de page.</summary>
    private async Task LoadAsync(Page page, bool force = false)
    {
        if (Api() is not { } api || page == Page.Account) { ShowPage(); return; }
        if (_loading) return;
        _loading = true;
        _error = null;
        ShowPage();
        try
        {
            switch (page)
            {
                case Page.Home or Page.Backups:
                    if (force || _summary is null) _summary = await api.SummaryAsync();
                    if (page == Page.Backups && (force || _schools is null)) _schools = await api.SchoolsAsync();
                    break;
                case Page.Schools or Page.Licenses:
                    if (force || _schools is null) _schools = await api.SchoolsAsync();
                    break;
                case Page.SchoolDetail when _selectedSchool is not null:
                    _school = await api.SchoolAsync(_selectedSchool);
                    break;
                case Page.Servers:
                    if (force || _servers is null) _servers = await api.ServersAsync();
                    break;
                case Page.Relays:
                    if (force || _relays is null) _relays = await api.RelaysAsync();
                    break;
                case Page.Activity:
                    if (force || _audit is null) _audit = await api.AuditAsync();
                    break;
            }
            _loadedAt = DateTimeOffset.Now;
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Lecture impossible. Réessayez."; }
        finally
        {
            _loading = false;
            await Dispatcher.UIThread.InvokeAsync(ShowPage);
        }
    }

    /// <summary>Action d'écriture : exécutée, puis données rafraîchies et message affiché.</summary>
    private async Task RunAsync(Func<OwnerApi, Task<JsonElement>> action, Page refresh, string? success = null)
    {
        if (Api() is not { } api || _loading) return;
        _error = null;
        try
        {
            var reply = await action(api);
            var notice = OwnerText.Str(reply, "notice");
            _notice = success ?? (notice.Length > 0 ? notice : "Opération enregistrée.");
            _summary = null; _schools = null; _audit = null; _relays = null;
        }
        catch (OwnerApiException ex) { _error = ex.Message; _notice = null; }
        catch (Exception) { _error = "Opération impossible. Réessayez."; _notice = null; }
        await LoadAsync(refresh, force: true);
    }

    private void ResetData()
    {
        _api?.Dispose();
        _api = null;
        _summary = _schools = _servers = _relays = _audit = _school = null;
        _selectedSchool = null;
    }

    // ───────────────────────── Pages ─────────────────────────

    private void ShowPage()
    {
        _heading.Text = _page == Page.SchoolDetail
            ? (_school is { } sc ? OwnerText.Str(OwnerText.Child(sc, "school"), "name") : "Établissement")
            : Sections.First(x => x.Page == _page).Name;
        _scroll.Content = _page switch
        {
            Page.Home => HomePage(),
            Page.Account => AccountPage(),
            _ when _credentials is not { IsComplete: true } => NotConfiguredPage(),
            Page.Schools => SchoolsPage(),
            Page.SchoolDetail => SchoolDetailPage(),
            Page.Licenses => LicensesPage(),
            Page.Servers => ServersPage(),
            Page.Relays => RelaysPage(),
            Page.Activity => ActivityPage(),
            Page.Backups => BackupsPage(),
            _ => HomePage()
        };
    }

    private StackPanel PageContainer()
    {
        var stack = new StackPanel
        {
            Margin = new Thickness(_compact ? 13 : 26, 22, _compact ? 13 : 26, 33),
            Spacing = 15,
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (_loading) stack.Children.Add(Banner("Chargement…", OwnerPalette.BlueWash, OwnerPalette.NavyLight));
        if (_error is not null) stack.Children.Add(Banner(_error, OwnerPalette.RedWash, OwnerPalette.Red));
        else if (_notice is not null) stack.Children.Add(Banner(_notice, OwnerPalette.GreenWash, OwnerPalette.Teal));
        return stack;
    }

    private static Control Banner(string text, IBrush fill, IBrush ink) =>
        Box(Text(text, 13, true, ink), fill, 12, new Thickness(14, 11));

    private Control PageTitle(string title, string subtitle, bool refresh = true)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(Text(title, _compact ? 23 : 28, true));
        text.Children.Add(Text(subtitle + (_loadedAt is { } at && refresh ? $" · mis à jour à {at:HH:mm}" : ""),
            12, false, OwnerPalette.Muted));
        grid.Children.Add(text);
        if (refresh)
        {
            var button = Button("↻  Actualiser", () => _ = LoadAsync(_page, force: true), subtle: true);
            button.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
        }
        return grid;
    }

    private Control NotConfiguredPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle(Sections.First(x => x.Page == _page).Name, "Accès non configuré sur cet appareil", false));
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Configurez l'accès sécurisé", 16, true));
        body.Children.Add(Text("Saisissez une fois le jeton de service Cloudflare Access et le jeton propriétaire. " +
            "Ils sont chiffrés sur cet appareil et jamais affichés.", 12, false, OwnerPalette.Muted));
        var go = Button("Configurer l'accès", () => Navigate(Page.Account), primary: true);
        go.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(go);
        stack.Children.Add(Box(body));
        return stack;
    }

    private Control WelcomePanel()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Text("CONSOLE PROPRIÉTAIRE", 11, true, OwnerPalette.BlueWash));
        body.Children.Add(Text("Pilotez SCOLIA Pro\navec précision.", _compact ? 25 : 32, true, OwnerPalette.Paper));
        body.Children.Add(Text(_credentials is { IsComplete: true }
            ? "Accès sécurisé configuré sur cet appareil · " + _credentials.Hint
            : "Espace réservé à JP-Services : licences, serveurs et sauvegardes, sans accès aux dossiers des apprenants.",
            13, false, OwnerPalette.BlueWash));
        if (_credentials is not { IsComplete: true })
        {
            var button = Button("Configurer l'accès", () => Navigate(Page.Account), primary: true);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(button);
        }
        return Box(body, OwnerPalette.Navy, 22, new Thickness(_compact ? 20 : 27));
    }

    private Control HomePage()
    {
        var stack = PageContainer();
        stack.Children.Add(WelcomePanel());
        if (_credentials is { IsComplete: true })
        {
            stack.Children.Add(PageTitle("Vue d'ensemble", "Chiffres du service JP-Services"));
            var s = _summary ?? default;
            var metrics = new WrapPanel { Orientation = Orientation.Horizontal };
            metrics.Children.Add(Metric("Établissements", Count(s, "schools"), $"{Count(s, "active_schools_30d")} actifs sur 30 jours",
                Page.Schools, OwnerPalette.BlueWash, OwnerPalette.Blue));
            metrics.Children.Add(Metric("Lecture seule", Count(s, "suspended"), "licences échues ou bloquées",
                Page.Licenses, OwnerPalette.RedWash, OwnerPalette.Red));
            metrics.Children.Add(Metric("Serveurs", Count(s, "server_devices"), "appareils enregistrés",
                Page.Servers, OwnerPalette.AmberWash, OwnerPalette.Amber));
            metrics.Children.Add(Metric("Sauvegardes", Count(s, "cloud_enabled"), $"{Count(s, "cloud_backup_late")} en retard",
                Page.Backups, OwnerPalette.GreenWash, OwnerPalette.Teal));
            stack.Children.Add(metrics);
        }
        stack.Children.Add(Text("Outils d'administration", 20, true));
        var tools = new StackPanel { Spacing = 9 };
        tools.Children.Add(CompactAction("Établissements et invitations", "Créer une invitation, ouvrir une école", Page.Schools));
        tools.Children.Add(CompactAction("Licences et renouvellements", "Paiements, essais, délais, blocages", Page.Licenses));
        tools.Children.Add(CompactAction("Relais Internet", "Catalogue WSS, activation d'un relais", Page.Relays));
        tools.Children.Add(CompactAction("Journal d'activité", "Toutes les opérations du propriétaire", Page.Activity));
        stack.Children.Add(tools);
        return stack;
    }

    private static string Count(JsonElement summary, string name) =>
        OwnerText.Number(summary, name)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    private Control Metric(string label, string value, string detail, Page page, IBrush wash, IBrush ink)
    {
        var pane = new StackPanel { Spacing = 6 };
        pane.Children.Add(Text(label.ToUpperInvariant(), 10, true, ink));
        pane.Children.Add(Text(value, 30, true));
        pane.Children.Add(Text(detail, 11, false, OwnerPalette.Muted));
        var button = new Button
        {
            Width = _compact ? (_narrow ? 290 : 151) : 205,
            MinHeight = 118,
            Margin = new Thickness(0, 0, 10, 10),
            Background = wash,
            BorderBrush = OwnerPalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14),
            Content = pane,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top
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

    /// <summary>Ligne de liste : titre, détail, pastille d'état à droite, ouvrant éventuellement un détail.</summary>
    private Control Row(string title, string detail, (string Label, LicenseTone Tone)? badge = null, Action? open = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text(detail, 11, false, OwnerPalette.Muted));
        grid.Children.Add(info);
        if (badge is { } b)
        {
            var (fill, ink) = b.Tone switch
            {
                LicenseTone.Good => (OwnerPalette.GreenWash, OwnerPalette.Teal),
                LicenseTone.Warning => (OwnerPalette.AmberWash, OwnerPalette.Amber),
                LicenseTone.Bad => (OwnerPalette.RedWash, OwnerPalette.Red),
                _ => (OwnerPalette.BlueWash, OwnerPalette.Blue)
            };
            var chip = Box(Text(b.Label, 11, true, ink), fill, 10, new Thickness(9, 4));
            chip.BorderThickness = new Thickness(0);
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(chip, 1);
            grid.Children.Add(chip);
        }
        if (open is null) return Box(grid, corner: 14, padding: new Thickness(14, 12));
        var button = new Button
        {
            Content = grid,
            Background = OwnerPalette.Paper,
            BorderBrush = OwnerPalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += (_, _) => open();
        return button;
    }

    private static Control Empty(string text) => Box(Text(text, 12, false, OwnerPalette.Muted), corner: 14);

    private void OpenSchool(string id)
    {
        _selectedSchool = id;
        _school = null;
        _notice = null;
        _page = Page.SchoolDetail;
        ApplyResponsive();
        _scroll.Offset = default;
        _ = LoadAsync(Page.SchoolDetail);
    }

    private Control SchoolsPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Établissements", "Écoles reliées à JP-Services"));

        // Invitation : nom exact de l'établissement, code à usage unique valable 14 jours.
        var invite = new StackPanel { Spacing = 9 };
        invite.Children.Add(Text("Nouvelle invitation", 16, true));
        invite.Children.Add(Text("Le nom doit être celui saisi sur le serveur de l'école. Code valable 14 jours, une seule fois.",
            12, false, OwnerPalette.Muted));
        var name = new TextBox { PlaceholderText = "Nom de l'établissement", MaxLength = 140, MinHeight = 42 };
        invite.Children.Add(name);
        var create = Button("Créer l'invitation", () =>
        {
            var school = name.Text?.Trim() ?? "";
            if (school.Length < 3) { _error = "Saisissez le nom de l'établissement (3 caractères au moins)."; ShowPage(); return; }
            _ = CreateInvitationAsync(a => a.CreateInvitationAsync(school));
        }, primary: true);
        create.HorizontalAlignment = HorizontalAlignment.Left;
        invite.Children.Add(create);
        if (_invitation is { } code) invite.Children.Add(InvitationResult(code));
        stack.Children.Add(Box(invite));

        var schools = OwnerText.Items(_schools, "schools");
        stack.Children.Add(Text($"Écoles enregistrées · {schools.Count}", 16, true));
        if (schools.Count == 0 && !_loading) stack.Children.Add(Empty("Aucun établissement relié pour l'instant."));
        foreach (var s in schools)
        {
            var license = OwnerText.Child(s, "license");
            var id = OwnerText.Str(s, "id");
            stack.Children.Add(Row(OwnerText.Str(s, "name"),
                $"Dernier signal {OwnerText.Ago(OwnerText.Number(s, "last_seen"))} · {OwnerText.Number(s, "device_count") ?? 0} serveur(s)" +
                (OwnerText.Flag(s, "cloud_enabled") ? " · sauvegarde en ligne" : ""),
                OwnerText.License(OwnerText.Str(license, "license_status")), () => OpenSchool(id)));
        }
        return stack;
    }

    private async Task CreateInvitationAsync(Func<OwnerApi, Task<JsonElement>> create)
    {
        if (Api() is not { } api) return;
        _error = null; _notice = null;
        try
        {
            var reply = await create(api);
            _invitation = (OwnerText.Str(reply, "invite_code"), OwnerText.Str(reply, "school_name"),
                OwnerText.Number(reply, "expires_at"));
            _notice = OwnerText.Str(reply, "notice");
            _audit = null;
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Invitation impossible. Réessayez."; }
        await Dispatcher.UIThread.InvokeAsync(ShowPage);
    }

    private Control InvitationResult((string Code, string School, long? Expires) invitation)
    {
        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(Text($"Invitation pour {invitation.School} · valable jusqu'au {OwnerText.DateTime(invitation.Expires)}",
            12, true, OwnerPalette.Teal));
        box.Children.Add(new SelectableTextBlock
        {
            Text = invitation.Code,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = OwnerPalette.Ink,
            TextWrapping = TextWrapping.Wrap
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Copier le code", () => _ = CopyAsync(invitation.Code)));
        actions.Children.Add(Button("Masquer", () => { _invitation = null; ShowPage(); }, subtle: true));
        box.Children.Add(actions);
        box.Children.Add(Text("Ce code n'est plus affiché ensuite. Transmettez-le uniquement au responsable du serveur.",
            11, false, OwnerPalette.Muted));
        return Box(box, OwnerPalette.GreenWash, 14, new Thickness(14));
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                _notice = "Code copié dans le presse-papiers.";
            }
        }
        catch (Exception) { _error = "Copie impossible : sélectionnez le code à la main."; }
        ShowPage();
    }

    private Control SchoolDetailPage()
    {
        var stack = PageContainer();
        var back = Button("‹  Établissements", () => Navigate(Page.Schools), subtle: true);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(back);
        if (_school is not { } data) return stack;
        var school = OwnerText.Child(data, "school");
        var license = OwnerText.Child(data, "license");
        var id = OwnerText.Str(school, "id");
        stack.Children.Add(PageTitle(OwnerText.Str(school, "name"), $"Reliée le {OwnerText.Date(OwnerText.Number(school, "created_at"))}"));

        var status = new StackPanel { Spacing = 6 };
        status.Children.Add(Text("LICENCE", 10, true, OwnerPalette.Muted));
        status.Children.Add(Text(OwnerText.LicenseLine(license), 17, true));
        status.Children.Add(Text(OwnerText.Flag(license, "readonly")
            ? "Le serveur de l'école fonctionne en lecture seule : consultations possibles, écritures bloquées."
            : "Accès complet : l'école travaille normalement.", 12, false, OwnerPalette.Muted));
        status.Children.Add(Text($"Facturation {(OwnerText.Flag(license, "billing_enabled") ? "activée" : "désactivée")} · " +
            $"essai jusqu'au {OwnerText.Date(OwnerText.Number(license, "trial_ends_at"))}" +
            (OwnerText.Str(school, "override_reason") is { Length: > 0 } reason ? $" · motif : {reason}" : ""),
            11, false, OwnerPalette.Muted));
        stack.Children.Add(Box(status));

        stack.Children.Add(Text("Renouvellement", 16, true));
        stack.Children.Add(DaysAction("Enregistrer un paiement", "Jours payés à partir d'aujourd'hui (ou de l'échéance en cours)", "365", 1100,
            days =>
            {
                var paid = OwnerText.Number(school, "paid_until") is { } current && current > Now() ? current : Now();
                return new Dictionary<string, object?> { ["billing_enabled"] = true, ["paid_until"] = paid + days * DayMs };
            }, id, "Confirmer le paiement"));
        stack.Children.Add(DaysAction("Délai supplémentaire", "Jours de délai après l'échéance (120 au plus)", "15", 120,
            days => new Dictionary<string, object?> { ["grace_until"] = Now() + days * DayMs }, id, "Accorder le délai"));
        stack.Children.Add(DaysAction("Durée de l'essai", "Jours d'essai comptés depuis la liaison (365 au plus)", "30", 365,
            days => new Dictionary<string, object?> { ["trial_days"] = days }, id, "Modifier l'essai"));

        stack.Children.Add(Text("Contrôle manuel", 16, true));
        stack.Children.Add(ReasonAction("Bloquer en lecture seule", "Motif (5 caractères au moins)", id,
            reason => new Dictionary<string, object?> { ["manual_override"] = "read_only", ["reason"] = reason, ["override_until"] = null },
            danger: true));
        stack.Children.Add(ReasonAction("Dérogation de 30 jours", "Motif de la dérogation", id,
            reason => new Dictionary<string, object?> { ["manual_override"] = "allow", ["reason"] = reason, ["override_until"] = Now() + 30 * DayMs }));
        var manual = OwnerText.Str(license, "manual_override");
        var actions = new WrapPanel();
        if (manual is "read_only" or "allow")
            actions.Children.Add(Confirm("Retirer le contrôle manuel", "Confirmer le retrait",
                () => RunAsync(a => a.UpdateLicenseAsync(id, new Dictionary<string, object?> { ["manual_override"] = "none", ["override_until"] = null }), Page.SchoolDetail)));
        var billing = OwnerText.Flag(license, "billing_enabled");
        actions.Children.Add(Confirm(billing ? "Désactiver la facturation" : "Activer la facturation",
            billing ? "Confirmer : licence gratuite" : "Confirmer l'activation",
            () => RunAsync(a => a.UpdateLicenseAsync(id, new Dictionary<string, object?> { ["billing_enabled"] = !billing }), Page.SchoolDetail)));
        stack.Children.Add(actions);

        stack.Children.Add(Text("Continuité", 16, true));
        var cloud = OwnerText.Flag(school, "cloud_requested");
        var continuity = new WrapPanel();
        continuity.Children.Add(Confirm(cloud ? "Retirer la sauvegarde en ligne" : "Proposer la sauvegarde en ligne",
            "Confirmer", () => RunAsync(a => a.RequestCloudAsync(id, !cloud), Page.SchoolDetail)));
        continuity.Children.Add(Confirm("Invitation de reprise (nouveau serveur)", "Confirmer : l'ancien serveur sera remplacé",
            () => CreateInvitationAsync(a => a.CreateRecoveryInvitationAsync(id)), danger: true));
        stack.Children.Add(continuity);
        if (_invitation is { } code) stack.Children.Add(InvitationResult(code));

        var devices = OwnerText.Items(data, "devices");
        stack.Children.Add(Text($"Serveurs · {devices.Count}", 16, true));
        if (devices.Count == 0) stack.Children.Add(Empty("Aucun serveur enregistré."));
        foreach (var d in devices)
            stack.Children.Add(Row(OwnerText.Str(d, "device_name"),
                $"Dernier signal {OwnerText.Ago(OwnerText.Number(d, "last_seen"))} · depuis le {OwnerText.Date(OwnerText.Number(d, "first_seen"))}" +
                (OwnerText.Str(d, "last_ip") is { Length: > 0 } ip ? $" · IP {ip}" : "")));

        var activity = OwnerText.Items(data, "activity");
        if (activity.Count > 0)
        {
            var logins = activity.Sum(a => OwnerText.Number(a, "login_count") ?? 0);
            stack.Children.Add(Text($"Activité sur {activity.Count} jours : {logins} connexions", 12, false, OwnerPalette.Muted));
        }
        return stack;
    }

    private const long DayMs = 86_400_000;
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private Control DaysAction(string title, string hint, string initial, int max,
        Func<int, Dictionary<string, object?>> patch, string schoolId, string confirm)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,Auto"), ColumnSpacing = 8 };
        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text(hint, 11, false, OwnerPalette.Muted));
        grid.Children.Add(info);
        var days = new TextBox { Text = initial, MaxLength = 4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(days, 1);
        grid.Children.Add(days);
        var go = Confirm("Appliquer", confirm, () =>
        {
            if (OwnerText.Days(days.Text, max) is not { } n)
            {
                _error = $"Nombre de jours invalide (1 à {max}).";
                ShowPage();
                return Task.CompletedTask;
            }
            return RunAsync(a => a.UpdateLicenseAsync(schoolId, patch(n)), Page.SchoolDetail);
        });
        go.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(go, 2);
        grid.Children.Add(go);
        return Box(grid, corner: 14, padding: new Thickness(14, 12));
    }

    private Control ReasonAction(string title, string hint, string schoolId,
        Func<string, Dictionary<string, object?>> patch, bool danger = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var info = new StackPanel { Spacing = 6 };
        info.Children.Add(Text(title, 14, true));
        var reason = new TextBox { PlaceholderText = hint, MaxLength = 180 };
        info.Children.Add(reason);
        grid.Children.Add(info);
        var go = Confirm("Appliquer", "Confirmer", () =>
        {
            var text = reason.Text?.Trim() ?? "";
            if (text.Length < 5) { _error = "Indiquez un motif (5 caractères au moins)."; ShowPage(); return Task.CompletedTask; }
            return RunAsync(a => a.UpdateLicenseAsync(schoolId, patch(text)), Page.SchoolDetail);
        }, danger);
        go.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(go, 1);
        grid.Children.Add(go);
        return Box(grid, corner: 14, padding: new Thickness(14, 12));
    }

    /// <summary>Bouton en deux temps : le premier appui demande confirmation, le second (dans les 6 s) exécute.</summary>
    private Button Confirm(string label, string confirm, Func<Task> action, bool danger = false)
    {
        var button = Button(label, () => { });
        button.Margin = new Thickness(0, 0, 8, 8);
        var armed = false;
        IDisposable? timer = null;
        button.Click += async (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                button.Content = confirm;
                button.Background = danger ? OwnerPalette.Red : OwnerPalette.Blue;
                button.Foreground = OwnerPalette.Paper;
                timer = DispatcherTimer.RunOnce(() =>
                {
                    armed = false;
                    button.Content = label;
                    button.Background = OwnerPalette.BlueWash;
                    button.Foreground = OwnerPalette.NavyLight;
                }, TimeSpan.FromSeconds(6));
                return;
            }
            timer?.Dispose();
            armed = false;
            button.IsEnabled = false;
            await action();
        };
        return button;
    }

    private Control LicensesPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Licences", "Statut effectif calculé par le service"));
        var schools = OwnerText.Items(_schools, "schools");
        var order = new[] { "suspended", "expired", "grace", "derogation", "trial", "active", "free" };
        foreach (var status in order)
        {
            var group = schools.Where(s => OwnerText.Str(OwnerText.Child(s, "license"), "license_status") == status)
                .OrderBy(s => OwnerText.Number(OwnerText.Child(s, "license"), "days_remaining") ?? long.MaxValue).ToList();
            if (group.Count == 0) continue;
            stack.Children.Add(Text($"{OwnerText.License(status).Label} · {group.Count}", 16, true));
            foreach (var s in group)
            {
                var id = OwnerText.Str(s, "id");
                stack.Children.Add(Row(OwnerText.Str(s, "name"), OwnerText.LicenseLine(OwnerText.Child(s, "license")),
                    OwnerText.License(status), () => OpenSchool(id)));
            }
        }
        if (schools.Count == 0 && !_loading) stack.Children.Add(Empty("Aucune licence suivie pour l'instant."));
        return stack;
    }

    private Control ServersPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Serveurs", "Appareils serveurs déclarés par les écoles"));
        var servers = OwnerText.Items(_servers, "servers");
        if (servers.Count == 0 && !_loading) stack.Children.Add(Empty("Aucun serveur enregistré."));
        foreach (var s in servers)
        {
            var recent = OwnerText.Str(s, "status") == "recent";
            stack.Children.Add(Row(OwnerText.Str(s, "device_name"),
                $"{OwnerText.Str(s, "school_name")} · dernier signal {OwnerText.Ago(OwnerText.Number(s, "last_seen"))}" +
                (OwnerText.Str(s, "public_ip") is { Length: > 0 } ip ? $" · IP {ip}" : ""),
                recent ? ("En ligne", LicenseTone.Good) : ("Sans signal récent", LicenseTone.Warning),
                OwnerText.Str(s, "school_id") is { Length: > 0 } sid ? () => OpenSchool(sid) : null));
        }
        return stack;
    }

    private Control RelaysPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Relais Internet", "Catalogue WSS de JP-Services"));
        stack.Children.Add(Text("Un seul relais est actif à la fois. Chaque école doit approuver une nouvelle révision avant de l'utiliser.",
            12, false, OwnerPalette.Muted));
        var relays = OwnerText.Items(_relays, "relays");
        if (relays.Count == 0 && !_loading) stack.Children.Add(Empty("Aucun relais dans le catalogue. Ajoutez-en depuis la console web."));
        foreach (var r in relays)
        {
            var id = OwnerText.Str(r, "id");
            var active = OwnerText.Str(r, "state") == "active";
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(Row(OwnerText.Str(r, "label"),
                $"{OwnerText.Str(r, "url")} · révision {OwnerText.Str(r, "revision")} · modifié {OwnerText.Ago(OwnerText.Number(r, "updated_at"))}",
                active ? ("Actif", LicenseTone.Good) : ("Désactivé", LicenseTone.Info)));
            var action = Confirm(active ? "Désactiver" : "Activer ce relais",
                active ? "Confirmer la désactivation" : "Confirmer : remplace le relais actif",
                () => RunAsync(a => a.SetRelayStateAsync(id, !active), Page.Relays), danger: active);
            action.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Children.Add(action);
            stack.Children.Add(panel);
        }
        return stack;
    }

    private Control ActivityPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Journal d'activité", "100 dernières opérations du propriétaire"));
        var history = OwnerText.Items(_audit, "history");
        if (history.Count == 0 && !_loading) stack.Children.Add(Empty("Aucune opération enregistrée."));
        foreach (var h in history)
            stack.Children.Add(Row(OwnerText.Action(OwnerText.Str(h, "action")),
                OwnerText.DateTime(OwnerText.Number(h, "created_at")) +
                (OwnerText.Str(h, "school_name") is { Length: > 0 } name ? " · " + name : "")));
        return stack;
    }

    private Control BackupsPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Sauvegardes", "Métadonnées seulement : le contenu est chiffré par chaque école"));
        var s = _summary ?? default;
        var info = new StackPanel { Spacing = 6 };
        info.Children.Add(Text(OwnerText.Flag(s, "storage_configured") ? "Stockage Cloudflare R2 configuré" : "Stockage des sauvegardes non configuré",
            16, true, OwnerText.Flag(s, "storage_configured") ? OwnerPalette.Teal : OwnerPalette.Amber));
        info.Children.Add(Text($"{Count(s, "cloud_enabled")} école(s) sauvegardée(s) · {Count(s, "cloud_backup_late")} en retard (plus de 26 h) · " +
            $"{OwnerText.Bytes(OwnerText.Number(s, "cloud_backup_bytes"))} stockés · {Count(s, "cloud_pending_operations")} opération(s) en attente",
            12, false, OwnerPalette.Muted));
        stack.Children.Add(Box(info));
        foreach (var school in OwnerText.Items(_schools, "schools").Where(x => OwnerText.Flag(x, "cloud_enabled") || OwnerText.Flag(x, "cloud_requested")))
        {
            var id = OwnerText.Str(school, "id");
            var late = OwnerText.Flag(school, "cloud_backup_late");
            stack.Children.Add(Row(OwnerText.Str(school, "name"),
                OwnerText.Flag(school, "cloud_enabled")
                    ? $"Dernière sauvegarde {OwnerText.Ago(OwnerText.Number(school, "last_backup_at"))}"
                    : "Proposée, en attente d'activation par le serveur de l'école",
                OwnerText.Flag(school, "cloud_enabled") ? (late ? ("En retard", LicenseTone.Warning) : ("À jour", LicenseTone.Good)) : ("Proposée", LicenseTone.Info),
                () => OpenSchool(id)));
        }
        return stack;
    }

    private Control AccountPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Compte et sécurité", "SCOLIA Console " + OwnerInfo.Version + " · JP-Services", false));

        var access = new StackPanel { Spacing = 9 };
        access.Children.Add(Text("Accès sécurisé de cet appareil", 16, true));
        if (_vault is not { CanStore: true })
        {
            access.Children.Add(Text("Cet appareil ne permet pas de conserver des secrets chiffrés : l'accès natif est indisponible. " +
                "Utilisez la console web.", 12, false, OwnerPalette.Red));
        }
        else if (_credentials is { IsComplete: true })
        {
            access.Children.Add(Text($"Configuré · jeton de service {_credentials.Hint}. Les secrets sont chiffrés sur cet appareil.",
                12, false, OwnerPalette.Muted));
            var row = new WrapPanel();
            var test = Button("Tester la connexion", () => _ = LoadAsync(Page.Home, force: true).ContinueWith(_ =>
                Dispatcher.UIThread.Post(() => { if (_error is null) { _notice = "Connexion vérifiée : accès propriétaire valide."; ShowPage(); } })));
            test.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(test);
            row.Children.Add(Confirm("Effacer l'accès de cet appareil", "Confirmer l'effacement", () =>
            {
                _vault.Clear();
                _credentials = null;
                ResetData();
                _notice = "Accès effacé de cet appareil.";
                ShowPage();
                return Task.CompletedTask;
            }, danger: true));
            access.Children.Add(row);
        }
        else
        {
            access.Children.Add(Text("Dans Cloudflare Zero Trust, créez un jeton de service et ajoutez à l'application Access de la console " +
                "une règle « Service Auth » qui l'autorise. Saisissez ici son identifiant, son secret et le jeton propriétaire.",
                12, false, OwnerPalette.Muted));
            var id = new TextBox { PlaceholderText = "Identifiant du jeton de service", MaxLength = 200 };
            var secret = new TextBox { PlaceholderText = "Secret du jeton de service", PasswordChar = '•', MaxLength = 200 };
            var owner = new TextBox { PlaceholderText = "Jeton propriétaire", PasswordChar = '•', MaxLength = 400 };
            access.Children.Add(id);
            access.Children.Add(secret);
            access.Children.Add(owner);
            var save = Button("Enregistrer et vérifier", () => _ = SaveCredentialsAsync(
                new OwnerCredentials(id.Text ?? "", secret.Text ?? "", owner.Text ?? "")), primary: true);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            access.Children.Add(save);
        }
        stack.Children.Add(Box(access));

        var updates = new StackPanel { Spacing = 10 };
        updates.Children.Add(Text("Mises à jour JP-Services", 16, true));
        updates.Children.Add(Text("Canal indépendant de SCOLIA Pro · version " + OwnerInfo.Version, 12, false, OwnerPalette.Muted));
        var check = Button("Rechercher les mises à jour", () => _ = CheckUpdatesAsync(true));
        check.HorizontalAlignment = HorizontalAlignment.Left;
        updates.Children.Add(check);
        updates.Children.Add(_message);
        stack.Children.Add(Box(updates));

        var web = new StackPanel { Spacing = 8 };
        web.Children.Add(Text("Console web", 14, true));
        web.Children.Add(Text("Stockage R2, découverte TURN et ajout de relais restent dans la console web protégée.",
            12, false, OwnerPalette.Muted));
        var open = Button("Ouvrir la console web  ↗", () => _ = OpenConsoleAsync(), subtle: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        web.Children.Add(open);
        stack.Children.Add(Box(web, OwnerPalette.BlueWash, 15));
        return stack;
    }

    /// <summary>Vérifie l'accès AVANT d'enregistrer : des identifiants refusés ne sont jamais conservés.</summary>
    private async Task SaveCredentialsAsync(OwnerCredentials candidate)
    {
        _notice = null;
        if (!candidate.IsComplete)
        {
            _error = "Les trois champs sont obligatoires (identifiant, secret et jeton propriétaire).";
            ShowPage();
            return;
        }
        _loading = true;
        _error = null;
        ShowPage();
        try
        {
            using var probe = ApiFactory(candidate);
            var summary = await probe.SummaryAsync();
            _vault?.Save(candidate);
            ResetData();
            _credentials = candidate;
            _summary = summary;
            _loadedAt = DateTimeOffset.Now;
            _notice = "Accès vérifié et enregistré de façon chiffrée sur cet appareil.";
        }
        catch (OwnerApiException ex) { _error = ex.Message; }
        catch (Exception) { _error = "Vérification impossible. Réessayez."; }
        finally
        {
            _loading = false;
            await Dispatcher.UIThread.InvokeAsync(ShowPage);
        }
    }

    private async Task OpenConsoleAsync(string? section = null)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(new Uri(
                ConsoleUrl + (section is null ? "" : "#" + section))))
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
                "Aucune version officielle de SCOLIA Console n’est encore publiée." :
                best > new System.Version(OwnerInfo.Version) ?
                    "Mise à jour administrative " + best + " disponible." :
                    "La version administrateur est à jour sur le canal officiel.";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _message.Text = message;
                if (best > new System.Version(OwnerInfo.Version) && link is not null && interactive)
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
