using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Cours d'une promotion, avec son coefficient et son enseignant.</summary>
public sealed record CourseRowVm(Guid Id, string Code, string Name, string Coefficient, string Hours, string Teacher, int Assessments, CourseDto Source);

/// <summary>Évaluation d'un cours : devoir, interrogation, examen.</summary>
public sealed record AssessmentRowVm(Guid Id, string Title, string Kind, string Term, string Date, string MaxScore, string Weight, string Graded, AssessmentDto Source);

/// <summary>Ligne de saisie des notes : la note et le commentaire se modifient directement dans le tableau.</summary>
public sealed class GradeEditRowVm(Guid enrollmentId, string matricule, string fullName, string score, string? comment) : Observable
{
    private string scoreText = score;
    private string commentText = comment ?? string.Empty;

    public Guid EnrollmentId { get; } = enrollmentId;
    public string Matricule { get; } = matricule;
    public string FullName { get; } = fullName;
    public string Score { get => scoreText; set => Set(ref scoreText, value); }
    public string Comment { get => commentText; set => Set(ref commentText, value); }
}

/// <summary>Cours et notes : cours d'une promotion, évaluations du cours et saisie des notes.</summary>
public sealed class GradesPage : PageViewModel
{
    private ChoiceItem? promotion;
    private CourseRowVm? course;
    private AssessmentRowVm? assessment;
    private string subtitle = string.Empty;
    private string sheetTitle = "Sélectionnez une évaluation";
    private bool ready;

