using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

public sealed record CurriculumAssignmentRowVm(CurriculumTeachingItemDto Dto, string Element, string Teacher, IBrush TeacherBrush);

/// <summary>Secrétariat : attribution des branches (EPST) et des éléments constitutifs (LMD) aux enseignants.</summary>
public sealed class CurriculumAssignmentPage : PageViewModel
{
    private CurriculumAssignmentRowVm? selected;
    private string subtitle = string.Empty;

    public CurriculumAssignmentPage(ShellViewModel shell) : base(shell)
    {
        Action("Attribuer", MaterialIconKind.AccountPlusOutline, AssignAsync, primary: true, row: true, open: true);
        if (!IsUniversity)
            Action("Titulaire de classe", MaterialIconKind.AccountStarOutline, TitularAsync);
        Action("Retirer l'attribution", MaterialIconKind.AccountRemoveOutline, UnassignAsync, row: true, danger: true);
        ActionRefresh();
    }

    private bool IsUniversity => Shell.Session?.InstitutionType == Scolia.Domain.School.InstitutionProfiles.SuperieurLmd;
    public override string Title => IsUniversity ? "Attribution des EC" : "Attribution des branches";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountSchoolOutline;
    public ObservableCollection<CurriculumAssignmentRowVm> Items { get; } = [];
    public CurriculumAssignmentRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var items = await Shell.Api.GetAsync<List<CurriculumTeachingItemDto>>("api/etablissement/programme/attributions");
        Items.Clear();
        foreach (var i in items)
            Items.Add(new CurriculumAssignmentRowVm(i,
                (i.UeCode is { } ue ? $"{i.Semester} · {ue} › " : "") + $"{i.Code} · {i.Name}",
                i.TeacherName ?? "Non attribué", ThemeBrush.Get(i.TeacherName is null ? "ToneOrange" : "ToneGreen")));
        subtitle = $"{items.Count(i => i.TeacherId is null)} élément(s) sans enseignant sur {items.Count}";
        Raise(nameof(Subtitle));
    }

    private async Task<List<ChoiceItem>> TeacherChoicesAsync() =>
        (await Shell.Api.GetAsync<List<CurriculumTeacherDto>>("api/etablissement/programme/enseignants"))
            .Select(t => new ChoiceItem(t.Id.ToString(), t.FullName)).ToList();

    private async Task AssignAsync()
    {
        if (Selected?.Dto is not { } item) return;
        var teachers = await TeacherChoicesAsync();
        if (teachers.Count == 0) { Shell.Notify("Créez d'abord les enseignants dans Enseignants et cours › Comptes enseignants."); return; }
        var form = new FormViewModel($"Attribuer {item.Code} · {item.Name}", $"{item.PromotionName}. L'enseignant saisira les notes de cet élément.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/programme/{item.ItemId}/attribution",
                    new { teacherId = Guid.Parse(f.ChoiceValue("enseignant")!), confirmReplace = true });
                Shell.Notify("Attribution enregistrée.");
                return true;
            });
        form.AddChoice("enseignant", "Enseignant", teachers, item.TeacherId?.ToString() ?? teachers[0].Value, required: true);
        if (item.TeacherId is not null)
            form.AddHeader("Réattribution", "L'attribution actuelle est close et reste dans l'historique.");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task TitularAsync()
    {
        var classes = Items.Select(i => i.Dto).DistinctBy(i => i.PromotionId)
            .Select(i => new ChoiceItem(i.PromotionId.ToString(), i.PromotionName)).ToList();
        if (classes.Count == 0) { Shell.Notify("Ajoutez d'abord les branches des classes dans Cycles scolaires."); return; }
        var teachers = await TeacherChoicesAsync();
        if (teachers.Count == 0) { Shell.Notify("Créez d'abord les enseignants dans Comptes enseignants."); return; }
        var form = new FormViewModel("Titulaire de classe", "Au primaire, le titulaire enseigne toutes les branches de sa classe.",
            async f =>
            {
                var count = await Shell.Api.SendAsync<int>(HttpMethod.Put,
                    $"api/etablissement/programme/classes/{f.ChoiceValue("classe")}/titulaire",
                    new { teacherId = Guid.Parse(f.ChoiceValue("enseignant")!), confirmReplace = true });
                Shell.Notify($"{count} branche(s) attribuée(s) au titulaire.");
                return true;
            });
        form.AddChoice("classe", "Classe", classes, classes[0].Value, required: true);
        form.AddChoice("enseignant", "Titulaire", teachers, teachers[0].Value, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task UnassignAsync()
    {
        if (Selected?.Dto is not { TeacherId: not null } item) { Shell.Notify("Cet élément n'a pas d'enseignant."); return; }
        if (!await Shell.Confirm("Retirer l'attribution", $"{item.Name} : {item.TeacherName} ne pourra plus en saisir les notes.",
                "Retirer", danger: true)) return;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/etablissement/programme/{item.ItemId}/retrait-attribution", new { reason = (string?)null });
        Shell.Notify("Attribution retirée.");
        await RefreshAsync();
    }
}

public sealed class TeacherMarkRowVm(Guid enrollmentId, string matricule, string fullName, decimal? score, bool editable) : Observable
{
    private string text = score is { } s ? s.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) : string.Empty;
    public Guid EnrollmentId { get; } = enrollmentId;
    public string Matricule { get; } = matricule;
    public string FullName { get; } = fullName;
    public bool Editable { get; } = editable;
    public decimal? Original { get; } = score;
    public string Score { get => text; set => Set(ref text, value); }
}

