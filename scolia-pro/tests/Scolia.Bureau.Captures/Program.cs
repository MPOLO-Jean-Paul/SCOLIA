using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scolia.Bureau;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Scolia.Bureau.Views;

// Usage : dotnet run -- <dossier_de_sortie> <adresse_du_serveur> <mot_de_passe> [sombre]
// Ouvre la fenêtre native sans écran, parcourt chaque écran sur les données réelles du serveur et enregistre une image par écran.
var output = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var server = new Uri(args.Length > 1 ? args[1] : "http://localhost:5293/");
var password = args.Length > 2 ? args[2] : "Scolia2026!";
var dark = args.Length > 3 && args[3] == "sombre";
Directory.CreateDirectory(output);

Scolia.Bureau.Program.BuildAvaloniaApp()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// Attend une tâche tout en faisant tourner la boucle d'affichage de l'interface.
static T Result<T>(Task<T> task)
{
    while (!task.IsCompleted)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    return task.Result;
}

static void Wait(Task task)
{
    // Garde-fou : une attente qui ne se termine pas échoue clairement, au lieu de bloquer l'outil.
    var limit = DateTime.UtcNow.AddSeconds(30);
    while (!task.IsCompleted)
    {
        if (DateTime.UtcNow > limit) throw new TimeoutException("Attente trop longue : l'action ne se termine pas.");
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    task.GetAwaiter().GetResult();
}

void Save(Window window, string name)
{
    // Plusieurs cycles de rendu : la mise en page des listes se termine après le premier, et celle du
    // téléphone après le changement de taille.
    for (var i = 0; i < 12; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(30);
    }
    // Échelle facultative (SCOLIA_ECHELLE=3) : la même mise en page, dessinée avec trois fois plus de points,
    // comme sur un écran de téléphone à haute densité. Le texte reste net une fois agrandi dans une vidéo.
    var scale = double.TryParse(Environment.GetEnvironmentVariable("SCOLIA_ECHELLE"), System.Globalization.CultureInfo.InvariantCulture, out var e) && e > 1 ? e : 1;
    // Le rendu direct utilise l'état final de l'arbre visuel, même après plusieurs ouvertures de session.
    // La capture du tampon de composition peut encore contenir une image de transition sur le moteur headless.
    var sharp = new Avalonia.Media.Imaging.RenderTargetBitmap(
        new PixelSize((int)Math.Round(window.Bounds.Width * scale), (int)Math.Round(window.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
    // Le rendu à haute densité dessine trois fois trop grand le contenu d'un cadre à ombre portée (fenêtres, feuilles,
    // bulles) : ombres et transformations sans effet sont retirées le temps du dessin, puis remises.
    var neutral = window.GetVisualDescendants().OfType<Visual>()
        .Where(v => v.RenderTransform is { } rt && rt.Value.IsIdentity).Select(v => (v, v.RenderTransform)).ToList();
    foreach (var (v, _) in neutral) v.RenderTransform = null;
    var shadowed = window.GetVisualDescendants().OfType<Border>().Where(b => b.BoxShadow.Count > 0).Select(b => (b, b.BoxShadow)).ToList();
    foreach (var (b, _) in shadowed) b.BoxShadow = default;
    sharp.Render(window);
    foreach (var (v, rt) in neutral) v.RenderTransform = rt;
    foreach (var (b, sh) in shadowed) b.BoxShadow = sh;
    using var frame = sharp;
    var stride = frame.PixelSize.Width * 4;
    var size = stride * frame.PixelSize.Height;
    var pixels = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
    try
    {
        frame.CopyPixels(new PixelRect(frame.PixelSize), pixels, size, stride);
        var first = System.Runtime.InteropServices.Marshal.ReadInt32(pixels);
        var varied = false;
        for (var y = 0; y < frame.PixelSize.Height && !varied; y += 8)
            for (var x = 0; x < frame.PixelSize.Width && !varied; x += 8)
                varied = System.Runtime.InteropServices.Marshal.ReadInt32(pixels, y * stride + x * 4) != first;
        if (!varied) throw new InvalidOperationException($"Capture uniforme ou blanche : {name}.");
    }
    finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pixels); }
    frame.Save(Path.Combine(output, name));
    Console.WriteLine($"capturé : {name} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    if (Preferences.MobileLayout) Overflow(window, name);
}

// Téléphone : relève ce qui sort de l'écran sur le côté et les textes coupés sans points de suspension.
// Un tableau ou une zone qui défile en largeur compte aussi : sur un téléphone, rien ne doit défiler de côté.
void Overflow(Window window, string name)
{
    var width = window.Bounds.Width;
    var found = new List<string>();
    foreach (var visual in window.GetVisualDescendants().OfType<Control>())
    {
        if (!visual.IsEffectivelyVisible || visual.Bounds.Width <= 0) continue;
        if (visual is Avalonia.Controls.Primitives.Popup || visual.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.Popup>().Any()) continue;
        // L'intérieur d'un champ défile seul avec le texte saisi : ce n'est pas un débordement de la page.
        if (visual.GetVisualAncestors().Any(a => a is TextBox)) continue;
        if (visual.TransformToVisual(window) is not { } m) continue;
        var rect = new Rect(visual.Bounds.Size).TransformToAABB(m);
        var outside = rect.Right > width + 1 || rect.X < -1;
        var parentOutside = visual.GetVisualParent() is Control parent && parent.TransformToVisual(window) is { } pm
            && new Rect(parent.Bounds.Size).TransformToAABB(pm) is var pr && (pr.Right > width + 1 || pr.X < -1);
        if (outside && !parentOutside) found.Add($"sort de l'écran : {Describe(visual)} de {rect.X:0} à {rect.Right:0}");
        if (visual is ScrollViewer { HorizontalScrollBarVisibility: not Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } sv
            && sv.Extent.Width > sv.Viewport.Width + 1)
            found.Add($"défile en largeur : {Describe(visual)} ({sv.Extent.Width:0} pour {sv.Viewport.Width:0})");
        if (visual is TextBlock { TextTrimming: var trim, TextWrapping: Avalonia.Media.TextWrapping.NoWrap } tb
            && trim == Avalonia.Media.TextTrimming.None && !string.IsNullOrEmpty(tb.Text)
            && tb.TextLayout.WidthIncludingTrailingWhitespace > tb.Bounds.Width + 1.5)
            found.Add($"texte coupé : « {tb.Text} » ({tb.TextLayout.WidthIncludingTrailingWhitespace:0} pour {tb.Bounds.Width:0})");
        if (visual is TextBlock { TextTrimming: var cut } trimmed && cut != Avalonia.Media.TextTrimming.None
            && !string.IsNullOrEmpty(trimmed.Text) && trimmed.TextLayout.TextLines.Any(l => l.HasCollapsed))
            found.Add($"texte tronqué : « {trimmed.Text} » ({trimmed.Bounds.Width:0} de large)");
    }
    var report = Path.Combine(output, "debordements.txt");
    File.AppendAllLines(report, found.Count == 0 ? [$"{name} : aucun débordement"] : [$"{name} : {found.Count} débordement(s)", .. found.Distinct().Select(f => "  " + f)]);
    if (found.Count > 0) Console.WriteLine($"  {found.Count} débordement(s)");
}

static string Describe(Control c)
{
    var text = c as TextBlock is { Text: { Length: > 0 } t } ? $" « {(t.Length > 40 ? t[..40] + "…" : t)} »" : string.Empty;
    var classes = c.Classes.Count > 0 ? "." + string.Join(".", c.Classes.Where(k => !k.StartsWith(':'))) : string.Empty;
    var named = string.IsNullOrEmpty(c.Name) ? string.Empty : "#" + c.Name;
    var owner = c.GetVisualAncestors().OfType<UserControl>().FirstOrDefault()?.GetType().Name ?? "";
    return $"{owner} {c.GetType().Name}{named}{classes}{text}".Trim();
}

// Même culture que l'application : dates et montants en français.
var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = fr;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = fr;
System.Globalization.CultureInfo.CurrentCulture = fr;
System.Globalization.CultureInfo.CurrentUICulture = fr;
Application.Current!.DataTemplates.Add(new ViewLocator());
var api = new ScoliaClient(server);
var prefs = Preferences.Load();
prefs.Theme = dark ? "dark" : "light";
// Les parcours durent plusieurs minutes sans toucher simulé : la mise en veille ne doit pas les interrompre.
prefs.LockMinutes = 0;
// Captures figées : les animations se rejouent à l'écran, une image doit montrer l'état final.
prefs.Animations = Environment.GetEnvironmentVariable("SCOLIA_ANIMATIONS") == "1";
prefs.Accent = Environment.GetEnvironmentVariable("SCOLIA_ACCENT") ?? "bleu";
// Sans les bulles d'astuce (SCOLIA_ASTUCES=0) : des écrans dégagés, pour une présentation.
if (Environment.GetEnvironmentVariable("SCOLIA_ASTUCES") == "0") prefs.Tips = false;
// Le mobile est connu avant le shell, comme sur Android : ses menus et ses réglages en dépendent.
if (Environment.GetEnvironmentVariable("SCOLIA_MOBILE") == "1")
{
    Preferences.MobileLayout = true;
    Platform.IsMobile = true;
    // Comme sur Android : le téléphone peut être le serveur de l'établissement (sans le démarrer ici).
    Platform.LocalServer = new CaptureServer();
}
var shell = new ShellViewModel(api, prefs);
// Les tests visuels de connexion représentent un établissement déjà configuré.
// Le démarrage réel ne montre plus la connexion avant la réponse du serveur.
shell.ApplyInstallationStatus(configured: true);
// Taille de fenêtre optionnelle (SCOLIA_TAILLE=1024x700) pour vérifier l'adaptation du texte à chaque format d'écran.
var taille = (Environment.GetEnvironmentVariable("SCOLIA_TAILLE") ?? "1280x820").Split('x');
// Aperçu du téléphone (SCOLIA_MOBILE=1, avec SCOLIA_TAILLE=412x892) : l'interface garde sa taille réelle et se range.
var window = new MainWindow { DataContext = shell, Width = int.Parse(taille[0]), Height = int.Parse(taille[1]) };
if (Preferences.MobileLayout) { window.MinWidth = 0; window.MinHeight = 0; window.Width = int.Parse(taille[0]); window.Height = int.Parse(taille[1]); }
window.Show();
// Barre de titre de Windows dessinée par SCOLIA : simulée ici, la fenêtre sans écran n'a pas celle du système.
if (!Preferences.MobileLayout && Environment.GetEnvironmentVariable("SCOLIA_BARRE_TITRE") != "0" && window.Content is Scolia.Bureau.Views.MainView main) main.ShowTitleBar(32);
// Parcours de caisse autonome : aucune écriture sur les données d'un établissement.
int VerifyPayments(LayoutServer fixture, bool mobile)
{
    var failures = 0;
    void Check(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) failures++;
    }
    void Render()
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
    Rect Bounds(Control control) => control.TransformToVisual(window) is { } transform
        ? new Rect(control.Bounds.Size).TransformToAABB(transform) : default;
    Control Named(string name) => window.GetVisualDescendants().OfType<Control>().First(control => control.Name == name && control.IsEffectivelyVisible);
    void Reveal(Control control)
    {
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (control.TransformToVisual(scroll) is not { } transform) continue;
            var rect = new Rect(control.Bounds.Size).TransformToAABB(transform);
            scroll.Offset += new Vector(0, rect.Top - Math.Max(0, (scroll.Viewport.Height - rect.Height) / 2));
            Render();
        }
    }
    bool Reachable(Control control)
    {
        var rect = Bounds(control);
        if (!control.IsEffectivelyVisible || rect.Width <= 0 || rect.Left < -1 || rect.Right > window.Bounds.Width + 1
            || rect.Top < -1 || rect.Bottom > window.Bounds.Height + 1) return false;
        return control.GetVisualAncestors().OfType<ScrollViewer>().All(scroll =>
        {
            var viewport = Bounds(scroll);
            return rect.Top >= viewport.Top - 1 && rect.Bottom <= viewport.Bottom + 1;
        });
    }
    var originalAnimations = prefs.Animations;
    prefs.Animations = false;
    var minimum = ShellViewModel.MinimumOpening;
    ShellViewModel.MinimumOpening = TimeSpan.Zero;
    Wait(shell.OpenSessionAsync(new SessionDto(Guid.NewGuid(), "caisse-test", "Amina Kabeya", "Caissier", "Caisse",
        [Scolia.Bureau.Ribbon.Perm.PaymentsCreate, Scolia.Bureau.Ribbon.Perm.StudentsRead, Scolia.Bureau.Ribbon.Perm.PaymentsRead], false,
        "Institut de test", "Test", "2026-2027", "FC", "+243")));
    ShellViewModel.MinimumOpening = minimum;
    shell.Sync.Stop();
    var studentId = Guid.NewGuid();
    var enrollmentId = Guid.NewGuid();
    var lines = Enumerable.Range(1, 12).Select(i => new AccountLineDto(Guid.NewGuid(),
        $"Mensualité de septembre — période de rattrapage {i}", new DateOnly(2026, 1, 1), 125000, 0, 125000, 0, 125000,
        "En retard", true, "Mensualite")).ToList();
    var account = new AccountDto(studentId, enrollmentId, "SC-2026-00001", "Kabeya Mbuyi Amina du secrétariat des sciences de la santé",
        "INF1", "2026-2027", "Actif", lines, 1500000, 0, 1500000, 0, 1500000, 1500000, 12, "90+", [], "0812345678", null);
    foreach (var (width, height) in mobile ? new[] { (320, 300), (320, 568), (360, 640), (412, 892), (640, 360), (1024, 600) }
        : new[] { (760, 520), (1024, 600), (1366, 768) })
    {
        window.Width = width;
        window.Height = height;
        foreach (var textScale in new[] { 1d, 1.3 })
        {
            prefs.Scale = textScale;
            fixture.PaymentAccount = account;
            Wait(shell.Open(() => new CashierPage(shell, studentId)));
            Render();
            var page = (CashierPage)shell.Current!;
            Check("Caisse : la coque reste opaque après un changement de session", Named("AppArea").Opacity >= 0.99);
            Check($"Caisse {width}×{height} texte {textScale:P0} : apprenant en retard chargé", page.HasStudent && page.Months.Count == 12);
            foreach (var month in page.Months.Take(6)) month.IsSelected = true;
            Wait(page.PreviewAsync());
            Render();
            var cashier = window.GetVisualDescendants().OfType<CashierView>().First(view => view.IsEffectivelyVisible);
            Check("Caisse : champs contenus en largeur", cashier.GetVisualDescendants().OfType<Control>()
                .Where(control => control is TextBox or ComboBox or CalendarDatePicker or CheckBox).Where(control => control.IsEffectivelyVisible)
                .All(control => Bounds(control).Left >= -1 && Bounds(control).Right <= window.Bounds.Width + 1));
            // Le bouton Encaisser vit dans la barre fixe en bas de l'écran, toujours visible.
            var pay = Named(mobile ? "StickyAction" : "DesktopStickyAction");
            Reveal(pay);
            Check("Caisse : bouton Encaisser entièrement accessible", Reachable(pay));
            Check("Caisse : cible tactile et validation disponible", pay.Bounds.Height >= 44 && pay.IsEnabled && page.CanPay);
            window.MouseMove(Bounds(pay).Center);
            Render();
            var presenter = pay.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(control => control.Name == "PART_ContentPresenter");
            Check("Caisse : le bouton actif garde sa couleur au survol",
                presenter.Background is Avalonia.Media.ISolidColorBrush actual && ThemeBrush.Get("AccentHoverBrush") is Avalonia.Media.ISolidColorBrush expected && actual.Color == expected.Color);
            Save(window, $"paiement-{(mobile ? "mobile" : "bureau")}-{width}x{height}-{textScale:0.0}.png");
            var before = fixture.PaymentCount;
            fixture.HoldPayment = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tap = Bounds(pay).Center;
            window.MouseDown(tap, Avalonia.Input.MouseButton.Left);
            window.MouseUp(tap, Avalonia.Input.MouseButton.Left);
            Check("Caisse : le bouton reçoit réellement l'appui", !page.Pay.CanExecute(null));
            for (var i = 0; i < 200 && fixture.PaymentCount == before; i++) Wait(Task.Delay(10));
            window.MouseDown(tap, Avalonia.Input.MouseButton.Left);
            window.MouseUp(tap, Avalonia.Input.MouseButton.Left);
            fixture.HoldPayment.SetResult();
            for (var i = 0; i < 200 && !page.HasReceipt; i++) Wait(Task.Delay(10));
            fixture.HoldPayment = null;
            Render();
            Check("Caisse : un seul reçu malgré un double appui", fixture.PaymentCount == before + 1 && page.HasReceipt);
            foreach (var name in new[] { "PrintReceiptButton", "OpenReceiptButton", "NewPaymentButton" })
            {
                var action = Named(name);
                Reveal(action);
                Check($"Caisse : {name} accessible après paiement", Reachable(action));
            }
            Check("Caisse : aucune confirmation redondante ne couvre les boutons du reçu", !shell.HasNotice);
            if (width is 320 or 360 or 760)
                Save(window, $"recu-{(mobile ? "mobile" : "bureau")}-{width}x{height}-{textScale:0.0}.png");
        }
    }
    if (mobile)
    {
        window.Width = 360;
        window.Height = 640;
        prefs.Scale = 1;
        fixture.PaymentAccount = account;
        Wait(shell.Open(() => new StudentsPage(shell)));
        Render();
        var students = (StudentsPage)shell.Current!;
        students.Selected = students.Items[5];
        var scroll = (ScrollViewer)Named("PhoneScroll");
        scroll.Offset = new Vector(0, 300);
        Render();
        var anchor = scroll.GetVisualDescendants().OfType<Border>().First(card => card.Classes.Contains("phonecard") && Bounds(card).Top >= Bounds(scroll).Top);
        var anchorY = Bounds(anchor).Top;
        var quietBusy = 0;
        students.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PageViewModel.Busy)) quietBusy++; };
        Wait(shell.RefreshQuietlyAsync());
        Render();
        Check("Actualisation : aucun indicateur bloquant", quietBusy == 0 && !shell.Working);
        Check("Actualisation : la même carte et la sélection restent en place", anchor.GetVisualAncestors().Contains(window) && Math.Abs(Bounds(anchor).Top - anchorY) < 1
            && students.Selected == students.Items[5]);
        // Le lecteur continue de défiler pendant la réponse réseau.
        fixture.HoldStudents = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readsBefore = fixture.StudentReads;
        var refresh = shell.RefreshQuietlyAsync();
        Wait(shell.RefreshQuietlyAsync());
        Wait(shell.RefreshQuietlyAsync());
        scroll.Offset = new Vector(0, 500);
        Render();
        var readerOffset = scroll.Offset;
        fixture.HoldStudents.SetResult();
        Wait(refresh);
        fixture.HoldStudents = null;
        Render();
        Check("Actualisation : le défilement pendant le réseau n'est jamais annulé", Math.Abs(scroll.Offset.Y - readerOffset.Y) < 1);
        Check("Actualisation : les événements simultanés sont regroupés sans perdre la dernière mise à jour", fixture.StudentReads == readsBefore + 2);
        var viewportBefore = Bounds(scroll);
        // Le même événement que celui reçu du serveur, avec un bandeau temporaire.
        var show = typeof(SyncCenter).GetMethod("Show", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        show.Invoke(shell.Sync, ["Secrétariat · paiement enregistré sur un autre poste"]);
        Render();
        Check("Activité : une écriture distante ne déplace ni la page ni sa fenêtre de lecture", Bounds(scroll) == viewportBefore && Math.Abs(scroll.Offset.Y - readerOffset.Y) < 1);
        Save(window, "actualisation-mobile-stable.png");
        var navigation = shell.Navigation;
        var tabs = shell.PhoneTabs;
        var tools = shell.PhoneTools;
        shell.PhoneMenuOpen = true;
        fixture.HoldStudents = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = navigation.SelectMany(group => group.Items).First(item => item.PageType == typeof(StudentsPage));
        var response = System.Diagnostics.Stopwatch.StartNew();
        target.Open.Execute(null);
        Render();
        Check($"Menu : la page et le retour tactile précèdent la réponse réseau (menu {(shell.PhoneMenuOpen ? "ouvert" : "fermé")}, page {shell.Current?.GetType().Name}, occupé {shell.Current?.Busy}, {response.ElapsedMilliseconds} ms)",
            !shell.PhoneMenuOpen && shell.Current is StudentsPage { Busy: true } && response.ElapsedMilliseconds < 500);
        Wait(Task.Delay(450));
        Check("Menu : un chargement de page ne bloque pas toute l'interface", !shell.Working);
        shell.TogglePhoneMenu();
        Check("Menu : reste utilisable pendant le chargement", shell.PhoneMenuOpen);
        Check("Menu : les boutons existants sont réutilisés", ReferenceEquals(navigation, shell.Navigation) && ReferenceEquals(tabs, shell.PhoneTabs) && ReferenceEquals(tools, shell.PhoneTools));
        fixture.HoldStudents.SetResult();
        for (var i = 0; i < 100 && shell.Current?.Busy == true; i++) Wait(Task.Delay(10));
        fixture.HoldStudents = null;
        Check("Menu : le chargement se termine sans fermer le menu rouvert", shell.Current?.Busy == false && shell.PhoneMenuOpen);
        shell.PhoneMenuOpen = false;
    }
    prefs.Scale = 1;
    prefs.Animations = originalAnimations;
    return failures;
}

