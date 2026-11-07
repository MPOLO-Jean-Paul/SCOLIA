using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Apprenant inscrit et son code anonyme officiel.</summary>
public sealed record AnonymousCodeRowVm(Guid EnrollmentId, string Promotion, string Matricule, string FullName, string Code, string Origin, bool HasCode);

/// <summary>
/// Codes anonymes (fusion d'ITMCD) : les apprenants déjà inscrits dans SCOLIA sont importés dans la délibération
/// et reçoivent un code officiel. La copie d'examen porte ce code : l'encodage se fait sans connaître le nom.
/// </summary>
public sealed class AnonymousCodesPage : PageViewModel
{
    private ChoiceItem? promotion;
    private AnonymousCodeRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;

    public AnonymousCodesPage(ShellViewModel shell) : base(shell)
    {
        Action("Importer et attribuer les codes", MaterialIconKind.AccountArrowDown, AssignAsync, primary: true);
        Action("Saisir un code officiel", MaterialIconKind.Pencil, () => ChangeAsync(manual: true), row: true, open: true);
        Action("Tirer un nouveau code", MaterialIconKind.Dice5, () => ChangeAsync(manual: false), row: true);
        Action("Liste des codes (PDF)", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        Action("Liste des codes (Excel)", MaterialIconKind.FileExcel, () => ExportAsync("xlsx"));
        ActionRefresh();
    }

    public override string Title => "Codes anonymes";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Incognito;

    public ObservableCollection<AnonymousCodeRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public AnonymousCodeRowVm? Selected { get => selected; set => Set(ref selected, value); }

    private Guid? PromotionId => Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : null;

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        var rows = await Shell.Api.GetAsync<List<AnonymousCodeRowDto>>("api/deliberation/codes" + ScoliaClient.Query(("promotionId", PromotionId)));
        Items.Clear();
        foreach (var r in rows)
            Items.Add(new AnonymousCodeRowVm(r.EnrollmentId, r.PromotionCode, r.Matricule, r.FullName, r.Code ?? "—",
                r.Code is null ? "À attribuer" : r.IsManual ? "Code officiel saisi" : "Tiré par SCOLIA", r.Code is not null));
        var missing = rows.Count(r => r.Code is null);
        subtitle = $"{Format.Count(rows.Count, "apprenant inscrit", "apprenants inscrits")} · {(missing == 0 ? "tous ont un code" : $"{missing} sans code")}";
        Raise(nameof(Subtitle));
        Selected = null;
    }

    private async Task AssignAsync()
    {
        var scope = Promotion is { Value.Length: > 0 } p ? $"la promotion {p.Label}" : "toutes les promotions de l'année";
        if (!await Shell.Confirm("Importer les apprenants", $"Les apprenants inscrits dans {scope} reçoivent chacun un code anonyme officiel. Les codes déjà attribués ne changent pas.", "Attribuer les codes"))
            return;
        var result = await Shell.Api.SendAsync<CodeAssignmentResult>(HttpMethod.Post, "api/deliberation/codes/attribution", new { promotionId = PromotionId });
        Shell.Notify(result.Assigned == 0 ? "Tous les apprenants avaient déjà un code." : $"{Format.Count(result.Assigned, "code attribué", "codes attribués")} sur {result.Total} apprenant(s).");
        await RefreshAsync();
    }

    private async Task ChangeAsync(bool manual)
    {
        if (Selected is not { } row) return;
        var form = new FormViewModel(manual ? "Code officiel" : "Nouveau code", $"{row.FullName} · {row.Matricule}. Un code remplacé n'est plus jamais attribué cette année.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/deliberation/codes/{row.EnrollmentId}", new
            {
                code = manual ? f.Text("code") : null,
                reason = row.HasCode ? f.OptionalText("motif") : null,
            });
            Shell.Notify("Code anonyme enregistré.");
            return true;
        });
        if (manual) form.Add("code", "Code officiel", value: row.HasCode ? row.Code : null, required: true, hint: "Par exemple le code ITMCD de l'apprenant : P13W5R ou ITM-26-AB12.");
        if (row.HasCode) form.Add("motif", "Motif du remplacement", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private Task ExportAsync(string format) =>
        Shell.Download("api/deliberation/codes/liste" + ScoliaClient.Query(("promotionId", PromotionId), ("format", format)), $"codes-anonymes.{format}");
}

