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
        void Pair(string label, IBrush? foreground, IBrush? background, double minimum = 4.5)
        {
            var ratio = ThemeContrast.Ratio(ColorOf(foreground), ColorOf(background));
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
        var neutralText = new TextBlock { Text = "Voir le reçu" };
        var neutral = new Button { Content = neutralText, MinHeight = 44 };
        var heroText = new TextBlock { Text = "Action sur un fond blanc" };
        var hero = new Button { Content = heroText, Background = Brushes.White, Foreground = new SolidColorBrush(Color.Parse("#0B4FC4")), MinHeight = 44 };
        var input = new TextBox { Text = "Amina Kabeya", MinHeight = 44 };
        var placeholder = new TextBox { Watermark = "Nom ou matricule", MinHeight = 44 };
        var readOnly = new TextBox { Text = "TR-2026-00001", IsReadOnly = true, MinHeight = 44 };
        var choice = new ComboBox { ItemsSource = new[] { "Espèces", "Mobile Money" }, SelectedIndex = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var table = new DataGrid { ItemsSource = new[] { new ThemeRow("SC-001", "Amina Kabeya"), new ThemeRow("SC-002", "Paul Mpolo") }, Height = 130, AutoGenerateColumns = false };
        table.Columns.Add(new DataGridTextColumn { Header = "Matricule", Binding = new Binding("Number"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        table.Columns.Add(new DataGridTextColumn { Header = "Apprenant", Binding = new Binding("Name"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        var stack = new StackPanel { Spacing = 10, Children = { ordinary, muted, error, input, placeholder, readOnly, choice, primary, neutral, danger, hero, table } };
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
            Pair("champ en lecture seule", readOnly.Foreground, readOnly.Background);
            Pair("liste de choix", choice.Foreground, choice.Background);
            Pair("bouton principal composé", primaryText.Foreground, primary.Background);
            Pair("bouton secondaire composé", neutralText.Foreground, neutral.Background);
            Pair("bouton dangereux composé", dangerText.Foreground, danger.Background);
            Pair("texte hérité sur fond blanc", heroText.Foreground, hero.Background);
            foreach (var tone in new[] { "ToneBlue", "ToneGreen", "ToneOrange", "TonePurple", "ToneRed", "ToneTeal", "ToneIndigo", "TonePink", "ToneNeutral" })
                Pair(tone, ThemeBrush.Get(tone), ThemeBrush.Get("GroupBrush"));
            foreach (var text in table.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)))
                Pair($"tableau {text.Text}", text.Foreground, table.Background);
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