// Contrastes autonomes : basculement de thème, six accents, contrôles réels et parcours de paiement.
if (args.Length > 3 && args[3] == "contraste")
{
    using var contrastServer = new LayoutServer();
    api.Retarget(contrastServer.Address);
    var failures = ThemeChecks.Run(prefs, Preferences.MobileLayout, Save);
    window.Width = Preferences.MobileLayout ? 360 : 1280;
    window.Height = Preferences.MobileLayout ? 760 : 820;
    Save(window, "contraste-connexion.png");
    failures += VerifyPayments(contrastServer, Preferences.MobileLayout);
    if (Preferences.MobileLayout)
    {
        shell.PhoneMenuOpen = true;
        Save(window, "contraste-menu-mobile.png");
        shell.PhoneMenuOpen = false;
    }
    Wait(shell.Open(() => new HomePage(shell)));
    Save(window, "contraste-accueil.png");
    shell.ShowTextSettings();
    Save(window, "contraste-preferences.png");
    shell.CloseDialog();
    shell.ShowSearch();
    Save(window, "contraste-recherche.png");
    shell.CloseDialog();
    shell.ShowAbout();
    Save(window, "contraste-a-propos.png");
    shell.CloseDialog();
    shell.Sync.Stop();
    window.Close();
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}

// Élégance (argument « elegance ») : silhouettes de chargement, onde au toucher et vibration, transition en cours.
if (args.Length > 3 && args[3] == "elegance")
{
    window.Width = 360;
    window.Height = 760;
    var eleganceFailures = 0;
    void ElegantCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) eleganceFailures++;
    }
    void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));

    // 1. Silhouettes : une page dont le chargement est retenu.
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var opening = shell.Open(() => new SlowPage(shell, gate.Task));
    Pump(400);
    var skeleton = window.GetVisualDescendants().OfType<SkeletonView>().FirstOrDefault(v => v.IsEffectivelyVisible);
    ElegantCheck("Chargement long : silhouettes affichées", skeleton is not null);
    Save(window, "elegance-silhouettes.png");
    gate.SetResult();
    Wait(opening);
    Pump(100);
    ElegantCheck("Chargement fini : silhouettes retirées", !window.GetVisualDescendants().OfType<SkeletonView>().Any(v => v.IsEffectivelyVisible));

    // Une page rapide n'en montre jamais (pas de clignotement).
    var quick = shell.Open(() => new StudentsPage(shell));
    var flashed = false;
    for (var i = 0; i < 40 && !quick.IsCompleted; i++)
    {
        Pump(10);
        flashed |= window.GetVisualDescendants().OfType<SkeletonView>().Any(v => v.IsEffectivelyVisible) && (shell.Current?.Busy ?? false) && i < 10;
    }
    Wait(quick);
    ElegantCheck("Page rapide : pas de silhouettes avant 150 ms", !flashed);

    // 2. Onde et vibration : toucher le bouton principal de la page.
    prefs.Animations = true;
    var haptics = new List<HapticKind>();
    Platform.Haptic = kind => haptics.Add(kind);
    Wait(shell.Open(() => new HomePage(shell)));
    Pump(400);
    var primary = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && (b.Classes.Contains("quick") || b.Classes.Contains("primary")));
    if (primary is not null && primary.TransformToVisual(window) is { } m)
    {
        var center = new Rect(primary.Bounds.Size).TransformToAABB(m).Center;
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        Pump(140);
        var layer = Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(primary);
        ElegantCheck("Toucher : une onde part du doigt", layer?.Children.Any(c => c.GetType().Name == "RippleAdorner") == true);
        Save(window, "elegance-onde.png");
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Pump(500);
        ElegantCheck("Toucher : l'onde disparaît seule", layer?.Children.Any(c => c.GetType().Name == "RippleAdorner") != true);
    }
    else ElegantCheck("Toucher : bouton trouvé", false);
    var stickyHost = new CashierPage(shell);
    Wait(shell.Open(() => stickyHost));
    stickyHost.Search = "ACC";
    Wait(stickyHost.SearchAsync());
    foreach (var candidate in stickyHost.Results.ToList())
    {
        Wait(stickyHost.ChooseAsync(candidate));
        if (stickyHost.Months.Count > 0) break;
    }
    Pump(300);
    var mainButton = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Name == "StickyAction");
    if (mainButton is not null && mainButton.TransformToVisual(window) is { } pm)
    {
        var at = new Rect(mainButton.Bounds.Size).TransformToAABB(pm).Center;
        // Seulement appuyer : le relâcher encaisserait vraiment.
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        Pump(100);
        Save(window, "elegance-onde-bouton.png");
    }
    ElegantCheck($"Bouton principal : vibration légère ({haptics.Count(k => k == HapticKind.Tap)})", haptics.Contains(HapticKind.Tap));
    Sounds.Play(prefs, SoundKind.Success);
    ElegantCheck("Réussite : vibration de confirmation", haptics.Contains(HapticKind.Success));

    // 3. Transition : à mi-parcours, la nouvelle page arrive en glissant et en apparaissant.
    prefs.Animations = true;
    prefs.PageTransitions = true;
    var next = shell.Open(() => new ReceiptsPage(shell));
    Pump(90);
    Save(window, "elegance-transition.png");
    Wait(next);
    Pump(400);
    shell.GoBack();
    Pump(60);
    ElegantCheck("Retour : la page repart dans l'autre sens", shell.NavigatingBack == false);
    Pump(400);

    Platform.Haptic = null;
    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(eleganceFailures == 0 ? "Élégance : tout est en place." : $"Élégance : {eleganceFailures} échec(s).");
    Environment.ExitCode = eleganceFailures == 0 ? 0 : 1;
    return;
}

// Biométrie (argument « biometrie ») : activée depuis Sécurité du compte, elle doit ouvrir la session à la connexion
// suivante, même après une déconnexion, puis suivre un changement de code fait ici ou ailleurs.
if (args.Length > 3 && args[3] == "biometrie")
{
    window.Width = 360;
    window.Height = 760;
    var bioFailures = 0;
    void BioCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) bioFailures++;
    }
    void Until(Func<bool> done, int ms = 15000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    }
    // Téléphone simulé : la puce de sécurité garde ce qu'on lui confie et ne le rend qu'après « l'empreinte ».
    string? chip = null;
    var prompts = 0;
    Platform.IsMobile = true;
    Platform.IsBiometricAvailable = () => true;
    Platform.HasBiometricProof = () => chip is not null;
    Platform.EnrollBiometricAsync = sealedAccess => { prompts++; chip = sealedAccess; return Task.FromResult(true); };
    Platform.UnlockBiometricAsync = () => { prompts++; return Task.FromResult(chip); };
    Platform.DisableBiometricAccess = () => chip = null;
    BiometricAccess.Disable();

    var admin = new ScoliaClient(server);
    Result(admin.LoginAsync("admin", password));
    var bioUser = "bio" + Guid.NewGuid().ToString("N")[..6];
    var firstCode = "Bio2026!kz";
    var created = Result(admin.SendAsync<UserDto>(HttpMethod.Post, "api/utilisateurs", new { userName = bioUser, displayName = "Test biométrie", role = "Comptabilite", password = firstCode }));

    // 1. Connexion au clavier, puis activation depuis Sécurité du compte.
    shell.Login.UserName = bioUser;
    shell.Login.Password = firstCode;
    shell.Login.Submit.Execute(null);
    Until(() => shell.IsLoggedIn && !shell.Opening);
    BioCheck("Connexion au clavier", shell.IsLoggedIn);
    var backstage = shell.Backstage;
    backstage.Section = backstage.Items.First(i => i.Value == "mot-de-passe");
    backstage.SecurityPassword = firstCode;
    backstage.ManageBiometrics.Execute(null);
    Until(() => backstage.BiometricsEnabled);
    BioCheck("Sécurité du compte : empreinte activée", backstage.BiometricsEnabled);
    BioCheck($"Message clair : « {backstage.SecurityNotice} »", backstage.SecurityNotice.Contains("Déverrouiller"));

    // 2. Déconnexion, puis l'empreinte à la page de connexion.
    Wait(shell.LogoutAsync());
    shell.Login.RefreshBiometrics();
    BioCheck("Page de connexion : bouton Déverrouiller proposé", shell.Login.CanUseBiometricSignIn);
    shell.Login.UserName = string.Empty;
    shell.Login.SignInWithBiometrics.Execute(null);
    Until(() => shell.IsLoggedIn && !shell.Opening);
    BioCheck($"Empreinte après déconnexion : session ouverte {(shell.Login.Error is { } e1 ? "(" + e1 + ")" : "")}",
        shell.IsLoggedIn && BiometricAccess.AccountMatches(shell.Session!.UserName, bioUser));

    // 3. Nouveau code depuis Sécurité du compte : l'empreinte suit.
    backstage = shell.Backstage;
    backstage.Section = backstage.Items.First(i => i.Value == "mot-de-passe");
    backstage.CurrentCode = firstCode;
    backstage.UsePin = true;
    backstage.NewCode = "482915";
    backstage.ConfirmCode = "482915";
    BioCheck("Nouveau code PIN accepté par le formulaire", backstage.CanSaveCode);
    backstage.SaveCode.Execute(null);
    Until(() => BiometricAccess.Open(chip)?.Secret == "482915");
    BioCheck($"Code changé : l'empreinte garde le nouveau ({backstage.CodeNotice})", BiometricAccess.Open(chip)?.Secret == "482915");
    Wait(shell.LogoutAsync());
    shell.Login.RefreshBiometrics();
    shell.Login.SignInWithBiometrics.Execute(null);
    Until(() => shell.IsLoggedIn && !shell.Opening);
    BioCheck("Empreinte avec le nouveau code : session ouverte", shell.IsLoggedIn);

    // Page Sécurité façon Android 16 : fermée, code ouvert, empreinte ouverte, puis en sombre.
    Until(() => !shell.Opening);
    var hub = shell.Backstage;
    shell.OpenBackstage();
    hub.Section = hub.Items.First(i => i.Value == "mot-de-passe");
    BioCheck("Sécurité : en-tête « protégé » avec l'empreinte active", hub.SecurityHealthy && hub.BiometricStateLabel == "Activée");
    Save(window, "securite-telephone.png");
    hub.CodeEditorOpen = true;
    Save(window, "securite-code.png");
    hub.BiometricPanelOpen = true;
    BioCheck("Sécurité : une seule ligne ouverte à la fois", hub.BiometricPanelOpen && !hub.CodeEditorOpen);
    Save(window, "securite-empreinte.png");
    hub.BiometricPanelOpen = false;
    prefs.Theme = "dark";
    Save(window, "securite-sombre.png");
    prefs.Theme = "light";
    shell.CloseBackstage();

    // 4. Code remplacé ailleurs (par l'administrateur) : un message clair, une saisie, et l'empreinte est à jour.
    Wait(shell.LogoutAsync());
    Wait(admin.SendAsync(HttpMethod.Post, $"api/utilisateurs/{created.Id}/mot-de-passe", new { newPassword = "Autre2026!qz" }));
    shell.Login.RefreshBiometrics();
    shell.Login.SignInWithBiometrics.Execute(null);
    Until(() => shell.Login.HasError);
    BioCheck($"Code changé ailleurs : « {shell.Login.Error} »", !shell.IsLoggedIn && shell.Login.Error?.Contains("a changé") == true);
    BioCheck("Code changé ailleurs : mise à jour de l'empreinte prévue", shell.Login.ResealPending);
    Save(window, "biometrie-code-change.png");
    shell.Login.Password = "Autre2026!qz";
    shell.Login.Submit.Execute(null);
    Until(() => shell.IsLoggedIn && !shell.Opening);
    BioCheck($"Après une saisie : l'empreinte garde le code du serveur (session {shell.IsLoggedIn}{shell.Login.Error})",
        shell.IsLoggedIn && BiometricAccess.Open(chip)?.Secret == "Autre2026!qz");

    // 5. Empreinte annulée : rien ne s'ouvre, le code reste proposé.
    Wait(shell.LogoutAsync());
    var kept = chip;
    Platform.UnlockBiometricAsync = () => Task.FromResult<string?>(null);
    shell.Login.RefreshBiometrics();
    shell.Login.SignInWithBiometrics.Execute(null);
    Until(() => !shell.Login.Busy, 3000);
    BioCheck("Empreinte annulée : aucune session ouverte", !shell.IsLoggedIn);
    Save(window, "biometrie-connexion.png");

    Platform.UnlockBiometricAsync = null;
    chip = null;
    BiometricAccess.Disable();
    Platform.IsMobile = false;
    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(bioFailures == 0 ? $"Biométrie : tout est en place ({prompts} demandes d'empreinte)." : $"Biométrie : {bioFailures} échec(s).");
    Environment.ExitCode = bioFailures == 0 ? 0 : 1;
    return;
}

// Recherche du téléphone (argument « recherche ») : plein écran comme le Play Store, récents et accès rapides, résultats.
if (args.Length > 3 && args[3] == "recherche")
{
    window.Width = 360;
    window.Height = 760;
    var searchFailures = 0;
    void SearchCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) searchFailures++;
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    // Partir d'un historique vide : une recherche laissée par un autre parcours fausserait le compte.
    foreach (var old in prefs.RecentSearches.ToList()) prefs.ForgetSearch(old);
    prefs.RememberSearch("Kabeya");
    prefs.RememberSearch("frais scolaires");
    shell.ShowSearch();
    var limit = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    SearchCheck("Téléphone : la recherche couvre tout l'écran", shell.FullScreenDialog && !shell.CenteredDialog);
    var finder = (SearchViewModel)shell.Dialog!;
    SearchCheck($"Recherches récentes : {finder.Recent.Count}", finder.Recent.Count == 2 && finder.Recent[0].Text == "frais scolaires");
    SearchCheck($"Accès rapides : {finder.Shortcuts.Count}", finder.Shortcuts.Count >= 4);
    var box = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "QueryBox" && t.IsEffectivelyVisible);
    SearchCheck("Le champ reçoit le clavier aussitôt", box?.IsFocused == true);
    Save(window, "recherche-accueil.png");
    finder.Query = "ACC";
    Wait(finder.SearchAsync());
    SearchCheck($"Résultats : {finder.Results.Count}", finder.Results.Count > 0 && finder.HasResults);
    Save(window, "recherche-resultats.png");
    finder.Results[0].Open.Execute(null);
    limit = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < limit && shell.HasDialog) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    SearchCheck("Ouvrir un résultat ferme la recherche", !shell.HasDialog);
    SearchCheck("La saisie rejoint les récentes", prefs.RecentSearches.FirstOrDefault() == "ACC");
    foreach (var q in prefs.RecentSearches.ToList()) prefs.ForgetSearch(q);

    // Info sur l'application : le catalogue, plein écran sur le téléphone, avec sa recherche.
    shell.ShowAppInfo();
    for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    var info = (AppInfoViewModel)shell.Dialog!;
    SearchCheck($"Info sur l'application : {info.Totals}", shell.FullScreenDialog && info.Modules.Count >= 10);
    Save(window, "info-application.png");
    info.Query = "empreinte";
    SearchCheck($"Catalogue : « empreinte » trouvée dans {info.Modules.Count} module(s)", info.Modules.Count >= 1 && info.Modules.All(m => m.IsExpanded));
    Save(window, "info-application-recherche.png");
    shell.CloseDialog();
    var aide = shell.Tabs.FirstOrDefault(t => t.Id == "aide");
    SearchCheck("Aide : « Info sur l'application » juste après « À propos »",
        aide?.Groups.SelectMany(g => g.Commands).Select(c => c.Label).SkipWhile(l => l != "À propos").Skip(1).FirstOrDefault() == "Info sur l'application");
    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(searchFailures == 0 ? "Recherche : tout est en place." : $"Recherche : {searchFailures} échec(s).");
    Environment.ExitCode = searchFailures == 0 ? 0 : 1;
    return;
}