    public GradesPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.AcademicsWrite))
        {
            Action("Nouveau cours", MaterialIconKind.FileDocumentPlusOutline, () => NewCourseAsync(Shell, Promotion is { } p ? Guid.Parse(p.Value) : (Guid?)null));
            Action("Nouvelle évaluation", MaterialIconKind.ClipboardPlusOutline, () => NewAssessmentAsync(Shell, Course?.Id));
            Action("Enregistrer les notes", MaterialIconKind.ContentSave, SaveGradesAsync, primary: true);
        }
        Action("Résultats", MaterialIconKind.CertificateOutline, () => Shell.Open(() => new ResultsPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Cours et notes";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.BookOpenVariantOutline;

    public ObservableCollection<CourseRowVm> Courses { get; } = [];
    public ObservableCollection<AssessmentRowVm> Assessments { get; } = [];
    public ObservableCollection<GradeEditRowVm> Grades { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public string SheetTitle { get => sheetTitle; private set => Set(ref sheetTitle, value); }

    public ChoiceItem? Promotion
    {
        get => promotion;
        set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); }
    }

    public CourseRowVm? Course
    {
        get => course;
        set
        {
            if (Set(ref course, value)) _ = Shell.Run(LoadAssessmentsAsync);
        }
    }

    public AssessmentRowVm? Assessment
    {
        get => assessment;
        set
        {
            if (Set(ref assessment, value)) _ = Shell.Run(LoadSheetAsync);
        }
    }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        if (Promotion is null) return;

        var courses = await Shell.Api.GetAsync<List<CourseDto>>("api/scolarite/cours" + ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value))));
        Courses.Clear();
        foreach (var c in courses)
            Courses.Add(new CourseRowVm(c.Id, c.Code, c.Name, c.Coefficient.ToString("0.##"), $"{c.Hours} h", c.TeacherName ?? "—", c.AssessmentCount, c));
        subtitle = $"{Format.Count(courses.Count, "cours", "cours")} · {Promotion.Label}";
        Raise(nameof(Subtitle));
        course = null;
        Raise(nameof(Course));
        Assessments.Clear();
        Grades.Clear();
        SheetTitle = "Sélectionnez un cours";
    }

    private async Task LoadAssessmentsAsync()
    {
        Assessments.Clear();
        Grades.Clear();
        assessment = null;
        Raise(nameof(Assessment));
        SheetTitle = "Sélectionnez une évaluation";
        if (Course is null) return;
        var list = await Shell.Api.GetAsync<List<AssessmentDto>>($"api/scolarite/cours/{Course.Id}/evaluations");
        foreach (var a in list)
            Assessments.Add(new AssessmentRowVm(a.Id, a.Title, Labels.Kind(a.Kind), $"Période {a.Term}", Format.Date(a.Date),
                a.MaxScore.ToString("0.##"), a.Weight.ToString("0.##"), $"{a.GradedCount}", a));
    }

    private async Task LoadSheetAsync()
    {
        Grades.Clear();
        if (Assessment is null) return;
        var sheet = await Shell.Api.GetAsync<GradeSheetDto>($"api/scolarite/evaluations/{Assessment.Id}/notes");
        SheetTitle = $"{sheet.Assessment.Title} · sur {sheet.Assessment.MaxScore:0.##}";
        foreach (var r in sheet.Rows)
            Grades.Add(new GradeEditRowVm(r.EnrollmentId, r.Matricule, r.FullName, r.Score?.ToString("0.##") ?? string.Empty, r.Comment));
        Raise(nameof(SheetTitle));
    }

    public async Task SaveGradesAsync()
    {
        if (Assessment is null)
        {
            Shell.Notify("Sélectionnez une évaluation avant d'enregistrer.");
            return;
        }
        var rows = new List<object>();
        foreach (var g in Grades)
        {
            if (!string.IsNullOrWhiteSpace(g.Score) && !Format.TryAmount(g.Score, out _))
            {
                Shell.Notify($"Note invalide pour {g.FullName} : utilisez un nombre.");
                return;
            }
            var score = Format.TryAmount(g.Score, out var value) ? (decimal?)value : null;
            rows.Add(new { enrollmentId = g.EnrollmentId, score, comment = string.IsNullOrWhiteSpace(g.Comment) ? null : g.Comment.Trim() });
        }
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/scolarite/evaluations/{Assessment.Id}/notes", rows);
        Shell.Notify("Notes enregistrées.");
        await LoadSheetAsync();
    }

    /// <summary>Crée un cours dans la promotion choisie.</summary>
    public static async Task NewCourseAsync(ShellViewModel shell, Guid? promotionId = null)
    {
        var promotions = (await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Select(Choices.Promotion).ToList();
        var teachers = await shell.Api.GetAsync<List<TeacherDto>>("api/enseignants");
        var form = new FormViewModel("Nouveau cours", "Le cours apparaît dans la promotion et sert au calcul des moyennes.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/cours", new
            {
                promotionId = Guid.Parse(f.ChoiceValue("promotion")!),
                code = f.Text("code"),
                name = f.Text("intitule"),
                coefficient = f.Amount("coefficient"),
                hours = (int)f.Amount("heures"),
                teacherId = f.ChoiceValue("enseignant") is { } t ? Guid.Parse(t) : (Guid?)null,
                isActive = true,
            });
            shell.Notify("Cours créé.");
            return true;
        });
        form.AddChoice("promotion", "Promotion", promotions, promotionId?.ToString(), required: true);
        form.Add("code", "Code", required: true, hint: "Quatre caractères au plus, par exemple MATH");
        form.Add("intitule", "Intitulé", required: true);
        form.Add("coefficient", "Coefficient", FieldKind.Amount, "1", required: true);
        form.Add("heures", "Volume horaire", FieldKind.Amount, "30", required: true);
        form.AddChoice("enseignant", "Enseignant", teachers.Select(t => new ChoiceItem(t.Id.ToString(), $"{t.Nom} · {t.Function}")).ToList());
        if (await shell.Ask(form) && shell.Current is GradesPage page) await page.RefreshAsync();
    }

    /// <summary>Ajoute une évaluation à un cours. Sans cours choisi, la boîte propose tous les cours.</summary>
    public static async Task NewAssessmentAsync(ShellViewModel shell, Guid? courseId = null)
    {
        var promotions = await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        var courses = new List<ChoiceItem>();
        foreach (var p in promotions)
        {
            var list = await shell.Api.GetAsync<List<CourseDto>>("api/scolarite/cours" + ScoliaClient.Query(("promotionId", p.Id)));
            courses.AddRange(list.Select(c => new ChoiceItem(c.Id.ToString(), $"{p.Code} · {c.Code} · {c.Name}")));
        }
        var form = new FormViewModel("Nouvelle évaluation", "Une évaluation reçoit ensuite les notes de chaque apprenant.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/evaluations", new
            {
                courseId = Guid.Parse(f.ChoiceValue("cours")!),
                title = f.Text("intitule"),
                kind = f.ChoiceValue("nature"),
                term = (int)f.Amount("periode"),
                date = f.Day("date"),
                maxScore = f.Amount("maximum"),
                weight = f.Amount("poids"),
            });
            shell.Notify("Évaluation créée.");
            return true;
        });
        form.AddChoice("cours", "Cours", courses, courseId?.ToString(), required: true);
        form.Add("intitule", "Intitulé", required: true);
        form.AddChoice("nature", "Type", Labels.AssessmentKinds, "Devoir", required: true);
        form.Add("periode", "Période", FieldKind.Amount, "1", required: true);
        form.AddDay("date", "Date", DateOnly.FromDateTime(DateTime.Today), required: true);
        form.Add("maximum", "Note maximale", FieldKind.Amount, "20", required: true);
        form.Add("poids", "Poids dans la moyenne", FieldKind.Amount, "1", required: true);
        if (await shell.Ask(form) && shell.Current is GradesPage page) await page.RefreshAsync();
    }
}

