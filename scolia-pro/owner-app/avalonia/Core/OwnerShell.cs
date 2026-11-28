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
public sealed partial class OwnerShell : UserControl
{
    private const string ConsoleUrl =
        "https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console";
    private const string ReleasesApi =
        "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases?per_page=50";
    private const string Channel = "scolia-owner-v";
    private static readonly HttpClient Client = NewClient();

    private enum Page { Home, Schools, Licenses, Servers, Relays, Activity, Backups, Storage, Turn, Netlify, Account, SchoolDetail, Menu }
    private static readonly (Page Page, string Icon, string Name)[] Sections =
    [
        (Page.Home, "Home", "Accueil"),
        (Page.Schools, "Building", "Établissements"),
        (Page.Licenses, "Certificate", "Licences"),
        (Page.Servers, "PhoneLaptop", "Serveurs"),
        (Page.Relays, "Globe", "Relais"),
        (Page.Activity, "History", "Activité"),
        (Page.Backups, "Cloud", "Sauvegardes"),
        (Page.Storage, "Database", "Stockage cloud"),
        (Page.Turn, "Settings", "Découverte TURN"),
        (Page.Netlify, "CloudDismiss", "Secours HTTPS"),
        (Page.Account, "ShieldCheckmark", "Compte")
    ];