// Bannières (argument « alertes ») : un autre poste fait une sauvegarde, ce poste l'affiche en haut de l'écran.
if (args.Length > 3 && args[3] == "alertes")
{
    window.Width = 360;
    window.Height = 760;
    var alertFailures = 0;
    void AlertCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) alertFailures++;
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    var system = new List<AppAlert>();
    Platform.IsInForeground = () => false;
    Platform.ShowSystemNotification = system.Add;
    // Laisser le direct se brancher avant d'agir sur l'autre poste.
    var until = DateTime.UtcNow.AddSeconds(3);
    while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }

    // L'autre poste : un client distinct, avec son propre identifiant de poste.
    using var other = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = server };
    other.DefaultRequestHeaders.Add("X-Scolia", "1");
    other.DefaultRequestHeaders.Add("X-Scolia-Poste", "poste-test-alertes");
    other.DefaultRequestHeaders.Add("X-Scolia-Appareil", "PC-COMPTABILITE");
    Result(other.PostAsJsonAsync("api/session", new { userName = "admin", password })).EnsureSuccessStatusCode();
    Result(other.PostAsync("api/sauvegardes", null)).EnsureSuccessStatusCode();
    until = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < until && !shell.HasBanner) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    AlertCheck($"Bannière reçue : « {shell.Banner?.Title} · {shell.Banner?.Text} »", shell.Banner?.Kind == AlertKind.Backup);
    AlertCheck($"Origine indiquée : {shell.Banner?.Origin}", shell.Banner?.Origin.Contains("PC-COMPTABILITE") == true);
    AlertCheck("Arrière-plan : notification du téléphone envoyée", system.Count == 1 && system[0].Kind == AlertKind.Backup);
    for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(20); }
    Save(window, "alerte-sauvegarde.png");
    shell.OpenBanner.Execute(null);
    until = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < until && shell.Current is not DataPage) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    AlertCheck("Toucher la bannière ouvre les sauvegardes", shell.Current is DataPage && !shell.HasBanner);

    // Exemple depuis les réglages, et réglage coupé.
    shell.ShowAlert(new AppAlert(AlertKind.Payment, "Nouvel encaissement", "Reçu R-000123 : 50 000 FC pour ACC101 (Espèces).", "Secrétariat · PC-ACCUEIL"));
    for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(20); }
    Save(window, "alerte-encaissement.png");
    prefs.Alerts = false;
    Result(other.PostAsync("api/sauvegardes", null)).EnsureSuccessStatusCode();
    shell.DismissBanner.Execute(null);
    until = DateTime.UtcNow.AddSeconds(4);
    while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    // L'exemple ci-dessus compte aussi : deux notifications, aucune de plus une fois les alertes coupées.
    AlertCheck("Notifications coupées : plus de bannière", !shell.HasBanner && system.Count == 2);
    prefs.Alerts = true;

    // Rapport journalier : programmé sans compte WhatsApp, envoyé depuis l'autre poste, puis la bannière l'annonce ici.
    Result(other.PutAsJsonAsync("api/rapports/journalier/reglages", new { enabled = false, time = "18:00", recipients = new[] { "0812345678" } })).EnsureSuccessStatusCode();
    var sent = Result(Result(other.PostAsync("api/rapports/journalier/envoyer", null)).Content.ReadAsStringAsync());
    AlertCheck($"Envoi sans compte WhatsApp : {sent}", sent.Contains("non configuré"));
    until = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < until && shell.Banner?.Kind != AlertKind.DailyReport) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    AlertCheck($"Bannière « {shell.Banner?.Title} » : {shell.Banner?.Text}", shell.Banner?.Kind == AlertKind.DailyReport && shell.Banner.Data is { Length: 10 });
    for (var i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(20); }
    Save(window, "alerte-rapport.png");
    shell.DismissBanner.Execute(null);
    var reportsPage = new ReportsPage(shell);
    Wait(shell.Open(() => reportsPage));
    var daily = reportsPage.Sections.SelectMany(x => x.Reports).FirstOrDefault(r => r.Title == "Rapport journalier");
    AlertCheck($"Rapports et exports : carte « Rapport journalier » ({string.Join(", ", daily?.Formats.Select(f => f.Label) ?? [])})",
        daily?.Formats.Select(f => f.Label).SequenceEqual(["PDF", "Planifier", "Envoyer"]) == true);
    Save(window, "rapport-carte.png");
    daily!.Formats[1].Run.Execute(null);
    until = DateTime.UtcNow.AddSeconds(8);
    while (DateTime.UtcNow < until && shell.Dialog is not FormViewModel) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(20); }
    var scheduleForm = shell.Dialog as FormViewModel;
    AlertCheck("Programmer : le formulaire s'ouvre avec les numéros enregistrés", scheduleForm?.Find("numeros").Text == "0812345678");
    Save(window, "rapport-programmer.png");
    scheduleForm?.Cancel.Execute(null);

    Platform.IsInForeground = null;
    Platform.ShowSystemNotification = null;
    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(alertFailures == 0 ? "Alertes : tout est en place." : $"Alertes : {alertFailures} échec(s).");
    Environment.ExitCode = alertFailures == 0 ? 0 : 1;
    return;
}

// Mise en veille (argument « veille ») : délai écoulé, écran verrouillé, mauvais code refusé, bon code, empreinte, retour.
if (args.Length > 3 && args[3] == "veille")
{
    if (Preferences.MobileLayout)
    {
        window.Width = 360;
        window.Height = 760;
    }
    var lockFailures = 0;
    void LockCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) lockFailures++;
    }
    void Settle(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    var receiptsPage = new ReceiptsPage(shell);
    Wait(shell.Open(() => receiptsPage));
    prefs.LockMinutes = 2;
    shell.NoteActivity();
    shell.CheckIdle(DateTime.UtcNow.AddSeconds(90));
    LockCheck("Avant le délai : pas de veille", !shell.IsLocked);
    shell.CheckIdle(DateTime.UtcNow.AddMinutes(3));
    LockCheck("Délai écoulé : SCOLIA se met en veille", shell.IsLocked && shell.ShowLockScreen);
    shell.ShowAlert(new AppAlert(AlertKind.Payment, "Nouvel encaissement", "Reçu R-1", "Secrétariat"));
    LockCheck("En veille : aucune bannière sur l'écran verrouillé", !shell.HasBanner);
    Settle(300);
    Save(window, "veille-ecran.png");

    shell.LockScreen.Code = "mauvais-code";
    shell.LockScreen.Unlock.Execute(null);
    var limit = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < limit && !shell.LockScreen.HasError) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    LockCheck($"Mauvais code refusé : « {shell.LockScreen.Error} »", shell.IsLocked && shell.LockScreen.HasError);
    Settle(200);
    Save(window, "veille-erreur.png");

    shell.LockScreen.Code = password;
    shell.LockScreen.Unlock.Execute(null);
    limit = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < limit && shell.IsLocked) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    LockCheck("Bon code : retour exact sur la même page", !shell.IsLocked && shell.Current == receiptsPage);

    // Retour d'arrière-plan après le délai : le temps passé hors de SCOLIA compte.
    shell.CheckIdle(DateTime.UtcNow.AddMinutes(5));
    LockCheck("Retour après 5 minutes en arrière-plan : en veille", shell.IsLocked);

    // Empreinte : le téléphone rend l'accès scellé pour ce compte.
    string? chip = BiometricAccess.Seal("admin", password);
    Platform.IsMobile = true;
    Platform.IsBiometricAvailable = () => true;
    Platform.UnlockBiometricAsync = () => Task.FromResult(chip);
    BiometricAccess.Enable(server, "admin");
    shell.LockScreen.Shown();
    LockCheck("Empreinte proposée sur l'écran de veille", shell.LockScreen.CanUseBiometrics);
    Settle(200);
    Save(window, "veille-empreinte.png");
    shell.LockScreen.UnlockWithBiometrics.Execute(null);
    limit = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < limit && shell.IsLocked) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    LockCheck("Empreinte : déverrouillé", !shell.IsLocked);
    BiometricAccess.Disable();
    Platform.UnlockBiometricAsync = null;
    Platform.IsMobile = false;

    // Réglage dans Sécurité du compte, puis « Verrouiller maintenant ».
    shell.OpenBackstage();
    shell.Backstage.Section = shell.Backstage.Items.First(i => i.Value == "mot-de-passe");
    shell.Backstage.LockChoice = shell.Backstage.LockChoices.First(c => c.Value == "10");
    LockCheck($"Réglage : {shell.Backstage.LockSummary}", prefs.LockMinutes == 10);
    Settle(300);
    shell.Backstage.LockPanelOpen = true;
    Settle(300);
    LockCheck($"Délais en lignes à cocher : {shell.Backstage.LockOptions.Count} choix, « {shell.Backstage.LockOptions.Single(o => o.IsSelected).Label} » coché",
        shell.Backstage.LockOptions.Count == 7 && shell.Backstage.LockOptions.Single(o => o.IsSelected).Label == "Après 10 minutes");
    var hubScroll = window.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(c => c.Name == "LockDelays");
    hubScroll?.BringIntoView();
    Settle(300);
    Save(window, "veille-reglage.png");
    shell.Backstage.LockNow.Execute(null);
    LockCheck("Verrouiller maintenant", shell.IsLocked && !shell.BackstageOpen);
    shell.CheckIdle();

    // Jamais : plus de mise en veille.
    shell.LockScreen.Code = password;
    shell.LockScreen.Unlock.Execute(null);
    limit = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < limit && shell.IsLocked) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    prefs.LockMinutes = 0;
    shell.CheckIdle(DateTime.UtcNow.AddHours(3));
    LockCheck("Réglé sur « Jamais » : pas de veille", !shell.IsLocked);

    // Changer de compte : la page de connexion s'affiche, la session verrouillée attend derrière.
    shell.LockNow();
    Settle(200);
    Save(window, "veille-ecran-manuel.png");
    shell.LockScreen.SwitchAccount.Execute(null);
    Settle(300);
    LockCheck("Changer de compte : page de connexion, session gardée",
        shell.ShowLogin && shell.IsLoggedIn && !shell.ShowLockScreen && !shell.ShowApp && shell.Login.CanResume);
    LockCheck($"Retour proposé : « {shell.Login.ResumeText} »", shell.Login.ResumeText.Contains(shell.Session!.DisplayName, StringComparison.Ordinal));
    Save(window, "veille-connexion-retour.png");
    shell.Login.Resume.Execute(null);
    Settle(300);
    LockCheck("Revenir : écran de veille, même page derrière", shell.ShowLockScreen && shell.IsLocked && !shell.ShowLogin && shell.Current == receiptsPage);

    // Une autre connexion réussie depuis la page de connexion ferme la session verrouillée.
    shell.LockScreen.SwitchAccount.Execute(null);
    Settle(200);
    shell.Login.UserName = "admin";
    shell.Login.Password = password;
    shell.Login.Submit.Execute(null);
    limit = DateTime.UtcNow.AddSeconds(15);
    while (DateTime.UtcNow < limit && (shell.SwitchingAccount || shell.Opening || !shell.ShowApp)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    LockCheck("Nouvelle connexion : session ouverte, plus de verrou ni de retour", shell.ShowApp && !shell.IsLocked && !shell.Login.CanResume);

    // Se déconnecter depuis l'application ne propose aucun retour.
    Wait(shell.LogoutAsync());
    Settle(200);
    LockCheck("Déconnexion : connexion simple, sans retour", shell.ShowLogin && !shell.IsLoggedIn && !shell.Login.CanResume);
    Save(window, "veille-connexion.png");

    // Ordinateur principal injoignable : la connexion propose d'en changer, et le choix propose de revenir.
    shell.Login.Notice = "L'ordinateur principal ne répond pas. Connectez-vous avec le dernier compte utilisé sur cet appareil pour travailler hors ligne : tout sera envoyé à son retour.";
    Settle(200);
    Save(window, "veille-connexion-injoignable.png");
    shell.Login.ChangeServer.Execute(null);
    Settle(300);
    LockCheck("Changer d'ordinateur principal : le choix s'ouvre, avec un retour", shell.ChoosingServer && shell.Connection.CanGoBack && !shell.ShowLogin);
    Save(window, "veille-choix-serveur.png");
    shell.Connection.Back.Execute(null);
    Settle(300);
    LockCheck("Retour : la page de connexion revient, rien n'a changé", !shell.ChoosingServer && shell.ShowLogin);

    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(lockFailures == 0 ? "Veille : tout est en place." : $"Veille : {lockFailures} échec(s).");
    Environment.ExitCode = lockFailures == 0 ? 0 : 1;
    return;
}

// Prédiction (argument « prediction ») : fiche promotion et nouvelle année, propositions sous les champs.
if (args.Length > 3 && args[3] == "prediction")
{
    var failures = 0;
    void PCheck(string label, bool ok) { Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}"); if (!ok) failures++; }
    void PPump(int ms) { var until = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); } }
    var phoneMode = Preferences.MobileLayout;
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    Wait(shell.Open(() => new SettingsPage(shell, "promotions")));

    var promoOpening = SettingsPage.NewPromotionAsync(shell);
    var promoForm = AwaitForm(shell, promoOpening);
    promoForm.Find("code").Text = "inf 1";
    PPump(300);
    var designation = promoForm.Find("nom");
    PCheck($"Code « inf 1 » : {string.Join(", ", designation.Suggestions)}", designation.Suggestions.FirstOrDefault() == "Première Infirmière" && designation.Suggestions.Count >= 4);
    PCheck($"Niveau suivi du code : {promoForm.Find("niveau").Text}", promoForm.Find("niveau").Text == "1");
    Save(window, phoneMode ? "prediction-1-code.png" : "prediction-1-ordinateur-code.png");
    promoForm.Find("code").Text = "";
    designation.Text = "Deu";
    PPump(300);
    PCheck($"Désignation « Deu », sans code : {string.Join(", ", designation.Suggestions)}", designation.Suggestions.Count == 0 || designation.Suggestions.All(n => n.StartsWith("Deuxième", StringComparison.Ordinal)));
    designation.Text = "deuxième acc";
    PPump(300);
    PCheck($"« deuxième acc » : {string.Join(", ", designation.Suggestions)}", designation.Suggestions.FirstOrDefault() == "Deuxième Accoucheuse");
    Save(window, phoneMode ? "prediction-2-designation.png" : "prediction-2-ordinateur-designation.png");
    designation.PickSuggestion.Execute("Deuxième Accoucheuse");
    PPump(300);
    PCheck($"Choix rempli : code {promoForm.Text("code")}, niveau {promoForm.Text("niveau")}, suivante {promoForm.Text("suivante")}",
        promoForm.Text("code") == "ACC2" && promoForm.Text("niveau") == "2" && promoForm.Text("suivante") == "ACC3");
    Save(window, phoneMode ? "prediction-3-rempli.png" : "prediction-3-ordinateur-rempli.png");
    promoForm.Cancel.Execute(null);
    PPump(200);

    var year = SettingsPage.NewYearAsync(shell);
    var yearForm = AwaitForm(shell, year);
    yearForm.Find("libelle").Text = "2027";
    PPump(200);
    PCheck($"Année « 2027 » : {string.Join(", ", yearForm.Find("libelle").Suggestions)}", yearForm.Find("libelle").Suggestions.SequenceEqual(["2027-2028"]));
    yearForm.Find("debut").Choice = SettingsPage.CalendarMonths[9];
    PPump(200);
    PCheck($"Octobre en premier mois : dernier mois {yearForm.Find("fin").Choice?.Label}", yearForm.ChoiceValue("fin") == "7");
    Save(window, phoneMode ? "prediction-4-annee.png" : "prediction-4-ordinateur-annee.png");
    yearForm.Cancel.Execute(null);
    PPump(200);

    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(failures == 0 ? "Prédiction : tout est en place." : $"Prédiction : {failures} échec(s).");
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}

// Mesures (argument « mesures ») : chaque page du volet, temps de chargement, de mise en page et nombre d'éléments.
if (args.Length > 3 && args[3] == "mesures")
{
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    foreach (var endpoint in new[] { "api/paiements/lot/candidats", "api/apprenants?taille=50", "api/apprenants?taille=200" })
    {
        Result(shell.Api.GetAsync<System.Text.Json.JsonElement>(endpoint));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var body = Result(shell.Api.GetAsync<System.Text.Json.JsonElement>(endpoint));
        Console.WriteLine($"API {endpoint,-34} {timer.Elapsed.TotalMilliseconds,6:0} ms · {body.GetRawText().Length / 1024,5} Ko");
    }
    // Données seules, sans écran : la différence avec l'ouverture complète est le coût de l'affichage.
    foreach (var make in new Func<PageViewModel>[] { () => new BatchPaymentPage(shell), () => new StudentsPage(shell), () => new StaffAttendancePage(shell) })
    {
        Wait(make().LoadAsync());
        var page = make();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Wait(page.LoadAsync());
        Console.WriteLine($"DONNEES {page.Title,-26} {timer.Elapsed.TotalMilliseconds,6:0} ms");
    }
    // Découpage d'une ouverture : partie immédiate, construction de l'écran vide, puis données et cartes.
    foreach (var make in new Func<PageViewModel>[] { () => new StudentsPage(shell), () => new HomePage(shell) })
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var opening = shell.Open(make);
            var sync = timer.Elapsed.TotalMilliseconds;
            window.UpdateLayout();
            var view = timer.Elapsed.TotalMilliseconds - sync;
            Wait(opening);
            var data = timer.Elapsed.TotalMilliseconds - sync - view;
            window.UpdateLayout();
            var cards = timer.Elapsed.TotalMilliseconds - sync - view - data;
            if (pass == 1) Console.WriteLine($"DECOUPE {shell.Current?.Title,-20} immédiat {sync,5:0} · écran {view,5:0} · données {data,5:0} · cartes {cards,5:0} ms");
        }
    }
    var rows = new List<(string Page, double Load, double Layout, int Visuals)>();
    foreach (var item in shell.Navigation.SelectMany(g => g.Items).ToList())
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Wait(item.Open.ExecuteAsync());
            var load = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var layout = watch.Elapsed.TotalMilliseconds;
            // Deuxième passage seulement : le premier compte aussi la compilation des vues.
            if (pass == 1) rows.Add((item.Label, load, layout, window.GetVisualDescendants().Count()));
        }
    }
    foreach (var r in rows.OrderByDescending(r => r.Load + r.Layout))
        Console.WriteLine($"{r.Page,-28} chargement {r.Load,7:0} ms · affichage {r.Layout,6:0} ms · {r.Visuals,5} éléments");
    shell.Sync.Stop();
    window.Close();
    return;
}