/// <summary>Ligne des résultats : rang, moyenne générale, décision et moyennes par cours.</summary>
public sealed record ResultRowVm(Guid EnrollmentId, string Rank, string Matricule, string Name, string Average, string Decision, IBrush DecisionBrush, string Courses);

/// <summary>Résultats et bulletins : moyennes, rangs, décisions, bulletins PDF.</summary>
public sealed class ResultsPage : PageViewModel
{
    private ChoiceItem? promotion;
    private ChoiceItem? term;
    private ResultRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;

    public ResultsPage(ShellViewModel shell) : base(shell)
    {
        Action("Bulletin PDF", MaterialIconKind.FilePdfBox, BulletinAsync, primary: true);
        ActionRefresh();
    }

    public override string Title => "Résultats et bulletins";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CertificateOutline;

    public ObservableCollection<ResultRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Terms { get; } =
    [
        new(string.Empty, "Année entière"), new("1", "Période 1"), new("2", "Période 2"), new("3", "Période 3"),
    ];

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Term { get => term; set { if (Set(ref term, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ResultRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            term = Terms[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Term));
            ready = true;
        }
        if (Promotion is null) return;

        var results = await Shell.Api.GetAsync<ResultsDto>("api/scolarite/resultats" + ScoliaClient.Query(
            ("promotionId", Guid.Parse(Promotion.Value)), ("periode", Term?.Value is { Length: > 0 } t ? int.Parse(t) : (int?)null)));

        Items.Clear();
        foreach (var r in results.Rows)
        {
            var courses = string.Join(" · ", results.Courses.Select((name, i) => $"{name} {Fmt(r.CourseAverages.ElementAtOrDefault(i))}"));
            Items.Add(new ResultRowVm(r.EnrollmentId, r.Rank is int rank ? $"{rank}" : "—", r.Matricule, r.FullName, Fmt(r.Average),
                r.Decision, ThemeBrush.Get(r.Decision.Contains("dmis", StringComparison.OrdinalIgnoreCase) ? "ToneGreen" : "ToneOrange"), courses));
        }
        subtitle = $"{results.Promotion} · moyenne de classe {Fmt(results.ClassAverage)} · {results.Passed} admis";
        Raise(nameof(Subtitle));
    }

    private static string Fmt(decimal? value) => value is { } v ? v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) : "—";

