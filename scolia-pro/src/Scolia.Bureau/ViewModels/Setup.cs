using System.Text.RegularExpressions;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Domain.School;

namespace Scolia.Bureau.ViewModels;

/// <summary>Promotion saisie dans l'assistant, comme dans le paramétrage de la version 79 : code de 4 caractères, désignation, mensualité.</summary>
public sealed class PromotionDraftVm : Observable
{
    private readonly Func<PromotionDraftVm, PromotionNames.Context> context;
    private string code = string.Empty;
    private string name = string.Empty;
    private string fee = string.Empty;
    private IReadOnlyList<string> suggestions = [];
    private bool filling;

    public PromotionDraftVm(Action<PromotionDraftVm> remove, Func<PromotionDraftVm, PromotionNames.Context>? context = null)
    {
        this.context = context ?? (_ => PromotionNames.Context.Empty);
        Remove = new RelayCommand(() => remove(this));
        Pick = new RelayCommand<string>(Choose);
    }

    public string Code { get => code; set { if (Set(ref code, (value ?? string.Empty).ToUpperInvariant())) Refresh(); } }
    public string Name { get => name; set { if (Set(ref name, value ?? string.Empty)) Refresh(); } }
    public string Fee { get => fee; set => Set(ref fee, value); }
    public RelayCommand Remove { get; }

    /// <summary>Désignations prédites d'après le code et la saisie : « INF1 » donne « Première Infirmière »…</summary>
    public IReadOnlyList<string> Suggestions { get => suggestions; private set { if (Set(ref suggestions, value)) Raise(nameof(HasSuggestions)); } }
    public bool HasSuggestions => suggestions.Count > 0;
    public RelayCommand<string> Pick { get; }

    /// <summary>Ligne laissée vide : elle est ignorée.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fee);

    public void Refresh()
    {
        if (filling) return;
        var known = context(this);
        var used = known.Promotions.Select(p => PromotionNames.Normalize(p.Name)).ToHashSet();
        Suggestions = PromotionNames.Suggest(code, name, known).Where(n => !used.Contains(PromotionNames.Normalize(n))).Take(4).ToList();
    }

    /// <summary>Une désignation choisie remplit aussi le code (INF + niveau) s'il manque ou ne correspond pas.</summary>
    private void Choose(string? chosen)
    {
        if (string.IsNullOrEmpty(chosen)) return;
        filling = true;
        try
        {
            Name = chosen;
            if (PromotionNames.LevelOf(chosen) is { } level)
            {
                var prefix = PromotionNames.ParseCode(code).Prefix;
                if (prefix.Length < 2 && PromotionNames.TradeOf(chosen) is { } trade) prefix = PromotionNames.PrefixFor(trade, context(this)) ?? string.Empty;
                if (prefix.Length >= 2) Code = PromotionNames.Code(prefix, level);
            }
        }
        finally { filling = false; }
        Suggestions = [];
    }