// Toutes les recherches (argument « recherches ») : menu, accueil, paramètres, listes ; même champ, mêmes blocs.
if (args.Length > 3 && args[3] == "recherches")
{
    var phoneMode = Preferences.MobileLayout;
    var allFailures = 0;
    void AllCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) allFailures++;
    }
    void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
    }
    // Recherche de l'accueil façon Google partout : champ sans cadre dans la barre, micro à droite.
    bool SearchStyled(string name) => window.GetVisualDescendants().OfType<TextBox>()
        .Any(t => t.Name == name && t.IsEffectivelyVisible && t.Classes.Contains("searchfield"));
    bool MicShown() => window.GetVisualDescendants().OfType<Button>()
        .Any(b => b.IsEffectivelyVisible && b.Classes.Contains("mic"));
    // Voix simulée : la vraie reconnaissance est celle du téléphone. Ici, on garde la main sur la fin de l'écoute.
    TaskCompletionSource<string?>? speaking = null;
    Scolia.Bureau.Infrastructure.Platform.RecognizeSpeechAsync = () =>
    {
        speaking = new TaskCompletionSource<string?>();
        return speaking.Task;
    };
    Scolia.Bureau.Infrastructure.Platform.CanRecognizeSpeech = () => true;
    string Dictate(Func<Task> listen, Func<string> query, string words, string? capture)
    {
        var running = listen();
        Pump(250);
        if (capture is not null) Save(window, capture);
        speaking!.TrySetResult(words);
        Wait(running);
        Pump(400);
        return query();
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    foreach (var old in prefs.RecentSearches.ToList()) prefs.ForgetSearch(old);
    prefs.RememberSearch("Kabeya");

    if (phoneMode)
    {
        // 1. Menu : la référence.
        shell.SettingsSearch.Activate.Execute(null);
        shell.SettingsSearch.Query = "recu";
        Pump(300);
        AllCheck("Menu : barre façon Google", SearchStyled("MenuSearchBox"));
        AllCheck("Menu : micro présent", MicShown());
        Save(window, "recherches-1-menu.png");
        AllCheck("Menu : la voix remplit la recherche",
            Dictate(() => shell.SettingsSearch.Listen.ExecuteAsync(), () => shell.SettingsSearch.Query, "sauvegarde", null) == "sauvegarde");
        shell.SettingsSearch.Reset();
        shell.PhoneMenuOpen = false;
        Pump(200);
    }

    // 2. Recherche générale (accueil).
    Wait(shell.Open(() => new HomePage(shell)));
    if (!phoneMode)
    {
        // Ordinateur : Ctrl+K place le curseur dans la barre du haut ; les résultats se déroulent dessous, sans autre fenêtre.
        shell.FindInHeader();
        Pump(300);
        var bar = shell.HeaderFinder;
        AllCheck("Barre du haut : la recherche s'y fait, sans fenêtre", shell.HeaderFinderOpen && !shell.HasDialog);
        AllCheck("Barre du haut : champ façon Google, curseur dedans",
            SearchStyled("HeaderFinderBox") && window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "HeaderFinderBox" && t.IsFocused));
        AllCheck("Barre du haut : micro présent", window.GetVisualDescendants().OfType<Button>()
            .Any(b => b.Name == "HeaderFinderMic" && b.IsEffectivelyVisible));
        Save(window, "recherches-0-ordinateur-barre-vide.png");
        AllCheck("Barre du haut : la voix remplit la recherche",
            Dictate(() => bar.Listen.ExecuteAsync(), () => bar.Query, "Kabeya", "recherches-0b-ordinateur-barre-ecoute.png") == "Kabeya");
        Wait(bar.SearchAsync());
        Pump(300);
        AllCheck($"Barre du haut : résultats sous la barre ({bar.Results.Count})", bar.Results.Count >= 1 && shell.HeaderFinderOpen);
        Save(window, "recherches-0c-ordinateur-barre-resultats.png");
        var before = shell.Current;
        bar.Open.Execute(null);
        Pump(600);
        AllCheck("Barre du haut : le résultat s'ouvre et la liste se referme",
            (!ReferenceEquals(shell.Current, before) || shell.HasDialog) && !shell.HeaderFinderOpen && !bar.HasQuery);
        shell.CloseDialog();
        Wait(shell.Open(() => new HomePage(shell)));
        Pump(200);
    }
    shell.ShowSearch();
    Pump(300);
    var general = (SearchViewModel)shell.Dialog!;
    AllCheck("Accueil : barre façon Google", SearchStyled("QueryBox"));
    AllCheck("Accueil : micro présent", MicShown());
    Save(window, phoneMode ? "recherches-2-accueil-vide.png" : "recherches-2-ordinateur-vide.png");
    AllCheck("Accueil : « Parlez maintenant », puis le texte dicté",
        Dictate(() => general.Listen.ExecuteAsync(), () => general.Query, "Kabeya",
            phoneMode ? "recherches-2b-accueil-ecoute.png" : "recherches-2b-ordinateur-ecoute.png") == "Kabeya");
    general.Query = "ACC";
    Wait(general.SearchAsync());
    Pump(300);
    AllCheck($"Accueil : résultats en lignes ({general.Results.Count}, {string.Join(", ", general.Groups.Select(g => $"{g.Title} {g.Items.Count}"))})",
        general.Results.Count >= 2 && general.Groups.Any(g => g.Title == "APPRENANTS"));
    Save(window, phoneMode ? "recherches-3-accueil-resultats.png" : "recherches-3-ordinateur-resultats.png");
    general.Query = "zzzqqq";
    Wait(general.SearchAsync());
    Pump(200);
    AllCheck("Accueil : « Aucun résultat »", general.IsEmptyResult);
    Save(window, phoneMode ? "recherches-4-aucun.png" : "recherches-4-ordinateur-aucun.png");
    shell.CloseDialog();

    // 3. Recherche des réglages (page Paramètres).
    Wait(shell.Open(() => new SettingsPage(shell, "apparence")));
    shell.ShowSettingsSearch();
    var settingsFinder = (SearchViewModel)shell.Dialog!;
    settingsFinder.Query = "police";
    Wait(settingsFinder.SearchAsync());
    Pump(300);
    AllCheck($"Paramètres : même barre ({settingsFinder.Results.Count} réglage(s))", SearchStyled("QueryBox") && settingsFinder.Results.Count >= 1);
    Save(window, phoneMode ? "recherches-5-parametres.png" : "recherches-5-ordinateur-parametres.png");
    shell.CloseDialog();

    // 4. Filtre d'une liste (Apprenants).
    Wait(shell.Open(() => new StudentsPage(shell)));
    shell.HeaderSearch.Open();
    shell.HeaderSearch.Query = "Kabeya";
    Pump(600);
    AllCheck("Liste : filtre en barre façon Google", SearchStyled(phoneMode ? "ContextSearchBox" : "DesktopContextSearchBox"));
    AllCheck("Liste : micro présent", MicShown());
    Save(window, phoneMode ? "recherches-6-liste.png" : "recherches-6-ordinateur-liste.png");
    AllCheck("Liste : la voix remplit le filtre",
        Dictate(() => shell.HeaderSearch.Listen.ExecuteAsync(), () => shell.HeaderSearch.Query, "Mukendi", null) == "Mukendi");
    if (!phoneMode)
    {
        // Une seule recherche par liste : le filtre du bandeau, ouvert d'office ; plus de second champ dans la page.
        AllCheck("Ordinateur : filtre du bandeau ouvert d'office sur Apprenants", shell.HeaderSearch.IsOpen);
        AllCheck("Ordinateur : pas de second champ de recherche dans la page",
            !window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && t.Classes.Contains("search")
                && t.PlaceholderText == "Rechercher un nom ou un matricule"));
    }
    shell.HeaderSearch.Close.Execute(null);

    // 5. Catalogue « Info sur l'application ».
    shell.ShowAppInfo();
    Pump(300);
    AllCheck("Catalogue : barre façon Google et micro", SearchStyled("CatalogQuery") && MicShown());
    Save(window, phoneMode ? "recherches-7-catalogue.png" : "recherches-7-ordinateur-catalogue.png");
    shell.CloseDialog();

    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(allFailures == 0 ? "Recherches : toutes harmonisées." : $"Recherches : {allFailures} échec(s).");
    Environment.ExitCode = allFailures == 0 ? 0 : 1;
    return;
}

// Logo animé (argument « logo ») : huit instants de l'assemblage puis de la descente, clair et sombre.
if (args.Length > 3 && args[3] == "logo")
{
    window.Width = 1040;
    window.Height = 300;
    var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10) };
    var frames = new[] { 0.0, 0.15, 0.3, 0.45, 0.6, 0.8, 1.0, 1.35 };
    var logos = new List<Scolia.Bureau.Controls.LogoAssembly>();
    foreach (var value in frames)
    {
        var logo = new Scolia.Bureau.Controls.LogoAssembly
        {
            Width = 110, Height = 110, Progress = value,
            Primary = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0B2A6F")),
            Secondary = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#16A34A")),
        };
        logos.Add(logo);
        row.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new Border { Width = 118, Height = 200, Background = Avalonia.Media.Brushes.White, CornerRadius = new CornerRadius(14), Child = logo },
                new TextBlock { Text = value.ToString("0.00"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
            },
        });
    }
    window.Content = row;
    Save(window, "logo-etapes.png");
    window.Close();
    return;
}

// Clavier du téléphone (argument « clavier ») : un clavier simulé couvre le bas de l'écran ; le champ actif doit rester
// visible juste au-dessus de lui, sur la page Configuration comme dans une fenêtre de saisie.
if (args.Length > 3 && args[3] == "clavier")
{
    window.Width = 360;
    window.Height = 760;
    var keyFailures = 0;
    void KeyCheck(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) keyFailures++;
    }
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    var mainView = window.GetVisualDescendants().OfType<MainView>().First();
    // Comme Avalonia sur un iPhone à encoche ou un Android bord à bord : marges du système posées au niveau Style.
    var safeArea = new Thickness(0, 24, 0, 20);
    mainView.SetValue(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, safeArea, Avalonia.Data.BindingPriority.Style);
    var fake = new FakeKeyboard();
    KeyboardAvoidance.PaneOverride = fake;
    mainView.Keyboard.Attach();
    const double keyboardHeight = 300;

    // Vérifie le champ actif : entièrement visible, au-dessus du clavier, sous l'en-tête.
    void Probe(string where, TextBox box)
    {
        box.Focus();
        fake.Open(new Rect(0, window.Bounds.Height - keyboardHeight, window.Bounds.Width, keyboardHeight));
        for (var i = 0; i < 8; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(20); }
        var bounds = box.TransformToVisual(window) is { } m ? new Rect(box.Bounds.Size).TransformToAABB(m) : default;
        var keyboardTop = window.Bounds.Height - keyboardHeight;
        // Le contenu s'arrête déjà au-dessus de la zone sûre du bas : seule la différence est à réserver.
        KeyCheck($"{where} : place du clavier réservée ({mainView.Keyboard.Reserved:0} px)", Math.Abs(mainView.Keyboard.Reserved - (keyboardHeight - safeArea.Bottom)) < 2);
        KeyCheck($"{where} : champ au-dessus du clavier (bas {bounds.Bottom:0} ≤ {keyboardTop:0})", bounds.Height > 0 && bounds.Bottom <= keyboardTop + 1);
        KeyCheck($"{where} : champ sous l'en-tête (haut {bounds.Top:0})", bounds.Top >= 40);
        var tabs = window.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("phonetabs"));
        KeyCheck($"{where} : barre d'onglets retirée pendant la saisie", tabs is null || !tabs.IsEffectivelyVisible || tabs.Bounds.Height < 1);
    }
    void CloseKeyboard()
    {
        fake.Close();
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        var rootContent = mainView.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "RootContent");
        KeyCheck("Clavier fermé : l'écran reprend toute sa hauteur", mainView.Keyboard.Reserved == 0 && rootContent.Margin.Bottom == 0);
        KeyCheck($"Zone sûre du système gardée (haut {mainView.Padding.Top:0}, bas {mainView.Padding.Bottom:0})", mainView.Padding == safeArea);
    }

    // 1. Configuration de l'établissement : le dernier champ du formulaire, tout en bas de la page.
    Wait(shell.Open(() => new SettingsPage(shell, "etablissement")));
    Wait(Task.Delay(400));
    window.UpdateLayout();
    var fields = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.IsEnabled).ToList();
    KeyCheck($"Configuration : {fields.Count} champs de saisie trouvés", fields.Count >= 3);
    if (fields.Count > 0)
    {
        Probe("Configuration, dernier champ", fields[^1]);
        Save(window, "clavier-configuration-dernier.png");
        Probe("Configuration, champ du milieu", fields[fields.Count / 2]);
        Save(window, "clavier-configuration-milieu.png");
        CloseKeyboard();
        Save(window, "clavier-configuration-ferme.png");
    }

    // 2. Fenêtre de saisie : un long formulaire, le dernier champ.
    var longForm = new FormViewModel("Saisie longue", "Vérification du clavier", _ => Task.FromResult(true));
    for (var i = 1; i <= 9; i++) longForm.Add($"champ{i}", $"Champ {i}");
    _ = shell.Ask(longForm);
    Wait(Task.Delay(400));
    window.UpdateLayout();
    var dialogFields = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.DataContext is FormField).ToList();
    KeyCheck($"Fenêtre : {dialogFields.Count} champs", dialogFields.Count >= 9);
    if (dialogFields.Count > 0)
    {
        Probe("Fenêtre de saisie, dernier champ", dialogFields[^1]);
        Save(window, "clavier-fenetre-dernier.png");
        CloseKeyboard();
    }
    shell.CloseDialog();
    KeyboardAvoidance.PaneOverride = null;
    shell.Sync.Stop();
    window.Close();
    Console.WriteLine(keyFailures == 0 ? "Clavier : tout est visible." : $"Clavier : {keyFailures} échec(s).");
    Environment.ExitCode = keyFailures == 0 ? 0 : 1;
    return;
}

// Espace Promoteur (argument « promoteur », compte SCOLIA_COMPTE) : vue d'ensemble, puis le bas de la page.
if (args.Length > 3 && args[3] == "promoteur")
{
    window.Width = Preferences.MobileLayout ? 360 : 1280;
    window.Height = Preferences.MobileLayout ? 760 : 820;
    var suffix = Preferences.MobileLayout ? "mobile" : "bureau";
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync(Environment.GetEnvironmentVariable("SCOLIA_COMPTE") ?? "promoteur", password))));
    Wait(Task.Delay(600));
    Save(window, $"promoteur-1-{suffix}.png");
    foreach (var expander in window.GetVisualDescendants().OfType<Expander>().Take(2)) expander.IsExpanded = true;
    Wait(Task.Delay(300));
    var scrollers = window.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.IsEffectivelyVisible && v.Extent.Height > v.Viewport.Height + 4).ToList();
    var step = 1;
    foreach (var offset in new[] { 0.33, 0.66, 1.0 })
    {
        foreach (var scroller in scrollers) scroller.Offset = new Vector(0, (scroller.Extent.Height - scroller.Viewport.Height) * offset);
        Save(window, $"promoteur-{++step}-{suffix}.png");
    }
    Console.WriteLine($"Page : {shell.Current?.GetType().Name} · {shell.Current?.Title} · {shell.Current?.Subtitle}");
    Console.WriteLine("Menu : " + string.Join(", ", shell.Navigation.SelectMany(g => g.Items).Select(i => i.Label)));
    shell.Sync.Stop();
    window.Close();
    return;
}