    private readonly Grid _layout = new()
    {
        RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        ColumnDefinitions = new ColumnDefinitions("238,*")
    };
    private readonly Border _sidebar = new();
    private readonly Border _bottomNav = new();
    private readonly StackPanel _sidebarButtons = new() { Spacing = 5 };
    private readonly Grid _bottomButtons = new() { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*") };
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly TextBlock _heading = new()
    {
        FontSize = 20,
        FontWeight = FontWeight.SemiBold,
        Foreground = OwnerPalette.Ink,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
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
    private bool _short;

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
        Classes.Add("owner-root");
        Background = OwnerPalette.Background;
        Content = _layout;

        _sidebar.Background = OwnerPalette.Background;
        _sidebar.Child = BuildSidebar();
        Grid.SetRowSpan(_sidebar, 3);
        _layout.Children.Add(_sidebar);

        var top = BuildHeader();
        Grid.SetColumn(top, 1);
        _layout.Children.Add(top);

        var workspace = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        workspace.Children.Add(_statusPanel);
        Grid.SetRow(_scroll, 1);
        workspace.Children.Add(_scroll);
        Grid.SetColumn(workspace, 1);
        Grid.SetRow(workspace, 1);
        _layout.Children.Add(workspace);

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
            bool shortScreen = args.NewSize.Height < 520;
            if (compact == _compact && narrow == _narrow && shortScreen == _short) return;
            _compact = compact;
            _narrow = narrow;
            _short = shortScreen;
            ApplyResponsive();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _backHost = TopLevel.GetTopLevel(this);
            if (_backHost is not null) _backHost.BackRequested += OnBackRequested;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_backHost is not null) _backHost.BackRequested -= OnBackRequested;
            _backHost = null;
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

    private static TextBlock Text(string text, double size = 14, bool bold = false, IBrush? color = null)
    {
        var block = new TextBlock
        {
            Text = text, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = color ?? OwnerPalette.Ink, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (size <= 13) block.Classes.Add("ownerdetail");
        else if (size == 14) block.Classes.Add("ownerbody");
        else block.FontSize = size;
        return block;
    }

    private static Border Box(Control child, IBrush? fill = null, int corner = 16, Thickness? padding = null)
    {
        var border = new Border
        {
            Background = fill ?? OwnerPalette.Paper, BorderBrush = OwnerPalette.Border,
            BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(16), Child = child
        };
        border.Classes.Add("ownercard");
        if (corner < 12) border.CornerRadius = new CornerRadius(corner);
        return border;
    }

    private Button Button(string name, Action action, bool primary = false, bool subtle = false)
    {
        var button = new Button
        {
            Content = ButtonLabel(name),
            Background = primary ? OwnerPalette.Blue :
                subtle ? OwnerPalette.Background : OwnerPalette.BlueWash,
            Foreground = primary ? OwnerPalette.OnBlue : OwnerPalette.Ink,
            BorderBrush = primary ? OwnerPalette.Blue : OwnerPalette.Border,
            BorderThickness = new Thickness(primary ? 0 : 1),
            Padding = new Thickness(15, 11),
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("ownercmd");
        button.Classes.Add(primary ? "ownerprimary" : "ownersecondary");
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock ButtonLabel(string name)
    {
        var text = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        text.Classes.Add("ownerbody");
        return text;
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

    /// <summary>Fabrique du client ; remplacée seulement par les tests (serveur simulé).</summary>
    internal static Func<OwnerCredentials, OwnerApi> ApiFactory { get; set; } = credentials => new OwnerApi(credentials);

    private OwnerApi? Api()
    {
        if (_credentials is not { IsComplete: true } credentials) return null;
        return _api ??= ApiFactory(credentials);
    }

    /// <summary>Charge les données d'une page, puis l'affiche. Une erreur reste visible en tête de page.</summary>
    // ───────────────────────── Pages ─────────────────────────

    private void ShowPage(bool preserveScroll = false)
    {
        var offset = preserveScroll ? _scroll.Offset : default;
        _heading.Text = _page == Page.SchoolDetail
            ? (_school is { } sc ? OwnerText.Str(OwnerText.Child(sc, "school"), "name") : "Établissement")
            : _page == Page.Menu ? "Menu" : Sections.First(x => x.Page == _page).Name;
        _scroll.Content = _page switch
        {
            Page.Home => HomePage(),
            Page.Account => AccountPage(),
            Page.Menu => MenuPage(),
            _ when _credentials is not { IsComplete: true } => NotConfiguredPage(),
            Page.Schools => SchoolsPage(),
            Page.SchoolDetail => SchoolDetailPage(),
            Page.Licenses => LicensesPage(),
            Page.Servers => ServersPage(),
            Page.Relays => RelaysPage(),
            Page.Activity => ActivityPage(),
            Page.Backups => BackupsPage(),
            Page.Storage => StoragePage(),
            Page.Turn => TurnPage(),
            Page.Netlify => NetlifyPage(),
            _ => HomePage()
        };
        _scroll.Offset = offset;
        ApplyResponsive();
        ShowStatus();
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

    private Control SchoolsPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Établissements", "Écoles reliées à JP-Services"));

        // Invitation : nom exact de l'établissement, code à usage unique valable 14 jours.
        var invite = new StackPanel { Spacing = 9 };
        invite.Children.Add(Text("Nouvelle invitation", 16, true));
        invite.Children.Add(Text("Le nom doit être celui saisi sur le serveur de l'école. Code valable 14 jours, une seule fois.",
            12, false, OwnerPalette.Muted));
        invite.Children.Add(Text("Nom de l'établissement", 12, true));
        var name = Field("invitation-name", "Nom de l'établissement", hint: "Nom exact de l'établissement");
        name.MaxLength = 140;
        invite.Children.Add(name);
        var create = Button("Créer l'invitation", () =>
        {
            var school = name.Text?.Trim() ?? "";
            if (school.Length < 3) { _error = "Saisissez le nom de l'établissement (3 caractères au moins)."; ShowStatus(); return; }
            _ = CreateInvitationAsync(a => a.CreateInvitationAsync(school));
        }, primary: true);
        create.HorizontalAlignment = HorizontalAlignment.Left;
        invite.Children.Add(create);
        if (_invitation is { } code) invite.Children.Add(InvitationResult(code));
        stack.Children.Add(Box(invite));

        var schools = OwnerText.Items(_schools, "schools");
        stack.Children.Add(SearchableList(schools, "schools-search", "Rechercher un établissement",
            s => OwnerText.Str(s, "name") + " " + OwnerText.License(OwnerText.Str(OwnerText.Child(s, "license"), "license_status")).Label,
            s =>
        {
            var license = OwnerText.Child(s, "license");
            var id = OwnerText.Str(s, "id");
            return Row(OwnerText.Str(s, "name"),
                $"Dernier signal {OwnerText.Ago(OwnerText.Number(s, "last_seen"))} · {OwnerText.Number(s, "device_count") ?? 0} serveur(s)" +
                (OwnerText.Flag(s, "cloud_enabled") ? " · sauvegarde en ligne" : ""),
                OwnerText.License(OwnerText.Str(license, "license_status")), () => OpenSchool(id));
        }, "Aucun établissement relié pour l'instant."));
        return stack;
    }

    private Control InvitationResult((string Code, string School, long? Expires) invitation)
    {
        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(Text($"Invitation pour {invitation.School} · valable jusqu'au {OwnerText.DateTime(invitation.Expires)}",
            12, true, OwnerPalette.Teal));
        box.Children.Add(new SelectableTextBlock
        {
            Text = invitation.Code,
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
            else _error = "Presse-papiers indisponible : sélectionnez le code à la main.";
        }
        catch (Exception) { _error = "Copie impossible : sélectionnez le code à la main."; }
        ShowStatus();
    }

    private Control SchoolDetailPage()
    {
        var stack = PageContainer();
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
            (OwnerText.Str(license, "manual_override") is "read_only" or "allow" && OwnerText.Str(school, "override_reason") is { Length: > 0 } reason ? $" · motif : {reason}" : ""),
            11, false, OwnerPalette.Muted));
        stack.Children.Add(Box(status));

        stack.Children.Add(Text("Renouvellement", 16, true));
        stack.Children.Add(DaysAction("Enregistrer un paiement", "Jours payés à partir d'aujourd'hui (ou de l'échéance en cours)", "365", 1100,
            days =>
            {
                var paid = OwnerText.Number(school, "paid_until") is { } current && current > Now() ? current : Now();
                return new Dictionary<string, object?> { ["billing_enabled"] = true, ["paid_until"] = paid + days * DayMs,
                    ["manual_override"] = "none", ["override_until"] = null };
            }, id, OwnerText.Str(license, "manual_override") is "allow" or "read_only"
                ? "Confirmer le paiement et lever le contrôle manuel" : "Confirmer le paiement"));
        stack.Children.Add(DaysAction("Délai supplémentaire", "Ajouter des jours après l'échéance ou le délai en cours (120 au plus)", "15", 120,
            days => new Dictionary<string, object?> { ["grace_until"] = new[] { Now(), OwnerText.Number(school, "paid_until") ?? 0,
                OwnerText.Number(school, "grace_until") ?? 0, OwnerText.Number(license, "trial_ends_at") ?? 0 }.Max() + days * DayMs }, id, "Accorder le délai"));
        stack.Children.Add(DaysAction("Durée de l'essai", "Jours d'essai comptés depuis la liaison (0 à 365)", "30", 365,
            days => new Dictionary<string, object?> { ["trial_days"] = days }, id, "Modifier l'essai", minimum: 0));

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
        Func<int, Dictionary<string, object?>> patch, string schoolId, string confirm, int minimum = 1)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,96,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        var info = new StackPanel { Spacing = 3 };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text(hint, 11, false, OwnerPalette.Muted));
        grid.Children.Add(info);
        var days = Field($"days:{schoolId}:{title}", "Nombre de jours · " + title, initial);
        days.MaxLength = 4;
        var input = new StackPanel { Spacing = 4, Children = { Text("Jours", 12, true), days } };
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        var go = Confirm("Appliquer", confirm, () =>
        {
            if (OwnerText.Days(days.Text, max, minimum) is not { } n)
            {
                _error = $"Nombre de jours invalide ({minimum} à {max}).";
                ShowStatus();
                return Task.CompletedTask;
            }
            return RunAsync(a => a.UpdateLicenseAsync(schoolId, patch(n)), Page.SchoolDetail);
        });
        go.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(go, 2);
        grid.Children.Add(go);
        OwnerLayout.StackBelow(grid, 680);
        return Box(grid, corner: 14, padding: new Thickness(14, 12));
    }

    private Control ReasonAction(string title, string hint, string schoolId,
        Func<string, Dictionary<string, object?>> patch, bool danger = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        var info = new StackPanel { Spacing = 6 };
        info.Children.Add(Text(title, 14, true));
        info.Children.Add(Text("Motif", 12, true));
        var reason = Field($"reason:{schoolId}:{title}", "Motif · " + title, hint: hint);
        reason.MaxLength = 180;
        info.Children.Add(reason);
        grid.Children.Add(info);
        var go = Confirm("Appliquer", "Confirmer", () =>
        {
            var text = reason.Text?.Trim() ?? "";
            if (text.Length < 5) { _error = "Indiquez un motif (5 caractères au moins)."; ShowStatus(); return Task.CompletedTask; }
            return RunAsync(a => a.UpdateLicenseAsync(schoolId, patch(text)), Page.SchoolDetail);
        }, danger);
        go.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(go, 1);
        grid.Children.Add(go);
        OwnerLayout.StackBelow(grid, 680);
        return Box(grid, corner: 14, padding: new Thickness(14, 12));
    }

    /// <summary>Bouton en deux temps : le premier appui demande confirmation, le second (dans les 6 s) exécute.</summary>
    private Button Confirm(string label, string confirm, Func<Task> action, bool danger = false)
    {
        var button = Button(label, () => { });
        button.Margin = new Thickness(0, 0, 8, 8);
        var armed = false;
        var running = false;
        IDisposable? timer = null;
        var background = button.Background;
        var foreground = button.Foreground;
        void Reset()
        {
            armed = false;
            button.Content = ButtonLabel(label);
            button.Background = background;
            button.Foreground = foreground;
            Avalonia.Automation.AutomationProperties.SetName(button, label);
        }
        button.DetachedFromVisualTree += (_, _) => { timer?.Dispose(); Reset(); };
        button.Click += async (_, _) =>
        {
            if (running || _writing) return;
            if (!armed)
            {
                armed = true;
                button.Content = ButtonLabel(confirm);
                button.Background = danger ? OwnerPalette.Red : OwnerPalette.Blue;
                button.Foreground = OwnerPalette.OnBlue;
                Avalonia.Automation.AutomationProperties.SetName(button, confirm);
                timer = DispatcherTimer.RunOnce(() =>
                {
                    Reset();
                }, TimeSpan.FromSeconds(6));
                return;
            }
            timer?.Dispose();
            armed = false;
            running = true;
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception) { _error = "Opération impossible. Réessayez."; ShowStatus(); }
            finally { running = false; button.IsEnabled = true; Reset(); }
        };
        return button;
    }

    private Control LicensesPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Licences", "Statut effectif calculé par le service"));
        var schools = OwnerText.Items(_schools, "schools");
        var order = new[] { "suspended", "expired", "grace", "derogation", "trial", "active", "free" };
        var sorted = schools.OrderBy(s =>
        {
            var rank = Array.IndexOf(order, OwnerText.Str(OwnerText.Child(s, "license"), "license_status"));
            return rank < 0 ? int.MaxValue : rank;
        }).ThenBy(s => OwnerText.Number(OwnerText.Child(s, "license"), "days_remaining") ?? long.MaxValue).ToArray();
        stack.Children.Add(SearchableList(sorted, "license-search", "Rechercher une licence",
            s => OwnerText.Str(s, "name") + " " + OwnerText.License(OwnerText.Str(OwnerText.Child(s, "license"), "license_status")).Label,
            s =>
            {
                var id = OwnerText.Str(s, "id");
                return Row(OwnerText.Str(s, "name"), OwnerText.LicenseLine(OwnerText.Child(s, "license")),
                    OwnerText.License(OwnerText.Str(OwnerText.Child(s, "license"), "license_status")), () => OpenSchool(id));
            }, "Aucune licence suivie pour l'instant.", s => OwnerText.License(OwnerText.Str(OwnerText.Child(s, "license"), "license_status")).Label));
        return stack;
    }

