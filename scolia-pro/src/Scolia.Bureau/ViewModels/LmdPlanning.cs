using System.Collections.ObjectModel;
using System.Net.Http;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Domain.School;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Configuration native Windows/Android : pas de modification des délibérations
/// historiques ITM ; les maquettes restent des documents pédagogiques internes.
/// </summary>
public sealed class LmdPlanningPage : PageViewModel
{
    private ChoiceItem? selectedPromotion;
    private string subtitle = "Facultés, filières, semestres et UE/EC";
    private IReadOnlyList<ChoiceItem> promotionChoices = [];
    public LmdPlanningPage(ShellViewModel shell) : base(shell)
    {
        AddStructure = new AsyncCommand(AddStructureAsync);
        ConfigureSemesters = new AsyncCommand(ConfigureSemestersAsync);
        AddUnit = new AsyncCommand(AddUnitAsync);
        AddElement = new AsyncCommand(AddElementAsync);
        Action("Nouvelle structure", MaterialIconKind.SitemapOutline, AddStructureAsync, primary: true);
        Action("Calendrier S1/S2", MaterialIconKind.CalendarMonthOutline, ConfigureSemestersAsync);
        Action("Frais par tranches", MaterialIconKind.CashMultiple, InstallmentsAsync);
        ActionRefresh();
    }

    public override string Title => "Organisation LMD";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.SchoolOutline;
    public ObservableCollection<LmdStructureDto> Structures { get; } = [];
    public ObservableCollection<LmdSemesterDto> Semesters { get; } = [];
    public ObservableCollection<LmdProgramItemDto> Program { get; } = [];
    public IReadOnlyList<ChoiceItem> PromotionChoices
    {
        get => promotionChoices;
        private set => Set(ref promotionChoices, value);
    }
    public ChoiceItem? SelectedPromotion
    {
        get => selectedPromotion;
        set
        {
            if (Set(ref selectedPromotion, value)) _ = Shell.Run(LoadProgramAsync);
        }
    }

    public AsyncCommand AddStructure { get; }
    public AsyncCommand ConfigureSemesters { get; }
    public AsyncCommand AddUnit { get; }
    public AsyncCommand AddElement { get; }

    public override async Task LoadAsync()
    {
        var structures = await Shell.Api.GetAsync<List<LmdStructureDto>>("api/etablissement/lmd/structures");
        Structures.Clear();
        foreach (var structure in structures) Structures.Add(structure);

        var semesters = await Shell.Api.GetAsync<List<LmdSemesterDto>>("api/etablissement/lmd/semestres");
        Semesters.Clear();
        foreach (var term in semesters) Semesters.Add(term);

        var promotions = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        var selectedId = selectedPromotion?.Value;
        PromotionChoices = promotions.Where(p => p.IsActive)
            .Select(p => new ChoiceItem(p.Id.ToString(), p.Code + " · " + p.Name)).ToList();
        selectedPromotion = PromotionChoices.FirstOrDefault(p => p.Value == selectedId) ??
            (PromotionChoices.Count > 0 ? PromotionChoices[0] : null);
        Raise(nameof(SelectedPromotion));
        await LoadProgramAsync();
        subtitle = $"{Structures.Count} structure(s) · {Semesters.Count} semestre(s) · {Program.Count} ligne(s) de programme";
        Raise(nameof(Subtitle));
    }

    internal PromotionDto? FlutterPromotion { get; private set; }
    internal string FlutterCurrency { get; private set; } = "";
    internal async Task LoadForFlutterAsync(string? promotionId)
    {
        await LoadAsync();
        if (!string.IsNullOrEmpty(promotionId) && SelectedPromotion?.Value != promotionId)
        {
            selectedPromotion = PromotionChoices.FirstOrDefault(p => p.Value == promotionId)
                ?? throw new ArgumentException("Promotion invalide.");
            Raise(nameof(SelectedPromotion));
            await LoadProgramAsync();
            subtitle = $"{Structures.Count} structure(s) · {Semesters.Count} semestre(s) · {Program.Count} ligne(s) de programme";
            Raise(nameof(Subtitle));
        }
        var promotions = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        FlutterPromotion = promotions.FirstOrDefault(p => p.IsActive && p.Id.ToString() == SelectedPromotion?.Value);
        FlutterCurrency = (await Shell.Api.GetAsync<SchoolDto>("api/etablissement")).CurrencySymbol;
    }

    private async Task LoadProgramAsync()
    {
        Program.Clear();
        if (SelectedPromotion is null) return;
        var rows = await Shell.Api.GetAsync<List<LmdProgramItemDto>>(
            $"api/etablissement/lmd/promotions/{SelectedPromotion.Value}/programme");
        foreach (var item in rows) Program.Add(item);
    }