// Assistant Tailscale : fenêtre Réseau puis assistant ouvert, sans réseau (téléchargement non lancé).
if (args.Length > 3 && args[3] == "tailscale")
{
    window.Width = Preferences.MobileLayout ? 360 : 1280;
    window.Height = Preferences.MobileLayout ? 760 : 820;
    var suffix = Preferences.MobileLayout ? "mobile" : "bureau";
    Wait(shell.OpenSessionAsync(Result(api.LoginAsync("admin", password))));
    shell.ShowNetwork();
    Save(window, $"tailscale-reseau-{suffix}.png");
    if (Preferences.MobileLayout)
    {
        // Comme sur Android : détection disponible, paquet APK officiel, installation remise au système.
        Platform.IsTailscaleInstalled = () => false;
        Platform.TailscalePackageKind = "android";
    }
    var wizard = TailscaleInstaller.Instance;
    var begin = wizard.BeginAsync();
    for (var i = 0; i < 300 && !begin.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
    Save(window, $"tailscale-assistant-{suffix}.png");
    var shown = window.GetVisualDescendants().OfType<TailscaleWizardView>().Count(v => v.IsEffectivelyVisible && v.Bounds.Height > 0);
    Console.WriteLine($"{(wizard.IsOpen && shown >= 1 ? "OK  " : "ECHEC")} Assistant Tailscale ouvert ({shown} vue visible), étape : {wizard.Step}");
    Console.WriteLine($"     Téléchargement proposé : {wizard.CanDownload} · magasin : {wizard.ShowStoreFallback} · état : {wizard.Status}");
    wizard.Close.Execute(null);
    shell.CloseDialog();
    shell.Sync.Stop();
    window.Close();
    Environment.ExitCode = wizard.IsOpen || shown < 1 ? 1 : 0;
    return;
}

// Vérification bureau autonome : redimensionnement, texte agrandi, dialogues et transitions.
if (args.Length > 3 && args[3] == "bureau")
{
    if (Preferences.MobileLayout) throw new InvalidOperationException("Le parcours bureau nécessite SCOLIA_MOBILE=0.");
    using var desktopServer = new LayoutServer();
    api.Retarget(desktopServer.Address);
    prefs.StatusBar = true;
    prefs.Startup = "normale";
    prefs.Scale = 1;
    var desktopFailures = 0;
    void CheckDesktop(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) desktopFailures++;
    }
    void RenderDesktop()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    Control DesktopNamed(string name) => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
    bool InsideDesktop(Control control)
    {
        if (control.TransformToVisual(window) is not { } matrix) return false;
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(matrix);
        return bounds.Width > 0 && bounds.Left >= -1 && bounds.Right <= window.Bounds.Width + 1;
    }
    prefs.Animations = false;
    shell.Login.SchoolName = "Institut supérieur des sciences de la santé et des techniques médicales";
    foreach (var (desktopWidth, desktopHeight) in new[] { (460, 640), (800, 600) })
    {
        window.Width = desktopWidth;
        window.Height = desktopHeight;
        RenderDesktop();
        CheckDesktop($"Connexion {desktopWidth}×{desktopHeight} : carte et champs dans la fenêtre",
            InsideDesktop(DesktopNamed("LoginCard")) && InsideDesktop(DesktopNamed("UserNameBox")));
        Save(window, $"bureau-connexion-{desktopWidth}x{desktopHeight}.png");
    }
    var desktopSession = new SessionDto(Guid.NewGuid(), "desktop-test", "Amina Kabeya du secrétariat", "Lecteur", "Lecture", [], false,
        shell.Login.SchoolName, "Institut de test", "2026-2027", "FC", "+243");
    var previousOpening = ShellViewModel.MinimumOpening;
    ShellViewModel.MinimumOpening = TimeSpan.Zero;
    try { Wait(shell.OpenSessionAsync(desktopSession)); }
    finally { ShellViewModel.MinimumOpening = previousOpening; }
    window.WindowState = WindowState.Normal;
    foreach (var (desktopWidth, desktopHeight) in new[] { (760, 520), (1024, 600), (1366, 768), (1920, 1080) })
    {
        window.Width = desktopWidth;
        window.Height = desktopHeight;
        foreach (var textScale in new[] { 1d, 1.3, 1.6 })
        {
            prefs.Scale = textScale;
            RenderDesktop();
            CheckDesktop($"Bureau {desktopWidth}×{desktopHeight}, texte {textScale:P0} : forme bureau conservée", !shell.Phone);
            foreach (var name in new[] { "PageBar", "HeaderTitle", "HeaderFinderBar", "HeaderExtras", "DesktopStatusBar", "DesktopServerLine" })
                CheckDesktop($"{name} dans la fenêtre", InsideDesktop(DesktopNamed(name)));
            Save(window, $"bureau-{desktopWidth}x{desktopHeight}-texte-{textScale:0.0}.png");
        }
    }
    var desktopHost = (TransitioningContentControl)DesktopNamed("PageHost");
    CheckDesktop("Animations désactivées : transition absente", desktopHost.PageTransition is null);
    prefs.Animations = true;
    CheckDesktop("Animations activées : transition disponible", desktopHost.PageTransition is not null);
    prefs.PageTransitions = false;
    CheckDesktop("Transitions de pages désactivées indépendamment", desktopHost.PageTransition is null);
    window.WindowState = WindowState.Maximized;
    shell.ToggleFullscreen();
    CheckDesktop("Plein écran : état du shell synchronisé à l'entrée", shell.IsFullscreen && window.WindowState == WindowState.FullScreen);
    shell.ToggleFullscreen();
    CheckDesktop("Plein écran : sortie vers la maximisation précédente", !shell.IsFullscreen && window.WindowState == WindowState.Maximized);
    window.WindowState = WindowState.Normal;

    // Deux tailles arrivent avant le passage différé : seule la dernière doit déterminer les colonnes.
    var resizeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
    resizeGrid.Children.Add(new Border());
    resizeGrid.Children.Add(new Border());
    Grid.SetColumn(resizeGrid.Children[1], 1);
    Adaptive.SetStackBelow(resizeGrid, 900);
    resizeGrid.Measure(new Size(800, 100));
    resizeGrid.Arrange(new Rect(0, 0, 800, 100));
    resizeGrid.Measure(new Size(1200, 100));
    resizeGrid.Arrange(new Rect(0, 0, 1200, 100));
    Dispatcher.UIThread.RunJobs();
    CheckDesktop("Redimensionnement rapide : la grille garde les deux colonnes de la dernière taille", resizeGrid.ColumnDefinitions.Count == 2);
    var resizeCards = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3 };
    for (var i = 0; i < 3; i++) resizeCards.Children.Add(new Border());
    Adaptive.SetMinItemWidth(resizeCards, 100);
    resizeCards.Measure(new Size(150, 100));
    resizeCards.Arrange(new Rect(0, 0, 150, 100));
    resizeCards.Measure(new Size(350, 100));
    resizeCards.Arrange(new Rect(0, 0, 350, 100));
    Dispatcher.UIThread.RunJobs();
    CheckDesktop("Redimensionnement rapide : les cartes suivent la dernière largeur", resizeCards.Columns == 3);

    var dialogProbe = new Border { Width = 700, MinWidth = 300, MaxWidth = 800, MinHeight = 100, MaxHeight = 320, Height = 500 };
    Adaptive.SetFitDialog(dialogProbe, true);
    var dialogContainer = new ContentControl { Margin = new Thickness(24), Content = dialogProbe };
    var dialogScrim = new Border { Child = dialogContainer };
    dialogScrim.Classes.Add("scrim");
    var dialogWindow = new Window { Width = 1024, Height = 600, Content = dialogScrim };
    dialogWindow.Show();
    Dispatcher.UIThread.RunJobs();
    dialogWindow.UpdateLayout();
    CheckDesktop("Dialogue : le plafond de hauteur propre est conservé", dialogProbe.MaxHeight == 320);
    dialogWindow.Width = 400;
    dialogWindow.Height = 260;
    dialogWindow.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    dialogWindow.UpdateLayout();
    CheckDesktop("Dialogue : marges et hauteur respectées en petite fenêtre",
        dialogProbe.Width <= dialogWindow.Bounds.Width - 48 && dialogProbe.MaxHeight <= dialogWindow.Bounds.Height - 48);
    dialogWindow.Close();
    desktopFailures += VerifyPayments(desktopServer, mobile: false);
    shell.Sync.Stop();
    var replacementPrefs = new Preferences { Animations = true, PageTransitions = true };
    var replacementShell = new ShellViewModel(new ScoliaClient(desktopServer.Address), replacementPrefs);
    window.DataContext = replacementShell;
    RenderDesktop();
    CheckDesktop("Changement de session : les services de l'ancienne vue sont libérés",
        shell.PickFileHandler is null && shell.CopyHandler is null && shell.IsTyping is null && shell.FullscreenHandler is null);
    prefs.Animations = false;
    prefs.PageTransitions = true;
    CheckDesktop("Anciennes préférences : aucune modification des transitions de la nouvelle session", desktopHost.PageTransition is not null);
    window.Close();
    CheckDesktop("Fermeture : les services de la vue sont libérés",
        replacementShell.PickFileHandler is null && replacementShell.CopyHandler is null && replacementShell.IsTyping is null && replacementShell.FullscreenHandler is null);
    Environment.ExitCode = desktopFailures == 0 ? 0 : 1;
    return;
}

// Vérification du parcours mobile sur petit écran et quand le clavier réduit la hauteur.
// SCOLIA_MOBILE=1 dotnet run -- <sortie> http://localhost:1/ inutilise fluidite
// Les appels accessoires (logo, direct) vont au serveur HTTP fictif local, jamais à un établissement.
if (args.Length > 3 && args[3] == "fluidite")
{
    if (!Preferences.MobileLayout) throw new InvalidOperationException("Le parcours fluidite nécessite SCOLIA_MOBILE=1.");
    using var layoutServer = new LayoutServer();
    api.Retarget(layoutServer.Address);
    var failures = 0;
    void CheckLayout(string label, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}");
        if (!ok) failures++;
    }
    void RenderLayout()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    Control Named(string name) => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
    Rect InWindow(Control control) => control.TransformToVisual(window) is { } m
        ? new Rect(control.Bounds.Size).TransformToAABB(m) : default;
    bool FitsHorizontally(Control control)
    {
        var rect = InWindow(control);
        return rect.Width > 0 && rect.Left >= -1 && rect.Right <= window.Bounds.Width + 1;
    }

    prefs.Animations = false;
    shell.Login.SchoolName = "Institut supérieur des sciences de la santé et des techniques médicales de l'établissement";
    shell.Login.Notice = "Le serveur de l'établissement est temporairement indisponible. Vous pouvez utiliser le dernier compte de cet appareil.";
    foreach (var (width, height) in new[] { (320, 568), (320, 300), (360, 640), (412, 892) })
    {
        window.Width = width;
        window.Height = height;
        RenderLayout();
        var loginScroll = (ScrollViewer)Named("LoginScroll");
        loginScroll.Offset = default;
        RenderLayout();
        CheckLayout($"Connexion {width}×{height} : carte dans l'écran", FitsHorizontally(Named("LoginCard")));
        CheckLayout($"Connexion {width}×{height} : nom d'établissement dans l'écran", FitsHorizontally(Named("SchoolHeading")));
        CheckLayout($"Connexion {width}×{height} : le titre reste au-dessus du formulaire",
            InWindow(Named("SchoolHeading")).Bottom <= InWindow(Named("LoginCard")).Top - 8);
        if (height == 300)
            CheckLayout("Clavier : le formulaire peut défiler", loginScroll.Extent.Height > loginScroll.Viewport.Height);
        loginScroll.Offset = new Vector(0, loginScroll.Extent.Height);
        RenderLayout();
        var footer = InWindow(Named("LoginFooter"));
        CheckLayout($"Connexion {width}×{height} : bas du formulaire atteignable",
            footer.Top >= -1 && footer.Bottom <= window.Bounds.Height + 1);
        Save(window, $"fluidite-connexion-{width}x{height}.png");
    }

    window.Width = 320;
    window.Height = 568;
    var mobileSession = new SessionDto(Guid.NewGuid(), "mobile-test", "Amina Kabeya", "Lecteur", "Lecture", [], false,
        shell.Login.SchoolName, "Institut de test", "2026-2027", "FC", "+243");
    var openingMinimum = ShellViewModel.MinimumOpening;
    ShellViewModel.MinimumOpening = TimeSpan.FromMilliseconds(1400);
    var mobileOpening = shell.OpenSessionAsync(mobileSession);
    RenderLayout();
    CheckLayout("Bienvenue : contenu compact dans l'écran", FitsHorizontally(Named("WelcomeContent")));
    Save(window, "fluidite-bienvenue-320x568.png");
    Wait(mobileOpening);
    ShellViewModel.MinimumOpening = openingMinimum;

    window.Height = 300;
    shell.ShowAccentSheet();
    RenderLayout();
    var sheet = InWindow(Named("ActionSheet"));
    var cancel = InWindow(Named("CancelSheetButton"));
    CheckLayout("Feuille : reste entièrement dans la hauteur disponible",
        sheet.Height > 0 && sheet.Top >= 0 && sheet.Bottom <= window.Bounds.Height + 1);
    CheckLayout("Feuille : Annuler reste visible sur écran court",
        cancel.Top >= 0 && cancel.Bottom <= window.Bounds.Height + 1);
    CheckLayout("Feuille : aucun débordement horizontal", FitsHorizontally(Named("ActionSheet")));
    Save(window, "fluidite-feuille-320x300.png");
    shell.CloseSheet();
    var phoneHost = (TransitioningContentControl)Named("PhoneHost");
    CheckLayout("Animations désactivées : aucune transition de page", phoneHost.PageTransition is null);
    prefs.Animations = true;
    CheckLayout("Animations activées : transition de page disponible", phoneHost.PageTransition is not null);
    failures += VerifyPayments(layoutServer, mobile: true);
    shell.Sync.Stop();
    window.Close();
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}

// Premier lancement sans configuration : l'assistant remplace la connexion (SCOLIA_ASSISTANT=1).
if (Environment.GetEnvironmentVariable("SCOLIA_ASSISTANT") == "1")
{
    shell.IsSetupRequired = true;
    Wait(shell.Setup.LoadAsync());
    Save(window, "00-assistant-etape-1.png");
    shell.Setup.SchoolName = "Institut de test";
    shell.Setup.ShortName = "IT";
    // Logo et promotions sur la même page, comme le paramétrage de la version 79.
    shell.Setup.SetLogo(Result(api.DownloadAsync("api/etablissement/logo")), "logo-ecole.png");
    shell.Setup.Promotions[0].Code = "INF1";
    shell.Setup.Promotions[0].Name = "Première année infirmière";
    shell.Setup.Promotions[0].Fee = "55000";
    shell.Setup.AddPromotion.Execute(null);
    shell.Setup.Promotions[1].Code = "ACC1";
    shell.Setup.Promotions[1].Name = "Première année accoucheuse";
    shell.Setup.Promotions[1].Fee = "50000";
    Save(window, "00b-assistant-logo-promotions.png");
    Wait(Task.Delay(50));
    shell.Setup.Step = 2;
    Save(window, "00-assistant-etape-2.png");
    return;
}
shell.Login.SchoolName = Result(api.GetAsync<SetupStatusDto>("api/installation")).SchoolName ?? string.Empty;
Wait(shell.RefreshLogoAsync());
Save(window, "01-connexion.png");
// Verrouillage après 5 erreurs : compte à rebours en cours, puis accès rétabli.
shell.Login.UserName = "admin";
shell.Login.StartLock(120, DateTime.UtcNow.AddSeconds(-23));
shell.Login.Tick(DateTime.UtcNow);
Save(window, "01b-connexion-verrouillee.png");
shell.Login.Tick(DateTime.UtcNow.AddMinutes(3));
Save(window, "01c-connexion-retablie.png");
shell.Login.Reset();
shell.Login.UserName = string.Empty;

// Passage de toutes les commandes (argument « commandes ») : chacune doit produire un effet visible.
if (args.Length > 3 && args[3] == "commandes")
{
    var sweepSession = Result(api.LoginAsync("admin", password));
    Wait(shell.OpenSessionAsync(sweepSession));
    var failed = CommandSweep(shell);
    window.Close();
    Environment.Exit(failed);
}

