using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Bureau.ViewModels;

internal static class FunctionalClientChecks
{
    public static void Run(string output)
    {
        var report = new List<string>();
        void Check(string name, bool valid)
        {
            if (!valid) throw new InvalidOperationException(name);
            report.Add("OK " + name);
        }
        using var server = new ScenarioServer();
        using var api = new ScoliaClient(server.Address);
        var shell = new ShellViewModel(api, new Preferences { Animations = false });
        // These transport tests exercise selections and writes, not authentication (covered by API tests).
        void Role(string role) => typeof(ShellViewModel).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(shell,
            new SessionDto(Guid.NewGuid(), "test", "Test", role, role, role == "Enseignant" ? [Perm.MarksEncode] : [Perm.AcademicsWrite], false, "Démonstration", "TEST", "2026-2027", "FC", "243"));
        Role("Secretariat");
        var courseId = Guid.NewGuid();
        var a = new AssessmentDto(Guid.NewGuid(), courseId, "Évaluation A", "Examen", 1, DateOnly.FromDateTime(DateTime.Today), 20, 1, 0);
        var b = a with { Id = Guid.NewGuid(), Title = "Évaluation B" };
        var enrollment = Guid.NewGuid();
        GradeSheetDto Sheet(AssessmentDto assessment, decimal score) => new(assessment, [new(enrollment, "CODE", assessment.Title, score, null)]);
        var releaseA = Signal();
        var releaseSave = Signal();
        server.Reply = async request =>
        {
            if (request.HttpMethod == "PUT") { await releaseSave.Task; return new { }; }
            if (request.Url!.AbsolutePath.Contains(a.Id.ToString(), StringComparison.Ordinal)) { await releaseA.Task; return Sheet(a, 3); }
            return Sheet(b, 12);
        };
        var grades = new GradesPage(shell) { Assessment = Row(a) };
        Until(() => server.Reads.Any(path => path.Contains(a.Id.ToString(), StringComparison.Ordinal)));
        Wait(grades.SaveGradesAsync());
        Check("Notes : aucun envoi avant le chargement de la feuille", server.Writes.IsEmpty);
        grades.Assessment = Row(b);
        Until(() => grades.Grades.Count == 1 && grades.Grades[0].FullName == b.Title);
        releaseA.SetResult();
        Pump();
        Check("Notes : une ancienne réponse ne mélange pas les évaluations", grades.Grades.Count == 1 && grades.Grades[0].Score == "12");
        grades.Grades[0].Score = "15";
        var saving = grades.SaveGradesAsync();
        Until(() => server.Writes.Count == 1);
        grades.Grades[0].Score = "17";
        releaseSave.SetResult();
        Wait(saving);
        Check("Notes : une saisie plus récente survit à la fin d'un enregistrement", grades.Grades[0].Score == "17");
        var write = server.Writes.Single();
        using var gradePayload = JsonDocument.Parse(write.Body);
        Check("Notes : l'envoi vise l'évaluation et les valeurs choisies", write.Path.Contains(b.Id.ToString(), StringComparison.Ordinal)
            && gradePayload.RootElement[0].GetProperty("score").GetDecimal() == 15);
        grades.Grades[0].Score = "21";
        Wait(grades.SaveGradesAsync());
        Check("Notes : une valeur supérieure au maximum n'est pas envoyée", server.Writes.Count == 1);

        var releaseCourse = Signal();
        var courseA = new CourseDto(Guid.NewGuid(), Guid.NewGuid(), "INF1", 1, "Cours A", 1, 30, 20, null, null, true, 1);
        var courseB = courseA with { Id = Guid.NewGuid(), Name = "Cours B" };
        server.Reply = async request =>
        {
            if (request.Url!.AbsolutePath.Contains(courseA.Id.ToString(), StringComparison.Ordinal)) { await releaseCourse.Task; return new[] { a }; }
            return new[] { b };
        };
        grades.Course = Course(courseA);
        grades.Course = Course(courseB);
        Until(() => grades.Assessments.Count == 1);
        releaseCourse.SetResult();
        Pump();
        Check("Cours : seule la dernière sélection fournit les évaluations", grades.Assessments.Count == 1 && grades.Assessments[0].Id == b.Id && grades.NoAssessment);

        var releaseMark = Signal();
        server.Reply = async request =>
        {
            var kind = request.QueryString["nature"] ?? "S1";
            if (kind == "S1") await releaseMark.Task;
            return new MarkSheetDto(courseId, "Cours", "Annuel", 20, kind, false, false, true,
                [new(enrollment, "CODE", null, "Anonyme", kind == "S1" ? 4 : 18, null, null, false)]);
        };
        var source = new DeliberationCourseDto(courseId, 1, "Cours", "Annuel", 20, null, 1, 0, 0, 0, false, false);
        var marks = new MarksPage(shell) { Course = new(courseId, "1", "Cours", "Annuel", "20", "", source) };
        marks.Kind = new ChoiceItem("S2", "Second semestre");
        Until(() => marks.Rows.Count == 1 && marks.Rows[0].Value == "18");
        releaseMark.SetResult();
        Pump();
        Check("Cotes : une ancienne feuille de S1 ne remplace pas S2", marks.Rows.Count == 1 && marks.Rows[0].Value == "18");

        var releaseBulkSave = Signal();
        server.Reply = async request => { await releaseBulkSave.Task; return new { }; };
        marks.Rows[0].Value = "13";
        var bulkWrites = server.Writes.Count;
        var savingMarks = ((AsyncCommand)marks.Actions.Single(a => a.Label == "Enregistrer les cotes").Command).ExecuteAsync();
        Until(() => server.Writes.Count > bulkWrites);
        marks.Rows[0].Value = "14";
        releaseBulkSave.SetResult();
        Wait(savingMarks);
        Check("Cotes : une modification pendant l'envoi reste affichée", marks.Rows[0].Value == "14" && marks.Rows[0].Original == "13");
        var bulkWrite = server.Writes.Last();
        using var markPayload = JsonDocument.Parse(bulkWrite.Body);
        Check("Cotes : le semestre envoyé correspond à la feuille modifiée", bulkWrite.Path.Contains("nature=S2", StringComparison.Ordinal)
            && markPayload.RootElement[0].GetProperty("value").GetDecimal() == 13);
        marks.Rows[0].Value = "13";
        Wait(((AsyncCommand)marks.Actions.Single(a => a.Label == "Enregistrer les cotes").Command).ExecuteAsync());
        Check("Cotes : une valeur déjà enregistrée n'est pas renvoyée", server.Writes.Count == bulkWrites + 1);

        Role("Enseignant");
        var releaseCode = Signal();
        server.Reply = async request =>
        {
            if (request.Url!.AbsolutePath.Contains("verifier-promotion", StringComparison.Ordinal))
            { await releaseCode.Task; return new CodeInCourseDto("ANCIEN", true, "Valide"); }
            return new MarkSheetDto(courseId, "Cours", "Annuel", 20, "S1", true, false, true, []);
        };
        var teacher = new MarksPage(shell) { Course = new(courseId, "1", "Cours", "Annuel", "20", "", source) };
        Pump();
        teacher.CodeQuery = "ANCIEN";
        var checking = teacher.CheckCode.ExecuteAsync();
        Until(() => server.Reads.Any(r => r.Contains("verifier-promotion", StringComparison.Ordinal)));
        teacher.CodeQuery = "NOUVEAU";
        releaseCode.SetResult();
        Wait(checking);
        Check("Code anonyme : une vérification ancienne ne valide pas un nouveau code", teacher.CodeQuery == "NOUVEAU" && !teacher.ShowScoreInput);

        var releaseCodeSave = Signal();
        server.Reply = async request =>
        {
            if (request.HttpMethod == "POST") { await releaseCodeSave.Task; return new MarkByCodeResult("CODE", "Cours", "S1", 10, null, 0); }
            return new MarkSheetDto(courseId, "Cours", "Annuel", 20, request.QueryString["nature"] ?? "S1", true, false, true,
                [new(enrollment, "CODE", null, null, null, null, null, false)]);
        };
        var nextTeacher = new MarksPage(shell) { Course = new(courseId, "1", "Cours", "Annuel", "20", "", source) };
        Until(() => nextTeacher.Rows.Count == 1);
        nextTeacher.CodeQuery = "CODE";
        Wait(nextTeacher.CheckCode.ExecuteAsync());
        nextTeacher.ScoreText = "10";
        var priorWrites = server.Writes.Count;
        var saveCode = nextTeacher.SaveCode.ExecuteAsync();
        Until(() => server.Writes.Count > priorWrites);
        nextTeacher.CodeQuery = "COPIE-SUIVANTE";
        releaseCodeSave.SetResult();
        Wait(saveCode);
        Check("Enseignant : enregistrer une cote ne supprime pas la copie suivante", nextTeacher.CodeQuery == "COPIE-SUIVANTE" && !nextTeacher.ShowScoreInput);

        priorWrites = server.Writes.Count;
        var submitting = ((AsyncCommand)nextTeacher.Actions.Single(a => a.Label == "Remettre les cotes").Command).ExecuteAsync();
        Until(() => shell.Dialog is FormViewModel);
        var confirmation = (FormViewModel)shell.Dialog!;
        nextTeacher.Kind = new ChoiceItem("S2", "Second semestre");
        confirmation.Close(true);
        Wait(submitting);
        Check("Remise : une confirmation ancienne ne fige pas une autre feuille", server.Writes.Count == priorWrites);
        Until(() => nextTeacher.Rows.Count == 1);

        var picked = new TaskCompletionSource<PickedFile?>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.PickFileHandler = _ => picked.Task;
        var importing = ((AsyncCommand)nextTeacher.Actions.Single(a => a.Label == "Reprendre des cotes (fichier)").Command).ExecuteAsync();
        nextTeacher.Kind = new ChoiceItem("S1", "Premier semestre");
        picked.SetResult(new PickedFile("notes.csv", Encoding.UTF8.GetBytes("CODE;10")));
        Wait(importing);
        Check("Import : un fichier choisi pour une ancienne feuille n'est pas envoyé au nouveau cours", server.Writes.Count == priorWrites);
        shell.PickFileHandler = null;
        Until(() => nextTeacher.Rows.Count == 1);

        Role("Secretariat");
        var promotion = Guid.NewGuid();
        var elementA = Guid.NewGuid();
        var elementB = Guid.NewGuid();
        var releaseElement = Signal();
        server.Reply = async request =>
        {
            var path = request.Url!.AbsolutePath;
            if (path == "/api/etablissement/promotions") return new[] { new { id = promotion, code = "L1", name = "L1" } };
            if (path.EndsWith("/programme", StringComparison.Ordinal)) return new[] {
                new { id = elementA, kind = "TeachingElement", code = "A", name = "EC A", semester = "S1" },
                new { id = elementB, kind = "TeachingElement", code = "B", name = "EC B", semester = "S1" } };
            var first = path.Contains(elementA.ToString(), StringComparison.Ordinal);
            if (first) await releaseElement.Task;
            return new LmdMarkSheetDto(first ? elementA : elementB, first ? "A" : "B", "EC", "UE", "S1", 1, 20, true, null,
                [new(enrollment, "CODE", "Étudiant", first ? 5 : 16, true)]);
        };
        var lmd = new LmdMarksPage(shell);
        var loading = lmd.LoadAsync();
        Until(() => lmd.Elements.Count == 2);
        lmd.Element = lmd.Elements[1];
        Until(() => lmd.Items.Count == 1 && lmd.Items[0].Score == "16");
        releaseElement.SetResult();
        Wait(loading);
        Check("LMD : les réponses d'EC différents ne se mélangent pas", lmd.Items.Count == 1 && lmd.Items[0].Score == "16");

        var releaseLmdSave = Signal();
        server.Reply = async request => { await releaseLmdSave.Task; return new { }; };
        lmd.Items[0].Score = "17";
        var lmdWrites = server.Writes.Count;
        var savingLmd = ((AsyncCommand)lmd.Actions.Single(a => a.Label == "Enregistrer les notes").Command).ExecuteAsync();
        Until(() => server.Writes.Count > lmdWrites);
        lmd.Items[0].Score = "18";
        releaseLmdSave.SetResult();
        Wait(savingLmd);
        Check("LMD : une modification pendant l'envoi reste affichée", lmd.Items[0].Score == "18" && lmd.Items[0].Original == 17);
        var lmdWrite = server.Writes.Last();
        using var lmdPayload = JsonDocument.Parse(lmdWrite.Body);
        Check("LMD : l'envoi conserve l'EC, la session et les valeurs choisis", lmdWrite.Path.Contains(elementB.ToString(), StringComparison.Ordinal)
            && lmdPayload.RootElement.GetProperty("session").GetInt32() == 1
            && lmdPayload.RootElement.GetProperty("lines")[0].GetProperty("score").GetDecimal() == 17);
        lmd.Items[0].Score = "17";
        Wait(((AsyncCommand)lmd.Actions.Single(a => a.Label == "Enregistrer les notes").Command).ExecuteAsync());
        Check("LMD : une valeur déjà enregistrée n'est pas renvoyée", server.Writes.Count == lmdWrites + 1);

        server.Reply = request =>
        {
            if (request.Url!.AbsolutePath.EndsWith("categories", StringComparison.Ordinal)) return Task.FromResult<object>(new[] { "Transport" });
            var filtered = request.QueryString["categorie"] == "Transport";
            var page = int.TryParse(request.QueryString["page"], out var number) ? number : 1;
            var total = filtered ? 1 : 401;
            var items = Enumerable.Range((page - 1) * 200, Math.Min(200, Math.Max(0, total - (page - 1) * 200)))
                .Select(i => new { id = Guid.NewGuid(), number = $"DP-{i}", date = DateOnly.FromDateTime(DateTime.Today), category = "Transport", beneficiary = "Test", description = "Test", amount = 10, method = "Especes", status = "Valide", recordedBy = "Test" }).ToArray();
            return Task.FromResult<object>(new { items, total, page, pageSize = 200, sum = total * 10 });
        };
        var expenses = new ExpensesPage(shell);
        Wait(expenses.LoadAsync());
        Check("Dépenses : le total comprend toutes les pages", expenses.Subtitle.Contains(Format.Money(4010, "FC"), StringComparison.Ordinal));
        Wait(expenses.NextPage.ExecuteAsync());
        Check("Dépenses : les lignes au-delà de 200 sont accessibles", expenses.Items.Count == 200 && expenses.Items[0].Number == "DP-200" && expenses.CanPrevious);
        Wait(expenses.NextPage.ExecuteAsync());
        Check("Dépenses : dernière page et limite de navigation correctes", expenses.Items.Count == 1 && !expenses.CanNext);
        expenses.Category = expenses.Categories[1];
        Until(() => expenses.Items.Count == 1 && !expenses.HasPages);
        Check("Dépenses : un filtre revient à la première page", expenses.Items[0].Number == "DP-0" && !expenses.CanPrevious);

        var about = new AboutViewModel(shell);
        shell.CopyHandler = null;
        Wait(about.CopyContacts.ExecuteAsync());
        Check("À propos : aucune fausse confirmation si la copie est indisponible", shell.Notice?.Contains("indisponible", StringComparison.Ordinal) == true);
        shell.CopyHandler = _ => throw new IOException("Presse-papiers indisponible pour le test.");
        Wait(about.CopyContacts.ExecuteAsync());
        Check("À propos : une erreur du presse-papiers ne ferme pas l'application", shell.Notice?.Contains("Impossible", StringComparison.Ordinal) == true);
        var copied = string.Empty;
        shell.CopyHandler = text => { copied = text; return Task.CompletedTask; };
        Wait(about.CopyContacts.ExecuteAsync());
        Check("À propos : les coordonnées copiées sont complètes", copied.Contains(AboutInfo.Developer, StringComparison.Ordinal)
            && copied.Contains(AboutInfo.Phone, StringComparison.Ordinal) && copied.Contains(AboutInfo.Email, StringComparison.Ordinal) && copied.Contains(AboutInfo.Website, StringComparison.Ordinal));
        var detachedView = new Scolia.Bureau.Views.MainView { DataContext = shell };
        Wait(about.CopyContacts.ExecuteAsync());
        Check("Presse-papiers natif : une fenêtre détachée ne confirme pas une copie fictive", shell.Notice?.Contains("Impossible", StringComparison.Ordinal) == true);
        detachedView.DataContext = null;
        File.WriteAllLines(Path.Combine(output, "fonctionnement-verifie.txt"), report);
        Console.WriteLine(string.Join(Environment.NewLine, report));
    }