    private async Task AddStructureAsync()
    {
        var kinds = new[]
        {
            new ChoiceItem("Faculty", "Faculté"),
            new ChoiceItem("Department", "Département"),
            new ChoiceItem("Filiere", "Filière"),
            new ChoiceItem("Domain", "Domaine"),
            new ChoiceItem("Mention", "Mention"),
            new ChoiceItem("Pathway", "Parcours"),
            new ChoiceItem("Speciality", "Spécialité"),
        };
        var parents = new[] { new ChoiceItem("", "Racine (faculté ou domaine)") }
            .Concat(Structures.Where(s => s.IsActive)
                .Select(s => new ChoiceItem(s.Id.ToString(), TextCase.LmdKindLabel(s.Kind) + " · " + s.Name))).ToList();
        var form = new FormViewModel("Nouvelle structure LMD",
            "Hiérarchie autorisée : faculté → département → filière ou domaine → mention → parcours → spécialité.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/lmd/structures", new
                {
                    kind = f.ChoiceValue("nature"),
                    parentId = Guid.TryParse(f.ChoiceValue("parent"), out var pid) ? pid : (Guid?)null,
                    code = f.Text("code"),
                    name = f.Text("nom"),
                });
                Shell.Notify("Structure LMD enregistrée.");
                return true;
            });
        form.AddChoice("nature", "Nature de l'unité", kinds, required: true);
        form.AddChoice("parent", "Parent", parents, "");
        form.Add("code", "Code", required: true, hint: "Unique au même niveau et au sein du même parent.");
        form.Add("nom", "Dénomination", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ConfigureSemestersAsync()
    {
        var years = await Shell.Api.GetAsync<List<AcademicYearDto>>("api/etablissement/annees");
        var year = years.FirstOrDefault(y => y.IsCurrent);
        if (year is null) { Shell.Notify("Configurez d'abord l'année académique courante."); return; }
        var span = year.EndDate.DayNumber - year.StartDate.DayNumber + 1;
        var midpoint = year.StartDate.AddDays(Math.Max(1, span / 2) - 1);
        var s1 = Semesters.FirstOrDefault(s => s.Code == "S1");
        var s2 = Semesters.FirstOrDefault(s => s.Code == "S2");
        var form = new FormViewModel("Calendrier académique S1 / S2",
            "Les dates proposées sont modifiables : vérifiez le calendrier officiel de l'établissement avant de valider.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put, "api/etablissement/lmd/semestres", new
                {
                    firstStart = f.Day("s1debut"),
                    firstEnd = f.Day("s1fin"),
                    secondStart = f.Day("s2debut"),
                    secondEnd = f.Day("s2fin"),
                });
                Shell.Notify("Semestres S1 et S2 enregistrés, sans modifier les échéances des frais.");
                return true;
            });
        form.AddDay("s1debut", "S1 — début", s1?.StartDate ?? year.StartDate, true);
        form.AddDay("s1fin", "S1 — fin", s1?.EndDate ?? midpoint, true);
        form.AddDay("s2debut", "S2 — début", s2?.StartDate ?? midpoint.AddDays(1), true);
        form.AddDay("s2fin", "S2 — fin", s2?.EndDate ?? year.EndDate, true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task AddUnitAsync()
    {
        if (SelectedPromotion is null)
        {
            Shell.Notify("Créez une promotion dans Paramètres puis sélectionnez-la.");
            return;
        }
        var paths = Structures.Where(s => s.IsActive &&
            s.Kind is ("Filiere" or "Pathway" or "Speciality"))
            .Select(s => new ChoiceItem(s.Id.ToString(), s.Name)).ToList();
        if (paths.Count == 0)
        {
            Shell.Notify("Créez d'abord une filière ou un parcours complet dans Structures LMD.");
            return;
        }
        var terms = new[] { new ChoiceItem("S1", "Semestre 1"), new ChoiceItem("S2", "Semestre 2") };
        var promotionId = SelectedPromotion.Value;
        var form = new FormViewModel("Nouvelle unité d'enseignement (UE)",
            "Une UE doit contenir au moins un EC ; les crédits appartiennent à l'UE.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post,
                    $"api/etablissement/lmd/promotions/{promotionId}/ues", new
                    {
                        academicUnitId = Guid.Parse(f.ChoiceValue("filiere")!),
                        semester = f.ChoiceValue("semestre"),
                        code = f.Text("uecode"), name = f.Text("uenom"),
                        credits = f.Amount("credits"), hours = f.Amount("heures"),
                        elementCode = f.Text("eccode"), elementName = f.Text("ecnom"),
                        elementHours = f.Amount("echeures"),
                    });
                Shell.Notify("UE et son premier EC enregistrés dans la maquette.");
                return true;
            });
        form.AddChoice("filiere", "Filière ou parcours", paths, required: true);
        form.AddChoice("semestre", "Semestre", terms, "S1", required: true);
        form.Add("uecode", "Code UE", required: true);
        form.Add("uenom", "Intitulé UE", required: true);
        form.Add("credits", "Crédits UE", FieldKind.Amount, required: true);
        form.Add("heures", "Volume horaire UE", FieldKind.Amount, "30", required: true);
        form.AddHeader("Premier élément constitutif", "Ajoutez les autres EC ensuite.");
        form.Add("eccode", "Code EC", required: true);
        form.Add("ecnom", "Intitulé EC", required: true);
        form.Add("echeures", "Volume horaire EC", FieldKind.Amount, "30", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task AddElementAsync()
    {
        if (SelectedPromotion is null)
        {
            Shell.Notify("Sélectionnez d'abord une promotion LMD.");
            return;
        }
        var parents = Program.Where(p => p.Kind == "TeachingUnit")
            .Select(p => new ChoiceItem(p.Id.ToString(), p.Code + " · " + p.Name)).ToList();
        if (parents.Count == 0) { Shell.Notify("Créez d'abord une UE."); return; }
        var promotionId = SelectedPromotion.Value;
        var form = new FormViewModel("Nouvel élément constitutif (EC)",
            "Un EC reste lié à l'UE parente et à son semestre.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post,
                    $"api/etablissement/lmd/promotions/{promotionId}/ecs", new
                    {
                        parentItemId = Guid.Parse(f.ChoiceValue("parent")!),
                        code = f.Text("code"), name = f.Text("nom"),
                        hours = f.Amount("heures"),
                    });
                Shell.Notify("Élément constitutif enregistré.");
                return true;
            });
        form.AddChoice("parent", "UE parente", parents, required: true);
        form.Add("code", "Code EC", required: true);
        form.Add("nom", "Intitulé EC", required: true);
        form.Add("heures", "Volume horaire", FieldKind.Amount, "30", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    /// <summary>Frais académiques de la promotion choisie, en tranches datées (monnaie de l'établissement).</summary>
    private async Task InstallmentsAsync()
    {
        if (SelectedPromotion is null) { Shell.Notify("Sélectionnez d'abord une promotion."); return; }
        var form = await CreateInstallmentFormAsync();
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    internal async Task<FormViewModel> CreateInstallmentFormAsync()
    {
        if (!Shell.Can(Perm.SettingsWrite))
            throw new UnauthorizedAccessException("Configuration des frais non autorisée.");
        var selectedId = SelectedPromotion?.Value ?? throw new InvalidOperationException("Sélectionnez une promotion.");
        var promotions = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        var promotion = promotions.First(p => p.IsActive && p.Id.ToString() == selectedId);
        var existing = promotion.Fees.Where(f => LmdInstallmentPlan.IsTranche(f.Label)).OrderBy(f => f.DueDate).ToList();
        var school = await Shell.Api.GetAsync<SchoolDto>("api/etablissement");
        var symbol = school.CurrencySymbol;
        var totalBefore = existing.Sum(f => f.Amount);
        var form = new FormViewModel($"Frais académiques — {promotion.Name}",
            "Montant annuel propre à cette promotion. Les premières tranches sont plus élevées ; les échéances sont réparties dans l’année. Les encaissements sont protégés.",
            async f =>
            {
                var total = f.Amount("annuel");
                var count = int.Parse(f.ChoiceValue("tranches") ?? "3", System.Globalization.CultureInfo.InvariantCulture);
                if (existing.Count > 0 && total == totalBefore && count == existing.Count) return true;
                try { _ = LmdInstallmentPlan.Split(total, count, school.WholeAmountsOnly); }
                catch (ArgumentException ex) { f.Find("annuel").Error = ex.Message; return false; }
                await Shell.Api.SendAsync(HttpMethod.Put, $"api/etablissement/lmd/promotions/{promotion.Id}/tranches",
                    new { annualFee = new { annualTotal = total, count } });
                Shell.Notify($"Frais académiques enregistrés en {count} tranche(s).");
                return true;
            });
        var annual = form.Add("annuel", $"Total annuel ({symbol})", FieldKind.Amount,
            totalBefore > 0 ? totalBefore.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : null, required: true);
        form.AddChoice("tranches", "Nombre de tranches", Enumerable.Range(1, LmdInstallmentPlan.MaxCount)
            .Select(n => new ChoiceItem(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{n} tranche(s)")).ToList(),
            (existing.Count > 0 ? existing.Count : 3).ToString(System.Globalization.CultureInfo.InvariantCulture), required: true);
        void Preview()
        {
            var total = form.Amount("annuel");
            var count = int.Parse(form.ChoiceValue("tranches") ?? "3", System.Globalization.CultureInfo.InvariantCulture);
            if (existing.Count > 0 && total == totalBefore && count == existing.Count)
            {
                annual.Hint = "Plan conservé : " + string.Join(" · ", existing.Select((f, i) => $"T{i + 1} : {f.Amount:0.##} {symbol}"));
                return;
            }
            try { annual.Hint = string.Join(" · ", LmdInstallmentPlan.Split(total, count, school.WholeAmountsOnly).Select((a, i) => $"T{i + 1} : {a:0.##} {symbol}")); }
            catch (ArgumentException ex) { annual.Hint = ex.Message; }
        }
        form.Changed = _ => Preview();
        Preview();
        return form;
    }
}