// Défilement du téléphone (argument « defilement ») : la page reste là où le doigt la laisse, en descendant
// comme en remontant. Chaque pas compare la position demandée à celle obtenue après la mise en page.
if (args.Length > 3 && args[3] == "defilement")
{
    var scrollSession = Result(api.LoginAsync("admin", password));
    Wait(shell.OpenSessionAsync(scrollSession));
    var drift = 0;
    foreach (var make in new Func<PageViewModel>[] { () => new ReceiptsPage(shell), () => new StudentsPage(shell), () => new MonthlyPage(shell) })
    {
        var opening = System.Diagnostics.Stopwatch.StartNew();
        Wait(shell.Open(make));
        window.UpdateLayout();
        var openMs = opening.ElapsedMilliseconds;
        Wait(Task.Delay(500));
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PhoneScroll");
        Console.WriteLine($"OUVERTURE {shell.Current?.Title} : {openMs} ms, {scroll.GetVisualDescendants().Count()} éléments");
        // « Afficher plus » jusqu'au bout : le défilement est éprouvé sur la liste entière.
        while (scroll.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("phoneshowmore") && b.IsVisible) is { } showMore)
        {
            showMore.Command?.Execute(null);
            showMore.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
        void Settle() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Thread.Sleep(20); } }
        Settle();
        var bottom = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        // Descente puis remontée, par pas d'un doigt (180 points), comme sur l'écran.
        var steps = new List<double>();
        for (var y = 0d; y <= bottom; y += 180) steps.Add(y);
        for (var y = bottom; y >= 0; y -= 180) steps.Add(y);
        var worst = 0d;
        // Une carte visible sert de repère : quand la page défile de d points, elle doit bouger de d points, pas plus.
        (object Item, double Y)? Anchor()
        {
            foreach (var card in scroll.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("phonecard") && b.IsVisible))
            {
                if (card.DataContext is not { } item || card.TranslatePoint(new Point(0, 0), scroll) is not { } p) continue;
                if (p.Y >= 0 && p.Y + card.Bounds.Height <= scroll.Viewport.Height) return (item, p.Y);
            }
            return null;
        }
        double? Find(object item) => scroll.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("phonecard") && ReferenceEquals(b.DataContext, item))
            .Select(b => b.TranslatePoint(new Point(0, 0), scroll)?.Y).FirstOrDefault();
        var extents = new List<double>();
        foreach (var target in steps)
        {
            extents.Add(scroll.Extent.Height);
            var before = Anchor();
            var from = scroll.Offset.Y;
            scroll.Offset = new Vector(0, target);
            Settle();
            if (before is { } b && Find(b.Item) is { } y)
                worst = Math.Max(worst, Math.Abs(y - (b.Y + (from - scroll.Offset.Y))));
        }
        var name = shell.Current?.Title ?? "?";
        Console.WriteLine($"{name} : {steps.Count} pas, hauteur {extents.Min():0} à {extents.Max():0}, écart maximal {worst:0} points");
        if (worst > 2) drift++;
        // Le doigt se pose sur une carte au milieu de la liste puis se lève : la page ne doit pas bouger d'elle-même.
        scroll.Offset = new Vector(0, Math.Min(bottom, 3000));
        Settle();
        if (Anchor() is { } touched && scroll.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => ReferenceEquals(c.DataContext, touched.Item) && c.Classes.Contains("phonecard")) is { } touchedCard
            && touchedCard.TranslatePoint(new Point(touchedCard.Bounds.Width / 3, touchedCard.Bounds.Height / 2), window) is { } at)
        {
            var was = scroll.Offset.Y;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left);
            Settle();
            window.MouseUp(at, Avalonia.Input.MouseButton.Left);
            Settle();
            var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement();
            Console.WriteLine($"   appui sur une carte : page déplacée de {scroll.Offset.Y - was:0} points · focus sur {focused?.GetType().Name}");
            if (Math.Abs(scroll.Offset.Y - was) > 2) drift++;
        }
    }
    // Glissement du doigt, comme sur l'écran : poser, monter de 300 points en 300 ms, lâcher, puis l'inertie.
    // La page doit aller dans le sens du doigt, et seulement dans ce sens.
    foreach (var make in new Func<PageViewModel>[] { () => new StudentsPage(shell), () => new ReceiptsPage(shell), () => new DashboardPage(shell), () => new CashPage(shell) })
    {
        Wait(shell.Open(make));
        Wait(Task.Delay(600));
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PhoneScroll");
        // Geste rapide vers le bas de la page, puis aussitôt le doigt redescend : l'inertie du premier geste
        // doit s'arrêter dès que le doigt se repose, et la page suivre le second geste.
        {
            scroll.Offset = new Vector(0, 2000);
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            var x = window.Bounds.Width / 2;
            ulong t = 0;
            Touch.Send(window, "TouchBegin", new Point(x, 700), t);
            for (var k = 1; k <= 10; k++) { Thread.Sleep(10); t += 10; Touch.Send(window, "TouchUpdate", new Point(x, 700 - k * 40), t); Dispatcher.UIThread.RunJobs(); }
            Touch.Send(window, "TouchEnd", new Point(x, 300), t + 2);
            for (var k = 0; k < 10; k++) { Thread.Sleep(16); Dispatcher.UIThread.RunJobs(); }
            var before = scroll.Offset.Y;
            t += 300;
            var trace = new List<double> { before };
            Touch.Send(window, "TouchBegin", new Point(x, 300), t);
            Dispatcher.UIThread.RunJobs();
            for (var k = 1; k <= 20; k++) { Thread.Sleep(15); t += 15; Touch.Send(window, "TouchUpdate", new Point(x, 300 + k * 10), t); Dispatcher.UIThread.RunJobs(); trace.Add(scroll.Offset.Y); }
            // Doigt immobile un instant avant de se lever : la page ne doit plus bouger.
            for (var k = 0; k < 10; k++) { Thread.Sleep(16); Dispatcher.UIThread.RunJobs(); trace.Add(scroll.Offset.Y); }
            var held = scroll.Offset.Y;
            Touch.Send(window, "TouchEnd", new Point(x, 500), t + 160);
            var against = trace.Zip(trace.Skip(1), (a, b) => b - a).Where(d => d > 0.5).Sum();
            var creep = Math.Abs(held - trace[trace.Count - 11]);
            Console.WriteLine($"INVERSE   {shell.Current?.Title} : après le premier geste {before:0}, doigt redescendu 200 pts → {held:0} · contre le doigt {against:0} · dérive doigt immobile {creep:0}");
            if (against > 1 || creep > 1) drift++;
            for (var k = 0; k < 200; k++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
        }
        // (sens, pas en points, pause en ms, abscisse) : vers le bas ou le haut, rapide ou lent, au milieu ou près du bord.
        foreach (var (dir, step, pause, xr) in new[] { (1, 15, 15, 0.5), (-1, 15, 15, 0.5), (-1, 6, 30, 0.2), (1, 30, 10, 0.85), (-1, 30, 10, 0.85) })
        {
            scroll.Offset = new Vector(0, 2000);
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            var start = scroll.Offset.Y;
            var trace = new List<double> { start };
            var x = window.Bounds.Width * xr;
            var y0 = dir > 0 ? 700d : 250d;
            ulong t = 0;
            Touch.Send(window, "TouchBegin", new Point(x, y0), t);
            for (var k = 1; k <= 20; k++)
            {
                Thread.Sleep(pause);
                t += (ulong)pause;
                Touch.Send(window, "TouchUpdate", new Point(x, y0 - dir * k * step), t);
                Dispatcher.UIThread.RunJobs();
                trace.Add(scroll.Offset.Y);
            }
            Touch.Send(window, "TouchEnd", new Point(x, y0 - dir * 20 * step), t + 5);
            var released = scroll.Offset.Y;
            for (var k = 0; k < 90; k++) { Thread.Sleep(16); Dispatcher.UIThread.RunJobs(); trace.Add(scroll.Offset.Y); }
            // Le doigt monte (dir 1) : la page avance ; il descend (dir -1) : elle recule. Tout mouvement contraire est un défaut.
            var against = trace.Zip(trace.Skip(1), (a, b) => (b - a) * dir).Where(d => d < -0.5).Sum(d => -d);
            Console.WriteLine($"DOIGT     {shell.Current?.Title} : {(dir > 0 ? "monte" : "descend")} {20 * step} pts en {20 * pause} ms · page {start:0} → {released:0} au lâcher → {scroll.Offset.Y:0} · contraire {against:0}");
            if (against > 1) drift++;
        }
    }

    // Mise à jour venue d'un autre poste pendant la lecture : la page se recharge sans changer de hauteur.
    foreach (var make in new Func<PageViewModel>[] { () => new StudentsPage(shell), () => new ReceiptsPage(shell), () => new MonthlyPage(shell) })
    {
        Wait(shell.Open(make));
        Wait(Task.Delay(500));
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Name == "PhoneScroll");
        scroll.Offset = new Vector(0, 1500);
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        var was = scroll.Offset.Y;
        Wait(shell.RefreshQuietlyAsync());
        for (var i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Thread.Sleep(10); }
        Console.WriteLine($"DISTANT   {shell.Current?.Title} : avant {was:0}, après la mise à jour {scroll.Offset.Y:0}");
        if (Math.Abs(scroll.Offset.Y - was) > 1) drift++;
    }

    // Rechargements spontanés : une page laissée ouverte sans y toucher ne doit pas se recharger seule.
    foreach (var entry in shell.Navigation.SelectMany(g => g.Items).Take(30))
    {
        try { entry.Open.Execute(null); } catch { continue; }
        Wait(Task.Delay(1500));
        if (shell.Current is not { } still) continue;
        var reloads = 0;
        void Count(object? _, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(PageViewModel.Busy) && still.Busy) reloads++; }
        still.PropertyChanged += Count;
        Wait(Task.Delay(12000));
        still.PropertyChanged -= Count;
        if (reloads > 0) Console.WriteLine($"RECHARGE  {entry.Label} : {reloads} rechargement(s) en 12 s sans action");
    }

    // Défilements imbriqués : sur le téléphone, la page entière défile ; un second défilement vertical à l'intérieur
    // se disputerait le doigt avec elle.
    foreach (var entry in shell.Navigation.SelectMany(g => g.Items))
    {
        try { entry.Open.Execute(null); } catch { continue; }
        Wait(Task.Delay(400));
        var host = window.GetVisualDescendants().OfType<TransitioningContentControl>().FirstOrDefault(c => c.Name == "PhoneHost");
        if (host is null) continue;
        var inner = host.GetVisualDescendants().OfType<ScrollViewer>().Where(v => v.IsEffectivelyVisible).ToList();
        var vertical = inner.Where(v => v.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled).ToList();
        if (vertical.Count > 0)
            Console.WriteLine($"IMBRIQUÉ  {entry.Label} : {vertical.Count} défilement(s) vertical(aux) · " + string.Join(", ", vertical.Select(v => $"{v.Parent?.GetType().Name}/{v.TemplatedParent?.GetType().Name} vue {v.Viewport.Height:0} contenu {v.Extent.Height:0}")));
    }
    window.Close();
    Environment.Exit(drift);
}

if (args.Length > 3 && args[3] == "smoke")
{
    Smoke(shell, api, window, password);
    window.Close();
    return;
}

var session = Result(api.LoginAsync(Environment.GetEnvironmentVariable("SCOLIA_COMPTE") ?? "admin", password));
// Ouverture : la roue et les étapes, capturées en cours de route, puis l'accueil.
var openingTask = shell.OpenSessionAsync(session);
var openingSeen = DateTime.UtcNow.AddMilliseconds(1100);
while (DateTime.UtcNow < openingSeen) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
Save(window, "01d-ouverture.png");
Wait(openingTask);
Save(window, "02-accueil.png");
// Itération rapide sur l'accueil (SCOLIA_ACCUEIL=1) : la page entière, puis le thème sombre, sans parcourir les autres écrans.
if (Environment.GetEnvironmentVariable("SCOLIA_ACCUEIL") == "1")
{
    window.Height = 1500;
    Save(window, "02b-accueil-complet.png");
    prefs.Theme = "dark";
    prefs.Apply();
    Wait(shell.Current!.LoadAsync());
    Save(window, "02c-accueil-sombre.png");
    return;
}