/// <summary>
/// Enseignant d'une école ou d'une université : ses branches ou ses EC, une feuille de notes à la fois.
/// Le serveur refuse tout élément qui ne lui est pas attribué.
/// </summary>
public sealed class TeacherCurriculumPage : PageViewModel
{
    private List<CurriculumTeachingItemDto> mine = [];
    private ChoiceItem? element;
    private ChoiceItem? period;
    private ChoiceItem? session;
    private string evaluationCode = "INTERRO1";
    private string evaluationTitle = "Interrogation";
    private string maxScore = "10";
    private string lockReason = string.Empty;
    private string subtitle = string.Empty;
    private TeacherMarkRowVm? selected;
    private bool ready;

    public TeacherCurriculumPage(ShellViewModel shell) : base(shell)
    {
        session = Sessions[0];
        Action("Enregistrer les notes", MaterialIconKind.ContentSaveOutline, SaveAsync, primary: true);
        Action("Charger l'évaluation", MaterialIconKind.FileFind, () => Shell.Run(LoadSheetAsync));
        ActionRefresh();
    }

    public override string Title => "Mes branches et notes";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Pen;
    public ObservableCollection<TeacherMarkRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> ElementChoices { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> PeriodChoices { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Sessions { get; } = [new("1", "Première session"), new("2", "Deuxième session")];
    public ChoiceItem? Element { get => element; set { if (Set(ref element, value) && ready) { Raise(nameof(IsUniversityElement)); Raise(nameof(IsSchoolElement)); _ = Shell.Run(LoadPeriodsAndSheetAsync); } } }
    public ChoiceItem? Period { get => period; set { if (Set(ref period, value) && ready) _ = Shell.Run(LoadSheetAsync); } }
    public ChoiceItem? Session { get => session; set { if (Set(ref session, value) && ready) _ = Shell.Run(LoadSheetAsync); } }
    public string EvaluationCode { get => evaluationCode; set => Set(ref evaluationCode, value); }
    public string EvaluationTitle { get => evaluationTitle; set => Set(ref evaluationTitle, value); }
    public string MaxScore { get => maxScore; set => Set(ref maxScore, value); }
    public TeacherMarkRowVm? Selected { get => selected; set => Set(ref selected, value); }
    public string LockReason { get => lockReason; private set { if (Set(ref lockReason, value)) Raise(nameof(IsLocked)); } }
    public bool IsLocked => !string.IsNullOrEmpty(lockReason);
    private CurriculumTeachingItemDto? Current => mine.FirstOrDefault(m => m.ItemId.ToString() == Element?.Value);
    public bool IsUniversityElement => Current?.Kind == "EC";
    public bool IsSchoolElement => Current is { Kind: "Branche" };

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            mine = await Shell.Api.GetAsync<List<CurriculumTeachingItemDto>>("api/enseignant/mes-elements");
            ElementChoices = mine.Select(m => new ChoiceItem(m.ItemId.ToString(),
                $"{m.PromotionName} · " + (m.UeCode is { } ue ? $"{m.Semester} · {ue} › " : "") + $"{m.Code} · {m.Name}")).ToList();
            element = ElementChoices.Count > 0 ? ElementChoices[0] : null;
            Raise(nameof(ElementChoices));
            Raise(nameof(Element));
            Raise(nameof(IsUniversityElement));
            Raise(nameof(IsSchoolElement));
            ready = true;
            await LoadPeriodsAsync();
        }
        await LoadSheetAsync();
    }

    private async Task LoadPeriodsAndSheetAsync()
    {
        await LoadPeriodsAsync();
        await LoadSheetAsync();
    }