    private async Task BulletinAsync()
    {
        if (Selected is null || Promotion is null)
        {
            Shell.Notify("Sélectionnez un apprenant pour produire son bulletin.");
            return;
        }
        var query = ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value)), ("inscriptionId", Selected.EnrollmentId),
            ("periode", Term?.Value is { Length: > 0 } t ? int.Parse(t) : (int?)null));
        await Shell.Download($"api/scolarite/bulletin.pdf{query}", $"bulletin-{Selected.Matricule}.pdf");
    }
}

/// <summary>Ligne de présence : statut modifiable et remarque.</summary>
public sealed class AttendanceEditRowVm(Guid enrollmentId, string matricule, string fullName, ChoiceItem initialStatus, string? note) : Observable
{
    private ChoiceItem statusChoice = initialStatus;
    private string noteText = note ?? string.Empty;

    public Guid EnrollmentId { get; } = enrollmentId;
    public string Matricule { get; } = matricule;
    public string FullName { get; } = fullName;
    public IReadOnlyList<ChoiceItem> Choices => Labels.Attendance;
    public ChoiceItem Status { get => statusChoice; set => Set(ref statusChoice, value); }
    public string Note { get => noteText; set => Set(ref noteText, value); }
}

/// <summary>Bilan d'assiduité d'un apprenant sur la période.</summary>
public sealed record AttendanceSummaryRowVm(string Matricule, string Name, string Present, string Late, string Absent, string Justified, string Rate, IBrush RateBrush);

/// <summary>Présences : appel quotidien par promotion, et bilan d'assiduité sur 30 jours.</summary>
public sealed class AttendancePage : PageViewModel
{
    private ChoiceItem? promotion;
    private DateTime? day = DateTime.Now;
    private bool ready;
    private bool showSummary;
    private string subtitle = string.Empty;

    public AttendancePage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.AcademicsWrite))
        {
            Action("Tous présents", MaterialIconKind.AccountCheckOutline, MarkAllPresentAsync);
            Action("Enregistrer l'appel", MaterialIconKind.ContentSave, SaveAsync, primary: true);
        }
        ActionRefresh();
    }

    public override string Title => "Présences";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ClipboardCheckOutline;

    public ObservableCollection<AttendanceEditRowVm> Rows { get; } = [];
    public ObservableCollection<AttendanceSummaryRowVm> Summary { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Statuses { get; } = Labels.Attendance;

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public DateTime? Day { get => day; set { if (Set(ref day, value) && ready) _ = Shell.Run(RefreshAsync); } }

    /// <summary>Vrai pour afficher le bilan, faux pour la feuille d'appel.</summary>
    public bool ShowSummary
    {
        get => showSummary;
        set { if (Set(ref showSummary, value) && ready) _ = Shell.Run(RefreshAsync); }
    }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        if (Promotion is null) return;

        var date = ToDay(Day);
        var promotionId = Guid.Parse(Promotion.Value);
        if (ShowSummary)
        {
            var bilan = await Shell.Api.GetAsync<List<AttendanceSummaryDto>>("api/scolarite/presences/bilan" + ScoliaClient.Query(
                ("promotionId", promotionId), ("du", date.AddDays(-30)), ("au", date)));
            Summary.Clear();
            foreach (var b in bilan)
                Summary.Add(new AttendanceSummaryRowVm(b.Matricule, b.FullName, $"{b.Present}", $"{b.Late}", $"{b.Absent}", $"{b.Justified}",
                    $"{b.Rate:0.0} %", ThemeBrush.Get(b.Rate >= 90 ? "ToneGreen" : b.Rate >= 75 ? "ToneOrange" : "ToneRed")));
            subtitle = $"Bilan du {Format.Date(date.AddDays(-30))} au {Format.Date(date)} · {Promotion.Label}";
        }
        else
        {
            var sheet = await Shell.Api.GetAsync<AttendanceSheetDto>("api/scolarite/presences" + ScoliaClient.Query(("promotionId", promotionId), ("date", date)));
            Rows.Clear();
            foreach (var r in sheet.Rows)
            {
                var status = Statuses.FirstOrDefault(s => s.Value == r.Status) ?? Statuses[0];
                Rows.Add(new AttendanceEditRowVm(r.EnrollmentId, r.Matricule, r.FullName, status, r.Note));
            }
            var absent = Rows.Count(r => r.Status.Value == "Absent");
            subtitle = $"Appel du {Format.Date(date)} · {Promotion.Label} · {absent} absent(s)";
        }
        Raise(nameof(Subtitle));
    }

    private Task MarkAllPresentAsync()
    {
        foreach (var r in Rows) r.Status = Statuses[0];
        Shell.Notify("Tous les apprenants sont marqués présents. Enregistrez l'appel pour le conserver.");
        return Task.CompletedTask;
    }

    public async Task SaveAsync()
    {
        if (Promotion is null) return;
        var date = ToDay(Day);
        var rows = Rows.Select(r => new { enrollmentId = r.EnrollmentId, status = r.Status.Value, note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim() }).ToList();
        await Shell.Api.SendAsync(HttpMethod.Put, "api/scolarite/presences" + ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value)), ("date", date)), rows);
        Shell.Notify("Appel enregistré.");
    }
}

