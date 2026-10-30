using System.Text.RegularExpressions;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Promotion saisie dans l'assistant, comme dans le paramétrage de la version 79 : code de 4 caractères, désignation, mensualité.</summary>
public sealed class PromotionDraftVm : Observable
{
    private string code = string.Empty;
    private string name = string.Empty;
    private string fee = string.Empty;

    public PromotionDraftVm(Action<PromotionDraftVm> remove) => Remove = new RelayCommand(() => remove(this));

    public string Code { get => code; set => Set(ref code, value.ToUpperInvariant()); }
    public string Name { get => name; set => Set(ref name, value); }
    public string Fee { get => fee; set => Set(ref fee, value); }
    public RelayCommand Remove { get; }

    /// <summary>Ligne laissée vide : elle est ignorée.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fee);
}

/// <summary>
/// Assistant de première installation, en deux étapes comme dans la version précédente :
/// 1) établissement, logo, année scolaire et promotions, 2) comptes : le secrétariat, qui administre SCOLIA, et la comptabilité.
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
        PrepareTailnet = new AsyncCommand(Installer.BeginAsync);
        Installer.Refresh();
        AddPromotion = new RelayCommand(() => Promotions.Add(new PromotionDraftVm(d => Promotions.Remove(d))));
        PickLogo = new AsyncCommand(PickLogoAsync);
        ClearLogo = new RelayCommand(() => SetLogo(null, null));
        AddPromotion.Execute(null);
    }

    // ---- Étape 1 : logo et promotions ----
    private byte[]? logoData;
    private string? logoName;
    private Avalonia.Media.Imaging.Bitmap? logo;

    /// <summary>Logo choisi : il figure ensuite sur la connexion, les reçus, les listes et les cartes d'accès.</summary>
    public Avalonia.Media.Imaging.Bitmap? Logo { get => logo; private set { if (Set(ref logo, value)) Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;
    public string LogoLine => logoName ?? "Aucun logo choisi : le symbole neutre de SCOLIA sera affiché.";

    public System.Collections.ObjectModel.ObservableCollection<PromotionDraftVm> Promotions { get; } = [];
    public TailscaleInstaller Installer { get; } = TailscaleInstaller.Instance;
    public AsyncCommand PrepareTailnet { get; }
    public RelayCommand AddPromotion { get; }
    public AsyncCommand PickLogo { get; }
    public RelayCommand ClearLogo { get; }

    private async Task PickLogoAsync()
    {
        if (shell.PickFileHandler is null) return;
        var picked = await shell.PickFileHandler([".png", ".jpg", ".jpeg"]);
        if (picked is null) return;
        SetLogo(picked.Data, picked.Name);
    }

    public void SetLogo(byte[]? data, string? name)
    {
        try
        {
            Logo = data is null ? null : new Avalonia.Media.Imaging.Bitmap(new MemoryStream(data));
            logoData = data;
            logoName = name;
            Error = string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Error = "Cette image ne peut pas être lue. Choisissez un fichier PNG ou JPEG.";
        }
        Raise(nameof(LogoLine));
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
    public string StepTitle => step == 1 ? "Établissement, logo, année et promotions" : "Comptes et sécurité";

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
    // Le compte secrétariat est l'administrateur : ses fonctions, le paramétrage et les utilisateurs.
    private string adminUserName = "secretariat";
    public string AdminUserName { get => adminUserName; set => Set(ref adminUserName, value); }

    private string adminDisplayName = "Secrétariat";
    public string AdminDisplayName { get => adminDisplayName; set => Set(ref adminDisplayName, value); }

    private string adminPassword = string.Empty;
    public string AdminPassword { get => adminPassword; set => Set(ref adminPassword, value); }

    private string adminConfirm = string.Empty;
    public string AdminConfirm { get => adminConfirm; set => Set(ref adminConfirm, value); }

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
            var problems = await SaveLogoAndPromotionsAsync();
            await shell.CompleteSetupAsync(session);
            if (problems.Count > 0) shell.Notify(string.Join(" ", problems));
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
        if (Phone.Length > 0 && !(Phone.Length == PhoneInput.Length && Phone[0] == '0')) return "Le téléphone compte 10 chiffres commençant par 0, par exemple 0812345678.";
        var years = YearPattern.Match(YearLabel);
        if (!years.Success || int.Parse(years.Groups[2].Value) != int.Parse(years.Groups[1].Value) + 1)
            return "L'année scolaire doit couvrir deux années consécutives, par exemple 2026 - 2027.";
        if (StartMonth is null || MonthCount is null) return "Choisissez le mois de début et la durée de l'année.";
        return ValidatePromotions();
    }

    /// <summary>Promotions de l'assistant : code de 4 lettres ou chiffres, unique, désignation et mensualité en francs entiers.</summary>
    public string? ValidatePromotions()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rank = 0;
        foreach (var p in Promotions)
        {
            rank++;
            if (p.IsBlank) continue;
            var code = p.Code.Trim();
            if (code.Length != 4 || !code.All(char.IsAsciiLetterOrDigit))
                return $"Promotion {rank} : le code compte 4 caractères, lettres ou chiffres, par exemple INF1.";
            if (!seen.Add(code)) return $"Promotion {rank} : le code {code} est déjà utilisé.";
            if (string.IsNullOrWhiteSpace(p.Name)) return $"Promotion {rank} : indiquez la désignation, par exemple « Première année ».";
            if (!decimal.TryParse(p.Fee.Replace(" ", string.Empty, StringComparison.Ordinal), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var fee)
                || fee <= 0 || fee != decimal.Truncate(fee))
                return $"Promotion {rank} : la mensualité est un montant entier en francs, par exemple 55000.";
        }
        return null;
    }

    /// <summary>Contrôles de l'étape 2 : identifiants uniques, mots de passe solides et confirmés.</summary>
    public string? ValidateStepTwo()
    {
        var accounts = new List<(string Who, string UserName, string DisplayName, string Password, string Confirm)>
        {
            ("Le compte secrétariat", AdminUserName, AdminDisplayName, AdminPassword, AdminConfirm),
        };
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

    /// <summary>
    /// Une fois l'établissement créé, le logo et les promotions passent par les mêmes règles que les Paramètres.
    /// Un échec n'annule pas l'installation : il est signalé, et se corrige ensuite dans Paramètres.
    /// </summary>
    private async Task<List<string>> SaveLogoAndPromotionsAsync()
    {
        var problems = new List<string>();
        if (logoData is not null)
        {
            try { await shell.Api.UploadAsync<object>(HttpMethod.Put, "api/etablissement/logo", [new UploadedFile("fichier", logoName ?? "logo.png", logoData)]); }
            catch (ApiException ex) { problems.Add($"Logo non enregistré : {ex.Message}"); }
        }
        foreach (var p in Promotions.Where(p => !p.IsBlank))
        {
            var code = p.Code.Trim();
            var level = code.FirstOrDefault(char.IsDigit) is var digit and not '\0' ? digit - '0' : 1;
            try
            {
                await shell.Api.SendAsync(HttpMethod.Post, "api/etablissement/promotions", new
                {
                    code, name = p.Name.Trim(), sectionId = (Guid?)null, level = Math.Max(level, 1), capacity = (int?)null, isActive = true,
                    nextPromotionCode = (string?)null,
                    monthlyFee = decimal.Parse(p.Fee.Replace(" ", string.Empty, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture),
                    oneTimeFees = Array.Empty<object>(),
                });
            }
            catch (ApiException ex) { problems.Add($"Promotion {code} non créée : {ex.Message}"); }
        }
        return problems;
    }

    private object BuildRequest()
    {
        var match = YearPattern.Match(YearLabel);
        var accounts = new List<object>();
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
