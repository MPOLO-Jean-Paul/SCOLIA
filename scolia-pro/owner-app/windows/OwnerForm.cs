using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace JPServices.Owner;

/// <summary>Coque Windows de la console SCOLIA. Le navigateur intégré suit Cloudflare Access + MFA.
/// Les jetons de l'administrateur ne sont ni stockés ni inclus dans l'exécutable.</summary>
public sealed class OwnerForm : Form
{
    private const string ConsoleUrl = "https://scolia-pro-owner.jeanpaulmpolo242.workers.dev/console";
    private const string ReleasesApi = "https://api.github.com/repos/MPOLO-Jean-Paul/SCOLIA/releases?per_page=50";
    private const string Channel = "scolia-owner-v";
    private const string AppVersion = "1.0.0";
    private static readonly Color Navy = Color.FromArgb(14, 27, 49);
    private static readonly Color Surface = Color.FromArgb(245, 248, 252);
    private static readonly Color Accent = Color.FromArgb(39, 102, 223);
    private static readonly Color Mute = Color.FromArgb(116, 132, 153);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true });
    private readonly WebView2 web = new();
    private readonly Label state = new();
    private readonly Label page = new();
    private bool updateChecking;
    private bool ready;

    public OwnerForm()
    {
        Text = "SCOLIA Pro — Administration JP-Services";
        Font = new Font("Segoe UI", 10f);
        BackColor = Surface;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 580);
        Size = new Size(1250, 790);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        Icon = SystemIcons.Shield;

        var app = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface,
            Margin = Padding.Empty
        };
        app.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
        app.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(app);

        var sidebar = BuildSidebar();
        app.Controls.Add(sidebar, 0, 0);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Surface,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        app.Controls.Add(main, 1, 0);

        main.Controls.Add(BuildTopbar(), 0, 0);
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 9), BackColor = Surface };
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = Color.White;
        frame.Controls.Add(web);
        main.Controls.Add(frame, 0, 1);

        state.Dock = DockStyle.Fill;
        state.ForeColor = Mute;
        state.Text = "  ● Connexion chiffrée · Cloudflare Access + MFA  |  Application JP-Services v" + AppVersion;
        state.TextAlign = ContentAlignment.MiddleLeft;
        state.Font = new Font(Font.FontFamily, 8.5f);
        main.Controls.Add(state, 0, 2);

        Shown += async (_, _) => await StartBrowserAsync();
        _ = CheckUpdatesAsync(false);
    }

    private Panel BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        var brand = new Label
        {
            Text = "S",
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            Size = new Size(47, 47),
            Location = new Point(20, 23),
            TextAlign = ContentAlignment.MiddleCenter
        };
        panel.Controls.Add(brand);
        panel.Controls.Add(new Label
        {
            Text = "JP-SERVICES",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold),
            Location = new Point(78, 24), Size = new Size(138, 23)
        });
        panel.Controls.Add(new Label
        {
            Text = "Espace propriétaire",
            ForeColor = Color.FromArgb(171, 193, 222),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(79, 48), Size = new Size(137, 20)
        });

        int y = 117;
        NavItem(panel, "▣   Tableau de bord", y, () => Navigate(ConsoleUrl)); y += 49;
        NavItem(panel, "⟳   Actualiser", y, () => { if (ready) web.Reload(); }); y += 49;
        NavItem(panel, "↓   Mises à jour", y, () => _ = CheckUpdatesAsync(true)); y += 49;
        NavItem(panel, "⊗   Verrouiller", y, LockSession); y += 49;

        var disclaimer = new Label
        {
            Text = "CANAL ADMINISTRATEUR\nAucune donnée scolaire locale.\nAccès réservé · MFA exigée.",
            ForeColor = Color.FromArgb(159, 180, 205),
            AutoSize = false, Height = 90, Dock = DockStyle.Bottom,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(22, 0, 0, 12),
            Font = new Font("Segoe UI", 8.1f)
        };
        panel.Controls.Add(disclaimer);
        return panel;
    }

    private void NavItem(Control container, string label, int y, Action callback)
    {
        var btn = new Button
        {
            Text = label,
            Location = new Point(11, y),
            Size = new Size(201, 41),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(27, 48, 78),
            Cursor = Cursors.Hand,
            Padding = new Padding(13, 0, 0, 0),
            TabStop = false
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 68, 111);
        btn.Click += (_, _) => callback();
        container.Controls.Add(btn);
    }

    private Panel BuildTopbar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        page.Text = "Console propriétaire";
        page.Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold);
        page.ForeColor = Navy;
        page.Location = new Point(19, 18);
        page.Size = new Size(310, 38);
        bar.Controls.Add(page);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 375,
            Height = 65,
            Padding = new Padding(0, 16, 10, 0),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.White
        };
        bar.Controls.Add(actions);
        AddButton(actions, "Mises à jour", () => _ = CheckUpdatesAsync(true), true);
        AddButton(actions, "Rafraîchir", () => { if(ready) web.Reload(); }, false);
        AddButton(actions, "↗ Navigateur", () => OpenExternal(ConsoleUrl), false);
        return bar;
    }

    private void AddButton(FlowLayoutPanel parent, string label, Action action, bool primary)
    {
        var button = new Button
        {
            Text = label, Width = label.Length * 7 + 31, Height = 33,
            Margin = new Padding(5, 0, 0, 0),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Navy,
            Font = new Font("Segoe UI", 9, FontStyle.Regular)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private async Task StartBrowserAsync()
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JP-Services", "SCOLIA-Owner", "WebViewProfile");
            Directory.CreateDirectory(directory);
            var environment = await CoreWebView2Environment.CreateAsync(null, directory);
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme != Uri.UriSchemeHttps)
                { e.Cancel = true; return; }

                var host = uri.Host.ToLowerInvariant();
                bool trusted = host == "scolia-pro-owner.jeanpaulmpolo242.workers.dev"
                    || host == "rapid-bird-7b6b.cloudflareaccess.com"
                    || host.EndsWith(".cloudflareaccess.com", StringComparison.Ordinal)
                    || host == "dash.cloudflare.com"
                    || host == "accounts.google.com"
                    || host == "login.microsoftonline.com";
                if (!trusted) { e.Cancel = true; OpenExternal(uri.AbsoluteUri); }
            };
            web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                state.Text = e.IsSuccess
                    ? "  ● Connexion HTTPS · Cloudflare Access et MFA  |  Administration JP-Services"
                    : "  ● La page n'a pas pu être chargée · Vérifiez la connexion";
            };
            web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    OpenExternal(uri.AbsoluteUri);
            };
            ready = true;
            Navigate(ConsoleUrl);
        }
        catch (Exception ex)
        {
            state.Text = "  ● Microsoft Edge WebView2 indisponible ou erreur réseau";
            MessageBox.Show(
                "Le moteur WebView2 est nécessaire sur Windows.\n\n" + ex.Message,
                "SCOLIA Admin - initialisation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Navigate(string url)
    {
        if (ready) web.CoreWebView2.Navigate(url);
    }

    private void LockSession()
    {
        if (!ready) return;
        if (MessageBox.Show("Effacer les cookies Cloudflare de cette application et verrouiller la session ?",
            "Verrouiller la console", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        web.CoreWebView2.CookieManager.DeleteAllCookies();
        Navigate(ConsoleUrl);
    }

    private static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* Le navigateur externe peut ne pas être installé. */ }
    }

    private static int VersionComparison(string a, string b)
    {
        return Version.TryParse(a, out var av) && Version.TryParse(b, out var bv)
            ? av.CompareTo(bv) : 0;
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (updateChecking) return;
        updateChecking = true;
        try
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("SCOLIA-JPServices-Admin/1.0");
            using var response = await Http.GetAsync(ReleasesApi);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var releases = document.RootElement.EnumerateArray()
                .Where(x => x.GetProperty("tag_name").GetString() is string tag && tag.StartsWith(Channel, StringComparison.Ordinal) &&
                    !x.GetProperty("draft").GetBoolean() && !x.GetProperty("prerelease").GetBoolean())
                .Select(x => (Version: x.GetProperty("tag_name").GetString()![Channel.Length..],
                    Url: x.GetProperty("html_url").GetString() ?? ""))
                .OrderByDescending(x => Version.TryParse(x.Version, out var ver) ? ver : new Version(0, 0))
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(releases.Version) &&
                VersionComparison(releases.Version, AppVersion) > 0)
            {
                if (MessageBox.Show(
                    "SCOLIA Admin " + releases.Version + " est disponible.\n\nCe canal est distinct des versions scolaires. Ouvrir la publication ?",
                    "Mise à jour JP-Services", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    == DialogResult.Yes) OpenExternal(releases.Url);
            }
            else if (interactive) MessageBox.Show("SCOLIA Admin " + AppVersion + " est à jour sur le canal JP-Services.",
                "Mises à jour", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            if (interactive) MessageBox.Show("Le service de mises à jour est indisponible. Réessayez avec Internet.",
                "Mises à jour", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { updateChecking = false; }
    }
}
