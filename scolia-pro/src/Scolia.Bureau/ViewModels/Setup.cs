using System.Text.RegularExpressions;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Assistant de première installation, en deux étapes comme dans la version précédente :
/// 1) établissement et année scolaire, 2) comptes et sécurité (administrateur, secrétariat, comptabilité).
/// </summary>
public sealed class SetupViewModel : Observable
{
    private static readonly Regex YearPattern = new(@"^\s*(\d{4})\s*-\s*(\d{4})\s*$");

    private readonly ShellViewModel shell;
    private int step = 1;
    private bool busy;
    private string error = string.Empty;

    public SetupViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        StartMonths = Months;
        MonthCounts = [.. Enumerable.Range(6, 7).Select(n => new ChoiceItem(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{n} mois"))];
        StartMonth = Months.First(m => m.Value == "9");
        MonthCount = MonthCounts.First(m => m.Value == "10");
        Next = new AsyncCommand(NextAsync);
        Back = new RelayCommand(() => { Step = 1; Error = string.Empty; });
        Finish = new AsyncCommand(FinishAsync);
        JoinExisting = new AsyncCommand(() => shell.ChooseServerAsync());
    }

    public IReadOnlyList<ChoiceItem> StartMonths { get; }
    public IReadOnlyList<ChoiceItem> MonthCounts { get; }

    private static readonly IReadOnlyList<ChoiceItem> Months =
    [
        new("1", "Janvier"), new("2", "Février"), new("3", "Mars"), new("4", "Avril"), new("5", "Mai"), new("6", "Juin"),
        new("7", "Juillet"), new("8", "Août"), new("9", "Septembre"), new("10", "Octobre"), new("11", "Novembre"), new("12", "Décembre"),
    ];

    /// <summary>Étape affichée : 1 ou 2.</summary>
    public int Step
    {
        get => step;
        set
        {
            if (Set(ref step, value))
            {
                Raise(nameof(IsStepOne));
                Raise(nameof(IsStepTwo));
                Raise(nameof(StepLabel));
                Raise(nameof(StepTitle));
                Raise(nameof(Progress));
            }
        }
    }

    /// <summary>Avancement de l'assistant, en pourcentage.</summary>
    public double Progress => step * 50.0;

    public bool IsStepOne => step == 1;
    public bool IsStepTwo => step == 2;
    public string StepLabel => $"Étape {step} sur 2";
    public string StepTitle => step == 1 ? "Établissement et année scolaire" : "Comptes et sécurité";

    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => error.Length > 0;

    // ---- Étape 1 : établissement ----
    private string schoolName = string.Empty;
    public string SchoolName { get => schoolName; set => Set(ref schoolName, value); }

    private string shortName = string.Empty;
    public string ShortName { get => shortName; set => Set(ref shortName, value); }

    private string city = string.Empty;
    public string City { get => city; set => Set(ref city, value); }

    private string phone = string.Empty;
    public string Phone { get => phone; set => Set(ref phone, value); }

    private string motto = string.Empty;
    public string Motto { get => motto; set => Set(ref motto, value); }

    private string yearLabel = string.Empty;
    public string YearLabel { get => yearLabel; set => Set(ref yearLabel, value); }

    private ChoiceItem startMonth = null!;
    public ChoiceItem StartMonth { get => startMonth; set => Set(ref startMonth, value); }

    private ChoiceItem monthCount = null!;
    public ChoiceItem MonthCount { get => monthCount; set => Set(ref monthCount, value); }

    public string CurrencyLine => "Monnaie : franc congolais (FC)";
    public bool LoadDemo { get; set; }

    // ---- Étape 2 : comptes ----
    private string adminUserName = "admin";
    public string AdminUserName { get => adminUserName; set => Set(ref adminUserName, value); }

    private string adminDisplayName = "Administrateur";
    public string AdminDisplayName { get => adminDisplayName; set => Set(ref adminDisplayName, value); }

    private string adminPassword = string.Empty;
    public string AdminPassword { get => adminPassword; set => Set(ref adminPassword, value); }

    private string adminConfirm = string.Empty;
    public string AdminConfirm { get => adminConfirm; set => Set(ref adminConfirm, value); }

    private bool useSecretariat = true;
    public bool UseSecretariat { get => useSecretariat; set => Set(ref useSecretariat, value); }

    private string secretariatUserName = "secretariat";
    public string SecretariatUserName { get => secretariatUserName; set => Set(ref secretariatUserName, value); }

    private string secretariatDisplayName = "Secrétariat";
    public string SecretariatDisplayName { get => secretariatDisplayName; set => Set(ref secretariatDisplayName, value); }

    private string secretariatPassword = string.Empty;
    public string SecretariatPassword { get => secretariatPassword; set => Set(ref secretariatPassword, value); }

    private string secretariatConfirm = string.Empty;
    public string SecretariatConfirm { get => secretariatConfirm; set => Set(ref secretariatConfirm, value); }

    private bool useComptabilite = true;
    public bool UseComptabilite { get => useComptabilite; set => Set(ref useComptabilite, value); }

    private string comptabiliteUserName = "comptabilite";
    public string ComptabiliteUserName { get => comptabiliteUserName; set => Set(ref comptabiliteUserName, value); }

    private string comptabiliteDisplayName = "Comptabilité";
    public string ComptabiliteDisplayName { get => comptabiliteDisplayName; set => Set(ref comptabiliteDisplayName, value); }

    private string comptabilitePassword = string.Empty;
    public string ComptabilitePassword { get => comptabilitePassword; set => Set(ref comptabilitePassword, value); }

    private string comptabiliteConfirm = string.Empty;
    public string ComptabiliteConfirm { get => comptabiliteConfirm; set => Set(ref comptabiliteConfirm, value); }

    public AsyncCommand Next { get; }
    public RelayCommand Back { get; }
    public AsyncCommand Finish { get; }

    /// <summary>Ce poste ne doit pas créer un établissement : il rejoint le SCOLIA déjà installé sur le réseau.</summary>
    public AsyncCommand JoinExisting { get; }

    /// <summary>Propose l'année scolaire en cours, calculée par le serveur.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var proposal = await shell.Api.GetAsync<YearProposalDto>("api/installation/annee-proposee");
            if (string.IsNullOrWhiteSpace(YearLabel)) YearLabel = proposal.Label;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException) { }
    }

