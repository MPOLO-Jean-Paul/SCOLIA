using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Cours d'une promotion, avec son numéro, son coefficient, son volume et son enseignant.</summary>
public sealed record CourseRowVm(Guid Id, string Number, string Name, string Coefficient, string Hours, string MaxScore, string Teacher, int Assessments, CourseDto Source);

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
            Action("Attributions et charges", MaterialIconKind.AccountSchoolOutline, () => Shell.Open(() => new TeachingPage(Shell)));
            Action("Nouvelle évaluation", MaterialIconKind.ClipboardPlusOutline, () => NewAssessmentAsync(Shell, Course?.Id));
            Action("Enregistrer les notes", MaterialIconKind.ContentSave, SaveGradesAsync, primary: true);
        }
        Action("Résultats", MaterialIconKind.CertificateOutline, () => Shell.Open(() => new ResultsPage(Shell)));
        Action("Cartes d'accès", MaterialIconKind.CardAccountDetailsOutline, () => Shell.Open(() => new CardsPage(Shell)));
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
            Courses.Add(new CourseRowVm(c.Id, c.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Name, c.Coefficient.ToString("0.##"), $"{c.Hours:0.##} h",
                c.MaxScore.ToString("0.##"), c.TeacherName ?? "—", c.AssessmentCount, c));
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

    /// <summary>Crée un cours dans la promotion choisie. Le numéro proposé est le premier libre de la promotion.</summary>
    public static async Task NewCourseAsync(ShellViewModel shell, Guid? promotionId = null)
    {
        var promotions = (await shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions")).Select(Choices.Promotion).ToList();
        var next = promotionId is { } pid
            ? (await shell.Api.GetAsync<NextNumberDto>($"api/scolarite/cours/numero-suivant?promotionId={pid}")).Numero
            : 1;
        var form = new FormViewModel("Nouveau cours", "Le cours est rattaché à une promotion. Son numéro reste réservé même après archivage.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/cours", new
            {
                promotionId = Guid.Parse(f.ChoiceValue("promotion")!),
                number = (int)f.Amount("numero"),
                name = f.Text("intitule"),
                coefficient = f.Amount("coefficient"),
                hours = f.Amount("heures"),
                maxScore = f.Amount("cotation"),
            });
            shell.Notify("Cours créé. Attribuez-le à un enseignant depuis Attributions et charges.");
            return true;
        });
        form.AddChoice("promotion", "Promotion", promotions, promotionId?.ToString(), required: true);
        form.Add("numero", "Numéro du cours", FieldKind.Amount, next.ToString(System.Globalization.CultureInfo.InvariantCulture), required: true, hint: "De 1 à 9999, unique dans la promotion.");
        form.Add("intitule", "Intitulé", required: true);
        form.Add("coefficient", "Coefficient", FieldKind.Amount, "1", required: true);
        form.Add("heures", "Volume horaire annuel", FieldKind.Amount, "30", required: true, hint: "Peut comporter une décimale, par exemple 30,5.");
        form.Add("cotation", "Cotation maximale (points)", FieldKind.Amount, "20", required: true);
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
            courses.AddRange(list.Select(c => new ChoiceItem(c.Id.ToString(), $"{p.Code} · {c.Number} · {c.Name}")));
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
            Action("Confirmer le jour de cours", MaterialIconKind.CalendarCheckOutline, () => ConfirmDayAsync(true));
            Action("Retirer la confirmation", MaterialIconKind.CalendarRemoveOutline, () => ConfirmDayAsync(false));
        }
        Action("Apprenants à traiter", MaterialIconKind.AlertCircleOutline, ShowAlertsAsync);
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
                // Une absence non signée ou une exclusion se saisit comme absence : l'enregistrement fixe le statut.
                var status = r.Status switch
                {
                    "Absence non signée" or "Exclu (discipline)" => Statuses.First(s => s.Value == "Absent"),
                    _ => Statuses.FirstOrDefault(s => s.Value == r.Status) ?? Statuses[0],
                };
                var note = r.Status == "Exclu (discipline)" ? "Exclu par une décision disciplinaire" : r.Note;
                Rows.Add(new AttendanceEditRowVm(r.EnrollmentId, r.Matricule, r.FullName, status, note));
            }
            var absent = Rows.Count(r => r.Status.Value == "Absent");
            var dayState = sheet.CourseDay ? "jour de cours confirmé" : "jour non confirmé";
            subtitle = $"Appel du {Format.Date(date)} · {Promotion.Label} · {absent} absent(s) · {dayState}";
        }
        Raise(nameof(Subtitle));
    }

    private Task MarkAllPresentAsync()
    {
        foreach (var r in Rows) r.Status = Statuses[0];
        Shell.Notify("Tous les apprenants sont marqués présents. Enregistrez l'appel pour le conserver.");
        return Task.CompletedTask;
    }

    /// <summary>Confirme ou retire le jour de cours : seul un jour confirmé et passé sans appel produit une absence non signée.</summary>
    private async Task ConfirmDayAsync(bool confirm)
    {
        if (Promotion is null) return;
        var date = ToDay(Day);
        await Shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/jours-de-cours" + (confirm ? string.Empty : "/retrait") + ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value))), new[] { date });
        Shell.Notify(confirm ? "Jour de cours confirmé." : "Confirmation retirée.");
        await RefreshAsync();
    }

    /// <summary>Apprenants dont les absences non justifiées atteignent le seuil, sur les 30 derniers jours.</summary>
    private async Task ShowAlertsAsync()
    {
        if (Promotion is null) return;
        var date = ToDay(Day);
        var alerts = await Shell.Api.GetAsync<List<AttendanceAlertDto>>("api/scolarite/presences/alertes" + ScoliaClient.Query(
            ("promotionId", Guid.Parse(Promotion.Value)), ("du", date.AddDays(-30)), ("au", date)));
        var form = new FormViewModel("Apprenants à traiter", "Seuils : 3 absences non justifiées, ou 3 jours de cours consécutifs absents. Période : 30 jours.", _ => Task.FromResult(true))
        {
            Message = alerts.Count == 0
                ? "Aucun apprenant ne dépasse les seuils."
                : string.Join(Environment.NewLine, alerts.Select(a => $"{a.Matricule} · {a.FullName} : {a.UnjustifiedAbsences} absence(s) non justifiée(s), {a.LongestRun} jour(s) consécutif(s)")),
            SubmitText = "Fermer",
            ShowCancel = false,
        };
        await Shell.Ask(form);
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
/// <summary>Ligne du registre disciplinaire, prête à afficher.</summary>
public sealed record DisciplineRowVm(Guid Id, Guid EnrollmentId, string Date, string Matricule, string Name, string Measure, string Reason,
    string Period, string Duration, string ReturnFrom, string Reception, string State, string Status, string Origin,
    bool IsConvocation, bool CanReceive, bool CanCancel);

/// <summary>Registre disciplinaire : renvois temporaires et définitifs, convocations et réception des parents (V79 : Scolarité > Discipline).</summary>
public sealed class DisciplinePage : PageViewModel
{
    private static readonly IReadOnlyList<ChoiceItem> MeasureChoices =
    [
        new("", "Toutes les mesures"), new("RenvoiTemporaire", "Renvoi temporaire"), new("RenvoiDefinitif", "Renvoi définitif"), new("Convocation", "Convocation"),
    ];

    private static readonly IReadOnlyList<ChoiceItem> ReceptionChoices =
    [
        new("Recu", "Parent reçu"), new("Absent", "Parent absent"),
    ];

    private ChoiceItem? promotion;
    private ChoiceItem? measure;
    private DisciplineRowVm? selected;
    private bool ready;
    private string subtitle = string.Empty;

    public DisciplinePage(ShellViewModel shell) : base(shell)
    {
        measure = MeasureChoices[0];
        if (Shell.Can(Perm.AcademicsWrite))
        {
            Action("Nouvelle décision", MaterialIconKind.Plus, () => NewDecisionAsync(Shell), primary: true);
            Action("Parent reçu ou absent", MaterialIconKind.AccountCheckOutline, ReceptionAsync);
        }
        Action("Convocation à imprimer", MaterialIconKind.Printer, PrintConvocationAsync);
        Action("Historique de l'apprenant", MaterialIconKind.History, ShowHistoryAsync);
        if (Shell.Can(Perm.DisciplineCancel)) Action("Annuler la décision", MaterialIconKind.Cancel, CancelAsync);
        Action("Exporter en CSV", MaterialIconKind.FileExportOutline, ExportAsync);
        ActionRefresh();
    }

    public override string Title => "Discipline";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AlertOctagonOutline;

    public ObservableCollection<DisciplineRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Measures => MeasureChoices;

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Measure { get => measure; set { if (Set(ref measure, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public DisciplineRowVm? Selected { get => selected; set => Set(ref selected, value); }

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

        var rows = await Shell.Api.GetAsync<List<DisciplineRowDto>>("api/scolarite/discipline/registre" + ScoliaClient.Query(
            ("promotionId", Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : (Guid?)null),
            ("mesure", Measure?.Value is { Length: > 0 } m ? m : null)));
        Items.Clear();
        foreach (var r in rows)
            Items.Add(new DisciplineRowVm(r.Id, r.EnrollmentId, Day(r.DecisionDate), r.Matricule, r.FullName, r.Measure, r.Reason,
                r.FirstDay is { } first && r.LastDay is { } last ? $"{Day(first)} au {Day(last)}" : r.AppointmentAt is { } at ? $"RDV {at:dd/MM/yyyy HH:mm}" : "—",
                r.Days is { } days ? $"{days} j" : "—", Day(r.ReturnFrom), r.Reception, r.State, r.EffectiveStatus,
                r.IsAutomatic ? "Automatique" : "Décidé", r.Measure == "Convocation", r.CanReceive, r.CanCancel));
        var definitive = rows.Count(r => r.Measure == "Renvoi définitif" && r.State == "Validée");
        subtitle = $"{Format.Count(rows.Count, "décision", "décisions")} · {definitive} renvoi(s) définitif(s)";
        Raise(nameof(Subtitle));
    }

    private static string Day(DateOnly? day) => day is { } d ? Format.Date(d) : "—";

    /// <summary>Nouvelle décision : renvoi temporaire, renvoi définitif ou convocation. Le serveur applique les règles.</summary>
    public static async Task NewDecisionAsync(ShellViewModel shell)
    {
        var students = await shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + ScoliaClient.Query(("statut", "Actif"), ("taille", 500)));
        var choices = students.Items.Where(s => s.EnrollmentId is not null).Select(Choices.Student).ToList();
        // Clé de répétition : un double clic sur Enregistrer ne crée pas une seconde sanction.
        var requestKey = Guid.NewGuid().ToString();
        var form = new FormViewModel("Nouvelle décision", "Le registre garde chaque décision. Pour corriger, annulez-la puis enregistrez-la à nouveau.", async f =>
        {
            var measureValue = f.ChoiceValue("mesure")!;
            var isConvocation = measureValue == "Convocation";
            var isRenvoi = !isConvocation;
            DateOnly? OptionalDay(string key) => f.Day(key);
            DateTime? appointment = null;
            if (isConvocation)
            {
                var time = TimeSpan.TryParse(f.Text("heure"), System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.FromHours(9);
                appointment = (f.Day("rdv") ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue) + time;
            }
            await shell.Api.SendAsync(HttpMethod.Post, "api/scolarite/discipline/registre", new
            {
                enrollmentId = Guid.Parse(f.ChoiceValue("apprenant")!),
                measure = measureValue,
                reason = f.Text("motif"),
                decisionDate = OptionalDay("date") ?? DateOnly.FromDateTime(DateTime.Today),
                firstDay = isRenvoi && measureValue == "RenvoiTemporaire" ? OptionalDay("premier") : null,
                lastDay = isRenvoi && measureValue == "RenvoiTemporaire" ? OptionalDay("dernier") : null,
                appointmentAt = appointment,
                confirmDefinitive = f.Flag("confirme"),
                requestKey,
            });
            shell.Notify("Décision enregistrée dans le registre.");
            return true;
        });
        form.AddChoice("apprenant", "Apprenant", choices, null, required: true);
        form.AddChoice("mesure", "Mesure", MeasureChoices.Skip(1).ToList(), "RenvoiTemporaire", required: true);
        form.Add("motif", "Motif", FieldKind.Multiline, required: true, hint: "Entre 5 et 2000 caractères.");
        form.AddDay("date", "Date de la décision", DateOnly.FromDateTime(DateTime.Today), required: true);
        form.AddDay("premier", "Premier jour du renvoi", DateOnly.FromDateTime(DateTime.Today));
        form.AddDay("dernier", "Dernier jour du renvoi", DateOnly.FromDateTime(DateTime.Today));
        form.AddDay("rdv", "Date du rendez-vous", DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
        form.Add("heure", "Heure du rendez-vous", value: "09:00");
        form.AddFlag("confirme", "Je confirme le renvoi définitif ou le renvoi automatique", false);
        if (await shell.Ask(form) && shell.Current is DisciplinePage page) await page.RefreshAsync();
    }

    private async Task ReceptionAsync()
    {
        if (Selected is not { IsConvocation: true, CanReceive: true } row)
        {
            Shell.Notify("Sélectionnez une convocation en attente de réponse.");
            return;
        }
        var form = new FormViewModel("Parent reçu ou absent", $"Convocation de {row.Name} · {row.Period}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/scolarite/discipline/registre/{row.Id}/reception", new { reception = f.ChoiceValue("issue"), note = f.Text("note") });
            Shell.Notify("Suite de la convocation enregistrée.");
            return true;
        });
        form.AddChoice("issue", "Issue", ReceptionChoices, "Recu", required: true);
        form.Add("note", "Compte rendu", FieldKind.Multiline, required: true, hint: "Obligatoire pour toute réception ou absence.");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task PrintConvocationAsync()
    {
        if (Selected is not { IsConvocation: true } row)
        {
            Shell.Notify("Sélectionnez une convocation.");
            return;
        }
        var bytes = await Shell.Api.DownloadAsync($"api/scolarite/discipline/registre/{row.Id}/convocation");
        Files.Open(Files.Save(bytes, $"convocation-{row.Matricule}.html"));
    }

    private async Task ShowHistoryAsync()
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez une ligne pour voir l'historique de l'apprenant.");
            return;
        }
        var history = await Shell.Api.GetAsync<List<DisciplineRowDto>>($"api/scolarite/discipline/inscriptions/{row.EnrollmentId}/historique");
        var lines = history.Select(h => $"{Day(h.DecisionDate)} · {h.Measure} · {h.State}" + (h.CancelReason is null ? string.Empty : $" (motif d'annulation : {h.CancelReason})"));
        var form = new FormViewModel($"Historique : {row.Name}", $"{history.Count} décision(s), toutes années confondues.", _ => Task.FromResult(true))
        {
            Message = string.Join(Environment.NewLine, lines), SubmitText = "Fermer", ShowCancel = false,
        };
        await Shell.Ask(form);
    }

    private async Task CancelAsync()
    {
        if (Selected is not { CanCancel: true } row)
        {
            Shell.Notify("Sélectionnez une décision validée à annuler.");
            return;
        }
        var form = new FormViewModel("Annuler la décision", "Réservé à l'administrateur. La décision reste dans l'historique avec son motif.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/scolarite/discipline/registre/{row.Id}/annulation", new { reason = f.Text("motif") });
            Shell.Notify("Décision annulée. Elle reste consultable dans l'historique.");
            return true;
        }) { Danger = true, SubmitText = "Annuler la décision" };
        form.Add("motif", "Motif de l'annulation", FieldKind.Multiline, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ExportAsync()
    {
        var bytes = await Shell.Api.DownloadAsync("api/scolarite/discipline/registre/export.csv" + ScoliaClient.Query(
            ("promotionId", Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : (Guid?)null),
            ("mesure", Measure?.Value is { Length: > 0 } m ? m : null)));
        Files.Save(bytes, "discipline.csv");
        Shell.Notify("Export enregistré dans Téléchargements.");
    }
}
