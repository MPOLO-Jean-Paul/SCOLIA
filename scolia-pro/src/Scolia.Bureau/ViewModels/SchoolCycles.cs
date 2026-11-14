using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Domain.School;

namespace Scolia.Bureau.ViewModels;

/// <summary>Une ligne éditable de cycle, utilisant les champs du secrétariat.</summary>
public sealed class SchoolCycleDraftVm : Observable
{
    private bool included;
    private bool chargeMonthlyTuition;
    private string name;
    private string authorizationNumber;
    private string monthlyAmount;

    public SchoolCycleDraftVm(string kind, string label,
        SchoolCycleConfigurationItemDto? saved)
    {
        Kind = kind;
        Label = label;
        included = saved?.IsActive ?? false;
        name = saved?.Name ?? label;
        authorizationNumber = saved?.AuthorizationNumber ?? string.Empty;
        chargeMonthlyTuition = saved?.ChargeMonthlyTuition ?? false;
        monthlyAmount = saved is null ? string.Empty :
            saved.MonthlyAmount.ToString("0.##", CultureInfo.CurrentCulture);
        ClassCount = saved?.ClassCount ?? 0;
    }

    public string Kind { get; }
    public string Label { get; }
    public int ClassCount { get; }
    public bool HasClasses => ClassCount > 0;
    public bool Included { get => included; set => Set(ref included, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string AuthorizationNumber
    {
        get => authorizationNumber;
        set => Set(ref authorizationNumber, value);
    }
    public bool ChargeMonthlyTuition
    {
        get => chargeMonthlyTuition;
        set => Set(ref chargeMonthlyTuition, value);
    }
    public string MonthlyAmount { get => monthlyAmount; set => Set(ref monthlyAmount, value); }
}

/// <summary>
/// Écran de configuration dédié aux écoles et complexes ; il n'est pas affiché
/// sur les profils ITM/IM ou LMD. Tant que ces nouveaux moteurs sont désactivés,
/// l'écran reste un composant préparé, jamais un outil de facturation.
/// </summary>
public sealed class SchoolCyclesPage : PageViewModel
{
    private string subtitle = string.Empty;
    private string governance = "Prive";
    private bool canConfigure;

    public SchoolCyclesPage(ShellViewModel shell) : base(shell)
    {
        Save = new AsyncCommand(SaveAsync);
        AddPeriod = new AsyncCommand(AddPeriodAsync);
        AddSubject = new AsyncCommand(AddSubjectAsync);
        SetSubjectMax = new AsyncCommand(SetSubjectMaxAsync);
        Action("Enregistrer les cycles", MaterialIconKind.ContentSaveOutline, SaveAsync, primary: true);
        ActionRefresh();
    }

    public override string Title => "Cycles scolaires";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.SchoolOutline;

    public ObservableCollection<SchoolCycleDraftVm> Cycles { get; } = [];
    public string Governance { get => governance; private set => Set(ref governance, value); }
    public bool IsPrivate => Governance == "Prive";
    public bool CanConfigure { get => canConfigure; private set => Set(ref canConfigure, value); }
    public AsyncCommand Save { get; }
    public AsyncCommand AddPeriod { get; }
    public AsyncCommand AddSubject { get; }

    private ChoiceItem? selectedCycle;
    private ChoiceItem? selectedPromotion;
    private IReadOnlyList<ChoiceItem> cycleChoices = [];
    private IReadOnlyList<ChoiceItem> promotionChoices = [];
    public ObservableCollection<SchoolPeriodDto> Periods { get; } = [];
    public ObservableCollection<AnnualSubjectDto> Subjects { get; } = [];
    public IReadOnlyList<ChoiceItem> CycleChoices
    {
        get => cycleChoices;
        private set => Set(ref cycleChoices, value);
    }
    public IReadOnlyList<ChoiceItem> PromotionChoices
    {
        get => promotionChoices;
        private set => Set(ref promotionChoices, value);
    }
    public ChoiceItem? SelectedCycle
    {
        get => selectedCycle;
        set { if (Set(ref selectedCycle, value)) _ = Shell.Run(LoadPeriodsAsync); }
    }
    public ChoiceItem? SelectedPromotion
    {
        get => selectedPromotion;
        set { if (Set(ref selectedPromotion, value)) _ = Shell.Run(LoadSubjectsAsync); }
    }

    public override async Task LoadAsync()
    {
        var plan = await Shell.Api.GetAsync<SchoolCycleConfigurationDto>("api/etablissement/cycles");
        Governance = plan.Governance;
        Raise(nameof(IsPrivate));
        CanConfigure = InstitutionProfiles.IsEnabled(plan.InstitutionType);
        subtitle = plan.InstitutionType == InstitutionProfiles.ComplexeScolaire
            ? "Un établissement · plusieurs cycles · tarifs indépendants"
            : "Classes et tarifs du cycle scolaire";
        Raise(nameof(Subtitle));

        Cycles.Clear();
        foreach (var (kind, label) in new[]
        {
            (InstitutionProfiles.Maternelle, "Maternelle"),
            (InstitutionProfiles.Primaire, "Primaire"),
            (InstitutionProfiles.Secondaire, "Secondaire"),
        })
        {
            var saved = plan.Cycles.FirstOrDefault(c =>
                string.Equals(c.Kind, kind, StringComparison.OrdinalIgnoreCase));
            var row = new SchoolCycleDraftVm(kind, label, saved);
            if (saved is null && plan.InstitutionType == kind)
                row.Included = true;
            Cycles.Add(row);
        }
        CycleChoices = plan.Cycles.Where(c => c.IsActive)
            .Select(c => new ChoiceItem(c.Id.ToString(), c.Name)).ToList();
        selectedCycle = CycleChoices.Count > 0 ? CycleChoices[0] : null;
        Raise(nameof(SelectedCycle));
        var classes = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");
        PromotionChoices = classes.Where(p => p.IsActive && p.SchoolCycleId is not null)
            .Select(p => new ChoiceItem(p.Id.ToString(), p.Code + " · " + p.Name)).ToList();
        selectedPromotion = PromotionChoices.Count > 0 ? PromotionChoices[0] : null;
        Raise(nameof(SelectedPromotion));
        await LoadPeriodsAsync();
        await LoadSubjectsAsync();
    }

    private async Task SaveAsync()
    {
        if (!CanConfigure)
        {
            Shell.Notify("Cette configuration de cycles n'est pas disponible pour ce profil.");
            return;
        }
        var chosen = Cycles.Where(c => c.Included).ToArray();
        if (!IsPrivate && chosen.Any(c => c.ChargeMonthlyTuition))
        {
            Shell.Notify("Établissement public : les mensualités nécessitent une validation réglementaire préalable.");
            return;
        }
        var drafts = new List<object>();
        foreach (var row in chosen)
        {
            decimal amount = 0;
            if (row.ChargeMonthlyTuition &&
                (!Format.TryAmount(row.MonthlyAmount, out amount) || amount <= 0))
            {
                Shell.Notify($"Indiquez une mensualité valide pour le cycle {row.Label}.");
                return;
            }
            drafts.Add(new
            {
                kind = row.Kind,
                name = row.Name.Trim(),
                authorizationNumber = string.IsNullOrWhiteSpace(row.AuthorizationNumber)
                    ? null : row.AuthorizationNumber.Trim(),
                chargeMonthlyTuition = row.ChargeMonthlyTuition,
                monthlyAmount = amount,
            });
        }
        await Shell.Api.SendAsync<SchoolCycleConfigurationDto>(HttpMethod.Put,
            "api/etablissement/cycles", new { cycles = drafts });
        Shell.Notify("Cycles scolaires et tarifs de référence enregistrés. Aucune mensualité n'a été facturée.");
        await RefreshAsync();
    }
    private async Task LoadPeriodsAsync()
    {
        Periods.Clear();
        if (SelectedCycle is null) return;
        var items = await Shell.Api.GetAsync<List<SchoolPeriodDto>>(
            $"api/etablissement/cycles/{SelectedCycle.Value}/periodes");
        foreach (var item in items) Periods.Add(item);
    }

    private async Task LoadSubjectsAsync()
    {
        Subjects.Clear();
        if (SelectedPromotion is null) return;
        var items = await Shell.Api.GetAsync<List<AnnualSubjectDto>>(
            $"api/etablissement/cycles/promotions/{SelectedPromotion.Value}/matieres");
        foreach (var item in items) Subjects.Add(item);
    }

    private async Task AddPeriodAsync()
    {
        if (SelectedCycle is null)
        {
            Shell.Notify("Configurez d'abord les cycles scolaires de l'établissement.");
            return;
        }
        var years = await Shell.Api.GetAsync<List<AcademicYearDto>>("api/etablissement/annees");
        var year = years.FirstOrDefault(y => y.IsCurrent);
        if (year is null) { Shell.Notify("Année scolaire courante introuvable."); return; }
        var start = Periods.Count == 0 ? year.StartDate : Periods[^1].EndDate.AddDays(1);
        if (start > year.EndDate)
        {
            Shell.Notify("La dernière période couvre déjà la fin de l'année.");
            return;
        }
        var days = Math.Min(29, year.EndDate.DayNumber - start.DayNumber);
        var end = start.AddDays(days);
        var cycle = SelectedCycle.Value;
        var ordinal = Periods.Count + 1;
        var form = new FormViewModel("Nouvelle période pédagogique",
            "Chaque cycle possède son propre calendrier. Vérifiez les dates de l'année ; aucune période n'est créée automatiquement.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post,
                    $"api/etablissement/cycles/{cycle}/periodes", new
                    {
                        code = f.Text("code"), name = f.Text("nom"),
                        termCode = f.Text("trimestre"),
                        startDate = f.Day("debut"), endDate = f.Day("fin"),
                        isExam = f.Flag("examen"),
                    });
                Shell.Notify("Période pédagogique enregistrée sans modifier les anciennes évaluations.");
                return true;
            });
        form.Add("code", "Code", value: $"P{ordinal}", required: true);
        form.Add("nom", "Période", value: $"Période {ordinal}", required: true);
        form.Add("trimestre", "Trimestre ou regroupement", value: "T1", required: true);
        form.AddDay("debut", "Début", start, true);
        form.AddDay("fin", "Fin", end, true);
        form.AddFlag("examen", "Examen de fin de trimestre ou de semestre (maximum doublé selon le règlement)", false);
        if (await Shell.Ask(form)) await LoadPeriodsAsync();
    }