    private Task NextAsync()
    {
        Error = ValidateStepOne() ?? string.Empty;
        if (HasError) return Task.CompletedTask;
        Step = 2;
        return Task.CompletedTask;
    }

    private async Task FinishAsync()
    {
        Error = ValidateStepTwo() ?? string.Empty;
        if (HasError) return;
        Busy = true;
        try
        {
            var session = await shell.Api.SendAsync<SessionDto>(HttpMethod.Post, "api/installation", BuildRequest());
            await shell.CompleteSetupAsync(session);
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est démarré."; }
        finally { Busy = false; }
    }

    /// <summary>Contrôles de l'étape 1, avant de passer aux comptes. Renvoie le premier problème, ou null.</summary>
    public string? ValidateStepOne()
    {
        if (string.IsNullOrWhiteSpace(SchoolName)) return "Indiquez le nom de l'établissement.";
        if (string.IsNullOrWhiteSpace(ShortName)) return "Indiquez le sigle de l'établissement.";
        var years = YearPattern.Match(YearLabel);
        if (!years.Success || int.Parse(years.Groups[2].Value) != int.Parse(years.Groups[1].Value) + 1)
            return "L'année scolaire doit couvrir deux années consécutives, par exemple 2026 - 2027.";
        if (StartMonth is null || MonthCount is null) return "Choisissez le mois de début et la durée de l'année.";
        return null;
    }

    /// <summary>Contrôles de l'étape 2 : identifiants uniques, mots de passe solides et confirmés.</summary>
    public string? ValidateStepTwo()
    {
        var accounts = new List<(string Who, string UserName, string DisplayName, string Password, string Confirm)>
        {
            ("L'administrateur", AdminUserName, AdminDisplayName, AdminPassword, AdminConfirm),
        };
        if (UseSecretariat) accounts.Add(("Le compte secrétariat", SecretariatUserName, SecretariatDisplayName, SecretariatPassword, SecretariatConfirm));
        if (UseComptabilite) accounts.Add(("Le compte comptabilité", ComptabiliteUserName, ComptabiliteDisplayName, ComptabilitePassword, ComptabiliteConfirm));

        foreach (var (who, userName, displayName, password, confirm) in accounts)
        {
            var problem = ValidateAccount(who, userName, displayName, password, confirm);
            if (problem is not null) return problem;
        }
        var names = accounts.Select(a => a.UserName.Trim().ToLowerInvariant()).ToList();
        return names.Distinct().Count() == names.Count ? null : "Deux comptes ne peuvent pas porter le même identifiant.";
    }

    private static string? ValidateAccount(string who, string userName, string displayName, string password, string confirm)
    {
        var name = userName.Trim().ToLowerInvariant();
        if (name.Length is < 3 or > 40 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            return $"{who} : identifiant de 3 à 40 caractères (lettres, chiffres, point, tiret ou tiret bas).";
        if (string.IsNullOrWhiteSpace(displayName)) return $"{who} : indiquez le nom de la personne.";
        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return $"{who} : le mot de passe compte au moins 8 caractères, avec des lettres et des chiffres.";
        if (password != confirm) return $"{who} : les deux mots de passe ne correspondent pas.";
        return null;
    }

    private object BuildRequest()
    {
        var match = YearPattern.Match(YearLabel);
        var accounts = new List<object>();
        if (UseSecretariat) accounts.Add(new { role = "Secretariat", userName = SecretariatUserName.Trim(), displayName = SecretariatDisplayName.Trim(), password = SecretariatPassword });
        if (UseComptabilite) accounts.Add(new { role = "Comptabilite", userName = ComptabiliteUserName.Trim(), displayName = ComptabiliteDisplayName.Trim(), password = ComptabilitePassword });
        return new
        {
            schoolName = SchoolName.Trim(),
            shortName = ShortName.Trim(),
            motto = Motto.Trim() is { Length: > 0 } motto ? motto : null,
            city = City.Trim() is { Length: > 0 } city ? city : null,
            phone = Phone.Trim() is { Length: > 0 } phone ? phone : null,
            currencyCode = "CDF",
            currencySymbol = "FC",
            yearLabel = $"{match.Groups[1].Value}-{match.Groups[2].Value}",
            startMonth = int.Parse(StartMonth.Value, System.Globalization.CultureInfo.InvariantCulture),
            monthCount = int.Parse(MonthCount.Value, System.Globalization.CultureInfo.InvariantCulture),
            adminUserName = AdminUserName.Trim(),
            adminDisplayName = AdminDisplayName.Trim(),
            adminPassword = AdminPassword,
            loadDemoData = LoadDemo,
            accounts,
        };
    }
}
