using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Apprenant proposé par la recherche de l'encaissement.</summary>
public sealed record PayerRow(Guid? EnrollmentId, Guid StudentId, string Label, string Detail);

/// <summary>Ligne de répartition d'un versement sur une échéance.</summary>
public sealed record AllocationRowVm(string Label, string Amount, string Remaining);

/// <summary>Ligne du journal des reçus.</summary>
public sealed record ReceiptRowVm(Guid Id, string Number, string Date, string Matricule, string Student, string Promotion, string Amount, string Method, string Status, IBrush StatusBrush, string Cashier);

/// <summary>Encaissement : recherche de l'apprenant, aperçu de la répartition, puis reçu numéroté.</summary>
public sealed class CashierPage : PageViewModel
{
    private readonly Guid? initialStudent;
    private DispatcherTimer? searchTimer;
    private DispatcherTimer? previewTimer;
    private string search = string.Empty;
    // La recherche se fait dans la barre du haut ; le bouton du corps le rappelle, sans message en double.
    private string? searchStatus;
    private int searchVersion;
    private int selectionVersion;
    private PayerRow? selected;
    private EnrollmentChoice? enrollment;
    private DateTime? day = DateTime.Now;
    private ChoiceItem? method;
    private MonthRowVm? selectedMonth;
    private bool updatingMonthSelection;
    private string reference = string.Empty;
    private string amountText = string.Empty;
    private string payerPhone = string.Empty;
    private string note = string.Empty;
    private string studentName = string.Empty;
    private string studentInfo = string.Empty;
    private string balance = string.Empty;
    private string overdue = string.Empty;
    private string balanceAfter = string.Empty;
    private string? hint;
    private ReceiptDto? receipt;
    private string? localReceiptText;
    private Guid? localReceiptId;
    private AccountDto? currentAccount;
    private int previewVersion;
    private bool previewing;
    private bool previewValid;
    private bool paying;