/// <summary>Cours dans l'encodage : organisation, maxima et avancement par semestre.</summary>
public sealed record DeliberationCourseRowVm(Guid Id, string Number, string Name, string Semester, string Max, string Progress, DeliberationCourseDto Source);

/// <summary>Ligne de la feuille de cotes : la cote se tape directement ; la cote retenue (avec appui) s'affiche à côté.</summary>
public sealed class MarkEditRowVm(Guid enrollmentId, string code, string matricule, string fullName, string value, string retained, bool failed) : Observable
{
    private string text = value;

    public Guid EnrollmentId { get; } = enrollmentId;
    public string Code { get; } = code;
    public string Matricule { get; } = matricule;
    public string FullName { get; } = fullName;
    public string Retained { get; } = retained;
    public IBrush RetainedBrush { get; } = ThemeBrush.Get(failed ? "ToneRed" : "ToneNeutral");
    public string Original { get; } = value;
    public string Value { get => text; set => Set(ref text, value); }
}

/// <summary>
/// Encodage des cotes de délibération (ITMCD) : cote du S1, du S2 et moyenne d'appui par cours,
/// en feuille nominative ou anonyme, et saisie rapide par code anonyme.
/// </summary>
public sealed class MarksPage : PageViewModel
{
    private static readonly IReadOnlyList<ChoiceItem> AllKinds = [new("S1", "Cote du S1"), new("S2", "Cote du S2"), new("Appui", "Moyenne d'appui (sur 10)")];

    private ChoiceItem? promotion;
    private DeliberationCourseRowVm? course;
    private ChoiceItem? kind;
    private bool anonymous;
    private bool ready;
    private string subtitle = string.Empty;
    private string sheetTitle = "Choisissez un cours";
    private string sheetHint = string.Empty;

