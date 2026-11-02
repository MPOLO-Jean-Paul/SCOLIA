using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scolia.Bureau.Infrastructure;

static class ThemeChecks
{
    public static int Run(Preferences prefs, bool mobile, Action<Window, string> save)
    {
        var failures = 0;
        void Check(string label, bool ok)
        {
            Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} Contraste : {label}");
            if (!ok) failures++;
        }
        void Render(Window window)
        {
            for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        }
        Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
        void Pair(string label, IBrush? foreground, IBrush? background, double minimum = 4.5, double opacity = 1)
        {
            var fg = ColorOf(foreground);
            var bg = ColorOf(background);
            var alpha = opacity * (foreground?.Opacity ?? 1) * fg.A / 255d;
            fg = Color.FromRgb((byte)(fg.R * alpha + bg.R * (1 - alpha)), (byte)(fg.G * alpha + bg.G * (1 - alpha)), (byte)(fg.B * alpha + bg.B * (1 - alpha)));
            var ratio = ThemeContrast.Ratio(fg, bg);
            Check($"{label} ({ratio:0.00}:1)", ratio >= minimum);
        }
        prefs.Animations = false;
        prefs.Theme = "light";
        prefs.Apply();
        var retained = ThemeBrush.Get("ToneIndigo");
        var oldColor = ColorOf(retained);
        var ordinary = new TextBlock { Text = "Apprenant et solde du compte" };
        var muted = new TextBlock { Text = "Informations complémentaires" };
        muted.Classes.Add("muted");
        var error = new TextBlock { Text = "Paiement en retard" };
        error.Classes.Add("error");
        var primaryText = new TextBlock { Text = "Valider le paiement" };
        var primary = new Button { Content = primaryText, MinHeight = 44 };
        primary.Classes.Add("primary");
        var dangerText = new TextBlock { Text = "Annuler le reçu" };
        var danger = new Button { Content = dangerText, MinHeight = 44 };
        danger.Classes.Add("danger");
        var sheetText = new TextBlock { Text = "Action sensible du menu mobile" };
        var sheet = new Button { Content = sheetText, MinHeight = 44 };
        sheet.Classes.AddRange(new[] { "sheetitem", "danger" });
        var countText = new TextBlock { Text = "2" };
        var count = new Border { Child = countText, Background = ThemeBrush.Get("AccentBrush") };
        count.Classes.Add("peercount");
        var neutralText = new TextBlock { Text = "Voir le reçu" };
        var neutral = new Button { Content = neutralText, MinHeight = 44 };
        var heroText = new TextBlock { Text = "Action sur un fond blanc" };
        var hero = new Button { Content = heroText, Background = Brushes.White, Foreground = new SolidColorBrush(Color.Parse("#0B4FC4")), MinHeight = 44 };
        var input = new TextBox { Text = "Amina Kabeya", MinHeight = 44 };
        var placeholder = new TextBox { PlaceholderText = "Nom ou matricule", MinHeight = 44 };
        var readOnly = new TextBox { Text = "TR-2026-00001", IsReadOnly = true, MinHeight = 44 };
        var choice = new ComboBox { ItemsSource = new[] { "Espèces", "Mobile Money" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var table = new DataGrid { ItemsSource = new[] { new ThemeRow("SC-001", "Amina Kabeya"), new ThemeRow("SC-002", "Paul Mpolo") }, Height = 130, IsReadOnly = true, AutoGenerateColumns = false };
        table.Columns.Add(new DataGridTextColumn { Header = "Matricule", Binding = new Binding("Number"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Apprenant", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        var stack = new StackPanel { Spacing = 10, Children = { ordinary, muted, error, input, placeholder, readOnly, choice, primary, neutral, danger, sheet, count, hero, table } };
        var card = new Border { Child = stack, Padding = new Thickness(16) };
        card.Classes.Add("card");
        var probe = new Window { Width = mobile ? 360 : 960, Height = mobile ? 900 : 960, Content = new ScrollViewer { Content = card }, Title = "Contraste SCOLIA" };
        probe.Show();
        foreach (var accent in new[] { "bleu", "indigo", "violet", "vert", "orange", "graphite" })
        {
            prefs.Theme = "dark";
            prefs.Accent = accent;
            prefs.Apply();
            Render(probe);
            Check("les pinceaux des lignes existantes suivent le thème", ReferenceEquals(retained, ThemeBrush.Get("ToneIndigo")) && ColorOf(retained) != oldColor);
            Pair("texte principal", ordinary.Foreground, ThemeBrush.Get("GroupBrush"));
            Pair("texte secondaire", muted.Foreground, ThemeBrush.Get("GroupBrush"));
            Pair("message d'erreur", error.Foreground, ThemeBrush.Get("GroupBrush"));
            Pair("saisie", input.Foreground, input.Background);
            var hint = placeholder.GetVisualDescendants().OfType<TextBlock>().First(text => text.Name == "PART_Placeholder");
            Pair("indication du champ vide", hint.Foreground, placeholder.Background, opacity: hint.Opacity);
            Pair("sélection du texte saisi", input.SelectionForegroundBrush, input.SelectionBrush);
            Pair("champ en lecture seule", readOnly.Foreground, readOnly.Background);
            Pair("liste de choix", choice.Foreground, choice.Background);
            Pair("bouton principal composé", primaryText.Foreground, primary.Background);
            Pair("bouton secondaire composé", neutralText.Foreground, neutral.Background);
            Pair("bouton dangereux composé", dangerText.Foreground, danger.Background);
            Pair("action sensible sur fond neutre", sheetText.Foreground, ThemeBrush.Get("GroupBrush"));
            Pair("nombre de postes connectés", countText.Foreground, count.Background);
            Pair("texte hérité sur fond blanc", heroText.Foreground, hero.Background);
            foreach (var tone in new[] { "ToneBlue", "ToneGreen", "ToneOrange", "TonePurple", "ToneRed", "ToneTeal", "ToneIndigo", "TonePink", "ToneNeutral" })
                Pair(tone, ThemeBrush.Get(tone), ThemeBrush.Get("GroupBrush"));
            foreach (var text in table.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)))
                // Sur téléphone, le tableau est transparent et les cartes reposent sur le bloc du formulaire.
                Pair($"tableau {text.Text}", text.Foreground, ThemeBrush.Get("GroupBrush"));
            if (primary.TranslatePoint(new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), probe) is { } point)
            {
                probe.MouseMove(point);
                Render(probe);
                var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().First(c => c.Name == "PART_ContentPresenter");
                Pair("bouton principal au survol", primaryText.Foreground, presenter.Background);
            }
            if (accent is "bleu" or "vert") save(probe, $"contraste-controles-{(mobile ? "mobile" : "bureau")}-{accent}.png");
            prefs.Theme = "light";
            prefs.Apply();
            Render(probe);
            Pair("retour au thème clair", ordinary.Foreground, ThemeBrush.Get("GroupBrush"));
            Pair("retour au thème clair sur fond blanc", heroText.Foreground, hero.Background);
        }
        probe.Close();
        prefs.Accent = "bleu";
        prefs.Theme = "dark";
        prefs.Apply();
        return failures;
    }

    private sealed record ThemeRow(string Number, string Name);
}
