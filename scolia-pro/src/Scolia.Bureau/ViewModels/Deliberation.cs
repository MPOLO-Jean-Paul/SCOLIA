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
        Action("Reprendre des codes officiels (fichier)", MaterialIconKind.Upload, ImportFileAsync);
        Action("Reprendre des codes officiels (coller)", MaterialIconKind.FormatListChecks, ImportPasteAsync);
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

    /// <summary>Codes déjà portés par les apprenants (export d'ITMCD, CSV) : aperçu, puis attribution des lignes reconnues.</summary>
    private async Task ImportFileAsync()
    {
        if (Shell.PickFileHandler is null) return;
        var picked = await Shell.PickFileHandler([".csv", ".txt"]);
        if (picked is null) return;
        // Excel enregistre souvent le CSV en Windows-1252 : on retombe sur Latin-1 si le texte n'est pas de l'UTF-8 valide.
        var text = System.Text.Encoding.UTF8.GetString(picked.Data);
        if (text.Contains('\uFFFD', StringComparison.Ordinal)) text = System.Text.Encoding.Latin1.GetString(picked.Data);
        await ImportAsync(text);
    }

    private async Task ImportPasteAsync()
    {
        var text = string.Empty;
        var form = new FormViewModel("Reprendre des codes officiels",
            "Une ligne par apprenant : matricule ou nom complet, puis le code (par exemple « MUKENDI KABEYA GRACE ; P13W5R »). Ajoutez la promotion au milieu en cas d'homonymes. Les colonnes copiées depuis Excel conviennent.",
            f => { text = f.Text("liste"); return Task.FromResult(true); });
        form.Add("liste", "Liste des codes", FieldKind.Multiline, required: true);
        if (await Shell.Ask(form)) await ImportAsync(text);
    }

    private async Task ImportAsync(string text)
    {
        var preview = await Shell.Api.SendAsync<CodeImportResult>(HttpMethod.Post, "api/deliberation/codes/import", new { text, promotionId = PromotionId, apply = false });
        var problems = preview.Lines.Where(l => l.Status != "À attribuer" && l.Status != "Déjà attribué")
            .Select(l => $"Ligne {l.Line} · {l.Identifier} · {l.Code} : {l.Status}").Take(12).ToList();
        var message = $"{preview.Ready} code(s) à attribuer · {preview.Unchanged} déjà en place · {preview.Problems} ligne(s) à revoir."
            + (problems.Count == 0 ? string.Empty : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, problems)
                + (preview.Problems > problems.Count ? $"{Environment.NewLine}…" : string.Empty));
        if (preview.Ready == 0)
        {
            await Shell.Confirm("Rien à attribuer", message, "Fermer");
            return;
        }
        if (!await Shell.Confirm("Reprendre les codes officiels", message, "Attribuer")) return;
        var result = await Shell.Api.SendAsync<CodeImportResult>(HttpMethod.Post, "api/deliberation/codes/import", new { text, promotionId = PromotionId, apply = true });
        Shell.Notify($"{Format.Count(result.Assigned, "code officiel repris", "codes officiels repris")}.");
        await RefreshAsync();
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
    private static readonly IReadOnlyList<ChoiceItem> AllKinds =
        [new("S1", "Cote du S1"), new("S2", "Cote du S2"), new("Appui", "Moyenne d'appui (sur 10)"), new("Jury", "Cote du jury (épreuve)")];

    private ChoiceItem? promotion;
    private DeliberationCourseRowVm? course;
    private ChoiceItem? kind;
    private bool anonymous;
    private bool ready;
    private string subtitle = string.Empty;
    private string sheetTitle = "Choisissez un cours";
    private string sheetHint = string.Empty;

    /// <summary>Ouvre l'encodage, au besoin directement sur un cours (depuis l'espace enseignant ou le suivi).</summary>
    public MarksPage(ShellViewModel shell, Guid? promotionId = null, Guid? courseId = null) : base(shell)
    {
        wantedPromotion = promotionId;
        wantedCourse = courseId;
        teacher = Shell.TeacherOnly;
        // L'enseignant corrige des copies anonymes : sa feuille montre les codes, jamais les noms.
        anonymous = teacher;
        if (Shell.Can(Perm.AcademicsWrite) || teacher)
        {
            Action("Enregistrer les cotes", MaterialIconKind.ContentSave, SaveAsync, primary: true);
            if (!teacher) Action("Saisie par code anonyme", MaterialIconKind.Incognito, ByCodeAsync);
            Action("Remettre les cotes", MaterialIconKind.SendOutline, SubmitAsync);
            Action("Reprendre des cotes (fichier)", MaterialIconKind.Upload, ImportMarksFileAsync);
            Action("Reprendre des cotes (coller)", MaterialIconKind.FormatListChecks, ImportMarksPasteAsync);
        }
        if (Shell.Can(Perm.DeliberationManage))
        {
            Action("Renvoyer à l'enseignant", MaterialIconKind.AccountArrowRight, ReturnAsync);
            Action("Paramètres de délibération", MaterialIconKind.Tune, () => DeliberationPage.SettingsAsync(Shell));
        }
        Action("Relevé du cours (PDF)", MaterialIconKind.FileChart, () => ExportSheetAsync(failedOnly: false, "pdf"));
        Action("Relevé du cours (Excel)", MaterialIconKind.FileExcel, () => ExportSheetAsync(failedOnly: false, "xlsx"));
        Action("Liste des échecs (PDF)", MaterialIconKind.AlertCircleOutline, () => ExportSheetAsync(failedOnly: true, "pdf"));
        if (teacher) Action("Mon espace", MaterialIconKind.AccountSchool, () => Shell.Open(() => new TeacherHomePage(Shell)));
        if (!teacher)
        {
            Action("Suivi de l'encodage", MaterialIconKind.ProgressClock, () => Shell.Open(() => new EncodingProgressPage(Shell)));
            Action("Délibération", MaterialIconKind.Gavel, () => Shell.Open(() => new DeliberationPage(Shell)));
        }
        ActionRefresh();
    }

    private readonly bool teacher;
    private readonly Guid? wantedPromotion;
    private Guid? wantedCourse;
    private List<TeacherCourseDto> mine = [];
    private bool fresh;

    private string notice = string.Empty;

    // Encodage métier enseignant : la promotion et le cours sont choisis avant
    // de rechercher le code. Les suggestions ne contiennent ni nom ni matricule.
    private string codeQuery = string.Empty;
    private string? selectedSuggestion;
    private string validatedCode = string.Empty;
    private string scoreText = string.Empty;
    private string codeFeedback = "Saisissez les premiers caractères du code figurant sur la copie.";
    private MarkSheetDto? currentSheet;
    private AsyncCommand? checkCodeCommand;
    private AsyncCommand? saveCodeCommand;

    public ObservableCollection<string> CodeSuggestions { get; } = [];
    public bool ShowCodeEntry => teacher && Course is not null;
    public bool HasCodeSuggestions => CodeSuggestions.Count > 0 && validatedCode.Length == 0;
    public bool ShowScoreInput => teacher && validatedCode.Length > 0;
    public decimal ScoreMaximum => Kind?.Value == "Appui" ? 10m : Course?.Source.MaxScore ?? 0m;
    public string ScoreMaximumText => $"Note sur {ScoreMaximum:0.##}";
    public string CodeFeedback { get => codeFeedback; private set => Set(ref codeFeedback, value); }
    public string CodeQuery
    {
        get => codeQuery;
        set
        {
            if (!Set(ref codeQuery, value ?? string.Empty)) return;
            validatedCode = string.Empty;
            selectedSuggestion = null;
            Raise(nameof(SelectedSuggestion));
            Raise(nameof(ShowScoreInput));
            CodeFeedback = "Choisissez un code proposé ou cliquez sur « Vérifier ».";
            UpdateSuggestions();
        }
    }
    public string? SelectedSuggestion
    {
        get => selectedSuggestion;
        set
        {
            if (!Set(ref selectedSuggestion, value) || string.IsNullOrEmpty(value)) return;
            AcceptCode(value);
        }
    }
    public string ScoreText { get => scoreText; set => Set(ref scoreText, value ?? string.Empty); }
    public AsyncCommand CheckCode => checkCodeCommand ??= new AsyncCommand(() => Shell.Run(CheckCodeAsync));
    public AsyncCommand SaveCode => saveCodeCommand ??= new AsyncCommand(() => Shell.Run(SaveCodeAsync));

    private void ResetCodeEntry()
    {
        codeQuery = string.Empty;
        selectedSuggestion = null;
        validatedCode = string.Empty;
        scoreText = string.Empty;
        CodeSuggestions.Clear();
        foreach (var name in new[] { nameof(CodeQuery), nameof(SelectedSuggestion), nameof(ScoreText),
            nameof(ShowScoreInput), nameof(HasCodeSuggestions), nameof(ScoreMaximum),
            nameof(ScoreMaximumText), nameof(ShowCodeEntry) }) Raise(name);
        CodeFeedback = "Saisissez les premiers caractères du code figurant sur la copie.";
    }

    private void UpdateSuggestions()
    {
        CodeSuggestions.Clear();
        var query = (codeQuery ?? string.Empty).Trim();
        if (query.Length > 0 && ShowCodeEntry)
            foreach (var code in Rows.Select(r => r.Code).Where(c => c != "—" &&
                         c.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase).Take(15))
                CodeSuggestions.Add(code);
        Raise(nameof(HasCodeSuggestions));
    }

    private void AcceptCode(string code)
    {
        // La feuille a déjà été obtenue avec le cours et la promotion autorisés.
        var row = Rows.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
        if (row is null) { CodeFeedback = "Code non trouvé dans la promotion sélectionnée."; return; }
        validatedCode = row.Code;
        codeQuery = row.Code;
        selectedSuggestion = row.Code;
        scoreText = row.Value;
        CodeSuggestions.Clear();
        foreach (var name in new[] { nameof(CodeQuery), nameof(SelectedSuggestion), nameof(ScoreText),
            nameof(ShowScoreInput), nameof(HasCodeSuggestions) }) Raise(name);
        CodeFeedback = $"Code {validatedCode} validé · {ScoreMaximumText}. Indiquez maintenant la note obtenue.";
    }

    private async Task CheckCodeAsync()
    {
        if (!ShowCodeEntry || Course is null) return;
        var code = codeQuery.Trim().ToUpperInvariant();
        if (code.Length == 0) { CodeFeedback = "Saisissez le code anonyme."; return; }
        if (Rows.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            AcceptCode(code);
            return;
        }
        if (!Shell.Api.IsOnline)
        {
            CodeFeedback = "Code absent de cette promotion dans la copie locale. Reconnectez-vous pour vérifier le code.";
            return;
        }
        try
        {
            var check = await Shell.Api.GetAsync<CodeInCourseDto>(
                "api/deliberation/codes/verifier-promotion" + ScoliaClient.Query(("coursId", Course.Id), ("code", code)));
            if (!check.InPromotion) { CodeFeedback = "Apprenant hors promotion."; return; }
            await LoadSheetAsync();
            if (Rows.Any(r => string.Equals(r.Code, check.Code, StringComparison.OrdinalIgnoreCase)))
                AcceptCode(check.Code);
            else CodeFeedback = "Code valide, mais la feuille a changé. Actualisez les données du cours.";
        }
        catch (ApiException ex) when (ex.Status == 404)
        {
            CodeFeedback = "Code anonyme introuvable dans l'année scolaire.";
        }
    }

    private async Task SaveCodeAsync()
    {
        if (!ShowScoreInput || Course is null || Kind is null || validatedCode.Length == 0) return;
        if (currentSheet is not { EncodingOpen: true, Locked: false, SubmittedAtUtc: null })
        {
            CodeFeedback = "Encodage fermé, feuille remise ou délibération clôturée. Aucune note enregistrée.";
            return;
        }
        if (!Format.TryAmount(ScoreText, out var value) || value < 0 || value > ScoreMaximum)
        {
            CodeFeedback = $"Note invalide : saisissez une note comprise entre 0 et {ScoreMaximum:0.##}.";
            return;
        }
        var code = validatedCode;
        try
        {
            var result = await Shell.Api.SendAsync<MarkByCodeResult>(HttpMethod.Post, "api/deliberation/cotes-par-code",
                new { code, courseId = Course.Id, kind = Kind.Value, value });
            await LoadSheetAsync();
            ResetCodeEntry();
            CodeFeedback = $"Note {result.Value:0.##}/{ScoreMaximum:0.##} enregistrée pour {result.Code} · {result.Remaining} copie(s) restantes.";
        }
        catch (OfflineQueuedException)
        {
            // Le serveur vérifiera à la reprise l'attribution, la période et le maxima.
            // On conserve un accusé explicite : aucune note n'est annoncée comme validée.
            ResetCodeEntry();
            CodeFeedback = $"Note {value:0.##}/{ScoreMaximum:0.##} pour {code} en attente de synchronisation avec le serveur.";
        }
    }

    /// <summary>Espace enseignant : seulement ses cours attribués, en feuille anonyme.</summary>
    public bool IsTeacher => teacher;

    private string rules = string.Empty;

    /// <summary>
    /// Règles de l'encodage en vigueur (« Paramètres profs ») : l'enseignant voit d'un coup d'œil ce qui lui est permis.
    /// </summary>
    public string EncodingRules { get => rules; private set => Set(ref rules, value); }

    /// <summary>Dernières annonces de la délibération (ouverture de l'encodage, remise, renvoi), pour l'enseignant.</summary>
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => notice.Length > 0;
    public bool CanChooseAnonymous => !teacher;

    public override string Title => teacher ? "Mes cours" : "Encodage des cotes";
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
            ResetCodeEntry();
            Kinds = value is null ? AllKinds : AllKinds.Where(k => Accepts(value.Source.Semester, k.Value)).ToList();
            Raise(nameof(Kinds));
            if (kind is null || !Kinds.Contains(kind)) { kind = Kinds[0]; Raise(nameof(Kind)); }
            _ = Shell.Run(LoadSheetAsync);
        }
    }

    public ChoiceItem? Kind { get => kind; set { if (Set(ref kind, value) && value is not null)
        { ResetCodeEntry(); _ = Shell.Run(LoadSheetAsync); } } }

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
        "Jury" => semester is "JuryEpreuve" or "JuryBloc",
        _ => semester is not ("JuryEpreuve" or "JuryBloc"),
    };

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            if (teacher)
            {
                // Les promotions proposées sont celles des cours attribués à l'enseignant.
                mine = await Shell.Api.GetAsync<List<TeacherCourseDto>>("api/deliberation/mes-cours");
                fresh = true;
                Promotions = mine.GroupBy(m => m.PromotionId).Select(g => new ChoiceItem(g.Key.ToString(), g.First().Promotion)).ToList();
            }
            else Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.FirstOrDefault(p => wantedPromotion is { } wanted && p.Value == wanted.ToString())
                ?? (Promotions.Count > 0 ? Promotions[0] : null);
            kind = AllKinds[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Kind));
            ready = true;
        }
        var keep = course?.Id ?? wantedCourse;
        wantedCourse = null;
        Courses.Clear();
        Rows.Clear();
        ResetCodeEntry();
        var settings = await Shell.Api.GetAsync<DeliberationSettingsDto>("api/deliberation/reglages");
        EncodingRules = "Règles de l'encodage : S1 " + (settings.EncodingS1Open ? "ouvert" : "fermé") + " · S2 " + (settings.EncodingS2Open ? "ouvert" : "fermé")
            + " · moyennes d'appui " + (settings.SupportEnabled ? "actives" : "désactivées")
            + " · mise à jour des cotes enregistrées " + (settings.TeacherUpdates ? "autorisée" : "fermée (le secrétariat corrige)")
            + " · téléchargements " + (settings.TeacherDownloads ? "autorisés" : "fermés") + $" · seuil {settings.PassPercent:0.##} %.";
        if (Promotion is null) { subtitle = teacher ? "Aucun cours ne vous est attribué cette année" : "Aucune promotion"; Raise(nameof(Subtitle)); return; }
        List<DeliberationCourseDto> list;
        if (teacher)
        {
            // Première ouverture : la liste vient d'être lue pour les promotions, inutile de la relire.
            if (!fresh) mine = await Shell.Api.GetAsync<List<TeacherCourseDto>>("api/deliberation/mes-cours");
            fresh = false;
            list = mine.Where(m => m.PromotionId.ToString() == Promotion.Value).Select(m => m.Course).ToList();
            var notices = await Shell.Api.GetAsync<List<AnnouncementDto>>("api/deliberation/annonces");
            Notice = string.Join(Environment.NewLine, notices.Take(3).Select(n => $"{n.AtUtc.ToLocalTime():dd/MM HH:mm} · {n.Title} : {n.Text}"));
        }
        else list = await Shell.Api.GetAsync<List<DeliberationCourseDto>>("api/deliberation/cours" + ScoliaClient.Query(("promotionId", Guid.Parse(Promotion.Value))));
        foreach (var c in list)
        {
            var progress = c.Semester switch
            {
                "JuryEpreuve" or "JuryBloc" => $"Jury {c.GradedJury}/{c.Students}",
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
        currentSheet = null;
        ResetCodeEntry();
        if (Course is null || Kind is null) return;
        var sheet = await Shell.Api.GetAsync<MarkSheetDto>("api/deliberation/feuille" + ScoliaClient.Query(
            ("coursId", Course.Id), ("nature", Kind.Value), ("anonyme", Anonymous ? "true" : "false")));
        currentSheet = sheet;
        Raise(nameof(ScoreMaximum));
        Raise(nameof(ScoreMaximumText));
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
        UpdateSuggestions();
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
            if (Format.TryAmount(r.Value, out var checkedValue) && (checkedValue < 0 || checkedValue > ScoreMaximum))
            {
                Shell.Notify($"Note hors limites pour {(Anonymous ? r.Code : r.FullName)} : maximum {ScoreMaximum:0.##}.");
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

    /// <summary>Cotes déjà tenues ailleurs (export d'ITMCD, Excel, CSV) : aperçu, puis enregistrement des lignes reconnues.</summary>
    private async Task ImportMarksFileAsync()
    {
        if (Course is null || Kind is null) { Shell.Notify("Choisissez d'abord le cours et la cote à reprendre."); return; }
        if (Shell.PickFileHandler is null) return;
        var picked = await Shell.PickFileHandler([".csv", ".txt"]);
        if (picked is null) return;
        var text = System.Text.Encoding.UTF8.GetString(picked.Data);
        if (text.Contains('\uFFFD', StringComparison.Ordinal)) text = System.Text.Encoding.Latin1.GetString(picked.Data);
        await ImportMarksAsync(text);
    }

    private async Task ImportMarksPasteAsync()
    {
        if (Course is null || Kind is null) { Shell.Notify("Choisissez d'abord le cours et la cote à reprendre."); return; }
        var text = string.Empty;
        var form = new FormViewModel("Reprendre des cotes", $"{Course.Number}. {Course.Name} · {Kind.Label}. Une ligne par copie : "
            + (teacher ? "code anonyme ; cote." : "code anonyme (ou matricule) ; cote.") + " Les colonnes copiées depuis Excel conviennent.",
            f => { text = f.Text("liste"); return Task.FromResult(true); });
        form.Add("liste", "Liste des cotes", FieldKind.Multiline, required: true);
        if (await Shell.Ask(form)) await ImportMarksAsync(text);
    }

    private async Task ImportMarksAsync(string text)
    {
        if (Course is null || Kind is null) return;
        var body = new { courseId = Course.Id, kind = Kind.Value, text, apply = false };
        var preview = await Shell.Api.SendAsync<MarkImportResult>(HttpMethod.Post, "api/deliberation/cotes/import", body);
        var problems = preview.Lines.Where(l => l.Status is not ("À enregistrer" or "Remplace" or "Inchangée"))
            .Select(l => $"Ligne {l.Line} · {l.Identifier} · {l.Value} : {l.Status}").Take(12).ToList();
        var replaced = preview.Lines.Count(l => l.Status == "Remplace");
        var message = $"{preview.Ready} cote(s) à enregistrer{(replaced > 0 ? $" dont {replaced} remplaçant une cote existante" : string.Empty)} · "
            + $"{preview.Unchanged} inchangée(s) · {preview.Problems} ligne(s) à revoir."
            + (problems.Count == 0 ? string.Empty : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, problems)
                + (preview.Problems > problems.Count ? $"{Environment.NewLine}…" : string.Empty));
        if (preview.Ready == 0)
        {
            await Shell.Confirm("Rien à enregistrer", message, "Fermer");
            return;
        }
        if (!await Shell.Confirm("Reprendre les cotes", message, "Enregistrer")) return;
        var result = await Shell.Api.SendAsync<MarkImportResult>(HttpMethod.Post, "api/deliberation/cotes/import", body with { apply = true });
        Shell.Notify($"{Format.Count(result.Saved, "cote reprise", "cotes reprises")}.");
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
            Action("Paramètres de délibération", MaterialIconKind.Tune, () => SettingsAsync(Shell));
            Action("Créer les épreuves du jury", MaterialIconKind.FormatListChecks, CreateJuryEvaluationsAsync);
            Action("Grilles de toutes les promotions (Excel)", MaterialIconKind.FileExcel, () =>
                Shell.Download("api/deliberation/grilles.xlsx" + ScoliaClient.Query(("periode", PeriodValue)),
                    "grilles-deliberation-toutes-promotions.xlsx"));
        }
        Action("Procès-verbal (PDF)", MaterialIconKind.FileSign, () => DownloadAsync("proces-verbal.pdf", null, "proces-verbal.pdf"));
        Action("Grille des cotes (PDF)", MaterialIconKind.Grid, () => DownloadAsync("grille", "pdf", "grille-des-cotes.pdf"));
        Action("Grille des cotes (Excel)", MaterialIconKind.FileExcel, () => DownloadAsync("grille", "xlsx", "grille-des-cotes.xlsx"));
        Action("Palmarès (PDF)", MaterialIconKind.Podium, () => DownloadAsync("palmares", "pdf", "palmares.pdf"));
        Action("Bulletin (PDF)", MaterialIconKind.FilePdfBox, BulletinAsync, row: true);
        Action("Tous les bulletins (PDF)", MaterialIconKind.FileMultiple, () => DownloadAsync("bulletins.pdf", null, "bulletins-deliberation.pdf"));
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
            r.Failed == 0 ? string.Empty : r.Failed.ToString(CultureInfo.InvariantCulture), r.Computed, r.Grade is { } g ? $"{r.Decision} ({g})" : r.Decision,
            ThemeBrush.Get(Tone(r.Decision)), r.Mention,
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
        if (r.Totals is { } t)
            lines = lines.Append(string.Empty).Append($"TOTAL 1 (épreuves) {t.Total1:0.##}/{t.Total1Max:0.##} · SOUS-TOTAL 1 (cours) {t.Subtotal1:0.##}/{t.Subtotal1Max:0.##} · "
                + $"SOUS-TOTAL 2 (blocs) {t.Subtotal2:0.##}/{t.Subtotal2Max:0.##} · TOTAL 2 {t.Total2:0.##}/{t.Total2Max:0.##} · TOTAL GÉNÉRAL {t.General:0.##}/{t.GeneralMax:0.##}"
                + (r.Grade is { } g ? $" · code {g}" : string.Empty));
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
        var authorities = await Shell.Api.GetAsync<AuthoritiesDto>("api/deliberation/autorites");
        form.Add("president", "Président du jury", value: authorities.JuryPresident, required: true);
        form.Add("membres", "Membres présents", FieldKind.Multiline,
            value: authorities.JuryMembers is { Count: > 0 } m ? string.Join(Environment.NewLine, m) : null,
            hint: "Vide : les membres enregistrés dans « Autorités et jury »."); 
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

    /// <summary>Autorités de l'année (projet Délibération) : elles signent le bulletin, la grille et le procès-verbal.</summary>
    public static Task AuthoritiesAsync(ShellViewModel shell) => shell.Open(() => new DeliberationSettingsPage(shell));

    /// <summary>Crée les épreuves du jury standard (stage, situation, dossier, épreuve d'intégration, blocs) dans la promotion.</summary>
    private async Task CreateJuryEvaluationsAsync()
    {
        if (PromotionId is not { } pid) return;
        if (!await Shell.Confirm("Épreuves du jury", $"{Promotion?.Label} : création du stage (150), de la situation d'intégration (50), du dossier d'évaluation (90), "
            + "de l'épreuve d'intégration (225) et des blocs prévention (60), diagnostic (50) et gestion (50). Elles ne comptent qu'au jury annuel et restent modifiables dans « Cours et notes ».", "Créer"))
            return;
        var result = await Shell.Api.SendAsync<JuryEvaluationsResult>(HttpMethod.Post, "api/deliberation/epreuves-jury" + ScoliaClient.Query(("promotionId", pid)), new { });
        Shell.Notify(result.Created == 0 ? "Les épreuves du jury existaient déjà." : $"{Format.Count(result.Created, "épreuve créée", "épreuves créées")}.");
        await RefreshAsync();
    }

    /// <summary>Les réglages se trouvent dans la page « Paramètres de délibération », rangés par blocs.</summary>
    public static Task SettingsAsync(ShellViewModel shell) => shell.Open(() => new DeliberationSettingsPage(shell));
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
            r.TeacherName ?? "Non attribué", $"{r.Graded} / {r.Students} · {r.Percent:0.#} %", $"{r.Percent:0.#} %", r.Failed == 0 ? string.Empty : r.Failed.ToString(CultureInfo.InvariantCulture),
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

/// <summary>
/// Paramètres de délibération, rangés en blocs comme dans le projet Délibération : encodage des cotes par les enseignants
/// (« Paramètres profs »), règles de la délibération, autorités (administration et jury) et épreuves du jury.
/// Le secrétariat y trouve en un seul endroit tout ce qui gouverne la gestion des cotes.
/// </summary>
public sealed class DeliberationSettingsPage : PageViewModel
{
    private bool s1 = true, s2 = true, support = true, updates, downloads = true;
    private string pass = "60", prefect = string.Empty, principal = string.Empty, president = string.Empty, secretary = string.Empty;
    private string member1 = string.Empty, member2 = string.Empty, member3 = string.Empty, member4 = string.Empty;
    private ChoiceItem? promotion;
    private string subtitle = string.Empty;

    public DeliberationSettingsPage(ShellViewModel shell) : base(shell)
    {
        Action("Enregistrer les paramètres", MaterialIconKind.ContentSave, SaveSettingsAsync, primary: true);
        Action("Enregistrer les autorités", MaterialIconKind.AccountCheck, SaveAuthoritiesAsync);
        Action("Encodage des cotes", MaterialIconKind.Pen, () => Shell.Open(() => new MarksPage(Shell)));
        Action("Délibération", MaterialIconKind.Gavel, () => Shell.Open(() => new DeliberationPage(Shell)));
        ActionRefresh();
    }

    public override string Title => "Paramètres de délibération";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Tune;

    // Bloc 1 — Encodage des cotes par les enseignants (« Paramètres profs »).
    public bool EncodingS1Open { get => s1; set => Set(ref s1, value); }
    public bool EncodingS2Open { get => s2; set => Set(ref s2, value); }
    public bool SupportEnabled { get => support; set => Set(ref support, value); }
    public bool TeacherUpdates { get => updates; set => Set(ref updates, value); }
    public bool TeacherDownloads { get => downloads; set => Set(ref downloads, value); }

    // Bloc 2 — Règles de la délibération.
    public string PassPercent { get => pass; set => Set(ref pass, value); }
    public string Rules =>
        "Cote d'un cours de 0 au maxima ; échec sous la moitié du maxima. Moyenne d'appui sur 10 : si la cote ne dépasse pas 75 % du maxima, "
        + "elle ajoute jusqu'à 25 % du maxima. Cours annuel : moyenne du S1 et du S2. Admis au seuil sans cote manquante ; mentions A (90 %), "
        + "B (80 %), C (70 %), D (60 %), E. Jury annuel : codes P (réussite partout), A (cours en échec), E (sous le seuil).";

    // Bloc 3 — Autorités : administration.
    public string Prefect { get => prefect; set => Set(ref prefect, value); }
    public string Principal { get => principal; set => Set(ref principal, value); }

    // Bloc 4 — Autorités : jury.
    public string JuryPresident { get => president; set => Set(ref president, value); }
    public string JurySecretary { get => secretary; set => Set(ref secretary, value); }
    public string Member1 { get => member1; set => Set(ref member1, value); }
    public string Member2 { get => member2; set => Set(ref member2, value); }
    public string Member3 { get => member3; set => Set(ref member3, value); }
    public string Member4 { get => member4; set => Set(ref member4, value); }

    // Bloc 5 — Épreuves du jury.
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set => Set(ref promotion, value); }
    public AsyncCommand CreateJuryCommand => new(() => Shell.Run(CreateJuryEvaluationsAsync));

    public override async Task LoadAsync()
    {
        var settings = await Shell.Api.GetAsync<DeliberationSettingsDto>("api/deliberation/reglages");
        var authorities = await Shell.Api.GetAsync<AuthoritiesDto>("api/deliberation/autorites");
        EncodingS1Open = settings.EncodingS1Open;
        EncodingS2Open = settings.EncodingS2Open;
        SupportEnabled = settings.SupportEnabled;
        TeacherUpdates = settings.TeacherUpdates;
        TeacherDownloads = settings.TeacherDownloads;
        PassPercent = settings.PassPercent.ToString("0.##", CultureInfo.CurrentCulture);
        Prefect = authorities.Prefect ?? string.Empty;
        Principal = authorities.Principal ?? string.Empty;
        JuryPresident = authorities.JuryPresident ?? string.Empty;
        JurySecretary = authorities.JurySecretary ?? string.Empty;
        var members = authorities.JuryMembers ?? [];
        Member1 = members.Count > 0 ? members[0] : string.Empty;
        Member2 = members.Count > 1 ? members[1] : string.Empty;
        Member3 = members.Count > 2 ? members[2] : string.Empty;
        Member4 = members.Count > 3 ? members[3] : string.Empty;
        if (Promotions.Count == 0)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            Raise(nameof(Promotions));
            Promotion = Promotions.Count > 0 ? Promotions[0] : null;
        }
        var open = new[] { EncodingS1Open ? "S1" : null, EncodingS2Open ? "S2" : null }.OfType<string>().ToList();
        subtitle = $"Encodage {(open.Count == 0 ? "fermé" : "ouvert : " + string.Join(" et ", open))} · seuil {PassPercent} %";
        Raise(nameof(Subtitle));
    }

    private async Task SaveSettingsAsync()
    {
        if (!Format.TryAmount(PassPercent, out var threshold))
        {
            Shell.Notify("Seuil de réussite : tapez un pourcentage, par exemple 60.");
            return;
        }
        await Shell.Api.SendAsync(HttpMethod.Put, "api/deliberation/reglages", new
        {
            encodingS1Open = EncodingS1Open, encodingS2Open = EncodingS2Open, supportEnabled = SupportEnabled, passPercent = threshold,
            teacherUpdates = TeacherUpdates, teacherDownloads = TeacherDownloads,
        });
        Shell.Notify("Paramètres enregistrés. Les enseignants sont prévenus des changements d'encodage.");
        await RefreshAsync();
    }

    private async Task SaveAuthoritiesAsync()
    {
        await Shell.Api.SendAsync(HttpMethod.Put, "api/deliberation/autorites", new
        {
            prefect = Optional(Prefect), principal = Optional(Principal), juryPresident = Optional(JuryPresident), jurySecretary = Optional(JurySecretary),
            juryMembers = new[] { Member1, Member2, Member3, Member4 }.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToArray(),
        });
        Shell.Notify("Autorités enregistrées : elles figurent sur le bulletin, la grille et le procès-verbal.");
        await RefreshAsync();
    }

    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task CreateJuryEvaluationsAsync()
    {
        if (Promotion is not { } p) { Shell.Notify("Choisissez une promotion."); return; }
        if (!await Shell.Confirm("Épreuves du jury", $"{p.Label} : stage (150), situation d'intégration (50), dossier d'évaluation (90), épreuve d'intégration (225), "
            + "blocs prévention (60), diagnostic (50) et gestion (50). Elles ne comptent qu'au jury annuel et restent modifiables dans « Cours et notes ».", "Créer"))
            return;
        var result = await Shell.Api.SendAsync<JuryEvaluationsResult>(HttpMethod.Post, "api/deliberation/epreuves-jury" + ScoliaClient.Query(("promotionId", Guid.Parse(p.Value))), new { });
        Shell.Notify(result.Created == 0 ? "Les épreuves du jury existaient déjà." : $"{Format.Count(result.Created, "épreuve créée", "épreuves créées")} pour {p.Label}.");
    }
}