    public MarksPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.AcademicsWrite))
        {
            Action("Enregistrer les cotes", MaterialIconKind.ContentSave, SaveAsync, primary: true);
            Action("Saisie par code anonyme", MaterialIconKind.Incognito, ByCodeAsync);
            Action("Remettre les cotes", MaterialIconKind.SendOutline, SubmitAsync);
        }
        if (Shell.Can(Perm.DeliberationManage))
        {
            Action("Renvoyer à l'enseignant", MaterialIconKind.AccountArrowRight, ReturnAsync);
            Action("Réglages de l'encodage", MaterialIconKind.ToggleSwitchOutline, () => DeliberationPage.SettingsAsync(Shell));
        }
        Action("Relevé du cours (PDF)", MaterialIconKind.FileChart, () => ExportSheetAsync(failedOnly: false, "pdf"));
        Action("Relevé du cours (Excel)", MaterialIconKind.FileExcel, () => ExportSheetAsync(failedOnly: false, "xlsx"));
        Action("Liste des échecs (PDF)", MaterialIconKind.AlertCircleOutline, () => ExportSheetAsync(failedOnly: true, "pdf"));
        Action("Suivi de l'encodage", MaterialIconKind.ProgressClock, () => Shell.Open(() => new EncodingProgressPage(Shell)));
        Action("Délibération", MaterialIconKind.Gavel, () => Shell.Open(() => new DeliberationPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Encodage des cotes";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Pen;

    public ObservableCollection<DeliberationCourseRowVm> Courses { get; } = [];
    public ObservableCollection<MarkEditRowVm> Rows { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Kinds { get; private set; } = AllKinds;
    public string SheetTitle { get => sheetTitle; private set => Set(ref sheetTitle, value); }
    public string SheetHint { get => sheetHint; private set => Set(ref sheetHint, value); }
    public bool NoCourse => course is null;
    public bool ShowNames => !anonymous;

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }

    public DeliberationCourseRowVm? Course
    {
        get => course;
        set
        {
            if (!Set(ref course, value)) return;
            Raise(nameof(NoCourse));
            Kinds = value is null ? AllKinds : AllKinds.Where(k => Accepts(value.Source.Semester, k.Value)).ToList();
            Raise(nameof(Kinds));
            if (kind is null || !Kinds.Contains(kind)) { kind = Kinds[0]; Raise(nameof(Kind)); }
            _ = Shell.Run(LoadSheetAsync);
        }
    }

    public ChoiceItem? Kind { get => kind; set { if (Set(ref kind, value) && value is not null) _ = Shell.Run(LoadSheetAsync); } }

    /// <summary>Feuille anonyme : triée par code, sans nom ni matricule, comme pour la correction des copies.</summary>
    public bool Anonymous
    {
        get => anonymous;
        set { if (Set(ref anonymous, value)) { Raise(nameof(ShowNames)); _ = Shell.Run(LoadSheetAsync); } }
    }

    private static bool Accepts(string semester, string kind) => kind switch
    {
        "S1" => semester is "S1" or "Annuel",
        "S2" => semester is "S2" or "Annuel",
        _ => true,
    };

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            kind = AllKinds[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Kind));
            ready = true;
        }
        var keep = course?.Id;
        Courses.Clear();
        Rows.Clear();
        if (Promotion is null) { subtitle = "Aucune promotion"; Raise(nameof(Subtitle)); return; }
        var list = await Shell.Api.GetAsync<List<DeliberationCourseDto>>("api/deliberation/cours" + ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value))));
        foreach (var c in list)
        {
            var progress = c.Semester switch
            {
                "S1" => $"S1 {c.GradedS1}/{c.Students}",
                "S2" => $"S2 {c.GradedS2}/{c.Students}",
                _ => $"S1 {c.GradedS1}/{c.Students} · S2 {c.GradedS2}/{c.Students}",
            } + (c.LockedS1 || c.LockedS2 ? " · figé" : c.SubmittedS1 || c.SubmittedS2 ? " · remis" : string.Empty);
            Courses.Add(new DeliberationCourseRowVm(c.Id, c.Number.ToString(CultureInfo.InvariantCulture), c.Name, Labels.Semester(c.Semester), c.MaxScore.ToString("0.##"), progress, c));
        }
        subtitle = $"{Format.Count(list.Count, "cours", "cours")} · {Promotion.Label}";
        Raise(nameof(Subtitle));
        course = null;
        Raise(nameof(NoCourse));
        Course = keep is { } id ? Courses.FirstOrDefault(c => c.Id == id) : null;
        if (Course is null) { SheetTitle = "Choisissez un cours"; SheetHint = string.Empty; }
    }

    private async Task LoadSheetAsync()
    {
        Rows.Clear();
        if (Course is null || Kind is null) return;
        var sheet = await Shell.Api.GetAsync<MarkSheetDto>("api/deliberation/feuille" + ScoliaClient.Query(
            ("coursId", Course.Id), ("nature", Kind.Value), ("anonyme", Anonymous ? "true" : "false")));
        var scale = sheet.Kind == "Appui" ? "10" : sheet.MaxScore.ToString("0.##");
        SheetTitle = $"{sheet.Course} · {Kind.Label} · sur {scale}";
        var figures = $" {sheet.Failed} échec(s)" + (sheet.Average is { } avg ? $", moyenne {avg:0.##}." : ".");
        SheetHint = (sheet.Locked ? "Délibération clôturée : ces cotes sont figées."
            : !sheet.EncodingOpen ? "Encodage fermé dans les réglages de la délibération."
            : sheet.SubmittedAtUtc is { } at
                ? $"Cotes remises le {at.ToLocalTime():dd/MM/yyyy à HH:mm} par {sheet.SubmittedBy} : seul le jury peut encore les corriger ou renvoyer la feuille."
            : "Tapez chaque cote, puis « Enregistrer les cotes ». Une case vidée efface la cote. Une fois complète, « Remettre les cotes ».") + figures;
        foreach (var r in sheet.Rows)
            Rows.Add(new MarkEditRowVm(r.EnrollmentId, r.Code ?? "—", r.Matricule ?? string.Empty, r.FullName ?? "Anonyme",
                r.Value?.ToString("0.##") ?? string.Empty, r.Retained?.ToString("0.##") ?? "—", r.Failed));
    }

    private async Task SaveAsync()
    {
        if (Course is null || Kind is null)
        {
            Shell.Notify("Choisissez un cours avant d'enregistrer.");
            return;
        }
        var changes = new List<object>();
        foreach (var r in Rows.Where(r => r.Value.Trim() != r.Original))
        {
            if (!string.IsNullOrWhiteSpace(r.Value) && !Format.TryAmount(r.Value, out _))
            {
                Shell.Notify($"Cote invalide pour {(Anonymous ? r.Code : r.FullName)} : tapez un nombre.");
                return;
            }
            changes.Add(new { enrollmentId = r.EnrollmentId, value = Format.TryAmount(r.Value, out var v) ? (decimal?)v : null });
        }
        if (changes.Count == 0)
        {
            Shell.Notify("Aucune cote modifiée.");
            return;
        }
        await Shell.Api.SendAsync(HttpMethod.Put, "api/deliberation/feuille" + ScoliaClient.Query(("coursId", Course.Id), ("nature", Kind.Value)), changes);
        Shell.Notify($"{Format.Count(changes.Count, "cote enregistrée", "cotes enregistrées")}.");
        await RefreshAsync();
    }

    /// <summary>Remise officielle de la feuille : après elle, seul le jury corrige ou renvoie les cotes.</summary>
    private async Task SubmitAsync()
    {
        if (Course is null || Kind is null)
        {
            Shell.Notify("Choisissez d'abord le cours et la cote à remettre.");
            return;
        }
        if (Rows.Any(r => r.Value.Trim() != r.Original))
        {
            Shell.Notify("Enregistrez d'abord les cotes modifiées.");
            return;
        }
        if (!await Shell.Confirm("Remettre les cotes", $"{Course.Number}. {Course.Name} · {Kind.Label}. Après la remise, vous ne pourrez plus modifier ces cotes : seul le jury pourra les corriger ou vous renvoyer la feuille.", "Remettre"))
            return;
        var result = await Shell.Api.SendAsync<MarkSubmissionDto>(HttpMethod.Post, "api/deliberation/remise" + ScoliaClient.Query(("coursId", Course.Id), ("nature", Kind.Value)), new { });
        Shell.Notify($"Cotes remises : {result.Graded} sur {result.Students} apprenant(s).");
        await RefreshAsync();
    }

    /// <summary>Le jury renvoie une feuille remise à l'enseignant, avec un motif : elle redevient modifiable.</summary>
    private async Task ReturnAsync()
    {
        if (Course is null || Kind is null)
        {
            Shell.Notify("Choisissez d'abord le cours et la cote.");
            return;
        }
        var (courseId, kindValue) = (Course.Id, Kind.Value);
        var form = new FormViewModel("Renvoyer la feuille", $"{Course.Number}. {Course.Name} · {Kind.Label}. La feuille redevient modifiable par l'enseignant.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, "api/deliberation/renvoi" + ScoliaClient.Query(("coursId", courseId), ("nature", kindValue)), new { reason = f.Text("motif") });
            Shell.Notify("Feuille renvoyée à l'enseignant.");
            return true;
        });
        form.Add("motif", "Motif du renvoi", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private Task ExportSheetAsync(bool failedOnly, string format)
    {
        if (Course is null || Kind is null)
        {
            Shell.Notify("Choisissez d'abord un cours.");
            return Task.CompletedTask;
        }
        var name = failedOnly ? "liste-des-echecs" : "releve-des-cotes";
        return Shell.Download("api/deliberation/feuille/export" + ScoliaClient.Query(("coursId", Course.Id), ("nature", Kind.Value),
            ("echecs", failedOnly ? "true" : "false"), ("format", format)), $"{name}.{format}");
    }

    /// <summary>Saisie en série : code de la copie, puis cote. La boîte reste ouverte pour la copie suivante.</summary>
    private async Task ByCodeAsync()
    {
        if (Course is null || Kind is null)
        {
            Shell.Notify("Choisissez d'abord le cours et la cote à encoder.");
            return;
        }
        var (courseId, kindValue, label) = (Course.Id, Kind.Value, $"{Course.Number}. {Course.Name} · {Kind.Label}");
        var last = string.Empty;
        while (true)
        {
            var form = new FormViewModel("Saisie par code anonyme", label, async f =>
            {
                var result = await Shell.Api.SendAsync<MarkByCodeResult>(HttpMethod.Post, "api/deliberation/cotes-par-code", new
                {
                    code = f.Text("code"), courseId, kind = kindValue, value = f.Amount("cote"),
                });
                last = $"{result.Code} : {result.Value:0.##}" + (result.Previous is { } p ? $" (avant {p:0.##})" : string.Empty) + $" · reste {result.Remaining} copie(s).";
                return true;
            })
            {
                Message = last.Length == 0 ? "Tapez le code inscrit sur la copie et la cote. Le nom de l'apprenant n'est jamais affiché." : $"Enregistré — {last}",
                SubmitText = "Enregistrer et continuer",
            };
            form.Add("code", "Code anonyme", required: true);
            form.Add("cote", kindValue == "Appui" ? "Moyenne d'appui (sur 10)" : "Cote", FieldKind.Amount, required: true);
            if (!await Shell.Ask(form)) break;
        }
        if (last.Length > 0) await RefreshAsync();
    }
}