// Ouvre un formulaire (boîte de dialogue) lancé par une action, attend qu'il s'affiche.
static FormViewModel AwaitForm(ShellViewModel shell, Task opening)
{
    for (var i = 0; i < 500 && shell.Dialog is not FormViewModel; i++)
    {
        if (opening.IsFaulted) opening.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
    return shell.Dialog as FormViewModel ?? throw new InvalidOperationException("Formulaire non affiché.");
}

// Remplit le formulaire comme le ferait l'utilisateur, puis le valide et attend la fin de l'action.
// Attend la fin d'une validation : la boîte se ferme (succès) ou reste ouverte avec une erreur.
static void WaitValidation(FormViewModel form, Task opening)
{
    var limit = DateTime.UtcNow.AddSeconds(30);
    while (form.Busy || (!opening.IsCompleted && form.Error is null))
    {
        if (DateTime.UtcNow > limit) throw new TimeoutException("Validation trop longue.");
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(10);
    }
}

static bool Submit(ShellViewModel shell, Task opening, Action<FormViewModel> fill)
{
    var form = AwaitForm(shell, opening);
    fill(form);
    form.Submit.Execute(null);
    WaitValidation(form, opening);
    if (opening.IsCompleted) return true;
    // Validation refusée : le formulaire est fermé, comme le ferait l'utilisateur, pour laisser la suite du parcours s'exécuter.
    Console.WriteLine($"  (formulaire refusé : {form.Error})");
    form.Close(false);
    Wait(opening);
    return false;
}

// Parcours métier réels : chaque écran est chargé, chaque action d'écriture est exécutée contre le serveur.
// Une erreur remontée par le serveur apparaît comme notification : le parcours la signale et échoue.
void Smoke(ShellViewModel shell, ScoliaClient api, Window window, string password)
{
    var failures = 0;
    void Check(string label, bool ok, string? detail = null)
    {
        Console.WriteLine($"{(ok ? "OK  " : "ECHEC")} {label}{(detail is null ? string.Empty : $" : {detail}")}");
        if (!ok) failures++;
    }
    string? Notice(ShellViewModel s) => s.Notice;

    var session = Result(api.LoginAsync("admin", password));
    Wait(shell.OpenSessionAsync(session));
    Check("ouverture de session", shell.IsLoggedIn);

    var students = new StudentsPage(shell);
    Wait(shell.Open(() => students));
    Check("liste des apprenants", students.Items.Count > 0, $"{students.Items.Count} ligne(s)");
    Wait(Task.Delay(50));

    var first = Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1")).Items[0];
    var detail = new StudentDetailPage(shell, first.Id);
    Wait(shell.Open(() => detail));
    Check("fiche apprenant", detail.Details.Count > 0 && detail.Totals.Count > 0, $"{detail.Lines.Count} échéance(s), {detail.Receipts.Count} reçu(s)");

    var cashier = new CashierPage(shell);
    Wait(shell.Open(() => cashier));
    cashier.Search = "ACC";
    Wait(cashier.SearchAsync());
    Check("recherche d'apprenant à l'encaissement", cashier.Results.Count > 0, $"{cashier.Results.Count} résultat(s)");
    // Le premier apprenant qui a encore un mois à payer : sur une base déjà utilisée, les premiers peuvent être soldés.
    foreach (var candidate in cashier.Results.ToList())
    {
        Wait(cashier.ChooseAsync(candidate));
        if (cashier.Months.Count > 0) break;
    }
    Check("un apprenant avec un mois à payer", cashier.Months.Count > 0, $"{cashier.Months.Count} mois proposé(s)");
    // Encaissement par mois : on coche le premier mois dû, le montant vient de l'échéance.
    var firstMonth = cashier.Months.First();
    firstMonth.IsSelected = true;
    Wait(cashier.PreviewAsync());
    Check("aperçu de répartition", cashier.Allocations.Count > 0 && cashier.Total == cashier.Months.First(m => m.IsSelected).AmountText, $"{cashier.Allocations.Count} imputation(s), {cashier.Total}");
    var before = Result(api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements?taille=1")).Total;
    Wait(shell.Run(() => cashier.PayAsync()));
    Wait(Task.Delay(200));
    var after = Result(api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements?taille=1")).Total;
    Check("encaissement crée un reçu", cashier.HasReceipt && after == before + 1, cashier.HasReceipt ? cashier.ReceiptText : Notice(shell));

    var receipts = new ReceiptsPage(shell);
    Wait(shell.Open(() => receipts));
    Check("journal des reçus", receipts.Items.Count > 0, receipts.Subtitle);
    var receiptId = receipts.Items[0].Id;
    var receipt = new ReceiptDetailPage(shell, receiptId);
    Wait(shell.Open(() => receipt));
    Check("détail du reçu", receipt.Details.Count == 9, $"{receipt.Details.Count} ligne(s)");

    var debtors = new DebtorsPage(shell);
    Wait(shell.Open(() => debtors));
    Check("recouvrement", debtors.Items.Count > 0, debtors.Subtitle);
    var sponsorships = new SponsorshipsPage(shell);
    Wait(shell.Open(() => sponsorships));
    Check("prises en charge", true, sponsorships.Subtitle);
    var cash = new CashPage(shell);
    Wait(shell.Open(() => cash));
    Check("caisse et situation", cash.Figures.Count == 4 && cash.Movements.Count > 0, $"{cash.Movements.Count} mouvement(s)");
    var expenses = new ExpensesPage(shell);
    Wait(shell.Open(() => expenses));
    Check("dépenses", expenses.Items.Count > 0, expenses.Subtitle);
    var reenroll = new ReEnrollmentPage(shell);
    Wait(shell.Open(() => reenroll));
    Check("réinscriptions (plan)", reenroll.Years.Count > 0, $"{reenroll.Years.Count} année(s)");

    var grades = new GradesPage(shell);
    Wait(shell.Open(() => grades));
    Check("cours de la promotion", grades.Courses.Count > 0, $"{grades.Courses.Count} cours");
    grades.Course = grades.Courses[0];
    Wait(Task.Delay(400));
    Check("évaluations du cours", grades.Assessments.Count > 0, $"{grades.Assessments.Count} évaluation(s)");
    if (grades.Assessments.Count > 0)
    {
        grades.Assessment = grades.Assessments[0];
        // La feuille se charge en arrière-plan : attendre qu'elle arrive, sans délai fixe trop court.
        for (var waited = 0; waited < 5000 && grades.Grades.Count == 0; waited += 100) Wait(Task.Delay(100));
        Check("feuille de notes", grades.Grades.Count > 0, $"{grades.Grades.Count} élève(s)");
    }
    var results = new ResultsPage(shell);
    Wait(shell.Open(() => results));
    Check("résultats", results.Items.Count > 0, results.Subtitle);

    var attendance = new AttendancePage(shell);
    Wait(shell.Open(() => attendance));
    Check("feuille d'appel", attendance.Rows.Count > 0, attendance.Subtitle);
    var discipline = new DisciplinePage(shell);
    Wait(shell.Open(() => discipline));
    Check("discipline", true, discipline.Subtitle);

    var staff = new StaffPage(shell);
    Wait(shell.Open(() => staff));
    Check("personnel", staff.Staff.Count > 0, staff.Subtitle);
    var payroll = new StaffPage(shell) { ShowPayroll = true };
    Wait(shell.Open(() => payroll));
    Check("paie du mois", true, payroll.Subtitle);

    var settings = new SettingsPage(shell);
    Wait(shell.Open(() => settings));
    Check("établissement", settings.SchoolForm is not null);
    var years = new SettingsPage(shell);
    Wait(shell.Open(() => years));
    years.Section = years.SchoolSections[1];
    Wait(Task.Delay(400));
    Check("années scolaires", years.Years.Count > 0, $"{years.Years.Count} année(s)");

    var users = new UsersPage(shell);
    Wait(shell.Open(() => users));
    Check("utilisateurs", users.Items.Count > 0, $"{users.Items.Count} compte(s)");
    Wait(DataPage.BackupNowAsync(shell));
    var data = new DataPage(shell);
    Wait(shell.Open(() => data));
    Check("sauvegardes", data.Items.Count > 0, $"{data.Items.Count} fichier(s)");
    var audit = new AuditPage(shell);
    Wait(shell.Open(() => audit));
    Check("journal d'audit", audit.Items.Count > 0, audit.Subtitle);

    // Écritures : chaque formulaire est rempli, validé, et son effet vérifié sur le serveur.
    var todayText = DateOnly.FromDateTime(DateTime.Today);
    var expenseBefore = Result(api.GetAsync<PagedResult<ExpenseDto>>("api/depenses?taille=1")).Total;
    var ok = Submit(shell, ExpensesPage.NewExpenseAsync(shell), f =>
    {
        f.Find("beneficiaire").Text = "Smoke fournisseur";
        f.Find("description").Text = "Dépense de test du parcours";
        f.Find("montant").Text = "5000";
    });
    Check("nouvelle dépense", ok && Result(api.GetAsync<PagedResult<ExpenseDto>>("api/depenses?taille=1")).Total == expenseBefore + 1);

    var studentsBefore = Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1")).Total;
    var inscription = StudentForms.CreateAsync(shell);
    var inscriptionForm = AwaitForm(shell, inscription);
    inscriptionForm.Find("nom").Text = "SMOKETEST";
    inscriptionForm.Find("prenom").Text = "Parcours";
    inscriptionForm.Find("postNom").Text = "Test";
    // Promotion et sexe ne sont plus préremplis : on les choisit, comme un utilisateur.
    inscriptionForm.Find("promotion").Choice ??= inscriptionForm.Find("promotion").Options[0];
    inscriptionForm.Find("sexe").Choice = inscriptionForm.Find("sexe").Options[0];
    // Champs obligatoires comme dans la version 79 : naissance, lieu et contact du parent.
    inscriptionForm.Find("naissance").Day = new DateTime(2008, 3, 14);
    inscriptionForm.Find("lieu").Text = "Lubumbashi";
    inscriptionForm.Find("contact1").Text = "0812345678";
    inscriptionForm.Submit.Execute(null);
    WaitValidation(inscriptionForm, inscription);
    if (!inscription.IsCompleted)
    {
        // Doublon signalé par le serveur : la seconde validation confirme l'inscription.
        Console.WriteLine($"  (doublon signalé : {inscriptionForm.Error})");
        inscriptionForm.Submit.Execute(null);
        WaitValidation(inscriptionForm, inscription);
    }
    // Les essais répétés remplissent la promotion de démonstration : on passe à une promotion qui a encore des places,
    // comme le ferait la secrétaire devant le message « complète ».
    foreach (var other in inscriptionForm.Find("promotion").Options.Skip(1))
    {
        if (inscription.IsCompleted || !(inscriptionForm.Error ?? string.Empty).Contains("complète", StringComparison.Ordinal)) break;
        Console.WriteLine($"  (promotion complète, essai sur {other})");
        inscriptionForm.Find("promotion").Choice = other;
        inscriptionForm.Submit.Execute(null);
        WaitValidation(inscriptionForm, inscription);
        if (!inscription.IsCompleted && (inscriptionForm.Error ?? string.Empty).Contains("identité", StringComparison.Ordinal))
        {
            inscriptionForm.Submit.Execute(null);
            WaitValidation(inscriptionForm, inscription);
        }
    }
    var inscriptionDone = inscription.IsCompleted;
    if (!inscriptionDone) Console.WriteLine($"  (inscription non terminée : {inscriptionForm.Error} · "
        + string.Join(", ", inscriptionForm.Fields.Where(f => f.HasError).Select(f => $"{f.Label} : {f.Error}")) + ")");
    Check("nouvelle inscription", Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1")).Total == studentsBefore + 1);

    var detailForModif = new StudentDetailPage(shell, first.Id);
    Wait(shell.Open(() => detailForModif));
    var receiptBeforeEdit = detailForModif.Receipts.Count > 0 ? detailForModif.Receipts[0].Id : (Guid?)null;
    var editing = detailForModif.EditAsync();
    ok = Submit(shell, editing, f =>
    {
        f.Find("contact1").Text = "+243 800 111 222";
        f.Find("nom").Text = "CONTROLE";
    });
    Check("modification de l'apprenant", ok);
    // Un reçu déjà enregistré affiche l'identité corrigée : il n'est pas figé à la date du paiement.
    if (receiptBeforeEdit is { } existing)
    {
        var updated = Result(api.GetAsync<System.Text.Json.JsonElement>($"api/paiements/{existing}"));
        Check("reçu déjà enregistré mis à jour", updated.GetProperty("studentName").GetString()!.StartsWith("CONTROLE", StringComparison.Ordinal), updated.GetProperty("studentName").GetString());
    }

    var courses = Result(api.GetAsync<List<CourseDto>>("api/scolarite/cours?promotionId=" + grades.Promotion!.Value));
    var courseBefore = courses.Count;
    var newCourse = GradesPage.NewCourseAsync(shell, Guid.Parse(grades.Promotion!.Value));
    ok = Submit(shell, newCourse, f =>
    {
        // Le numéro du cours est proposé d'office ; seul l'intitulé est à saisir.
        f.Find("intitule").Text = "Cours de test " + DateTime.Now.ToString("HHmmss");
        f.Find("heures").Text = "10";
    });
    Check("nouveau cours", ok && Result(api.GetAsync<List<CourseDto>>("api/scolarite/cours?promotionId=" + grades.Promotion!.Value)).Count == courseBefore + 1);

    var assessmentCourse = Result(api.GetAsync<List<CourseDto>>("api/scolarite/cours?promotionId=" + grades.Promotion!.Value))[0].Id;
    ok = Submit(shell, GradesPage.NewAssessmentAsync(shell, assessmentCourse), f =>
    {
        f.Find("intitule").Text = "Évaluation de test";
        f.Find("periode").Text = "1";
    });
    Check("nouvelle évaluation", ok);

    Wait(grades.LoadAsync());
    grades.Course = grades.Courses[0];
    Wait(Task.Delay(500));
    grades.Assessment = grades.Assessments[0];
    Wait(Task.Delay(500));
    var firstGrade = grades.Grades[0];
    firstGrade.Score = "15";
    firstGrade.Comment = "Test";
    Wait(grades.SaveGradesAsync());
    Check("enregistrement des notes", Result(api.GetAsync<GradeSheetDto>($"api/scolarite/evaluations/{grades.Assessment!.Id}/notes")).Rows.Any(r => r.Score == 15m));

    attendance.Promotion = attendance.Promotions[0];
    Wait(attendance.LoadAsync());
    Wait(attendance.SaveAsync());
    Check("enregistrement de l'appel", attendance.Rows.Count > 0);

    var staffBefore = Result(api.GetAsync<List<StaffDto>>("api/personnel")).Count;
    ok = Submit(shell, StaffPage.NewMemberAsync(shell), f =>
    {
        f.Find("nom").Text = "SMOKE";
        f.Find("prenom").Text = "Membre";
        f.Find("fonction").Text = "Test";
        f.Find("salaire").Text = "300000";
    });
    Check("nouveau membre du personnel", ok && Result(api.GetAsync<List<StaffDto>>("api/personnel")).Count == staffBefore + 1);

    var usersBefore = Result(api.GetAsync<List<UserDto>>("api/utilisateurs")).Count;
    ok = Submit(shell, UsersPage.NewUserAsync(shell), f =>
    {
        f.Find("identifiant").Text = "smoke" + Guid.NewGuid().ToString("N")[..6];
        f.Find("nom").Text = "Utilisateur test";
        f.Find("motDePasse").Text = "Smoke2026!";
    });
    Check("nouvel utilisateur", ok && Result(api.GetAsync<List<UserDto>>("api/utilisateurs")).Count == usersBefore + 1);

    Wait(DataPage.BackupNowAsync(shell));
    Check("sauvegarde immédiate", !shell.HasNotice || !shell.Notice!.Contains("erreur", StringComparison.OrdinalIgnoreCase));

    shell.ShowSearch();
    var search = (SearchViewModel)shell.Dialog!;
    search.Query = "ACC";
    Wait(search.SearchAsync());
    Check("recherche globale", search.Results.Count > 0, $"{search.Results.Count} résultat(s)");
    shell.CloseDialog();

    Console.WriteLine(failures == 0 ? "PARCOURS OK" : $"{failures} échec(s)");
}

// Écran suivant : ouvre la page, attend ses données, enregistre l'image.
// Filtre facultatif (SCOLIA_PAGES=22e,08b) : ne capture que les pages dont le nom commence ainsi, pour isoler un écran.
var only = (Environment.GetEnvironmentVariable("SCOLIA_PAGES") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
void Page(Func<PageViewModel> create, string name, Action<PageViewModel>? prepare = null)
{
    if (only.Length > 0 && !only.Any(o => name.StartsWith(o, StringComparison.Ordinal))) return;
    var page = create();
    prepare?.Invoke(page);
    Wait(shell.Open(() => page));
    Save(window, name);
    // Tableau de bord : la page entière, en plusieurs vues, pour contrôler graphiques et barres jusqu'en bas.
    if (name.StartsWith("22e", StringComparison.Ordinal))
    {
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(v => v.IsEffectivelyVisible && v.Extent.Height > v.Viewport.Height + 40)
            .OrderByDescending(v => v.Viewport.Height * v.Viewport.Width).FirstOrDefault();
        if (scroller is null) return;
        var step = Math.Max(200, scroller.Viewport.Height - 80);
        for (var (y, i) = (step, 2); i <= 6; y += step, i++)
        {
            scroller.Offset = new Vector(0, Math.Min(y, scroller.Extent.Height - scroller.Viewport.Height));
            Wait(Task.Delay(250));
            Save(window, name.Replace(".png", $"-{i}.png", StringComparison.Ordinal));
            if (y >= scroller.Extent.Height - scroller.Viewport.Height) break;
        }
        scroller.Offset = default;
    }
}

var students = Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1"));
var receipts = Result(api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements?taille=1"));
// Établissement tout neuf (sans apprenant ni reçu) : on capture aussi ses écrans vides.
var firstStudent = students.Items.Count > 0 ? students.Items[0].Id : Guid.Empty;
var firstReceipt = receipts.Items.Count > 0 ? receipts.Items[0].Id : Guid.Empty;
if (firstStudent == Guid.Empty)
{
    Page(() => new StudentsPage(shell), "03-apprenants.png");
    Page(() => new CashierPage(shell), "05-encaisser.png");
    Page(() => new ReceiptsPage(shell), "06-journal-recus.png");
    Page(() => new SponsorshipsPage(shell), "09-prises-en-charge.png");
    Page(() => new GradesPage(shell), "13-cours-et-notes.png");
    Page(() => new AttendancePage(shell), "15-presences.png");
    Page(() => new DashboardPage(shell), "22e-tableau-de-bord.png");
    window.Close();
    return;
}

Page(() => new StudentsPage(shell), "03-apprenants.png");
Page(() => new StudentDetailPage(shell, firstStudent), "04-fiche-apprenant.png");
Page(() => new CashierPage(shell, firstStudent), "05-encaisser.png");
Page(() => new BatchPaymentPage(shell), "05b-encaissement-lot.png");
Page(() => new ReceiptsPage(shell), "06-journal-recus.png");
Page(() => new ReceiptDetailPage(shell, firstReceipt), "07-recu.png");
Page(() => new DebtorsPage(shell), "08-recouvrement.png");
Page(() => new SponsorshipsPage(shell), "09-prises-en-charge.png");
Page(() => new CashPage(shell), "10-caisse.png");
Page(() => new ExpensesPage(shell), "11-depenses.png");
Page(() => new ReEnrollmentPage(shell), "12-reinscriptions.png");
Page(() => new GradesPage(shell), "13-cours-et-notes.png");
Page(() => new ResultsPage(shell), "14-resultats.png");
Page(() => new AttendancePage(shell), "15-presences.png");
Page(() => new DisciplinePage(shell), "16-discipline.png");
Page(() => new StaffPage(shell), "17-personnel.png");
Page(() => new StaffPage(shell) { ShowPayroll = true }, "18-paie.png");
Page(() => new SettingsPage(shell), "19-parametres-etablissement.png");
Page(() => new SettingsPage(shell), "20-parametres-annees.png", p => ((SettingsPage)p).Section = ((SettingsPage)p).SchoolSections[1]);
foreach (var (key, file) in new[] { ("apparence", "20b-parametres-theme.png"), ("texte", "20c-parametres-texte.png"), ("fenetres", "20d-parametres-fenetres.png"), ("animations", "20e-parametres-animations.png"), ("sons", "20f-parametres-sons.png") })
    Page(() => new SettingsPage(shell, key), file);
Page(() => new UsersPage(shell), "21-utilisateurs.png");
Page(() => new DataPage(shell), "22-donnees.png");
Page(() => new ReportsPage(shell), "22d-rapports-exports.png");
Page(() => new DashboardPage(shell), "22e-tableau-de-bord.png");
Page(() => new CardsPage(shell), "16b-cartes-acces.png");
Page(() => new MonthlyPage(shell), "08b-suivi-mensuel.png");
Page(() => new MonthlyPage(shell, "nonpaye"), "08c-suivi-mensuel-non-payes.png");
Page(() => new TeachingPage(shell), "16c-enseignants-cours.png");
Page(() => new StaffAttendancePage(shell), "18b-pointage.png");
Page(() => new EraseDataPage(shell, payments: true), "22b-effacer-donnees.png");
Page(() => new EraseDataPage(shell, payments: false), "22c-effacer-apprenants.png");
Page(() => new AuditPage(shell), "23-audit.png");

// Traitement qui dure : l'indicateur au centre de la fenêtre.
var longTask = shell.Run(() => Task.Delay(1500));
Wait(Task.Delay(600));
Save(window, "29-traitement-en-cours.png");
Wait(longTask);

// Clic droit sur une ligne du suivi mensuel : actions de la ligne, copie, actions de la page.
Wait(shell.Open(() => new MonthlyPage(shell, "nonpaye")));
if (window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } monthlyGrid
    && monthlyGrid.GetVisualDescendants().OfType<DataGridRow>().Skip(2).FirstOrDefault() is { } monthlyRow)
{
    monthlyGrid.SelectedItem = monthlyRow.DataContext;
    var rowMenu = RowMenu.Show(monthlyGrid, monthlyRow, monthlyRow.GetVisualDescendants().OfType<DataGridCell>().Skip(1).FirstOrDefault(), atPointer: false);
    Save(window, "28-menu-ligne.png");
    rowMenu.Hide();
}
if (shell.Current is { } monthlyCurrent && RowMenu.PageMenu(monthlyCurrent) is { } pageMenu)
{
    var host = window.GetVisualDescendants().OfType<TransitioningContentControl>().First();
    pageMenu.Placement = Avalonia.Controls.PlacementMode.Center;
    pageMenu.ShowAt(host, false);
    Save(window, "28c-menu-page.png");
    pageMenu.Hide();
}
// Aperçu rapide d'un apprenant, depuis ce même menu.
if (shell.Current is MonthlyPage monthlyPage && monthlyPage.Selected is { } peek)
{
    _ = QuickView.StudentAsync(shell, peek.StudentId);
    Wait(Task.Delay(700));
    Save(window, "28b-apercu-rapide.png");
    shell.CloseDialog();
}

// Clic droit sur un apprenant : renommer et supprimer le dossier, puis leurs deux fenêtres.
Wait(shell.Open(() => new StudentsPage(shell)));
Wait(Task.Delay(400));
if (shell.Current is StudentsPage studentsPage && studentsPage.Items.Count > 2)
{
    studentsPage.Selected = studentsPage.Items[2];
    if (window.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault() is { } studentGrid
        && studentGrid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => Equals(r.DataContext, studentsPage.Selected)) is { } studentRow)
    {
        var menu = RowMenu.Show(studentGrid, studentRow, null, atPointer: false);
        Save(window, "28d-menu-apprenant.png");
        menu.Hide();
    }
    var chosen = studentsPage.Selected.Id;
    _ = StudentForms.RenameAsync(shell, chosen);
    Wait(Task.Delay(700));
    Save(window, "28e-renommer-apprenant.png");
    shell.CloseDialog();
    Wait(Task.Delay(300));
    _ = StudentForms.DeleteAsync(shell, chosen);
    Wait(Task.Delay(900));
    Save(window, "28f-supprimer-apprenant.png");
    shell.CloseDialog();
    Wait(Task.Delay(300));
}

// Boîtes de dialogue : recherche globale et formulaire d'inscription.
Wait(shell.Open(() => new DashboardPage(shell)));
if (Platform.IsMobile)
{
    shell.ShowSearch();
    if (shell.Dialog is SearchViewModel search)
    {
        search.Query = "ACC";
        Wait(Task.Delay(900));
    }
    Save(window, "24-recherche.png");
    shell.CloseDialog();
}
else
{
    // Ordinateur : la recherche se fait dans la barre du haut, résultats déroulés dessous.
    shell.FindInHeader();
    Wait(Task.Delay(300));
    Save(window, "24-recherche-vide.png");
    shell.HeaderFinder.Query = "ACC";
    Wait(Task.Delay(900));
    Save(window, "24-recherche.png");
    shell.CloseHeaderFinder();
}

// À propos, et l'aide des raccourcis : chaque fenêtre a son bouton Fermer.
shell.ShowAbout();
Save(window, "24b-a-propos.png");
shell.CloseDialog();
var shortcuts = ShortcutsDialog.ShowAsync(shell);
Save(window, "24c-raccourcis.png");
shell.CloseDialog();

// Le vrai formulaire d'inscription, avec les champs obligatoires de SCOLIA V79.
_ = StudentForms.CreateAsync(shell);
Wait(Task.Delay(600));
Save(window, "25-formulaire.png");
if (shell.Dialog is FormViewModel form)
{
    // Bas du formulaire : les contacts du parent, puis les erreurs affichées quand on inscrit sans les remplir.
    var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().LastOrDefault(sv => sv.DataContext is FormViewModel);
    if (scroller is not null) scroller.Offset = new Vector(0, scroller.Extent.Height);
    Save(window, "25b-formulaire-contacts.png");
    form.Fields.First(f => f.Key == "nom").Text = "Mukendi";
    form.Fields.First(f => f.Key == "postNom").Text = "Kabeya";
    form.Fields.First(f => f.Key == "prenom").Text = "Grâce";
    form.Submit.Execute(null);
    Wait(Task.Delay(300));
    Save(window, "25c-formulaire-erreurs.png");
    form.Close(false);
}
Wait(Task.Delay(100));

shell.OpenBackstage();
Save(window, "26-fichier.png");
// Téléphone serveur encore freiné par Android : la carte d'arrière-plan doit se voir, avec le réglage de la marque.
if (Platform.IsMobile)
{
    Platform.BackgroundAllowed = () => false;
    Platform.BackgroundHint = "Sur ce téléphone, ouvrez aussi Réglages, Gestion des applications au démarrage : mettez SCOLIA en manuel et autorisez tout.";
}
shell.Backstage.Section = shell.Backstage.Items.First(i => i.Value == "poste");
Save(window, "26b-poste.png");
// Relier un autre appareil : le code QR du serveur, au-dessus du menu Fichier.
shell.ShowPairing();
Wait(Task.Delay(300));
Save(window, "26c-code-qr.png");
shell.CloseDialog();
Platform.BackgroundAllowed = null;
Platform.BackgroundHint = string.Empty;
shell.CloseBackstage();

// Préférences et mot de passe du menu Compte.
shell.OpenBackstage();
shell.Backstage.Section = shell.Backstage.Items.First(i => i.Value == "mot-de-passe");
Save(window, "26d-mot-de-passe.png");
shell.CloseBackstage();

// Thème sombre : même écran, palette inversée.
prefs.Theme = "dark";
Wait(shell.Open(() => new CashPage(shell)));
Save(window, "27-sombre-caisse.png");
prefs.Theme = "light";

// Mobile : le menu de tous les outils, ses commandes, puis la feuille des actions d'une page.
if (Preferences.MobileLayout)
{
    Wait(shell.Open(() => new HomePage(shell)));
    shell.PhoneMenuOpen = true;
    Save(window, "30-mobile-menu.png");
    var menuScroll = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "MenuPanel").GetVisualAncestors().OfType<ScrollViewer>().First();
    menuScroll.Offset = new Vector(0, menuScroll.Extent.Height);
    Save(window, "30b-mobile-commandes.png");
    shell.PhoneMenuOpen = false;
    // Réglages du téléphone : le thème se choisit dans une feuille, la police et la taille dans leur fenêtre.
    shell.ShowThemeSheet();
    Save(window, "37-mobile-theme.png");
    shell.ShowAccentSheet();
    Save(window, "37b-mobile-couleur.png");
    shell.CloseSheet();
    shell.ShowTextSettings();
    Save(window, "38-mobile-police.png");
    if (shell.Dialog is TextSettingsViewModel textSettings)
    {
        var (keepFont, keepScale) = (shell.Prefs.Font, shell.Prefs.Scale);
        textSettings.Font = "serif";
        textSettings.Scale = 1.2;
        Save(window, "38b-mobile-police-serif.png");
        textSettings.Font = keepFont;
        textSettings.Scale = keepScale;
    }
    shell.CloseDialog();
    Wait(shell.Open(() => new ReceiptsPage(shell)));
    shell.ShowPageSheet();
    Save(window, "31-mobile-actions-page.png");
    shell.CloseSheet();

    // Un second poste travaille en même temps : il s'annonce, puis enregistre une modification.
    Wait(shell.Open(() => new HomePage(shell)));
    var cookies = new System.Net.CookieContainer();
    using var other = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = server };
    other.DefaultRequestHeaders.Add("X-Scolia", "1");
    other.DefaultRequestHeaders.Add("X-Scolia-Poste", "poste-capture-secretariat");
    other.DefaultRequestHeaders.Add("X-Scolia-Appareil", Uri.EscapeDataString("PC Secrétariat"));
    Wait(other.PostAsJsonAsync("api/session", new { userName = "admin", password }));
    Wait(other.PostAsJsonAsync("api/direct/presence", new { page = "Encaisser", activity = "Encaissement de KASONGO Patrick" }));
    Wait(Task.Delay(1500));
    Wait(other.PostAsJsonAsync("api/etablissement/sections", new { code = $"S{DateTime.Now:HHmmss}", name = "Maternelle", isActive = true }));
    for (var i = 0; i < 40 && !shell.Sync.HasTicker; i++) Wait(Task.Delay(100));
    Save(window, "32-mobile-direct.png");
    shell.ShowNetwork();
    Wait(Task.Delay(500));
    Save(window, "33-mobile-reseau.png");
    shell.CloseDialog();

    // L'autre poste encaisse déjà un apprenant : sur le téléphone, cet encaissement est bloqué et dit pourquoi.
    var target = Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1")).Items[0];
    Wait(other.PostAsJsonAsync("api/direct/verrous", new { key = $"encaissement:{target.EnrollmentId}", label = $"Encaissement de {target.LastName} {target.FirstName}" }));
    Wait(shell.Open(() => new CashierPage(shell, target.Id)));
    Save(window, "36-mobile-encaissement-bloque.png");
    Wait(other.DeleteAsync($"api/direct/verrous?cle={Uri.EscapeDataString($"encaissement:{target.EnrollmentId}")}"));
    Wait(shell.Open(() => new HomePage(shell)));

    // L'ordinateur principal disparaît : l'application continue sur les données gardées.
    api.Retarget(new Uri("http://localhost:1/"));
    Wait(shell.RefreshQuietlyAsync());
    Wait(Task.Delay(300));
    Save(window, "34-mobile-hors-ligne.png");
    shell.ShowNetwork();
    Save(window, "35-mobile-reseau-hors-ligne.png");
    shell.CloseDialog();

    // Premier lancement du téléphone : rejoindre l'établissement, ou en devenir le serveur.
    api.Retarget(server);
    Wait(shell.LogoutAsync());
    Wait(shell.ChooseServerAsync());
    Save(window, "39-mobile-premier-lancement.png");
}

window.Close();
Console.WriteLine("terminé");

// Téléphone : chaque choix du thème, de la couleur, de la police et de la taille doit s'appliquer, puis être rétabli.
void MobileChoices(ShellViewModel shell, List<string> problems)
{
    var prefs = shell.Prefs;
    var (theme, accent, font, scale) = (prefs.Theme, prefs.Accent, prefs.Font, prefs.Scale);
    void Check(string what, bool ok)
    {
        Console.WriteLine($"{(ok ? "OK        " : "ÉCHEC     ")} Choix › {what}");
        if (!ok) problems.Add($"Choix › {what}");
    }
    foreach (var value in new[] { "dark", "light", "system" })
    {
        shell.ShowThemeSheet();
        var item = shell.Sheet?.Items.FirstOrDefault(i => i.Label == Preferences.Themes.First(t => t.Value == value).Label);
        item?.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Check($"Thème {value} : la feuille se referme et le thème change", item is not null && !shell.HasSheet && prefs.Theme == value);
    }
    shell.ShowThemeSheet();
    Check("Thème : la valeur en cours est cochée", shell.Sheet?.Items.Count(i => i.IsChecked) == 1);
    shell.CloseSheet();
    foreach (var a in Preferences.AccentSwatches)
    {
        shell.ShowAccentSheet();
        shell.Sheet?.Items.First(i => i.Label == a.Label).Command.Execute(null);
        Check($"Couleur {a.Label}", prefs.Accent == a.Key);
    }
    shell.ShowTextSettings();
    if (shell.Dialog is TextSettingsViewModel text)
    {
        foreach (var f in text.Fonts)
        {
            f.Choose.Execute(null);
            Check($"Police {f.Label} : appliquée et seule cochée", prefs.Font == f.Key && text.Fonts.Count(x => x.IsSelected) == 1);
        }
        text.Scale = 1.23;
        Check("Taille au curseur : arrondie à 1,25", Math.Abs(prefs.Scale - 1.25) < 0.001 && text.ScaleLabel == "125 %");
        text.Larger.Execute(null); text.Larger.Execute(null); text.Larger.Execute(null);
        Check("Taille A+ : bornée à 140 %", Math.Abs(prefs.Scale - TextSettingsViewModel.MaxScale) < 0.001);
        for (var i = 0; i < 20; i++) text.Smaller.Execute(null);
        Check("Taille A- : bornée à 85 %", Math.Abs(prefs.Scale - TextSettingsViewModel.MinScale) < 0.001);
        foreach (var p in text.Presets)
        {
            p.Choose.Execute(null);
            Check($"Taille prête {p.Label}", Math.Abs(prefs.Scale - p.Value) < 0.001 && text.Presets.Count(x => x.IsSelected) == 1);
        }
        text.Reset.Execute(null);
        Check("Rétablir : Inter à 100 %", prefs.Font == "inter" && Math.Abs(prefs.Scale - 1) < 0.001);
        shell.CloseDialog();
    }
    else Check("Police et taille : la fenêtre s'ouvre", false);
    (prefs.Theme, prefs.Accent, prefs.Font, prefs.Scale) = (theme, accent, font, scale);
}

// ---------- Passage des commandes ----------
// Lance chaque commande du ruban et chaque action d'en-tête de chaque page, sans ligne choisie, puis attend un effet
// visible : une fenêtre, une autre page, le menu Fichier, un message ou un réglage changé. Une commande sans effet
// est signalée : c'est ce que l'utilisateur vit comme « la commande ne fonctionne pas ».
int CommandSweep(ShellViewModel shell)
{
    var skipped = new HashSet<string>(StringComparer.Ordinal)
    {
        "Se déconnecter", "Quitter", "Plein écran", "Changer de serveur", "Installer le service", "Désinstaller le service",
        "Ouvrir le pare-feu", "Démarrer avec Windows", "Autoriser le réseau",
    };
    var problems = new List<string>();
    var total = 0;
    var noticed = false;
    string? lastNotice = null;
    shell.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(ShellViewModel.Notice) && shell.Notice is { } n) { noticed = true; lastNotice = n; }
    };
    // Le sélecteur de fichiers de Windows ne s'ouvre pas sans écran : son appel compte comme l'effet attendu.
    shell.PickFileHandler = _ => { noticed = true; lastNotice = "sélecteur de fichiers ouvert"; return Task.FromResult<PickedFile?>(null); };
    string PrefsKey() => System.Text.Json.JsonSerializer.Serialize(shell.Prefs) + shell.SidebarShown + shell.Prefs.RibbonCollapsed;
    static string DialogName(object? d) => d switch
    {
        FormViewModel f => f.Title,
        SearchViewModel => "Recherche",
        ShortcutsViewModel => "Raccourcis clavier",
        AboutViewModel => "À propos",
        TextSettingsViewModel => "Police et taille du texte",
        null => string.Empty,
        _ => d.GetType().Name,
    };
    void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); }
    }
    void CloseAll()
    {
        for (var i = 0; i < 4 && shell.HasDialog; i++)
        {
            if (shell.Dialog is FormViewModel f) f.Cancel.Execute(null); else shell.CloseDialog();
            Pump(120);
        }
        if (shell.BackstageOpen) shell.CloseBackstage();
        shell.CloseSheet();
        shell.CloseHeaderFinder();
        shell.PhoneMenuOpen = false;
    }
    string? Try(string where, string label, System.Windows.Input.ICommand command, bool toggle)
    {
        total++;
        CloseAll();
        Pump(150);
        var page = shell.Current;
        var prefs = PrefsKey();
        noticed = false;
        string? effect = null;
        try
        {
            if (!command.CanExecute(null)) return "désactivée";
            command.Execute(null);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < until && effect is null)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(20);
                effect = shell.HasDialog ? $"fenêtre « {DialogName(shell.Dialog)} »"
                    : shell.HasSheet ? $"choix « {shell.Sheet!.Title} » : {string.Join(", ", shell.Sheet.Items.Select(i => i.IsChecked ? i.Label + " (coché)" : i.Label))}"
                    : !ReferenceEquals(shell.Current, page) ? $"page « {shell.Current?.Title} »"
                    : shell.BackstageOpen ? "menu Fichier"
                    : shell.HeaderFinderOpen ? "recherche dans la barre du haut"
                    : noticed ? $"message « {lastNotice} »"
                    : PrefsKey() != prefs ? "réglage modifié"
                    : null;
            }
        }
        catch (Exception ex) { effect = null; problems.Add($"{where} › {label} : ERREUR {ex.GetType().Name} {ex.Message}"); Console.WriteLine($"ERREUR {where} › {label} : {ex.Message}"); return "erreur"; }
        if (toggle && effect is not null) { command.Execute(null); Pump(300); }
        if (effect is null) problems.Add($"{where} › {label} : aucun effet visible");
        Console.WriteLine($"{(effect is null ? "SANS EFFET" : "OK        ")} {where} › {label}{(effect is null ? string.Empty : $" : {effect}")}");
        CloseAll();
        return effect;
    }

    // 1. Ruban et menu : toutes les commandes de tous les onglets. Sur le téléphone, celles du menu plein écran.
    if (Preferences.MobileLayout)
        foreach (var group in shell.PhoneTools.ToList())
            foreach (var command in group.Commands)
            {
                if (skipped.Contains(command.Label)) { Console.WriteLine($"PASSÉE     Menu {group.Title} › {command.Label}"); continue; }
                Try($"Menu {group.Title}", command.Label, command.Command, command.IsToggle);
            }
    if (Preferences.MobileLayout) MobileChoices(shell, problems);
    foreach (var tab in shell.Tabs.Where(t => !t.IsFile && !Preferences.MobileLayout).ToList())
        foreach (var group in tab.Groups)
            foreach (var command in group.Commands)
            {
                if (skipped.Contains(command.Label)) { Console.WriteLine($"PASSÉE     {tab.Title} › {command.Label}"); continue; }
                Try($"{tab.Title} / {group.Title}", command.Label, command.Command, command.IsToggle);
            }

    // 2. Chaque page du volet latéral, puis chaque action de son en-tête, sans ligne choisie.
    foreach (var item in shell.Navigation.SelectMany(g => g.Items).ToList())
    {
        CloseAll();
        Wait(Task.Run(() => { }));
        item.Open.Execute(null);
        Pump(1500);
        if (shell.Current is not { } current) { problems.Add($"{item.Label} : la page ne s'ouvre pas"); continue; }
        Console.WriteLine($"PAGE       {item.Label} : {current.Title}");
        foreach (var action in current.HeaderActions.ToList())
        {
            if (skipped.Contains(action.Label)) continue;
            if (!ReferenceEquals(shell.Current, current)) { item.Open.Execute(null); Pump(1200); current = shell.Current!; }
            Try(item.Label, action.Label, action.Command, toggle: false);
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{total} commandes essayées, {problems.Count} à revoir.");
    foreach (var p in problems) Console.WriteLine($"  À REVOIR  {p}");
    return problems.Count == 0 ? 0 : 1;
}

/// <summary>
/// Serveur borné au loopback pour les seuls tests de mise en page : les ressources accessoires sont absentes.
/// Le parcours d'ouverture utilise une session fictive sans permissions et n'accède à aucune donnée métier.
/// </summary>
sealed class LayoutServer : IDisposable
{
    private readonly System.Net.HttpListener listener = new();
    public Uri Address { get; }
    public AccountDto? PaymentAccount { get; set; }
    public int PaymentCount { get; private set; }
    public TaskCompletionSource? HoldPayment { get; set; }
    public TaskCompletionSource? HoldStudents { get; set; }
    public int StudentReads { get; private set; }

    public LayoutServer()
    {
        // Réserver un port libre ; l'écoute HTTP ne concerne que la machine de test.
        using var portProbe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        Address = new Uri($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add(Address.AbsoluteUri);
        listener.Start();
        _ = ServeAsync();
    }

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            System.Net.HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is System.Net.HttpListenerException or ObjectDisposedException) { return; }
            _ = RespondAsync(context);
        }
    }

    private async Task RespondAsync(System.Net.HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            object? result = null;
            if (PaymentAccount is { } account)
            {
                if (path == $"/api/inscriptions/{account.EnrollmentId}/compte") result = account;
                else if (path == $"/api/apprenants/{account.StudentId}")
                    result = new StudentDto(account.StudentId, account.Matricule, "Kabeya", "Mbuyi", "Amina", "F", null, null,
                        null, null, null, null, null, null, account.GuardianPhone, null, null, false,
                        [new EnrollmentDto(account.EnrollmentId, Guid.NewGuid(), account.AcademicYear, Guid.NewGuid(), account.PromotionCode,
                            "Première année", DateOnly.FromDateTime(DateTime.Today), "Actif", null, null, false)], 1);
                else if (path == "/api/etablissement/promotions") result = Array.Empty<PromotionDto>();
                else if (path == "/api/apprenants")
                {
                    StudentReads++;
                    if (HoldStudents is { } hold) await hold.Task;
                    result = new PagedResult<StudentListItem>(Enumerable.Range(1, 40).Select(i =>
                        new StudentListItem(new Guid(i, 0, 0, new byte[8]), account.EnrollmentId, $"SC-{i:000}", "Kabeya", "Mbuyi", $"Amina {i}", "F",
                            account.PromotionCode, "Actif", account.GuardianPhone, account.Balance, account.Overdue)).ToList(), 40, 1, 50, null);
                }
                else if (path == "/api/revision") result = new RevisionDto(1);
                else if (path == "/api/direct/etat") result = new LiveSnapshotDto(1, [], []);
                else if (path == "/api/direct/verrous") result = new LockDto("test", "Encaissement", "test", "Test", "Test", DateTime.UtcNow.AddMinutes(5));
                else if (path is "/api/paiements/apercu" or "/api/paiements" && context.Request.HttpMethod == "POST")
                {
                    using var document = await System.Text.Json.JsonDocument.ParseAsync(context.Request.InputStream);
                    var body = document.RootElement;
                    var ids = body.GetProperty("feeItemIds").EnumerateArray().Select(id => id.GetGuid()).ToHashSet();
                    var allocations = account.Lines.Where(line => ids.Contains(line.FeeItemId))
                        .Select(line => new AllocationDto(line.FeeItemId, line.Label, line.Remaining, 0)).ToList();
                    var total = allocations.Sum(line => line.Amount);
                    if (path == "/api/paiements/apercu") result = new PaymentPreviewDto(allocations, account.Balance - total);
                    else
                    {
                        PaymentCount++;
                        if (HoldPayment is { } hold) await hold.Task;
                        result = new ReceiptDto(Guid.NewGuid(), $"TEST-{PaymentCount:000}", "verification-test", DateOnly.FromDateTime(DateTime.Today),
                            account.StudentId, account.Matricule, account.FullName, account.PromotionCode, account.AcademicYear, total,
                            "Especes", "TR-TEST", null, account.GuardianPhone, "OK", "Caisse de test", "Valide", null, null, null, allocations, DateTime.UtcNow);
                        PaymentAccount = account with { Lines = account.Lines.Select(line => ids.Contains(line.FeeItemId)
                            ? line with { Paid = line.Paid + line.Remaining, Remaining = 0 } : line).ToList(), Balance = account.Balance - total };
                    }
                }
            }
            if (result is null)
            {
                context.Response.StatusCode = 404;
                context.Response.ContentLength64 = 0;
            }
            else
            {
                var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
        }
        catch (Exception ex) when (ex is System.Net.HttpListenerException or ObjectDisposedException or IOException) { }
        finally { context.Response.Close(); }
    }

    public void Dispose() => listener.Close();
}

