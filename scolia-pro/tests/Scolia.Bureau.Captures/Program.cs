using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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
    // Plusieurs cycles de rendu : la mise en page des listes se termine après le premier.
    for (var i = 0; i < 6; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(30);
    }
    var frame = window.CaptureRenderedFrame();
    if (frame is null) throw new InvalidOperationException($"Rendu indisponible pour {name}.");
    frame.Save(Path.Combine(output, name));
    Console.WriteLine($"capturé : {name} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
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
var shell = new ShellViewModel(api, prefs);
// Taille de fenêtre optionnelle (SCOLIA_TAILLE=1024x700) pour vérifier l'adaptation du texte à chaque format d'écran.
var taille = (Environment.GetEnvironmentVariable("SCOLIA_TAILLE") ?? "1280x820").Split('x');
var window = new MainWindow { DataContext = shell, Width = int.Parse(taille[0]), Height = int.Parse(taille[1]) };
window.Show();
// Premier lancement sans configuration : l'assistant remplace la connexion (SCOLIA_ASSISTANT=1).
if (Environment.GetEnvironmentVariable("SCOLIA_ASSISTANT") == "1")
{
    shell.IsSetupRequired = true;
    Wait(shell.Setup.LoadAsync());
    Save(window, "00-assistant-etape-1.png");
    shell.Setup.SchoolName = "Institut de test";
    shell.Setup.ShortName = "IT";
    Wait(Task.Delay(50));
    shell.Setup.Step = 2;
    Save(window, "00-assistant-etape-2.png");
    return;
}
shell.Login.SchoolName = Result(api.GetAsync<SetupStatusDto>("api/installation")).SchoolName ?? string.Empty;
Save(window, "01-connexion.png");

if (args.Length > 3 && args[3] == "smoke")
{
    Smoke(shell, api, window, password);
    window.Close();
    return;
}

var session = Result(api.LoginAsync("admin", password));
Wait(shell.OpenSessionAsync(session));
Save(window, "02-tableau-de-bord.png");
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
    years.Section = years.Sections[1];
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
        f.Find("telephone").Text = "+243 800 111 222";
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
        f.Find("code").Text = "T" + DateTime.Now.ToString("ss");
        f.Find("intitule").Text = "Cours de test";
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
void Page(Func<PageViewModel> create, string name, Action<PageViewModel>? prepare = null)
{
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
Page(() => new SettingsPage(shell), "20-parametres-annees.png", p => ((SettingsPage)p).Section = ((SettingsPage)p).Sections[1]);
Page(() => new UsersPage(shell), "21-utilisateurs.png");
Page(() => new DataPage(shell), "22-donnees.png");
Page(() => new EraseDataPage(shell, payments: true), "22b-effacer-donnees.png");
Page(() => new EraseDataPage(shell, payments: false), "22c-effacer-apprenants.png");
Page(() => new AuditPage(shell), "23-audit.png");

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

var form = new FormViewModel("Nouvelle inscription", "Le matricule est attribué automatiquement à l'enregistrement.", _ => Task.FromResult(false)) { SubmitText = "Inscrire" };
form.Add("nom", "Nom", required: true);
form.Add("prenom", "Prénom", required: true);
form.AddChoice("sexe", "Sexe", Labels.Genders, "M", required: true);
form.AddDay("naissance", "Date de naissance", null);
form.AddChoice("promotion", "Promotion", Result(api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Select(Choices.Promotion).ToList(), null, required: true);
form.Add("notes", "Observations", FieldKind.Multiline);
_ = shell.Ask(form);
Save(window, "25-formulaire.png");
form.Close(false);
Wait(Task.Delay(100));

shell.OpenBackstage();
Save(window, "26-fichier.png");
shell.Backstage.Section = shell.Backstage.Items.First(i => i.Value == "poste");
Save(window, "26b-poste.png");
shell.CloseBackstage();

// Chaque onglet du ruban : le contenu doit être le même que dans le catalogue web.
Wait(shell.Open(() => new DashboardPage(shell)));
foreach (var tab in shell.Tabs.Where(t => t.Id is "secretariat" or "finances" or "scolarite" or "administration" or "affichage" or "aide"))
{
    shell.SelectTab(tab);
    Save(window, $"ruban-{tab.Id}.png");
}

// Thème sombre : même écran, palette inversée.
prefs.Theme = "dark";
Wait(shell.Open(() => new CashPage(shell)));
Save(window, "27-sombre-caisse.png");
prefs.Theme = "light";

window.Close();
Console.WriteLine("terminé");