    /// <summary>Ligne suivante préparée d'après celle-ci : INF1 « Première Infirmière » donne INF2 « Deuxième Infirmière ».</summary>
    public (string Code, string Name)? Next()
    {
        var (prefix, level) = PromotionNames.ParseCode(code);
        if (prefix.Length < 2 || level is not { } l) return null;
        var trade = PromotionNames.TradeOf(name) ?? PromotionNames.TradeFor(prefix, context(this)) ?? string.Empty;
        return (PromotionNames.Code(prefix, l + 1), PromotionNames.Name(l + 1, trade));
    }
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
        InstitutionType = InstitutionTypes[0];
        StartMonths = Months;
        EndMonths = Months;
        MonthCounts = [.. Enumerable.Range(1, 12).Select(n => new ChoiceItem(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{n} mois"))];
        PaymentDeadlineDays = [.. Enumerable.Range(1, 28).Select(n => new ChoiceItem(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Le {n} du mois suivant"))];
        StartMonth = Months.First(m => m.Value == "9");
        EndMonth = Months.First(m => m.Value == "6");
        endChosen = false;
        MonthCount = MonthCounts.First(m => m.Value == "10");
        LastOnTimeDay = PaymentDeadlineDays.First(d => d.Value == "5");
        Next = new AsyncCommand(NextAsync);
        Back = new RelayCommand(() => { Step = 1; Error = string.Empty; });
        Finish = new AsyncCommand(FinishAsync);
        JoinExisting = new AsyncCommand(() => shell.ChooseServerAsync());
        AddPromotion = new RelayCommand(AddPromotionRow);
        PickLogo = new AsyncCommand(PickLogoAsync);
        ClearLogo = new RelayCommand(() => SetLogo(null, null));
        AddPromotion.Execute(null);
    }

    /// <summary>
    /// Nouvelle ligne : elle continue la précédente (INF1 puis INF2, « Deuxième Infirmière », même mensualité),
    /// comme une suite que l'on n'a plus qu'à vérifier.
    /// </summary>
    private void AddPromotionRow()
    {
        var row = new PromotionDraftVm(d => Promotions.Remove(d), DraftContext);
        if (Promotions.LastOrDefault() is { } previous && previous.Next() is { } next
            && Promotions.All(p => !p.Code.Equals(next.Code, StringComparison.OrdinalIgnoreCase)))
        {
            row.Code = next.Code;
            row.Name = next.Name;
            row.Fee = previous.Fee;
        }
        Promotions.Add(row);
        row.Refresh();
    }

    /// <summary>Les autres lignes de l'assistant servent d'exemples : un métier saisi une fois est reconnu ensuite.</summary>
    private PromotionNames.Context DraftContext(PromotionDraftVm current) => new([],
        Promotions.Where(p => p != current && !string.IsNullOrWhiteSpace(p.Code)).Select(p => (p.Code, p.Name)).ToList());

    // ---- Étape 1 : logo et promotions ----
    private byte[]? logoData;
    private string? logoName;
    private Avalonia.Media.Imaging.Bitmap? logo;

    /// <summary>Logo choisi : il figure ensuite sur la connexion, les reçus, les listes et les cartes d'accès.</summary>
    public Avalonia.Media.Imaging.Bitmap? Logo { get => logo; private set { if (Set(ref logo, value)) Raise(nameof(HasLogo)); } }
    public bool HasLogo => logo is not null;
    public string LogoLine => logoName ?? "Aucun logo choisi : le symbole neutre de SCOLIA sera affiché.";

    public System.Collections.ObjectModel.ObservableCollection<PromotionDraftVm> Promotions { get; } = [];
    public RelayCommand AddPromotion { get; }
    public AsyncCommand PickLogo { get; }
    public RelayCommand ClearLogo { get; }

    private async Task PickLogoAsync()
    {
        if (shell.PickFileHandler is null) return;
        var picked = await shell.PickFileHandler(LogoImage.Extensions);
        if (picked is null) return;
        SetLogo(picked.Data, picked.Name);
    }

    public void SetLogo(byte[]? data, string? name)
    {
        try
        {
            // Toute image courante est convertie en PNG léger : c'est ce PNG qui est montré puis enregistré.
            var png = data is null ? null : LogoImage.ToPng(data, name);
            Logo = png is null ? null : new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
            logoData = png;
            logoName = name;
            Error = string.Empty;
        }
        catch (LogoImageException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Error = $"Cette image ne peut pas être lue. Formats acceptés : {LogoImage.Formats}.";
        }
        Raise(nameof(LogoLine));
    }

    // Les écoles privées sont disponibles avec un aperçu pédagogique non officiel ;
    // le supérieur LMD et les établissements publics attendent validation métier.
    public IReadOnlyList<ChoiceItem> InstitutionTypes { get; } =
    [
        new("ItmIm", "ITM / IM"),
        new("Maternelle", "École maternelle privée"),
        new("Primaire", "École primaire privée"),
        new("Secondaire", "École secondaire / technique privée"),
        new("ComplexeScolaire", "Complexe scolaire privé — cycles à définir ensuite"),
        new("SuperieurLmd", "Université / supérieur LMD — en préparation"),
    ];
    private ChoiceItem institutionType = null!;
    public ChoiceItem InstitutionType { get => institutionType; set => Set(ref institutionType, value); }

    public IReadOnlyList<ChoiceItem> StartMonths { get; }
    public IReadOnlyList<ChoiceItem> EndMonths { get; }
    public IReadOnlyList<ChoiceItem> MonthCounts { get; }
    public IReadOnlyList<ChoiceItem> PaymentDeadlineDays { get; }

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
    /// <summary>Le dernier mois suit le premier (dix mois d'école) tant qu'il n'a pas été choisi à la main.</summary>
    public ChoiceItem StartMonth
    {
        get => startMonth;
        set
        {
            if (!Set(ref startMonth, value)) return;
            if (!endChosen && value is not null && endMonth is not null)
            {
                var start = int.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture);
                var end = MonthsAhead(start, 9).ToString(System.Globalization.CultureInfo.InvariantCulture);
                followingEnd = true;
                try { EndMonth = Months.First(m => m.Value == end); }
                finally { followingEnd = false; }
            }
            UpdateMonthRange();
        }
    }

    private bool endChosen;
    private bool followingEnd;
    private ChoiceItem endMonth = null!;
    public ChoiceItem EndMonth
    {
        get => endMonth;
        set
        {
            if (!Set(ref endMonth, value)) return;
            if (!followingEnd && startMonth is not null) endChosen = true;
            UpdateMonthRange();
        }
    }

    /// <summary>Mois situé n mois plus loin, en passant décembre : septembre + 9 donne juin.</summary>
    public static int MonthsAhead(int month, int count) => (month - 1 + count) % 12 + 1;

    private ChoiceItem monthCount = null!;
    /// <summary>Valeur calculée à partir du mois de début et de fin, jamais saisie à l'aveugle.</summary>
    public ChoiceItem MonthCount { get => monthCount; set => Set(ref monthCount, value); }

    private ChoiceItem lastOnTimeDay = null!;
    public ChoiceItem LastOnTimeDay { get => lastOnTimeDay; set { if (Set(ref lastOnTimeDay, value)) Raise(nameof(MonthRangeSummary)); } }

    public string MonthRangeSummary => StartMonth is null || EndMonth is null || LastOnTimeDay is null
        ? "Choisissez les mois et la date limite."
        : $"{MonthCount?.Label ?? "Période"} · {StartMonth.Label} à {EndMonth.Label}. " +
          $"Paiement autorisé jusqu'au {LastOnTimeDay.Value} du mois suivant, retard dès le lendemain.";

    private void UpdateMonthRange()
    {
        if (StartMonth is null || EndMonth is null) return;
        var start = int.Parse(StartMonth.Value, System.Globalization.CultureInfo.InvariantCulture);
        var end = int.Parse(EndMonth.Value, System.Globalization.CultureInfo.InvariantCulture);
        var count = (end - start + 12) % 12 + 1;
        if (MonthCounts is { Count: > 0 })
            MonthCount = MonthCounts.First(m => m.Value == count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Raise(nameof(MonthRangeSummary));
    }

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
        if (InstitutionType is null || !InstitutionProfiles.IsEnabled(InstitutionType.Value))
            return "Ce profil pédagogique n'est pas encore activé ; choisissez ITM / IM ou une école privée.";
        if (LoadDemo && InstitutionType.Value != InstitutionProfiles.ItmIm)
            return "Les données fictives ITM sont réservées au profil ITM / IM : désactivez les exemples.";
        if (string.IsNullOrWhiteSpace(SchoolName)) return "Indiquez le nom de l'établissement.";
        if (string.IsNullOrWhiteSpace(ShortName)) return "Indiquez le sigle de l'établissement.";
        if (Phone.Length > 0 && !(Phone.Length == PhoneInput.Length && Phone[0] == '0')) return "Le téléphone compte 10 chiffres commençant par 0, par exemple 0812345678.";
        var years = YearPattern.Match(YearLabel);
        if (!years.Success || int.Parse(years.Groups[2].Value) != int.Parse(years.Groups[1].Value) + 1)
            return "L'année scolaire doit couvrir deux années consécutives, par exemple 2026 - 2027.";
        if (StartMonth is null || EndMonth is null || MonthCount is null || LastOnTimeDay is null)
            return "Choisissez le mois de début, le mois de fin et le dernier jour sans retard.";
        return ValidatePromotions();
    }

    /// <summary>Promotions de l'assistant : code de 4 lettres ou chiffres, unique, désignation et mensualité en francs entiers.</summary>
    public string? ValidatePromotions()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rank = 0;
        if (InstitutionType.Value == InstitutionProfiles.ComplexeScolaire &&
            Promotions.Any(p => !p.IsBlank))
            return "Pour un complexe scolaire, installez d'abord l'établissement, choisissez ses cycles dans Paramètres > Cycles scolaires, puis créez les classes dans le cycle correspondant.";
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
                || (InstitutionType.Value == InstitutionProfiles.ItmIm ? fee <= 0 : fee < 0)
                || fee != decimal.Truncate(fee))
                return $"Promotion {rank} : indiquez un montant entier en francs (supérieur à zéro pour ITM/IM ; zéro autorisé si aucune mensualité n'est appliquée au cycle privé).";
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
        if (AccessCode.Problem(password) is { } weak)
            return $"{who} : {char.ToLowerInvariant(weak[0])}{weak[1..]}";
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
            try { await shell.Api.UploadAsync<object>(HttpMethod.Put, "api/etablissement/logo", [new UploadedFile("fichier", Path.ChangeExtension(logoName ?? "logo", ".png"), logoData)]); }
            catch (ApiException ex) { problems.Add($"Logo non enregistré : {ex.Message}"); }
        }
        // Sur une école à cycle unique, toutes les classes appartiennent à ce cycle.
        // Un complexe scolaire exige d'abord un choix explicite des cycles : aucun
        // rattachement de classe n'est inventé ou déduit d'un code ambigu.
        Guid? schoolCycleId = null;
        if (InstitutionType.Value is InstitutionProfiles.Maternelle or InstitutionProfiles.Primaire
            or InstitutionProfiles.Secondaire)
        {
            try
            {
                var configured = await shell.Api.GetAsync<SchoolCycleConfigurationDto>("api/etablissement/cycles");
                schoolCycleId = configured.Cycles.Single(c =>
                    c.Kind == InstitutionType.Value && c.IsActive).Id;
            }
            catch (Exception ex) when (ex is ApiException or InvalidOperationException)
            {
                problems.Add("Impossible de retrouver le cycle scolaire de l'établissement. Configurez les classes dans Paramètres : " + ex.Message);
                return problems;
            }
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
                    schoolCycleId,
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
            institutionType = InstitutionType.Value,
            schoolName = SchoolName.Trim(),
            shortName = ShortName.Trim(),
            motto = Motto.Trim() is { Length: > 0 } motto ? motto : null,
            city = City.Trim() is { Length: > 0 } city ? city : null,
            phone = Phone.Trim() is { Length: > 0 } phone ? phone : null,
            currencyCode = "CDF",
            currencySymbol = "FC",
            yearLabel = $"{match.Groups[1].Value}-{match.Groups[2].Value}",
            startMonth = int.Parse(StartMonth.Value, System.Globalization.CultureInfo.InvariantCulture),
            endMonth = int.Parse(EndMonth.Value, System.Globalization.CultureInfo.InvariantCulture),
            monthCount = int.Parse(MonthCount.Value, System.Globalization.CultureInfo.InvariantCulture),
            lastOnTimeDay = int.Parse(LastOnTimeDay.Value, System.Globalization.CultureInfo.InvariantCulture),
            adminUserName = AdminUserName.Trim(),
            adminDisplayName = AdminDisplayName.Trim(),
            adminPassword = AdminPassword,
            loadDemoData = LoadDemo,
            accounts,
        };
    }
}
