using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;
using Scolia.Bureau.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

internal static class FluentIconCapture
{
    public static void Run(string output)
    {
        FluentGlyphs.Install();
        var checks = 0;
        void Check(string label, bool ok)
        {
            if (!ok) throw new InvalidOperationException(label);
            checks++;
        }

        foreach (var name in new[] { "Home", "Building", "Certificate", "PhoneLaptop", "Globe", "History", "Cloud", "ShieldCheckmark", "AppsList" })
            Check($"Ressource partagée avec la console administrateur : {name}", Application.Current!.Resources.TryGetResource(
                "FluentArtwork" + name, Application.Current.ActualThemeVariant, out var resource) && resource is IImage { Size.Width: > 0, Size.Height: > 0 });

        // Compare actual pixels, including non-square containers: shapes must not be stretched or cropped.
        foreach (var kind in new[] { MaterialIconKind.ArrowLeft, MaterialIconKind.ArrowRight, MaterialIconKind.ChevronLeft,
                     MaterialIconKind.LanConnect, MaterialIconKind.LanDisconnect, MaterialIconKind.Wifi })
        foreach (var size in new[] { new Size(16, 16), new Size(20, 20), new Size(24, 24), new Size(32, 32),
                     new Size(48, 48), new Size(32, 16), new Size(16, 32) })
        {
            var icon = new FluentIcon { Kind = kind, Foreground = Brushes.Black };
            icon.Measure(size);
            icon.Arrange(new Rect(size));
            var pixels = Render(icon, (int)size.Width, (int)size.Height);
            var ink = Enumerable.Range(0, pixels.Length / 4).Where(i => pixels[i * 4 + 3] > 40).ToArray();
            Check($"{kind} {size}: pixels visibles", ink.Length > 4);
            Check($"{kind} {size}: aucun bord coupé", ink.All(i => i % (int)size.Width > 0 && i % (int)size.Width < size.Width - 1
                && i / (int)size.Width > 0 && i / (int)size.Width < size.Height - 1));
            var minX = ink.Min(i => i % (int)size.Width);
            var maxX = ink.Max(i => i % (int)size.Width);
            var minY = ink.Min(i => i / (int)size.Width);
            var maxY = ink.Max(i => i / (int)size.Width);
            var bounds = FluentGlyphs.For(kind).Bounds;
            var scale = Math.Min(size.Width, size.Height) / 24;
            Check($"{kind} {size}: proportions originales", Math.Abs(maxX - minX + 1 - bounds.Width * scale) < 2
                && Math.Abs(maxY - minY + 1 - bounds.Height * scale) < 2);
        }
        var left = RenderGlyph(MaterialIconKind.ArrowLeft);
        var right = RenderGlyph(MaterialIconKind.ArrowRight);
        var difference = 0;
        for (var y = 0; y < 48; y++)
        for (var x = 0; x < 48; x++)
            if (Math.Abs(left[(y * 48 + x) * 4 + 3] - right[(y * 48 + 47 - x) * 4 + 3]) > 30) difference++;
        Check("Retour/suivant : flèches symétriques au rendu", difference < 48);

        foreach (var dark in new[] { false, true })
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var ink = new SolidColorBrush(Color.Parse(dark ? "#EFF5FF" : "#12233F"));
            var background = new SolidColorBrush(Color.Parse(dark ? "#111A28" : "#F4F7FC"));
            var surface = new SolidColorBrush(Color.Parse(dark ? "#1C293B" : "#FFFFFF"));
            var grid = new UniformGrid { Columns = 4 };
            var outlines = new List<(string Label, DrawingImage Image)>();
            foreach (var (page, kind) in FeatureVisuals.PageIcons)
            {
                var drawing = FluentGlyphs.ArtworkFor(kind);
                if (FluentGlyphs.ArtworkKey(kind)!.StartsWith("FluentArtworkGlyph", StringComparison.Ordinal))
                    outlines.Add((page.Name, (DrawingImage)drawing!));
                Check($"{page.Name} : ressource native présente", Application.Current.Resources.TryGetResource(
                    FluentGlyphs.ArtworkKey(kind)!, Application.Current.ActualThemeVariant, out var resource) && ReferenceEquals(drawing, resource));
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                row.Children.Add(new Image { Source = drawing, Width = 40, Height = 40 });
                foreach (var size in new[] { 16, 20, 24 })
                    row.Children.Add(new MaterialIcon { Kind = kind, Width = size, Height = size, Foreground = ink, VerticalAlignment = VerticalAlignment.Center });
                var panel = new StackPanel { Spacing = 10 };
                panel.Children.Add(new TextBlock { Text = Label(page), FontSize = 13, FontWeight = FontWeight.Medium, Foreground = ink });
                panel.Children.Add(row);
                grid.Children.Add(new Border { Child = panel, Background = surface, Margin = new Thickness(5),
                    Padding = new Thickness(14), CornerRadius = new CornerRadius(10) });
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var kind in new[] { MaterialIconKind.ArrowLeft, MaterialIconKind.ArrowRight, MaterialIconKind.LanConnect, MaterialIconKind.LanDisconnect, MaterialIconKind.Wifi })
                buttons.Children.Add(new Button { Content = new MaterialIcon { Kind = kind, Width = 20, Height = 20 }, Width = 40, Height = 36 });
            buttons.Children.Add(new PathIcon { Data = AuthGlyphs.Back, Width = 20, Height = 20, Foreground = ink });
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = "SCOLIA · Repères des fonctionnalités", Foreground = ink, FontSize = 25, FontWeight = FontWeight.SemiBold });
            content.Children.Add(new TextBlock { Text = "Bleu et vert du logo · Illustration et tracés à 16, 20 et 24 points", Foreground = ink, Opacity = .7 });
            content.Children.Add(buttons);
            content.Children.Add(grid);
            var window = new Window { Width = 1100, Height = 1630, Background = background,
                Content = new Border { Padding = new Thickness(22), Child = content } };
            window.Show();
            window.Width = 1100;
            window.Height = 1630;
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            foreach (var (label, image) in outlines)
            {
                var shape = ((DrawingGroup)image.Drawing!).Children.OfType<GeometryDrawing>().Last();
                Check($"{label} : contraste du pictogramme en thème {(dark ? "sombre" : "clair")}",
                    shape.Brush is ISolidColorBrush brush && ThemeContrast.Ratio(brush.Color, surface.Color) >= 3);
            }
            foreach (var control in window.GetVisualDescendants().OfType<MaterialIcon>())
                Check($"{control.Kind} : grille fixe utilisée", control.GetVisualDescendants().OfType<FluentIcon>().Any());
            foreach (var control in window.GetVisualDescendants().OfType<PathIcon>())
                Check("PathIcon : grille fixe utilisée", control.GetVisualDescendants().OfType<FluentIcon>().Any());
            using var bitmap = new RenderTargetBitmap(new PixelSize(1100, 1630));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(output, dark ? "icones-scolia-sombre.png" : "icones-scolia-clair.png"));
            window.Close();
        }
        Console.WriteLine($"OK {checks} vérifications des icônes : grille, proportions, symétrie, ressources et thèmes.");
    }

    private static byte[] RenderGlyph(MaterialIconKind kind)
    {
        var icon = new FluentIcon { Kind = kind, Foreground = Brushes.Black };
        icon.Measure(new Size(48, 48));
        icon.Arrange(new Rect(0, 0, 48, 48));
        return Render(icon, 48, 48);
    }

    private static byte[] Render(Control icon, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(icon);
        var data = new byte[width * height * 4];
        var pointer = Marshal.AllocHGlobal(data.Length);
        try { bitmap.CopyPixels(new PixelRect(0, 0, width, height), pointer, data.Length, width * 4); Marshal.Copy(pointer, data, 0, data.Length); }
        finally { Marshal.FreeHGlobal(pointer); }
        return data;
    }

    private static string Label(Type page) => page.Name switch
    {
        nameof(HomePage) => "Accueil", nameof(StartPage) => "Tous les outils", nameof(PromoterDashboardPage) => "Pilotage",
        nameof(DashboardPage) => "Tableau de bord", nameof(ReportsPage) => "Rapports et exports", nameof(StudentsPage) => "Dossiers des élèves",
        nameof(StudentAccountsPage) => "Accès étudiants", nameof(ReEnrollmentPage) => "Réinscriptions", nameof(CardsPage) => "Cartes scolaires",
        nameof(CashierPage) => "Encaissement", nameof(BatchPaymentPage) => "Encaissement par lot", nameof(ReceiptsPage) => "Journal des reçus",
        nameof(MonthlyPage) => "Suivi mensuel", nameof(DebtorsPage) => "Recouvrement", nameof(CashPage) => "Caisse",
        nameof(ExpensesPage) => "Dépenses", nameof(SponsorshipsPage) => "Prises en charge", nameof(GradesPage) => "Cours et notes",
        nameof(ResultsPage) => "Résultats", nameof(StudentResultsPage) => "Mes résultats", nameof(EpstResultsPage) => "Résultats et bulletins",
        nameof(EpstDeliberationPage) => "Délibération scolaire", nameof(NationalExamsPage) => "Examens nationaux", nameof(LmdMarksPage) => "Notes des EC",
        nameof(LmdResultsPage) => "Délibération LMD", nameof(LmdDebtsPage) => "Dettes académiques", nameof(AttendancePage) => "Présences",
        nameof(DisciplinePage) => "Discipline", nameof(SchoolCyclesPage) => "Cycles scolaires", nameof(LmdPlanningPage) => "Parcours LMD",
        nameof(RegulationPage) => "Règlement pédagogique", nameof(CurriculumAssignmentPage) => "Attribution des branches", nameof(TeachingPage) => "Attribution des cours",
        nameof(TeacherAccountsPage) => "Accès enseignants", nameof(TeacherHomePage) => "Espace enseignant", nameof(TeacherCurriculumPage) => "Mes branches et notes",
        nameof(DeliberationSettingsPage) => "Paramètres de délibération", nameof(AnonymousCodesPage) => "Codes anonymes", nameof(MarksPage) => "Encodage des cotes",
        nameof(EncodingProgressPage) => "Progression de l'encodage", nameof(DeliberationPage) => "Délibération", nameof(StaffPage) => "Personnel et paie",
        nameof(StaffAttendancePage) => "Présences du personnel", nameof(NetworkHubPage) => "Réseau et postes", nameof(SettingsPage) => "Configuration",
        nameof(UsersPage) => "Utilisateurs", nameof(DataPage) => "Données", nameof(CloudBackupPage) => "Sauvegardes cloud",
        nameof(AuditPage) => "Journal d'audit", nameof(EraseDataPage) => "Effacement des données", _ => page.Name
    };
}