    private async Task LoadPeriodsAsync()
    {
        PeriodChoices = [];
        period = null;
        if (Current is { Kind: "Branche" } item)
        {
            var list = await Shell.Api.GetAsync<List<SchoolPeriodDto>>($"api/enseignant/branches/{item.ItemId}/periodes");
            PeriodChoices = list.Select(p => new ChoiceItem(p.Id.ToString(), $"{p.Code} · {p.Name}" + (p.IsExam ? " (examen)" : ""))).ToList();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var now = list.FirstOrDefault(p => p.StartDate <= today && today <= p.EndDate) ?? list.FirstOrDefault();
            period = now is null ? null : PeriodChoices.First(c => c.Value == now.Id.ToString());
            EvaluationCode = now?.IsExam == true ? "EXAMEN" : "INTERRO1";
        }
        Raise(nameof(PeriodChoices));
        Raise(nameof(Period));
    }

    private async Task LoadSheetAsync()
    {
        Items.Clear();
        LockReason = string.Empty;
        if (Current is not { } item) { subtitle = "Aucun élément ne vous est attribué : demandez-le au secrétariat."; Raise(nameof(Subtitle)); return; }
        if (item.Kind == "EC")
        {
            var sheet = await Shell.Api.GetAsync<LmdMarkSheetDto>(
                $"api/enseignant/lmd/promotions/{item.PromotionId}/ecs/{item.ItemId}/notes?session={Session?.Value ?? "1"}");
            foreach (var l in sheet.Lines) Items.Add(new TeacherMarkRowVm(l.EnrollmentId, l.Matricule, l.FullName, l.Score, l.Editable));
            LockReason = sheet.LockReason ?? (sheet.Lines.Any(l => l.Editable) ? string.Empty : "Aucun étudiant ne compose cet EC à cette session.");
            subtitle = $"{item.PromotionName} · {sheet.UeCode} › {sheet.Code} · notes sur {ProfileFormat.N(sheet.MarkScale)}";
        }
        else
        {
            if (Period is null) { subtitle = $"{item.PromotionName} · {item.Name} · choisissez la période"; Raise(nameof(Subtitle)); return; }
            var sheet = await Shell.Api.GetAsync<EpstMarkSheetDto>(
                $"api/enseignant/branches/{item.ItemId}/feuille?periode={Period.Value}&code={Uri.EscapeDataString(EvaluationCode)}");
            foreach (var l in sheet.Lines) Items.Add(new TeacherMarkRowVm(l.EnrollmentId, l.Matricule, l.FullName, l.Score, sheet.Editable));
            EvaluationTitle = sheet.Title;
            MaxScore = ProfileFormat.N(sheet.MaxScore);
            LockReason = sheet.LockReason ?? string.Empty;
            subtitle = $"{item.PromotionName} · {item.Name} · {sheet.PeriodCode}" +
                (sheet.ExistingCodes.Count > 0 ? " · évaluations : " + string.Join(", ", sheet.ExistingCodes) : "");
        }
        Raise(nameof(Subtitle));
    }

    private async Task SaveAsync()
    {
        if (Current is not { } item) return;
        var lines = new List<(Guid Id, decimal? Score)>();
        foreach (var row in Items.Where(r => r.Editable))
        {
            decimal? value = string.IsNullOrWhiteSpace(row.Score) ? null
                : Format.TryAmount(row.Score, out var parsed) ? parsed
                : throw new InvalidOperationException($"Note illisible pour {row.FullName} : « {row.Score} ».");
            if (value != row.Original) lines.Add((row.EnrollmentId, value));
        }
        if (lines.Count == 0) { Shell.Notify("Aucune note modifiée."); return; }
        if (item.Kind == "EC")
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/enseignant/lmd/promotions/{item.PromotionId}/ecs/{item.ItemId}/notes", new
            {
                session = int.Parse(Session?.Value ?? "1", CultureInfo.InvariantCulture),
                lines = lines.Select(l => new { enrollmentId = l.Id, score = l.Score }).ToArray(),
            });
        else
        {
            if (Period is null) { Shell.Notify("Choisissez la période."); return; }
            if (!Format.TryAmount(MaxScore, out var max) || max <= 0) { Shell.Notify("Indiquez le maximum de l'évaluation."); return; }
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/enseignant/branches/{item.ItemId}/feuille", new
            {
                periodId = Guid.Parse(Period.Value), evaluationCode = EvaluationCode, title = EvaluationTitle, maxScore = max,
                lines = lines.Select(l => new { enrollmentId = l.Id, score = l.Score }).ToArray(),
            });
        }
        Shell.Notify($"{lines.Count} note(s) enregistrée(s).");
        await LoadSheetAsync();
    }
}