/// <summary>Ligne du tableau du jury : rang, code, résultat calculé, décision et motif.</summary>
public sealed record DeliberationRowVm(Guid EnrollmentId, string Rank, string Code, string Matricule, string FullName, string Points, string Percent,
    string Missing, string Failed, string Computed, string Decision, IBrush DecisionBrush, string Mention, string Reason, bool Near, DeliberationRowDto Source);

/// <summary>
/// Délibération (ITMCD) : statistiques de la promotion, résultats calculés par le moteur unique, cas à examiner,
/// décisions motivées du jury, clôture avec mot de passe, réouverture tracée et documents officiels.
/// </summary>
public sealed class DeliberationPage : PageViewModel
{
    private ChoiceItem? promotion;
    private ChoiceItem? period;
    private DeliberationRowVm? selected;
    private bool onlyCases;
    private bool ready;
    private bool closed;
    private string subtitle = string.Empty;
    private string summary = string.Empty;
    private string juryLine = string.Empty;
    private List<DeliberationRowVm> all = [];

    public DeliberationPage(ShellViewModel shell) : base(shell)
    {
        Action("Fiche de l'apprenant", MaterialIconKind.CardAccountDetailsOutline, ShowStudentAsync, open: true);
        if (Shell.Can(Perm.DeliberationManage))
        {
            Action("Décision du jury", MaterialIconKind.Gavel, DecideAsync, row: true);
            Action("Clôturer la délibération", MaterialIconKind.LockCheck, CloseAsync, primary: true);
            Action("Rouvrir", MaterialIconKind.LockOpenVariant, ReopenAsync, danger: true);
            Action("Réglages de l'encodage", MaterialIconKind.ToggleSwitchOutline, () => SettingsAsync(Shell));
        }
        Action("Procès-verbal (PDF)", MaterialIconKind.FileSign, () => DownloadAsync("proces-verbal.pdf", null, "proces-verbal.pdf"));
        Action("Grille des cotes (PDF)", MaterialIconKind.Grid, () => DownloadAsync("grille", "pdf", "grille-des-cotes.pdf"));
        Action("Grille des cotes (Excel)", MaterialIconKind.FileExcel, () => DownloadAsync("grille", "xlsx", "grille-des-cotes.xlsx"));
        Action("Palmarès (PDF)", MaterialIconKind.Podium, () => DownloadAsync("palmares", "pdf", "palmares.pdf"));
        Action("Bulletin (PDF)", MaterialIconKind.FilePdfBox, BulletinAsync, row: true);
        Action("Encodage des cotes", MaterialIconKind.Pen, () => Shell.Open(() => new MarksPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Délibération";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Gavel;

    public ObservableCollection<DeliberationRowVm> Items { get; } = [];
    public ObservableCollection<CourseStatDto> CourseStats { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Periods { get; } = Labels.DeliberationPeriods;
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string JuryLine { get => juryLine; private set => Set(ref juryLine, value); }
    public bool IsClosed { get => closed; private set => Set(ref closed, value); }

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Period { get => period; set { if (Set(ref period, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public DeliberationRowVm? Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>Cas à examiner : dossiers proches du seuil ou avec une seule cote manquante, sans décision du jury.</summary>
    public bool OnlyCases { get => onlyCases; set { if (Set(ref onlyCases, value)) Fill(); } }

    private Guid? PromotionId => Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : null;
    private string PeriodValue => Period?.Value ?? "S1";

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            period = Periods[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Period));
            ready = true;
        }
        if (PromotionId is not { } pid) { subtitle = "Aucune promotion"; Raise(nameof(Subtitle)); return; }
        var board = await Shell.Api.GetAsync<DeliberationBoardDto>("api/deliberation/resultats" + ScoliaClient.Query(("promotionId", pid), ("periode", PeriodValue)));
        IsClosed = board.Status == "Clôturée";
        all = board.Rows.Select(r => new DeliberationRowVm(r.EnrollmentId, r.Rank?.ToString(CultureInfo.InvariantCulture) ?? "—", r.Code ?? "—", r.Matricule, r.FullName,
            $"{r.Points:0.##} / {r.Max:0.##}", $"{r.Percent:0.##} %", r.Missing == 0 ? string.Empty : r.Missing.ToString(CultureInfo.InvariantCulture),
            r.Failed == 0 ? string.Empty : r.Failed.ToString(CultureInfo.InvariantCulture), r.Computed, r.Decision, ThemeBrush.Get(Tone(r.Decision)), r.Mention,
            r.JuryReason ?? (r.NearThreshold ? "À examiner" : string.Empty), r.NearThreshold, r)).ToList();
        Fill();
        CourseStats.Clear();
        foreach (var c in board.Courses) CourseStats.Add(c);
        Summary = $"{board.Students} apprenant(s) · {board.Admitted} admis · {board.Failed} ajourné(s) · {board.Incomplete} incomplet(s) · réussite {board.SuccessRate:0.##} % · seuil {board.PassPercent:0.##} %";
        JuryLine = IsClosed
            ? $"Clôturée le {board.ClosedAtUtc!.Value.ToLocalTime():dd/MM/yyyy HH:mm} par {board.ClosedBy} · président : {board.President}"
            : board.ReopenCount > 0 ? $"Session rouverte ({board.ReopenCount}) : {board.LastReopenReason}" : "Session ouverte : résultats provisoires, recalculés à chaque cote.";
        subtitle = $"{board.Promotion} · {board.PeriodLabel} · {board.AcademicYear} · {board.Status}";
        Raise(nameof(Subtitle));
    }

    private void Fill()
    {
        Items.Clear();
        foreach (var r in onlyCases ? all.Where(r => r.Near) : all) Items.Add(r);
        Selected = null;
    }

    private static string Tone(string decision) => decision switch
    {
        _ when decision.StartsWith("Admis", StringComparison.Ordinal) => "ToneGreen",
        "Incomplet" => "ToneOrange",
        _ => "ToneRed",
    };

    private async Task ShowStudentAsync()
    {
        if (Selected is not { } row || PromotionId is not { } pid) return;
        var detail = await Shell.Api.GetAsync<StudentDeliberationDto>($"api/deliberation/resultats/{row.EnrollmentId}" + ScoliaClient.Query(("promotionId", pid), ("periode", PeriodValue)));
        var lines = detail.Courses.Select(c => $"{c.Number}. {c.Course} ({c.Semester}) : "
            + (c.Graded ? $"{c.Points:0.##} / {c.Max:0.##}" + (c.Bonus > 0 ? $" dont appui +{c.Bonus:0.##}" : string.Empty) + (c.Failed ? " · ÉCHEC" : string.Empty) : "COTE MANQUANTE"));
        var r = detail.Result;
        var form = new FormViewModel($"{r.FullName} · {r.Matricule}", $"{detail.Promotion} · {detail.Period} · {r.Percent:0.##} % · mention {r.Mention} · {r.Decision}", _ => Task.FromResult(true))
        {
            Message = string.Join(Environment.NewLine, lines) + (r.JuryReason is null ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Décision du jury : {r.JuryReason}"),
            SubmitText = "Fermer",
            ShowCancel = false,
        };
        await Shell.Ask(form);
    }

    private async Task DecideAsync()
    {
        if (Selected is not { } row || PromotionId is not { } pid) return;
        if (IsClosed)
        {
            Shell.Notify("Délibération clôturée : rouvrez-la, avec un motif, pour changer une décision.");
            return;
        }
        var choices = new List<ChoiceItem> { new(string.Empty, $"Résultat calculé ({row.Computed})"), new("Admis", "Admis par délibération"), new("Ajourné", "Ajourné par délibération") };
        var form = new FormViewModel("Décision du jury", $"{row.FullName} · {row.Percent} · calculé : {row.Computed}. Les cotes ne sont jamais modifiées.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, "api/deliberation/decisions", new
            {
                promotionId = pid, period = PeriodValue, enrollmentId = row.EnrollmentId, outcome = f.ChoiceValue("decision") is { Length: > 0 } d ? d : null, reason = f.OptionalText("motif"),
            });
            Shell.Notify("Décision du jury enregistrée.");
            return true;
        });
        form.AddChoice("decision", "Décision", choices, row.Source.JuryOutcome is { } current ? (current.StartsWith("Admis", StringComparison.Ordinal) ? "Admis" : "Ajourné") : string.Empty, required: false);
        form.Add("motif", "Motif (obligatoire pour une décision du jury)", FieldKind.Multiline, row.Source.JuryReason);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task CloseAsync()
    {
        if (PromotionId is not { } pid) return;
        if (IsClosed)
        {
            Shell.Notify("Cette délibération est déjà clôturée.");
            return;
        }
        var incomplete = all.Count(r => r.Computed == "Incomplet" && r.Source.JuryOutcome is null);
        var form = new FormViewModel("Clôturer la délibération", $"{Promotion?.Label} · {Period?.Label}. Les résultats sont figés et les cotes de la période verrouillées.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, "api/deliberation/cloture", new
            {
                promotionId = pid, period = PeriodValue, password = f.Text("motdepasse"), president = f.Text("president"), members = f.OptionalText("membres"), notes = f.OptionalText("notes"),
            });
            Shell.Notify("Délibération clôturée. Le procès-verbal est disponible.");
            return true;
        })
        {
            Message = incomplete == 0 ? "Tous les dossiers sont complets." : $"Attention : {incomplete} dossier(s) incomplet(s) sans décision du jury seront ajournés.",
            SubmitText = "Clôturer",
        };
        form.Add("president", "Président du jury", required: true);
        form.Add("membres", "Membres présents", FieldKind.Multiline);
        form.Add("notes", "Observations", FieldKind.Multiline);
        form.Add("motdepasse", "Votre mot de passe (confirmation)", FieldKind.Secret, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ReopenAsync()
    {
        if (PromotionId is not { } pid) return;
        if (!IsClosed)
        {
            Shell.Notify("Cette délibération n'est pas clôturée.");
            return;
        }
        var form = new FormViewModel("Rouvrir la délibération", "Les résultats figés sont retirés ; la réouverture et son motif restent au journal d'audit.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, "api/deliberation/reouverture", new { promotionId = pid, period = PeriodValue, password = f.Text("motdepasse"), reason = f.Text("motif") });
            Shell.Notify("Délibération rouverte.");
            return true;
        }) { SubmitText = "Rouvrir", Danger = true };
        form.Add("motif", "Motif (10 caractères au moins)", FieldKind.Multiline, required: true);
        form.Add("motdepasse", "Votre mot de passe (confirmation)", FieldKind.Secret, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private Task DownloadAsync(string path, string? format, string fileName) =>
        PromotionId is { } pid
            ? Shell.Download($"api/deliberation/{path}" + ScoliaClient.Query(("promotionId", pid), ("periode", PeriodValue), ("format", format)), fileName)
            : Task.CompletedTask;

    private Task BulletinAsync() =>
        Selected is { } row && PromotionId is { } pid
            ? Shell.Download("api/deliberation/bulletin.pdf" + ScoliaClient.Query(("promotionId", pid), ("periode", PeriodValue), ("inscriptionId", row.EnrollmentId)), $"bulletin-{row.Matricule}.pdf")
            : Task.CompletedTask;

    /// <summary>Interrupteurs d'ITMCD : encodage S1, encodage S2, moyennes d'appui, et seuil de réussite.</summary>
    public static async Task SettingsAsync(ShellViewModel shell)
    {
        var current = await shell.Api.GetAsync<DeliberationSettingsDto>("api/deliberation/reglages");
        var form = new FormViewModel("Réglages de l'encodage", "S'appliquent à toute l'année scolaire courante.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Put, "api/deliberation/reglages", new
            {
                encodingS1Open = f.Flag("s1"), encodingS2Open = f.Flag("s2"), supportEnabled = f.Flag("appui"), passPercent = f.Amount("seuil"),
            });
            shell.Notify("Réglages de la délibération enregistrés.");
            return true;
        });
        form.AddFlag("s1", "Encodage du premier semestre ouvert", current.EncodingS1Open);
        form.AddFlag("s2", "Encodage du second semestre ouvert", current.EncodingS2Open);
        form.AddFlag("appui", "Moyennes d'appui actives", current.SupportEnabled);
        form.Add("seuil", "Seuil de réussite (%)", FieldKind.Amount, current.PassPercent.ToString("0.##"), required: true);
        if (await shell.Ask(form) && shell.Current is { } page) await page.RefreshAsync();
    }
}

/// <summary>Ligne du suivi de l'encodage : un cours et une cote attendue (S1 ou S2).</summary>
public sealed record EncodingProgressRowVm(Guid CourseId, string Promotion, string Number, string Course, string Kind, string Teacher, string Progress,
    string Percent, string Failed, string Average, string Status, IBrush StatusBrush);

/// <summary>
/// Suivi de l'encodage (moniteur d'ITMCD) : pour chaque cours de l'année, l'enseignant, les cotes encodées,
/// les échecs, la moyenne et l'état de la feuille (à encoder, en cours, complète, remise, figée).
/// </summary>
public sealed class EncodingProgressPage : PageViewModel
{
    private static readonly IReadOnlyList<ChoiceItem> AllPeriods =
        [new(string.Empty, "Toute l'année (S1 et S2)"), new("S1", "Premier semestre"), new("S2", "Second semestre")];

    private ChoiceItem? promotion;
    private ChoiceItem? period;
    private bool onlyLate;
    private bool ready;
    private string subtitle = string.Empty;
    private string summary = string.Empty;
    private List<EncodingProgressRowVm> all = [];

    public EncodingProgressPage(ShellViewModel shell) : base(shell)
    {
        Action("Suivi (PDF)", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        Action("Suivi (Excel)", MaterialIconKind.FileExcel, () => ExportAsync("xlsx"));
        Action("Encodage des cotes", MaterialIconKind.Pen, () => Shell.Open(() => new MarksPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Suivi de l'encodage";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ProgressClock;

    public ObservableCollection<EncodingProgressRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Periods { get; } = AllPeriods;
    public string Summary { get => summary; private set => Set(ref summary, value); }

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Period { get => period; set { if (Set(ref period, value) && ready) _ = Shell.Run(RefreshAsync); } }

    /// <summary>Feuilles qui demandent une relance : ni remises ni figées.</summary>
    public bool OnlyLate { get => onlyLate; set { if (Set(ref onlyLate, value)) Fill(); } }

    private Guid? PromotionId => Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : null;

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            period = AllPeriods[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Period));
            ready = true;
        }
        var data = await Shell.Api.GetAsync<EncodingProgressDto>("api/deliberation/suivi" + ScoliaClient.Query(
            ("promotionId", PromotionId), ("periode", Period?.Value is { Length: > 0 } p ? p : null)));
        all = data.Rows.Select(r => new EncodingProgressRowVm(r.CourseId, r.Promotion, r.Number.ToString(CultureInfo.InvariantCulture), r.Course, r.Kind,
            r.TeacherName ?? "Non attribué", $"{r.Graded} / {r.Students}", $"{r.Percent:0.#} %", r.Failed == 0 ? string.Empty : r.Failed.ToString(CultureInfo.InvariantCulture),
            r.Average is { } a ? a.ToString("0.##") : "—", r.SubmittedAtUtc is { } at ? $"{r.Status} le {at.ToLocalTime():dd/MM HH:mm}" : r.Status,
            ThemeBrush.Get(r.Status switch { "Figé" => "ToneIndigo", "Remis" => "ToneGreen", "Complet" => "ToneTeal", "En cours" => "ToneOrange", _ => "ToneRed" }))).ToList();
        Fill();
        Summary = $"{data.GradedMarks} cote(s) encodée(s) sur {data.ExpectedMarks} ({data.Percent:0.#} %) · {data.Complete} feuille(s) complète(s) · "
            + $"{data.Submitted} remise(s) · {data.Locked} figée(s) sur {data.Sheets}";
        subtitle = $"{data.PeriodLabel} · {data.AcademicYear}";
        Raise(nameof(Subtitle));
    }

    private void Fill()
    {
        Items.Clear();
        foreach (var r in onlyLate ? all.Where(r => !r.Status.StartsWith("Remis", StringComparison.Ordinal) && r.Status != "Figé") : all) Items.Add(r);
    }

    private Task ExportAsync(string format) =>
        Shell.Download("api/deliberation/suivi/export" + ScoliaClient.Query(("promotionId", PromotionId),
            ("periode", Period?.Value is { Length: > 0 } p ? p : null), ("format", format)), $"suivi-encodage.{format}");
}