    private async Task AddSubjectAsync()
    {
        if (SelectedPromotion is null)
        {
            Shell.Notify("Créez d'abord une classe et rattachez-la à son cycle.");
            return;
        }
        var promotionId = SelectedPromotion.Value;
        var form = new FormViewModel("Nouvelle matière ou activité annuelle",
            "La matière appartient uniquement à cette classe et reste indépendante des cours ITM.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Post,
                    $"api/etablissement/cycles/promotions/{promotionId}/matieres", new
                    {
                        code = f.Text("code"), name = f.Text("nom"),
                        coefficient = f.Amount("coefficient"), hours = f.Amount("heures"),
                        periodMaxScore = f.Amount("maximum") is > 0 and var max ? max : (decimal?)null,
                    });
                Shell.Notify("Matière annuelle enregistrée.");
                return true;
            });
        form.Add("code", "Code matière", required: true);
        form.Add("nom", "Dénomination", required: true);
        form.Add("coefficient", "Coefficient", FieldKind.Amount, "1", required: true);
        form.Add("heures", "Volume horaire annuel", FieldKind.Amount, "30", required: true);
        form.Add("maximum", "Maximum par période", FieldKind.Amount, "10",
            hint: "Points de la branche pour une période de travail (par exemple 10, 20 ou 50). Laissez vide en maternelle.");
        if (await Shell.Ask(form)) await LoadSubjectsAsync();
    }

    public AsyncCommand SetSubjectMax { get; }

    /// <summary>Corrige le maximum par période d'une branche tant que la délibération n'est pas publiée.</summary>
    private async Task SetSubjectMaxAsync()
    {
        if (Subjects.Count == 0) { Shell.Notify("Ajoutez d'abord les branches de la classe."); return; }
        var choices = Subjects.Select(s => new ChoiceItem(s.Id.ToString(), $"{s.Code} · {s.Name}")).ToList();
        var form = new FormViewModel("Maximum d'une branche",
            "Le maximum de l'examen en découle selon le règlement pédagogique de l'année.",
            async f =>
            {
                await Shell.Api.SendAsync(HttpMethod.Put,
                    $"api/etablissement/cycles/matieres/{f.ChoiceValue("branche")}/maximum",
                    new { periodMaxScore = f.Amount("maximum") });
                Shell.Notify("Maximum de la branche enregistré.");
                return true;
            });
        form.AddChoice("branche", "Branche", choices, choices[0].Value, required: true);
        form.Add("maximum", "Maximum par période", FieldKind.Amount, "10", required: true);
        if (await Shell.Ask(form)) await LoadSubjectsAsync();
    }

}