    private static AssessmentRowVm Row(AssessmentDto a) => new(a.Id, a.Title, a.Kind, "1", "", "20", "1", "0", a);
    private static CourseRowVm Course(CourseDto c) => new(c.Id, "1", c.Name, "1", "30", "20", "", 1, c);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Until(Func<bool> predicate)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!predicate()) { if (DateTime.UtcNow > limit) throw new TimeoutException("Le scénario fonctionnel ne se termine pas."); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Pump() { for (var i = 0; i < 40; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); } }
}

internal sealed class ScenarioServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpListener listener = new();
    public Uri Address { get; }
    public Func<HttpListenerRequest, Task<object>> Reply { get; set; } = _ => Task.FromResult<object>(new { revision = 0 });
    public ConcurrentQueue<string> Reads { get; } = new();
    public ConcurrentQueue<(string Path, string Body)> Writes { get; } = new();
    public ScenarioServer()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Address = new Uri($"http://127.0.0.1:{port}/"); listener.Prefixes.Add(Address.AbsoluteUri); listener.Start(); _ = Serve();
    }
    private async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException) { return; }
            _ = Respond(context);
        }
    }
    private async Task Respond(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod == "GET") Reads.Enqueue(context.Request.RawUrl!);
            else { using var reader = new StreamReader(context.Request.InputStream); Writes.Enqueue((context.Request.RawUrl!, await reader.ReadToEndAsync())); }
            var reply = await Reply(context.Request);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply, JsonOptions));
            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException) { }
    }
    public void Dispose() => listener.Close();
}