    public CashierPage(ShellViewModel shell, Guid? studentId = null) : base(shell)
    {
        initialStudent = studentId;
        method = Labels.Methods[0];
        Pay = new AsyncCommand(() => Shell.Run(PayAsync));
        Find = new AsyncCommand(() => Shell.Run(SearchAsync));
        NewPayment = new AsyncCommand(() => Shell.Run(() => { Reset(); return Task.CompletedTask; }));
        PrintReceipt = new AsyncCommand(() => Shell.Run(() => PrintAsync()));
        OpenReceipt = new AsyncCommand(() => Shell.Run(() =>
            Shell.Open(() => new ReceiptDetailPage(Shell, receipt?.Id ?? localReceiptId
                ?? throw new InvalidOperationException("Aucun versement choisi.")))));
        ConfigureCalendar = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new SettingsPage(Shell, "annees"))));
        ConfigurePromotion = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new SettingsPage(Shell, "promotions"))));
        Action("Journal des reçus", MaterialIconKind.ReceiptText, () => Shell.Open(() => new ReceiptsPage(Shell)));
        // Le total et le bouton Encaisser restent en bas de l'écran dès qu'un apprenant est choisi :
        // pas besoin de faire défiler tout le formulaire pour valider.
        Sticky = new StickyBarVm("Encaisser", Pay) { Title = "Total à encaisser" };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CanPay) or nameof(Total) or nameof(TotalCaption) or nameof(HasStudent)
                or nameof(HasReceipt) or nameof(HasSelection) or nameof(IsLocked)) UpdateSticky();
        };
    }

    /// <summary>Mois choisis en une ligne courte : leurs noms jusqu'à deux, sinon le premier et le dernier.</summary>
    /// <summary>La date d'échéance est le premier jour de retard : le dernier jour pour payer est la veille.</summary>
    private static string Deadline(DateOnly firstLateDay, bool overdue)
    {
        var french = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return overdue
            ? $"En retard depuis le {firstLateDay.ToString("d MMM yyyy", french)}"
            : $"À payer jusqu'au {firstLateDay.AddDays(-1).ToString("d MMM yyyy", french)}";
    }

    private static string ShortMonth(string label)
    {
        foreach (var prefix in new[] { "Mensualité de ", "Mensualité d'", "Mensualité d’", "Mensualité " })
            if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return label[prefix.Length..];
        return label;
    }

    private string SelectedMonthsSummary()
    {
        // Dans le résumé, « Mensualité de septembre » se lit « septembre » : la barre reste courte.
        var chosen = Months.Where(m => m.IsSelected).Select(m => ShortMonth(m.Label)).ToList();
        // Avec des frais (inscription, examen…), on compte des « éléments » plutôt que des mois.
        var withFees = Months.Any(m => m.IsSelected && m.Label.EndsWith(" (frais)", StringComparison.Ordinal));
        var unit = withFees ? Format.Count(chosen.Count, "élément", "éléments") : Format.Count(chosen.Count, "mois", "mois");
        // La barre d'action Android est compacte : les intitulés configurables
        // peuvent contenir des périodes de rattrapage très longues. Les noms
        // complets restent dans la liste cochée, pas dans la barre fixe.
        if (Phone)
            return $"{unit} sélectionné{(chosen.Count > 1 ? "s" : "")}";
        return chosen.Count <= 2
            ? $"{unit} · {string.Join(", ", chosen)}"
            : $"{unit} · de {chosen[0]} à {chosen[^1]}";
    }

    /// <summary>Met la barre du bas au diapason du formulaire : total, mois choisis, ou ce qui manque encore.</summary>
    private void UpdateSticky()
    {
        if (Sticky is not { } bar) return;
        bar.IsVisible = HasStudent && !HasReceipt;
        bar.Value = HasValidAmount ? Money(PaymentAmount) : "—";
        bar.Detail = IsLocked ? "Encaissement en cours sur un autre poste"
            : !HasSelection ? "Choisissez au moins un mois"
            : !HasValidAmount ? "Vérifiez le montant saisi"
            : !HasValidReference ? "Référence de paiement obligatoire"
            : SelectedMonthsSummary();
        bar.CanRun = CanPay;
    }

    public override string Title => "Encaisser";
    public override string Subtitle => Phone ? "Mois payés · reçu" : "Mois payés, mode, reçu";
    public override MaterialIconKind Glyph => MaterialIconKind.HandCoin;

    public AsyncCommand Pay { get; }
    public AsyncCommand Find { get; }
    public AsyncCommand NewPayment { get; }
    public AsyncCommand PrintReceipt { get; }
    public AsyncCommand OpenReceipt { get; }
    public AsyncCommand ConfigureCalendar { get; }
    public AsyncCommand ConfigurePromotion { get; }
    public bool CanConfigureFees => Shell.Can(Perm.SettingsWrite);

    public ObservableCollection<PayerRow> Results { get; } = [];
    public ObservableCollection<AllocationRowVm> Allocations { get; } = [];
    public IReadOnlyList<ChoiceItem> Methods { get; } = Labels.Methods;

    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            // Invalide immédiatement les anciennes réponses pendant la nouvelle saisie.
            searchVersion++;
            Results.Clear();
            SearchStatus = search.Trim().Length == 0 ? null
                : search.Trim().Length < 2 ? "Encore un caractère : nom ou matricule, deux caractères au moins."
                : "Recherche en cours…";
            searchTimer?.Stop();
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Shell.Api.IsOnline ? 300 : 75) };
            searchTimer.Tick += async (_, _) => { searchTimer?.Stop(); await Shell.Run(SearchAsync); };
            searchTimer.Start();
        }
    }

    public string? SearchStatus
    {
        get => searchStatus;
        private set { if (Set(ref searchStatus, value)) Raise(nameof(HasSearchStatus)); }
    }
    public bool HasSearchStatus => !string.IsNullOrWhiteSpace(SearchStatus);

    public PayerRow? Selected
    {
        get => selected;
        set { if (Set(ref selected, value) && value is not null) _ = Shell.Run(() => ChooseAsync(value)); }
    }

    public string StudentName { get => studentName; private set => Set(ref studentName, value); }
    public string StudentInfo { get => studentInfo; private set => Set(ref studentInfo, value); }
    public string Balance { get => balance; private set => Set(ref balance, value); }
    public string Overdue { get => overdue; private set => Set(ref overdue, value); }
    /// <summary>Rouge quand un retard existe, vert quand l'apprenant est à jour.</summary>
    public IBrush OverdueBrush { get; private set; } = ThemeBrush.Get("ToneRed");
    public string BalanceAfter { get => balanceAfter; private set => Set(ref balanceAfter, value); }
    public string? Hint { get => hint; private set { if (Set(ref hint, value)) Raise(nameof(HasHint)); } }
    public bool HasHint => !string.IsNullOrEmpty(hint);
    public bool HasStudent => enrollment is not null;

    /// <summary>
    /// Arrivée sur Encaisser sans apprenant choisi : la seule recherche, celle de l'en-tête, s'ouvre d'elle-même.
    /// Venu d'une fiche (« Encaisser » sur un apprenant), elle reste fermée.
    /// </summary>
    public bool OpensHeaderSearch => initialStudent is null && enrollment is null && search.Length == 0;
    public RelayCommand OpenHeaderSearch => openHeaderSearch ??= new RelayCommand(() => Shell.HeaderSearch.Open());
    private RelayCommand? openHeaderSearch;
    /// <summary>Le formulaire de versement s'affiche quand un apprenant est choisi et tant que le reçu n'est pas fait.</summary>
    public bool ShowPayment => HasStudent && !HasReceipt;
    public ReceiptDto? Receipt { get => receipt; private set { if (Set(ref receipt, value)) { Raise(nameof(HasReceipt)); Raise(nameof(ShowPayment)); } } }
    public bool HasReceipt => receipt is not null || localReceiptText is not null;
    public bool HasLocalReceipt => localReceiptText is not null;
    public string ReceiptText => localReceiptText ?? (receipt is null ? string.Empty :
        $"Reçu N° {receipt.Number} enregistré pour {Money(receipt.Amount)}.");

    /// <summary>Mois à payer : un versement partiel peut couvrir tout ou partie des mois choisis.</summary>
    public ObservableCollection<MonthRowVm> Months { get; } = [];
    public bool HasMonths => Months.Count > 0;
    public bool NoAvailableMonths => HasStudent && !HasMonths;
    private string monthListStatus = string.Empty;
    public string MonthListStatus { get => monthListStatus; private set => Set(ref monthListStatus, value); }
    public bool HasSelection => SelectedCount > 0;
    public int SelectedCount => Months.Count(m => m.IsSelected);
    public decimal SelectedTotal => Months.Where(m => m.IsSelected).Sum(m => m.Amount);
    /// <summary>Montant réellement remis, qui peut être inférieur au solde des mois sélectionnés.</summary>
    public decimal PaymentAmount => decimal.TryParse(amountText,
        System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out var current)
            ? current
            : decimal.TryParse(amountText, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var invariant) ? invariant : 0m;
    public bool HasValidAmount => HasSelection && PaymentAmount > 0 && PaymentAmount <= SelectedTotal;
    public string AmountText
    {
        get => amountText;
        set
        {
            if (!Set(ref amountText, value)) return;
            Raise(nameof(Total));
            Raise(nameof(TotalCaption));
            Raise(nameof(HasValidAmount));
            SchedulePreview();
        }
    }
    public string Total => HasValidAmount ? Money(PaymentAmount) : "—";
    /// <summary>Choix rapide d'un mois ; les cases à cocher conservent le paiement de plusieurs mois.</summary>
    public MonthRowVm? SelectedMonth
    {
        get => selectedMonth;
        set
        {
            if (!Set(ref selectedMonth, value)) return;
            updatingMonthSelection = true;
            try
            {
                foreach (var month in Months)
                    month.IsSelected = value is not null && ReferenceEquals(month, value);
            }
            finally { updatingMonthSelection = false; }
            MonthsChanged();
        }
    }

    public string TotalCaption => SelectedCount == 0 ? "Aucun mois choisi"
        : HasValidAmount ? $"{SelectedCount} à payer · versement {Money(PaymentAmount)} / dû {Money(SelectedTotal)}"
        : $"Montant invalide : indiquez un montant entre 1 et {Money(SelectedTotal)}";

    /// <summary>Un mois coché ou décoché met à jour le total et l'aperçu de répartition.</summary>
    private void MonthsChanged()
    {
        if (updatingMonthSelection) return;
        // En cochant plusieurs mois, le choix rapide revient à son invite sans perdre les coches.
        var single = SelectedCount == 1 ? Months.First(m => m.IsSelected) : null;
        Set(ref selectedMonth, single);
        // Un changement de mois réinitialise le montant proposé au solde exact.
        amountText = SelectedTotal.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Raise(nameof(AmountText));
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Raise(nameof(HasSelection));
        Raise(nameof(HasValidAmount));
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        previewTimer?.Stop();
        previewValid = false;
        previewVersion++;
        previewing = HasValidAmount;
        Raise(nameof(CanPay));
        if (!HasValidAmount)
        {
            Allocations.Clear();
            BalanceAfter = string.Empty;
            return;
        }
        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Shell.Api.IsOnline ? 150 : 25) };
        previewTimer.Tick += async (_, _) => { previewTimer?.Stop(); await Shell.Run(PreviewAsync); };
        previewTimer.Start();
    }

    private List<Guid> SelectedIds() => Months.Where(m => m.IsSelected).Select(m => m.Id).ToList();

    public DateTime? Day { get => day; set => Set(ref day, value); }
    public ChoiceItem? Method
    {
        get => method;
        set
        {
            if (!Set(ref method, value)) return;
            // La référence interne SCOLIA ne doit jamais remplacer la confirmation d'un opérateur.
            Reference = IsCash && HasStudent ? NewReference(DateTime.Now) : string.Empty;
            Raise(nameof(NeedsExternalReference));
            Raise(nameof(CanPay));
        }
    }
    public bool IsCash => Method?.Value is null or "Especes" or "Cash";
    public bool NeedsExternalReference => !IsCash;
    public bool HasValidReference => IsCash || !string.IsNullOrWhiteSpace(Reference);
    /// <summary>Référence de confirmation saisie pour Mobile Money, banque ou chèque.</summary>
    public string Reference
    {
        get => reference;
        set
        {
            if (!Set(ref reference, value)) return;
            Raise(nameof(ReferenceText));
            Raise(nameof(CanPay));
            UpdateSticky();
        }
    }
    public string ReferenceText => string.IsNullOrEmpty(reference)
        ? (NeedsExternalReference ? "Référence de l'opérateur obligatoire" : "Choisissez un apprenant") : reference;
    /// <summary>Visé par : la personne connectée, qui reçoit l'argent. Le serveur l'inscrit aussi sur le reçu.</summary>
    public string ReviewedBy => Shell.AccountName;
    /// <summary>Téléphone du payeur : celui du tuteur, repris du dossier de l'apprenant choisi.</summary>
    public string PayerPhone { get => payerPhone; private set { if (Set(ref payerPhone, value)) Raise(nameof(PayerPhoneText)); } }
    public string PayerPhoneText => HasStudent ? (string.IsNullOrEmpty(payerPhone) ? "Aucun numéro au dossier" : payerPhone) : "Choisissez un apprenant";
    /// <summary>Observation facultative : laissée vide, le reçu porte « OK ».</summary>
    public string Note { get => note; set => Set(ref note, value); }

    /// <summary>Observation enregistrée quand rien n'est écrit.</summary>
    public const string DefaultNote = "OK";

    /// <summary>
    /// Référence de la transaction : date, heure et quatre signes tirés au hasard. Deux postes qui encaissent
    /// à la même seconde gardent ainsi des références différentes.
    /// </summary>
    public static string NewReference(DateTime now) =>
        $"TR{now:yyMMdd-HHmm}-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2))}";

    public override async Task LoadAsync()
    {
        if (initialStudent is Guid id && enrollment is null)
        {
            var detail = await Shell.Api.GetAsync<StudentDto>($"api/apprenants/{id}");
            var active = detail.Enrollments.FirstOrDefault(e => e.Status == "Actif") ?? (detail.Enrollments.Count > 0 ? detail.Enrollments[0] : null);
            if (active is not null)
            {
                await ChooseAsync(new PayerRow(active.Id, detail.Id, $"{detail.Matricule} · {Format.FullName(detail.LastName, detail.MiddleName, detail.FirstName)}", active.PromotionCode));
                Selected = null;
            }
        }
    }

    public override Task PrintAsync() => Receipt is { } r ? Shell.Download($"api/paiements/{r.Id}/recu.pdf", $"recu-{r.Number}.pdf") : base.PrintAsync();

    public async Task SearchAsync()
    {
        // Une recherche lancée tout de suite (Entrée) remplace celle qui attendait la fin de la frappe : une seule requête.
        searchTimer?.Stop();
        var version = ++searchVersion;
        var query = search.Trim();
        Results.Clear();
        if (query.Length < 2)
        {
            SearchStatus = query.Length == 0 ? null : "Encore un caractère : nom ou matricule, deux caractères au moins.";
            return;
        }
        SearchStatus = "Recherche en cours…";
        try
        {
            PagedResult<StudentListItem>? page = null;
            var local = !Shell.Api.IsOnline ? await FindLocallyAsync(query) : null;
            if (local is null)
            {
                try
                {
                    page = await Shell.Api.GetAsync<PagedResult<StudentListItem>>(
                        "api/apprenants" + ScoliaClient.Query(("q", query), ("taille", 12)));
                }
                catch (OfflineException) { local = await FindLocallyAsync(query); }
            }
            page ??= local?.Page;
            if (version != searchVersion) return;
            if (page is null)
            {
                SearchStatus = "Aucun registre local disponible. Préparez le mode hors ligne lorsque le serveur est accessible.";
                return;
            }
            foreach (var s in page.Items.Take(12))
                Results.Add(new PayerRow(s.EnrollmentId, s.Id, $"{s.Matricule} · {Format.FullName(s.LastName, s.MiddleName, s.FirstName)}",
                    $"{s.PromotionCode ?? "sans promotion"} · {Labels.Status(s.Status ?? string.Empty)}"));
            SearchStatus = page.Items.Count == 0 ? "Aucun apprenant trouvé dans les données disponibles."
                : local is { Complete: false } ? "Recherche limitée aux dossiers téléchargés ; préparation hors ligne incomplète."
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == searchVersion) SearchStatus = "Recherche indisponible. Vérifiez la connexion puis réessayez.";
            throw;
        }
    }

    private Task<OfflineStudentSearch.Result?> FindLocallyAsync(string term)
    {
        var store = Shell.Api.Offline;
        // Initialisation SQLite, déchiffrement et classement hors du thread
        // UI : les interactions Android restent réactives pendant le cache froid.
        return store is null
            ? Task.FromResult<OfflineStudentSearch.Result?>(null)
            : Task.Run(() => OfflineStudentSearch.Search(store, term, null, null, 1, 50));
    }

    public async Task ChooseAsync(PayerRow row)
    {
        // L'apprenant est trouvé : la barre de recherche du haut se referme, le clavier laisse la place au versement.
        if (Shell.Current == this && Shell.HeaderSearch.IsOpen) Shell.HeaderSearch.Close.Execute(null);
        var version = ++selectionVersion;
        ++previewVersion; // Invalide aussi l'aperçu de l'ancien apprenant.
        previewValid = false;
        previewing = true;
        enrollment = null;
        Months.Clear();
        MonthListStatus = string.Empty;
        selectedMonth = null;
        Allocations.Clear();
        BalanceAfter = string.Empty;
        LockedBy = null;
        Raise(nameof(SelectedMonth));
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Receipt = null;
        localReceiptText = null;
        localReceiptId = null;
        currentAccount = null;
        Raise(nameof(HasLocalReceipt));
        Raise(nameof(HasReceipt));
        Raise(nameof(ReceiptText));
        StudentName = string.Empty;
        StudentInfo = string.Empty;
        Balance = string.Empty;
        Overdue = string.Empty;
        Reference = string.Empty;
        Hint = "Chargement du compte de l'apprenant…";
        Raise(nameof(HasStudent));
        Raise(nameof(ShowPayment));
        Raise(nameof(HasMonths));
        Raise(nameof(NoAvailableMonths));
        Raise(nameof(HasSelection));
        Raise(nameof(CanPay));
        if (row.EnrollmentId is not { } eid)
        {
            Hint = "Cet apprenant n'a pas d'inscription active : encaissement impossible.";
            previewing = false;
            Raise(nameof(CanPay));
            return;
        }
        AccountDto account;
        try { account = await Shell.Api.GetAsync<AccountDto>($"api/inscriptions/{eid}/compte"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == selectionVersion)
            {
                Hint = "Compte indisponible. Vérifiez la connexion et sélectionnez à nouveau l'apprenant.";
                previewing = false;
                Raise(nameof(CanPay));
            }
            throw;
        }
        if (version != selectionVersion) return;
        previewing = false;
        currentAccount = account;
        enrollment = new EnrollmentChoice(eid, account.Balance);
        selectedMonth = null;
        Raise(nameof(SelectedMonth));
        // Le compte local projette déjà les versements en attente, y compris
        // partiels : ne pas masquer entièrement un mois encore partiellement dû.
        var pending = Shell.Api.Offline?.Outbox().Count(x => x.Path == "api/paiements") ?? 0;
        // Les mensualités de l'année, puis les autres frais des catégories créées (inscription, examen…),
        // chacun avec son échéance. Les frais pris en charge ou soldés ne sont pas proposés.
        var configuredMonths = account.Lines.Where(l => l.Kind == "Mensualite").ToArray();
        var configured = account.Lines.ToArray();
        foreach (var line in configured.Where(l => l.Remaining > 0).OrderBy(l => l.Kind == "Mensualite" ? 0 : 1).ThenBy(l => l.DueDate))
            Months.Add(new MonthRowVm(line.FeeItemId, line.Kind == "Mensualite" ? line.Label : $"{line.Label} (frais)", line.Remaining,
                Money(line.Remaining), line.IsOverdue, MonthsChanged, Deadline(line.DueDate, line.IsOverdue)));

        MonthListStatus = configuredMonths.Length == 0
            ? "Aucun mois n'est facturé pour cette promotion. Dans Configuration, vérifiez les mois de l'année et attribuez une mensualité à la promotion."
            : configured.All(l => l.Remaining <= 0)
                ? "Toutes les mensualités et tous les frais de cette année sont déjà réglés ou couverts."
                : Months.Count == 0
                    ? "Les mensualités et frais restant à payer sont déjà en attente de synchronisation sur cet appareil."
                    : Months.Any(m => m.Overdue)
                        ? "Le plus ancien en retard est coché. Cochez les autres mois ou frais payés."
                        : "Le prochain à payer est coché. Cochez les autres mois ou frais payés.";

        // Un choix immédiat évite le formulaire vide : l'utilisateur peut toujours changer de mois
        // ou en cocher plusieurs. Les mois déjà soldés ne peuvent jamais être encaissés une deuxième fois.
        if (Months.Count > 0)
            SelectedMonth = Months.FirstOrDefault(m => m.Overdue) ?? Months[0];
        Raise(nameof(HasMonths));
        Raise(nameof(NoAvailableMonths));
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Raise(nameof(HasSelection));
        StudentName = account.FullName;
        StudentInfo = $"Matricule {account.Matricule} · {account.PromotionCode} · {account.AcademicYear}";
        Balance = Money(account.Balance);
        Overdue = Money(account.Overdue);
        OverdueBrush = ThemeBrush.Get(account.Overdue > 0 ? "ToneRed" : "ToneGreen");
        Raise(nameof(OverdueBrush));
        // Aucun mois : l'encadré « Mois de paiement indisponibles » le dit déjà, avec les boutons pour y remédier.
        Hint = Months.Count == 0 ? null :
            pending > 0 ? $"{pending} encaissement(s) local(aux) attend(ent) leur confirmation par l'ordinateur principal." : null;
        Raise(nameof(HasStudent));
        Raise(nameof(ShowPayment));
        // Seul le mode de paiement reste à choisir : la référence, le visa et le téléphone se remplissent ici.
        Reference = IsCash ? NewReference(DateTime.Now) : string.Empty;
        PayerPhone = account.GuardianPhone?.Trim() ?? string.Empty;
        Raise(nameof(PayerPhoneText));
        Receipt = null;
        await ReserveAsync(eid, account.FullName);
        await PreviewAsync();
    }

    public async Task PreviewAsync()
    {
        previewTimer?.Stop();
        var version = ++previewVersion;
        if (enrollment is not { } e || !HasValidAmount)
        {
            Allocations.Clear();
            BalanceAfter = string.Empty;
            previewing = false;
            Raise(nameof(CanPay));
            return;
        }
        previewing = true;
        Raise(nameof(CanPay));
        try
        {
            PaymentPreviewDto preview;
            try
            {
                if (!Shell.Api.IsOnline && currentAccount is { } local)
                {
                    // Après basculement, zéro appel HTTP et zéro attente réseau.
                    preview = OfflineFinance.Preview(local, SelectedIds(), PaymentAmount);
                }
                else
                {
                    try
                    {
                        preview = await Shell.Api.SendAsync<PaymentPreviewDto>(HttpMethod.Post,
                            "api/paiements/apercu", new { enrollmentId = e.Id,
                                amount = PaymentAmount, feeItemIds = SelectedIds(), method = Method?.Value });
                    }
                    catch (OfflineException)
                    {
                        if (currentAccount is null)
                        {
                            if (version == previewVersion)
                                Hint = "Compte local absent : reconnectez-vous pour préparer les données.";
                            return;
                        }
                        preview = OfflineFinance.Preview(currentAccount, SelectedIds(), PaymentAmount);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                if (version == previewVersion) Hint = ex.Message;
                return;
            }
            if (version != previewVersion) return;
            CollectionSync.Apply(Allocations, preview.Allocations.Select(a => new AllocationRowVm(a.Label, Money(a.Amount), Money(a.RemainingAfter))));
            BalanceAfter = Money(preview.BalanceAfter);
            previewValid = true;
        }
        finally
        {
            if (version == previewVersion) { previewing = false; Raise(nameof(CanPay)); }
        }
    }

    /// <summary>Enregistre le versement : crée le reçu puis affiche sa confirmation.</summary>
    public async Task PayAsync()
    {
        // L'interface désactive le bouton ; la commande native applique la même sécurité.
        if (!CanPay || paying || previewing) return;
        paying = true;
        Raise(nameof(CanPay));
        try { await RecordPaymentAsync(); }
        finally { paying = false; Raise(nameof(CanPay)); }
    }

    private async Task RecordPaymentAsync()
    {
        if (enrollment is not { } e)
        {
            Hint = "Choisissez d'abord un apprenant.";
            return;
        }
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            Hint = "Cochez au moins un mois ou un frais à payer.";
            return;
        }
        if (IsLocked)
        {
            Hint = LockedBy;
            return;
        }
        Hint = null;
        // Récapitulatif avant l'enregistrement : un reçu ne se supprime pas, il ne s'annule qu'avec un motif.
        var chosen = Months.Where(m => m.IsSelected).Select(m => m.Label).ToList();
        var summary = $"{StudentName}\n{string.Join(", ", chosen)}\n\nMontant reçu : {Money(PaymentAmount)}\n" +
            $"Mode : {Method?.Label ?? "Espèces"} · Date : {ToDay(Day):dd/MM/yyyy}" +
            (IsCash || string.IsNullOrWhiteSpace(Reference) ? string.Empty : $"\nRéférence : {Reference.Trim()}");
        if (!await Shell.Confirm("Confirmer l'encaissement", summary, $"Encaisser {Money(PaymentAmount)}")) return;
        ReceiptDto created;
        var paymentKey = PaymentKeyFor(e.Id, ids);
        using var underLock = lockKey is { } key ? ScoliaClient.UnderLock(key) : null;
        try
        {
            created = await Shell.Api.SendAsync<ReceiptDto>(HttpMethod.Post, "api/paiements", new
        {
            enrollmentId = e.Id,
            amount = PaymentAmount,
            date = ToDay(Day),
            method = Method?.Value ?? "Especes",
            reference = IsCash && string.IsNullOrWhiteSpace(Reference) ? NewReference(DateTime.Now) : Reference.Trim(),
            payerPhone = string.IsNullOrWhiteSpace(PayerPhone) ? null : PayerPhone,
            note = string.IsNullOrWhiteSpace(Note) ? DefaultNote : Note.Trim(),
            feeItemIds = ids,
            idempotencyKey = paymentKey,
        });
        }
        catch (OfflineQueuedException)
        {
            ForgetPaymentKey();
            // L'argent est enregistré dans le journal SQLite chiffré AVANT
            // cette notification. Numéro temporaire unique par appareil ;
            // le numéro officiel sera attribué par le serveur.
            var pending = Shell.Api.Offline is { } store
                ? OfflineFinance.Pending(store).FirstOrDefault(p => p.Key == paymentKey)
                : null;
            var number = pending?.TemporaryNumber ?? $"LOCAL-{paymentKey[..8].ToUpperInvariant()}";
            var amount = PaymentAmount;
            Reference = string.Empty;
            await ChooseAsync(new PayerRow(e.Id, Guid.Empty, StudentName, string.Empty));
            localReceiptId = pending?.Id;
            localReceiptText = $"Encaissement local {number} · {Money(amount)}. " +
                "Enregistré sur cet appareil. Validation et numéro du reçu officiel automatiques après synchronisation.";
            Raise(nameof(HasLocalReceipt));
            Raise(nameof(HasReceipt));
            Raise(nameof(ShowPayment));
            Raise(nameof(ReceiptText));
            Hint = null;
            return;
        }
        ForgetPaymentKey();
        Reference = string.Empty;
        await ChooseAsync(new PayerRow(e.Id, created.StudentId, StudentName, string.Empty));
        Receipt = created;
        Raise(nameof(ReceiptText));
    }

    private string? paymentKey;
    private string? paymentFingerprint;

    /// <summary>
    /// Clé anti-doublon d'un versement. Elle est conservée tant que le même versement est retenté
    /// (réponse perdue après une coupure) : le serveur rend alors le reçu déjà créé au lieu d'en créer un second.
    /// Elle change dès que l'apprenant, les mois, le montant, le mode ou la date changent.
    /// </summary>
    private string PaymentKeyFor(Guid enrollmentId, IEnumerable<Guid> feeItemIds)
    {
        var fingerprint = string.Join('|', enrollmentId, string.Join(',', feeItemIds.OrderBy(x => x)),
            PaymentAmount.ToString(System.Globalization.CultureInfo.InvariantCulture), Method?.Value, ToDay(Day));
        if (paymentKey is null || paymentFingerprint != fingerprint)
        {
            paymentKey = Guid.NewGuid().ToString("N");
            paymentFingerprint = fingerprint;
        }
        return paymentKey;
    }

    private void ForgetPaymentKey()
    {
        paymentKey = null;
        paymentFingerprint = null;
    }

    private void Reset()
    {
        Receipt = null;
        localReceiptText = null;
        localReceiptId = null;
        Raise(nameof(HasReceipt));
        Raise(nameof(HasLocalReceipt));
        Raise(nameof(ShowPayment));
        Raise(nameof(ReceiptText));
        updatingMonthSelection = true;
        try
        {
            foreach (var month in Months) month.IsSelected = false;
        }
        finally { updatingMonthSelection = false; }
        // Après avoir enregistré un reçu, proposer le prochain mois dû plutôt qu'un formulaire vide.
        SelectedMonth = Months.FirstOrDefault(m => m.Overdue) ?? Months.FirstOrDefault();
        // Nouveau versement : nouvelle référence ; le téléphone reste celui de l'apprenant choisi.
        Reference = enrollment is null || !IsCash ? string.Empty : NewReference(DateTime.Now);
        Note = string.Empty;
        Day = DateTime.Now;
    }

    private sealed record EnrollmentChoice(Guid Id, decimal Balance);

    private string? lockKey;
    private string? lockedBy;

    /// <summary>Un autre poste encaisse déjà cet apprenant : l'encaissement est bloqué ici jusqu'à la fin du sien.</summary>
    public string? LockedBy { get => lockedBy; private set { if (Set(ref lockedBy, value)) { Raise(nameof(IsLocked)); Raise(nameof(CanPay)); } } }
    public bool IsLocked => lockedBy is not null;
    public bool CanPay => HasStudent && HasValidAmount && HasValidReference && !IsLocked && previewValid && !previewing && !paying;

    /// <summary>Réserve l'encaissement de cet apprenant pour ce poste, et l'annonce aux autres.</summary>
    private async Task ReserveAsync(Guid enrollmentId, string name)
    {
        var key = $"encaissement:{enrollmentId}";
        if (lockKey is { } previous && previous != key) await Shell.Sync.ReleaseAsync(previous);
        lockKey = key;
        var (granted, holder) = await Shell.Sync.AcquireAsync(key, $"Encaissement de {name}");
        if (lockKey != key)
        {
            if (granted) await Shell.Sync.ReleaseAsync(key);
            return;
        }
        LockedBy = granted ? null : holder;
        Shell.Sync.Activity = granted ? $"Encaissement de {name}" : null;
    }

    private bool watching;

    public override void Entered()
    {
        if (!watching)
        {
            watching = true;
            // L'autre poste a fini : l'encaissement redevient possible ici, sans recharger la page.
            Shell.Sync.LocksChanged += OnLocksChanged;
        }
        if (enrollment is { } e && StudentName.Length > 0) _ = ReserveAsync(e.Id, StudentName);
    }

    public override void Left()
    {
        if (lockKey is { } key) _ = Shell.Sync.ReleaseAsync(key);
        Shell.Sync.Activity = null;
        if (watching)
        {
            Shell.Sync.LocksChanged -= OnLocksChanged;
            watching = false;
        }
    }

    private void OnLocksChanged()
    {
        if (IsLocked && lockKey is { } key && Shell.Sync.HolderOf(key) is null && enrollment is { } e) _ = ReserveAsync(e.Id, StudentName);
    }
}