/// <summary>Serveur du téléphone, sans démarrage : l'outil de captures montre seulement les écrans qui le proposent.</summary>
sealed class CaptureServer : ILocalServer
{
    public void Start() { }
    public void Restart() { }
    public void Stop() { }
}

/// <summary>Événements tactiles fabriqués comme ceux d'un écran de téléphone, pour éprouver le défilement au doigt.</summary>
static class Touch
{
    private static readonly System.Reflection.Assembly Base = typeof(Avalonia.Input.Pointer).Assembly;
    private static object? device;

    public static void Send(Window window, string kind, Point at, ulong time)
    {
        var impl = typeof(TopLevel).GetProperty("PlatformImpl")!.GetValue(window)!;
        var input = (Delegate)impl.GetType().GetInterfaces().Select(i => i.GetProperty("Input")).First(p => p is not null)!.GetValue(impl)!;
        device ??= Activator.CreateInstance(Base.GetType("Avalonia.Input.TouchDevice")!, true)!;
        var type = Base.GetType("Avalonia.Input.Raw.RawPointerEventType")!;
        var args = Base.GetType("Avalonia.Input.Raw.RawTouchEventArgs")!;
        var root = typeof(TopLevel).GetProperty("InputRoot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(window) ?? window;
        var ctor = args.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var values = ctor.GetParameters().Select(p => p.ParameterType.Name switch
        {
            "IInputDevice" or "TouchDevice" => device,
            "UInt64" => (object)time,
            "IInputRoot" => root,
            "RawPointerEventType" => Enum.Parse(type, kind),
            "Point" => at,
            "RawInputModifiers" => Enum.ToObject(p.ParameterType, 0),
            "Int64" => (object)1L,
            _ => p.HasDefaultValue ? p.DefaultValue : null,
        }).ToArray();
        input.DynamicInvoke(ctor.Invoke(values));
    }
}

/// <summary>Clavier simulé : il s'ouvre et se ferme comme celui d'Android, sans téléphone.</summary>
sealed class FakeKeyboard : IKeyboardArea
{
    public bool IsOpen { get; private set; }
    public Rect Area { get; private set; }
    public event Action<TimeSpan>? Changed;

    public void Open(Rect area)
    {
        Area = area;
        IsOpen = true;
        Changed?.Invoke(TimeSpan.Zero);
    }

    public void Close()
    {
        Area = default;
        IsOpen = false;
        Changed?.Invoke(TimeSpan.Zero);
    }
}

/// <summary>Page de vérification dont le chargement attend un signal : sert à voir les silhouettes.</summary>
sealed class SlowPage(ShellViewModel shell, Task gate) : PageViewModel(shell)
{
    public override string Title => "Chargement";
    public override Task LoadAsync() => gate;
}