    private Control ServersPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Serveurs", "Appareils serveurs déclarés par les écoles"));
        var servers = OwnerText.Items(_servers, "servers");
        stack.Children.Add(SearchableList(servers, "server-search", "Rechercher un serveur",
            s => OwnerText.Str(s, "school_name") + " " + OwnerText.Str(s, "device_name") + " " + OwnerText.Str(s, "public_ip"), s =>
        {
            var recent = OwnerText.Str(s, "status") == "recent";
            return Row(OwnerText.Str(s, "device_name"),
                $"{OwnerText.Str(s, "school_name")} · dernier signal {OwnerText.Ago(OwnerText.Number(s, "last_seen"))}" +
                (OwnerText.Str(s, "public_ip") is { Length: > 0 } ip ? $" · IP {ip}" : ""),
                recent ? ("Signal récent", LicenseTone.Good) : ("Sans signal récent", LicenseTone.Warning),
                OwnerText.Str(s, "school_id") is { Length: > 0 } sid ? () => OpenSchool(sid) : null);
        }, "Aucun serveur enregistré."));
        return stack;
    }

    private Control RelaysPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Relais Internet", "Catalogue WSS de JP-Services"));
        stack.Children.Add(Text("Un seul relais est actif à la fois. Chaque école doit approuver une nouvelle révision avant de l'utiliser.",
            12, false, OwnerPalette.Muted));
        var preferred = new StackPanel { Spacing = 12 };
        preferred.Children.Add(Text("Cloudflare principal", 17, true));
        preferred.Children.Add(Text("Réutiliser le fournisseur Cloudflare ou le préparer dans le catalogue. Les établissements approuvent ensuite la nouvelle révision.", 13, color: OwnerPalette.Muted));
        preferred.Children.Add(Confirm("Utiliser Cloudflare comme relais principal", "Confirmer le choix Cloudflare",
            () => RunAsync(a => a.UseCloudflarePrimaryAsync(), Page.Relays)));
        stack.Children.Add(Box(preferred));

        var addition = new StackPanel { Spacing = 10 };
        addition.Children.Add(Text("Ajouter un relais WSS", 17, true));
        addition.Children.Add(Text("Nom du fournisseur", 12, true));
        var label = Field("relay-label", "Nom du fournisseur", hint: "Ex. Relais de secours");
        label.MaxLength = 90;
        addition.Children.Add(label);
        addition.Children.Add(Text("Adresse WSS publique", 12, true));
        var endpoint = Field("relay-endpoint", "Adresse WSS publique", hint: "wss://relais.exemple.org/");
        endpoint.MaxLength = 260;
        addition.Children.Add(endpoint);
        addition.Children.Add(Text("Le nouveau relais est créé désactivé. Aucune école ne change de fournisseur à cet ajout.", 13, color: OwnerPalette.Muted));
        addition.Children.Add(Confirm("Ajouter le relais", "Confirmer l'ajout désactivé",
            () => RunAsync(a => a.CreateRelayAsync(label.Text ?? "", endpoint.Text ?? ""), Page.Relays)));
        stack.Children.Add(Box(addition));
        var relays = OwnerText.Items(_relays, "relays");
        if (relays.Count == 0 && !_loading) stack.Children.Add(Empty("Aucun relais dans le catalogue. Préparez Cloudflare ou ajoutez un fournisseur."));
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
        stack.Children.Add(Button("Découvrir les relais TURN", () => Navigate(Page.Turn), subtle: true));
        stack.Children.Add(Button("Configurer le secours HTTPS", () => Navigate(Page.Netlify), subtle: true));
        return stack;
    }

    private Control ActivityPage()
    {
        var stack = PageContainer();
        stack.Children.Add(PageTitle("Journal d'activité", "100 dernières opérations du propriétaire"));
        var history = OwnerText.Items(_audit, "history");
        stack.Children.Add(SearchableList(history, "activity-search", "Rechercher une intervention",
            h => OwnerText.Action(OwnerText.Str(h, "action")) + " " + OwnerText.Str(h, "school_name"),
            h => Row(OwnerText.Action(OwnerText.Str(h, "action")),
                OwnerText.DateTime(OwnerText.Number(h, "created_at")) +
                (OwnerText.Str(h, "school_name") is { Length: > 0 } name ? " · " + name : "")), "Aucune opération enregistrée."));
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
        stack.Children.Add(Button("Configurer le stockage des sauvegardes", () => Navigate(Page.Storage), subtle: true));
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
            var test = Button("Tester la connexion", () => _ = TestConnectionAsync());
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
            Avalonia.Automation.AutomationProperties.SetName(id, "Identifiant du jeton de service");
            Avalonia.Automation.AutomationProperties.SetName(secret, "Secret du jeton de service");
            Avalonia.Automation.AutomationProperties.SetName(owner, "Jeton propriétaire");
            access.Children.Add(Text("Identifiant du jeton de service", 12, true));
            access.Children.Add(id);
            access.Children.Add(Text("Secret du jeton de service", 12, true));
            access.Children.Add(secret);
            access.Children.Add(Text("Jeton propriétaire", 12, true));
            access.Children.Add(owner);
            var save = Button("Enregistrer et vérifier", () => _ = SaveCredentialsAsync(
                new OwnerCredentials(id.Text ?? "", secret.Text ?? "", owner.Text ?? "")), primary: true);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            access.Children.Add(save);
        }
        stack.Children.Add(Box(access));
        stack.Children.Add(AppearancePanel());

        var updates = new StackPanel { Spacing = 10 };
        updates.Children.Add(Text("Mises à jour JP-Services", 16, true));
        updates.Children.Add(Text("Canal indépendant de SCOLIA Pro · version " + OwnerInfo.Version, 12, false, OwnerPalette.Muted));
        var check = Button("Rechercher les mises à jour", () => _ = CheckUpdatesAsync(true));
        check.HorizontalAlignment = HorizontalAlignment.Left;
        updates.Children.Add(check);
        if (_message.Parent is Panel previousMessages) previousMessages.Children.Remove(_message);
        updates.Children.Add(_message);
        stack.Children.Add(Box(updates));

        var web = new StackPanel { Spacing = 8 };
        web.Children.Add(Text("Console web", 14, true));
        web.Children.Add(Text("Retrouvez les mêmes outils d'administration dans la console web protégée.",
            12, false, OwnerPalette.Muted));
        var open = Button("Ouvrir la console web", () => _ = OpenConsoleAsync(), subtle: true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        web.Children.Add(open);
        stack.Children.Add(Box(web, OwnerPalette.BlueWash, 15));
        return stack;
    }

    /// <summary>Vérifie l'accès AVANT d'enregistrer : des identifiants refusés ne sont jamais conservés.</summary>
    private async Task OpenConsoleAsync(string? section = null)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(new Uri(
                ConsoleUrl + (section is null ? "" : "#" + section))))
                _error = "Impossible d’ouvrir le navigateur sur cet appareil.";
        }
        catch (Exception)
        {
            _error = "Ouverture du navigateur indisponible.";
        }
        ShowStatus();
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