/// <summary>Ligne d'incident de discipline.</summary>
public sealed record IncidentRowVm(string Date, string Matricule, string Name, string Category, string Description, string Sanction, string Severity, IBrush SeverityBrush, string RecordedBy);

/// <summary>Discipline : incidents par promotion, avec sanction et gravité.</summary>
public sealed class DisciplinePage : PageViewModel
{
    private ChoiceItem? promotion;
    private bool ready;
    private string subtitle = string.Empty;

    public DisciplinePage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.AcademicsWrite)) Action("Nouvel incident", MaterialIconKind.Plus, () => NewIncidentAsync(Shell), primary: true);
        ActionRefresh();
    }

    public override string Title => "Discipline";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AlertOctagonOutline;

    public ObservableCollection<IncidentRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }

        var rows = await Shell.Api.GetAsync<List<IncidentDto>>("api/scolarite/discipline" + ScoliaClient.Query(("promotionId", Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : (Guid?)null)));
        Items.Clear();
        foreach (var i in rows)
            Items.Add(new IncidentRowVm(Format.Date(i.Date), i.Matricule, i.FullName, i.Category, i.Description, i.Sanction ?? "—",
                Labels.Severity(i.Severity), ThemeBrush.Get(Labels.Tone(i.Severity) switch { "red" => "ToneRed", "orange" => "ToneOrange", _ => "ToneNeutral" }), i.RecordedBy));
        subtitle = $"{Format.Count(rows.Count, "incident", "incidents")} · {rows.Count(r => r.Severity == "Grave")} grave(s)";
        Raise(nameof(Subtitle));
    }

    /// <summary>Signale un incident pour un apprenant actif.</summary>
    public static async Task NewIncidentAsync(ShellViewModel shell)
    {
        var students = await shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + ScoliaClient.Query(("statut", "Actif"), ("taille", 500)));
        var choices = students.Items.Where(s => s.EnrollmentId is not null).Select(Choices.Student).ToList();
        var form = new FormViewModel("Nouvel incident", "L'incident est consigné avec son auteur et ne peut pas être effacé.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/discipline", new
            {
                enrollmentId = Guid.Parse(f.ChoiceValue("apprenant")!),
                date = f.Day("date"),
                category = f.Text("categorie"),
                description = f.Text("description"),
                sanction = f.OptionalText("sanction"),
                severity = f.ChoiceValue("gravite"),
            });
            shell.Notify("Incident enregistré.");
            return true;
        });
        form.AddChoice("apprenant", "Apprenant", choices, null, required: true);
        form.AddDay("date", "Date", DateOnly.FromDateTime(DateTime.Today), required: true);
        form.Add("categorie", "Catégorie", required: true);
        form.Add("description", "Description", FieldKind.Multiline, required: true);
        form.Add("sanction", "Sanction");
        form.AddChoice("gravite", "Gravité", Labels.Severities, "Mineur", required: true);
        if (await shell.Ask(form) && shell.Current is DisciplinePage page) await page.RefreshAsync();
    }
}
