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
    }

    private async Task SaveAsync()
    {
        if (!CanConfigure)
        {
            Shell.Notify("Les profils maternelle, primaire et secondaire doivent être validés avant activation.");
            return;
        }
        var chosen = Cycles.Where(c => c.Included).ToArray();
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
}