/// <summary>Un mois à payer : coché pour l'encaissement, éventuellement partiellement soldé.</summary>
public sealed class MonthRowVm(Guid id, string label, decimal amount, string amountText, bool overdue, Action changed, string deadline = "") : Observable
{
    private bool selected;

    /// <summary>Échéance lisible : « à payer jusqu'au 5 déc. » ou « en retard depuis le 6 oct. ».</summary>
    public string Deadline { get; } = deadline;
    public bool HasDeadline => Deadline.Length > 0;

    public Guid Id { get; } = id;
    public string Label { get; } = label;
    public decimal Amount { get; } = amount;
    public string AmountText { get; } = amountText;
    public bool Overdue { get; } = overdue;

    public bool IsSelected
    {
        get => selected;
        set { if (Set(ref selected, value)) changed(); }
    }
}

/// <summary>Journal des reçus : filtres, totaux et exports.</summary>
public sealed class ReceiptsPage : PageViewModel
{
    private const int PageSize = 200;
    private string search = string.Empty;
    private DateTime? from;
    private DateTime? to;
    private ChoiceItem? method;
    private ChoiceItem? status;
    private ChoiceItem? promotion;
    private ReceiptRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private DispatcherTimer? timer;

    public ReceiptsPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.PaymentsCreate)) Action("Encaisser", MaterialIconKind.HandCoin, () => Shell.Open(() => new CashierPage(Shell)), primary: true);
        Action("Ouvrir le reçu", MaterialIconKind.ReceiptText, OpenSelectedAsync, open: true);
        Action("Excel", MaterialIconKind.FileExcel, () => ExportAsync("xlsx"));
        Action("PDF", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        ActionRefresh();
        MenuAction("Imprimer le reçu", MaterialIconKind.Printer, () =>
        {
            if (Selected is { } r) return Shell.Download($"api/paiements/{r.Id}/recu.pdf", $"recu-{r.Number}.pdf");
            Shell.Notify("Sélectionnez un reçu dans la liste.");
            return Task.CompletedTask;
        });
    }

    public override string Title => "Journal des reçus";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ReceiptTextOutline;

    public ObservableCollection<ReceiptRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Methods { get; } = new[] { new ChoiceItem(string.Empty, "Tous les modes") }.Concat(Labels.Methods).ToList();
    public IReadOnlyList<ChoiceItem> Statuses { get; } =
        [new(string.Empty, "Tous les statuts"), new("Valide", "Validé"),
         new("En attente", "À synchroniser"), new("Annule", "Annulé")];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];

    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            timer?.Stop();
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (_, _) => { timer?.Stop(); await Shell.Run(RefreshAsync); };
            timer.Start();
        }
    }

    public DateTime? From { get => from; set { if (Set(ref from, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public DateTime? To { get => to; set { if (Set(ref to, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Method { get => method; set { if (Set(ref method, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Status { get => status; set { if (Set(ref status, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ReceiptRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            method = Methods[0];
            status = Statuses[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Method));
            Raise(nameof(Status));
            ready = true;
        }

        PagedResult<ReceiptRowDto>? page = null;
        try { page = await Shell.Api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements" + Filters(PageSize)); }
        catch (OfflineException)
        {
            // Un filtre jamais consulté n'empêche pas de fouiller les reçus
            // déjà enregistrés sur cet appareil.
            var cached = Shell.Api.Offline?.ReadPage(
                "api/paiements" + ScoliaClient.Query(("page", 1), ("pageSize", 200)));
            if (cached is not null)
            {
                try { page = System.Text.Json.JsonSerializer.Deserialize<PagedResult<ReceiptRowDto>>(
                    cached.Value.Json, Json.Options); }
                catch (System.Text.Json.JsonException) { }
            }
        }
        var provisional = Shell.Api.Offline is { } store ? OfflineFinance.PendingRows(store) : [];
        var rows = (page?.Items ?? []).Concat(provisional)
            .DistinctBy(r => r.Id)
            .Where(r => string.IsNullOrWhiteSpace(Search) || string.Join(" ", r.Number,
                r.Matricule, r.StudentName, r.PromotionCode, r.Reference ?? string.Empty)
                    .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(r => From is null || r.Date >= ToDay(From))
            .Where(r => To is null || r.Date <= ToDay(To))
            .Where(r => string.IsNullOrWhiteSpace(Method?.Value) ||
                string.Equals(r.Method, Method.Value, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(Status?.Value) ||
                string.Equals(r.Status, Status.Value, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(Promotion?.Value) ||
                string.Equals(r.PromotionCode,
                    Promotions.FirstOrDefault(p => p.Value == Promotion.Value)?.Label.Split('·')[0].Trim(),
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Number).Take(PageSize).ToList();
        CollectionSync.Apply(Items, rows.Select(r => new ReceiptRowVm(r.Id, r.Number, Format.Date(r.Date), r.Matricule, r.StudentName, r.PromotionCode, Money(r.Amount),
                Labels.Method(r.Method), r.Status == "En attente" ? "À synchroniser" : Labels.Status(r.Status),
                ThemeBrush.Get(r.Status == "Valide" ? "ToneGreen" : r.Status == "En attente" ? "ToneOrange" : "ToneRed"), r.Cashier)));
        // Séparer les encaissements localement saisis des recettes officiellement
        // acceptées : aucun double comptage dans les chiffres du serveur.
        var localTotal = rows.Where(r => r.Status == "En attente").Sum(r => r.Amount);
        var sum = page?.Sum is { } s ? $" · confirmés {Money(s)}" : string.Empty;
        subtitle = $"{Format.Count(rows.Count, "versement", "versements")}{sum}"
            + (localTotal > 0 ? $" · locaux {Money(localTotal)} à synchroniser" : string.Empty)
            + (!Shell.Api.IsOnline ? " · copie locale" : string.Empty);
        Raise(nameof(Subtitle));
    }

    private string Filters(int? size) => ScoliaClient.Query(
        ("search", Search.Trim()), ("from", ToDayOrNull(From)), ("to", ToDayOrNull(To)), ("method", Method?.Value),
        ("status", Status?.Value), ("promotionId", Promotion?.Value), ("page", 1), ("pageSize", size));

    private static DateOnly? ToDayOrNull(DateTime? value) => value is null ? null : ToDay(value);

    private async Task ExportAsync(string format)
    {
        await Shell.Download($"api/paiements/export{Filters(null)}&format={format}", $"recus.{format}");
    }

    /// <summary>Ouvre le reçu sélectionné : double-clic ou bouton.</summary>
    public async Task OpenSelectedAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un reçu dans la liste.");
            return;
        }
        var id = Selected.Id;
        await Shell.Open(() => new ReceiptDetailPage(Shell, id));
    }
}

/// <summary>Détail d'un reçu : répartition, statut, impression, annulation motivée et vérification publique.</summary>
public sealed class ReceiptDetailPage : PageViewModel
{
    private readonly Guid receiptId;
    private ReceiptDto? receipt;
    private string title = "Reçu";
    private string subtitle = string.Empty;

    public ReceiptDetailPage(ShellViewModel shell, Guid receiptId) : base(shell)
    {
        this.receiptId = receiptId;
        if (Shell.Can(Perm.PaymentsCancel)) Action("Annuler le reçu", MaterialIconKind.CloseCircle, CancelAsync);
        Action("Imprimer", MaterialIconKind.Printer, PrintAsync, primary: true);
        ActionRefresh();
    }

    public override string Title => title;
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ReceiptTextOutline;

    public ObservableCollection<InfoLine> Details { get; } = [];
    public ObservableCollection<AllocationRowVm> Allocations { get; } = [];
    public bool IsCancelled => receipt?.Status == "Annule";
    public string CancellationText => receipt?.Status == "Annule"
        ? $"Annulé le {Format.DateTime(receipt.CancelledAtUtc ?? DateTime.UtcNow)} par {receipt.CancelledBy ?? "—"} : {receipt.CancellationReason ?? "sans motif"}"
        : string.Empty;

    public override async Task LoadAsync()
    {
        receipt = Shell.Api.Offline is { } store
            ? OfflineFinance.PendingReceipt(store, receiptId) : null;
        receipt ??= await Shell.Api.GetAsync<ReceiptDto>($"api/paiements/{receiptId}");
        title = receipt.Status == "En attente"
            ? $"Accusé local {receipt.Number}" : $"Reçu N° {receipt.Number}";
        subtitle = $"{receipt.StudentName} · " +
            (receipt.Status == "En attente" ? "Versement local à synchroniser" : Labels.Status(receipt.Status));
        Raise(nameof(Title));
        Raise(nameof(Subtitle));
        Raise(nameof(IsCancelled));
        Raise(nameof(CancellationText));

        Details.Clear();
        Details.Add(new InfoLine("Date", Format.Date(receipt.Date)));
        Details.Add(new InfoLine("Apprenant", $"{receipt.StudentName} · {receipt.Matricule}"));
        Details.Add(new InfoLine("Promotion", $"{receipt.PromotionCode} · {receipt.AcademicYear}"));
        Details.Add(new InfoLine("Montant", Money(receipt.Amount)));
        Details.Add(new InfoLine("Mode", Labels.Method(receipt.Method) + (receipt.Reference is null ? string.Empty : $" · réf. {receipt.Reference}")));
        Details.Add(new InfoLine("Versé par", string.IsNullOrWhiteSpace(receipt.PayerName) ? "—" : $"{receipt.PayerName}{(receipt.PayerPhone is null ? string.Empty : $" · {receipt.PayerPhone}")}"));
        Details.Add(new InfoLine("Visé par", receipt.Cashier));
        Details.Add(new InfoLine("Code de vérification",
            receipt.Status == "En attente" ? "À obtenir après validation du serveur" : receipt.VerificationCode));
        Details.Add(new InfoLine("Observation", receipt.Note ?? "—"));

        Allocations.Clear();
        foreach (var a in receipt.Allocations)
            Allocations.Add(new AllocationRowVm(a.Label, Money(a.Amount), Money(a.RemainingAfter)));
    }

    public override Task PrintAsync()
    {
        if (receipt?.Status == "En attente")
        {
            Shell.Notify("Le versement local est enregistré. Le reçu officiel imprimable sera disponible après synchronisation.");
            return Task.CompletedTask;
        }
        return Shell.Download($"api/paiements/{receiptId}/recu.pdf", $"recu-{receipt?.Number ?? receiptId.ToString()}.pdf");
    }

    public async Task CancelAsync()
    {
        if (receipt?.Status == "En attente")
        {
            Shell.Notify("Un encaissement local se retire depuis Réseau et activité > Opérations de cet appareil, avec confirmation.");
            return;
        }
        if (receipt is null || receipt.Status == "Annule")
        {
            Shell.Notify("Ce reçu est déjà annulé.");
            return;
        }
        var form = new FormViewModel("Annuler le reçu", $"Reçu N° {receipt.Number} de {Money(receipt.Amount)}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/paiements/{receiptId}/annulation", new { reason = f.Text("motif") });
            Shell.Notify("Reçu annulé. Il reste dans le journal.");
            return true;
        })
        { SubmitText = "Annuler le reçu", Danger = true };
        form.Add("motif", "Motif de l'annulation", FieldKind.Multiline, required: true, hint: "Le reçu n'est jamais supprimé : le motif reste dans le journal.");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    public void OpenVerification()
    {
        if (receipt is null) return;
        if (receipt.Status == "En attente")
        {
            Shell.Notify("Le code de vérification n'existe qu'après la confirmation du serveur.");
            return;
        }
        Files.Open(new Uri(Shell.Api.Server, $"verifier/{receipt.VerificationCode}").ToString());
    }
}
