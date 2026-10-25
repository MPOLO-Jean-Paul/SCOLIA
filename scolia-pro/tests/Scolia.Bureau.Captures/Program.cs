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
    Avalonia.Media.Imaging.Bitmap? frame;
    if (scale > 1)
    {
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
        frame = sharp;
    }
    else frame = window.CaptureRenderedFrame();
    if (frame is null) throw new InvalidOperationException($"Rendu indisponible pour {name}.");
    frame.Save(Path.Combine(output, name));
    Console.WriteLine($"capturé : {name} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    frame.Dispose();
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
// Captures figées : les animations se rejouent à l'écran, une image doit montrer l'état final.
prefs.Animations = Environment.GetEnvironmentVariable("SCOLIA_ANIMATIONS") == "1";
prefs.Accent = Environment.GetEnvironmentVariable("SCOLIA_ACCENT") ?? "bleu";
// Sans les bulles d'astuce (SCOLIA_ASTUCES=0) : des écrans dégagés, pour une présentation.
if (Environment.GetEnvironmentVariable("SCOLIA_ASTUCES") == "0") prefs.Tips = false;
// Le mobile est connu avant le shell, comme sur Android : ses menus et ses réglages en dépendent.
if (Environment.GetEnvironmentVariable("SCOLIA_MOBILE") == "1") { Preferences.MobileLayout = true; Platform.IsMobile = true; }
var shell = new ShellViewModel(api, prefs);
// Taille de fenêtre optionnelle (SCOLIA_TAILLE=1024x700) pour vérifier l'adaptation du texte à chaque format d'écran.
var taille = (Environment.GetEnvironmentVariable("SCOLIA_TAILLE") ?? "1280x820").Split('x');
// Aperçu du téléphone (SCOLIA_MOBILE=1, avec SCOLIA_TAILLE=412x892) : l'interface garde sa taille réelle et se range.
var window = new MainWindow { DataContext = shell, Width = int.Parse(taille[0]), Height = int.Parse(taille[1]) };
if (Preferences.MobileLayout) { window.MinWidth = 0; window.MinHeight = 0; window.Width = int.Parse(taille[0]); window.Height = int.Parse(taille[1]); }
window.Show();
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
    Wait(cashier.ChooseAsync(cashier.Results[0]));
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
        Wait(Task.Delay(400));
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
    var inscriptionDone = inscription.IsCompleted;
    if (!inscriptionDone) Console.WriteLine($"  (inscription non terminée : {inscriptionForm.Error})");
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
        f.Find("identifiant").Text = "smoke" + DateTime.Now.Second;
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
}

var students = Result(api.GetAsync<PagedResult<StudentListItem>>("api/apprenants?taille=1"));
var receipts = Result(api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements?taille=1"));
var firstStudent = students.Items[0].Id;
var firstReceipt = receipts.Items[0].Id;

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

// Boîtes de dialogue : recherche globale et formulaire d'inscription.
Wait(shell.Open(() => new DashboardPage(shell)));
shell.ShowSearch();
if (shell.Dialog is SearchViewModel search)
{
    search.Query = "ACC";
    Wait(Task.Delay(900));
}
Save(window, "24-recherche.png");
shell.CloseDialog();

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
shell.Backstage.Section = shell.Backstage.Items.First(i => i.Value == "poste");
Save(window, "26b-poste.png");
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
