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
    private string? searchStatus = "Saisissez au moins deux caractères pour rechercher un apprenant.";
    private int searchVersion;
    private int selectionVersion;
    private PayerRow? selected;
    private EnrollmentChoice? enrollment;
    private DateTime? day = DateTime.Now;
    private ChoiceItem? method;
    private MonthRowVm? selectedMonth;
    private bool updatingMonthSelection;
    private string reference = string.Empty;
    private string payerPhone = string.Empty;
    private string note = string.Empty;
    private string studentName = string.Empty;
    private string studentInfo = string.Empty;
    private string balance = string.Empty;
    private string overdue = string.Empty;
    private string balanceAfter = string.Empty;
    private string? hint;
    private ReceiptDto? receipt;
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
        OpenReceipt = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new ReceiptDetailPage(Shell, receipt!.Id))));
        Action("Journal des reçus", MaterialIconKind.ReceiptTextOutline, () => Shell.Open(() => new ReceiptsPage(Shell)));
    }

    public override string Title => "Encaisser";
    public override string Subtitle => Phone ? "Mois payés · reçu" : "Choisissez les mois payés, puis le mode de paiement";
    public override MaterialIconKind Glyph => MaterialIconKind.HandCoin;

    public AsyncCommand Pay { get; }
    public AsyncCommand Find { get; }
    public AsyncCommand NewPayment { get; }
    public AsyncCommand PrintReceipt { get; }
    public AsyncCommand OpenReceipt { get; }

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
            SearchStatus = search.Trim().Length < 2
                ? "Saisissez au moins deux caractères pour rechercher un apprenant."
                : "Recherche en cours…";
            searchTimer?.Stop();
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
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
    public ReceiptDto? Receipt { get => receipt; private set { if (Set(ref receipt, value)) Raise(nameof(HasReceipt)); } }
    public bool HasReceipt => receipt is not null;
    public string ReceiptText => receipt is null ? string.Empty : $"Reçu N° {receipt.Number} enregistré pour {Money(receipt.Amount)}.";

    /// <summary>Mois à payer : chaque mois se règle en entier, à son montant dû. Aucun montant n'est saisi.</summary>
    public ObservableCollection<MonthRowVm> Months { get; } = [];
    public bool HasMonths => Months.Count > 0;
    public bool HasSelection => SelectedCount > 0;
    public int SelectedCount => Months.Count(m => m.IsSelected);
    public decimal SelectedTotal => Months.Where(m => m.IsSelected).Sum(m => m.Amount);
    public string Total => HasSelection ? Money(SelectedTotal) : "—";
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

    public string TotalCaption => SelectedCount == 0 ? "Aucun mois choisi" : $"{SelectedCount} mois · total {Money(SelectedTotal)}";

    /// <summary>Un mois coché ou décoché met à jour le total et l'aperçu de répartition.</summary>
    private void MonthsChanged()
    {
        if (updatingMonthSelection) return;
        // En cochant plusieurs mois, le choix rapide revient à son invite sans perdre les coches.
        var single = SelectedCount == 1 ? Months.First(m => m.IsSelected) : null;
        Set(ref selectedMonth, single);
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Raise(nameof(HasSelection));
        previewing = true;
        previewValid = false;
        previewVersion++;
        Raise(nameof(CanPay));
        previewTimer?.Stop();
        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        previewTimer.Tick += async (_, _) => { previewTimer?.Stop(); await Shell.Run(PreviewAsync); };
        previewTimer.Start();
    }

    private List<Guid> SelectedIds() => Months.Where(m => m.IsSelected).Select(m => m.Id).ToList();

    public DateTime? Day { get => day; set => Set(ref day, value); }
    public ChoiceItem? Method { get => method; set => Set(ref method, value); }
    /// <summary>Référence de la transaction : donnée par SCOLIA dès que l'apprenant est choisi, elle ne se saisit pas.</summary>
    public string Reference { get => reference; private set { if (Set(ref reference, value)) Raise(nameof(ReferenceText)); } }
    public string ReferenceText => string.IsNullOrEmpty(reference) ? "Choisissez un apprenant" : reference;
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
        var version = ++searchVersion;
        var query = search.Trim();
        Results.Clear();
        if (query.Length < 2)
        {
            SearchStatus = "Saisissez au moins deux caractères pour rechercher un apprenant.";
            return;
        }
        SearchStatus = "Recherche en cours…";
        try
        {
            var page = await Shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + ScoliaClient.Query(("q", query), ("taille", 12)));
            // Une réponse tardive ne doit jamais remplacer les résultats d'une recherche plus récente.
            if (version != searchVersion) return;
            foreach (var s in page.Items)
                Results.Add(new PayerRow(s.EnrollmentId, s.Id, $"{s.Matricule} · {Format.FullName(s.LastName, s.MiddleName, s.FirstName)}",
                    $"{s.PromotionCode ?? "sans promotion"} · {Labels.Status(s.Status ?? string.Empty)}"));
            SearchStatus = page.Items.Count == 0 ? "Aucun apprenant trouvé. Vérifiez le nom ou le matricule." : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version == searchVersion) SearchStatus = "Recherche indisponible. Vérifiez la connexion puis réessayez.";
            throw;
        }
    }

    public async Task ChooseAsync(PayerRow row)
    {
        var version = ++selectionVersion;
        ++previewVersion; // Invalide aussi l'aperçu de l'ancien apprenant.
        previewValid = false;
        previewing = true;
        enrollment = null;
        Months.Clear();
        Receipt = null;
        StudentName = string.Empty;
        StudentInfo = string.Empty;
        Balance = string.Empty;
        Overdue = string.Empty;
        Reference = string.Empty;
        Hint = "Chargement du compte de l'apprenant…";
        Raise(nameof(HasStudent));
        Raise(nameof(HasMonths));
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
        enrollment = new EnrollmentChoice(eid, account.Balance);
        selectedMonth = null;
        Raise(nameof(SelectedMonth));
        // Les mois encaissés hors ligne sur cet appareil, pas encore envoyés, ne se proposent plus.
        var pending = Shell.Api.Offline?.PendingFeeItems(eid) ?? new HashSet<Guid>();
        // Seule la scolarité se règle par mois : inscription et examen ne figurent pas dans cette liste.
        foreach (var line in account.Lines.Where(l => l.Remaining > 0 && l.Kind == "Mensualite" && !pending.Contains(l.FeeItemId)))
            Months.Add(new MonthRowVm(line.FeeItemId, line.Label, line.Remaining, Money(line.Remaining), line.IsOverdue, MonthsChanged));
        Raise(nameof(HasMonths));
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Raise(nameof(HasSelection));
        StudentName = account.FullName;
        StudentInfo = $"Matricule {account.Matricule} · {account.PromotionCode} · {account.AcademicYear}";
        Balance = Money(account.Balance);
        Overdue = Money(account.Overdue);
        OverdueBrush = ThemeBrush.Get(account.Overdue > 0 ? "ToneRed" : "ToneGreen");
        Raise(nameof(OverdueBrush));
        Hint = pending.Count > 0 ? $"{pending.Count} mois déjà encaissé(s) sur cet appareil, en attente d'envoi à l'ordinateur principal." : null;
        Raise(nameof(HasStudent));
        // Seul le mode de paiement reste à choisir : la référence, le visa et le téléphone se remplissent ici.
        Reference = NewReference(DateTime.Now);
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
        if (enrollment is not { } e || !HasSelection)
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
                preview = await Shell.Api.SendAsync<PaymentPreviewDto>(HttpMethod.Post, "api/paiements/apercu",
                    new { enrollmentId = e.Id, amount = SelectedTotal, feeItemIds = SelectedIds(), method = Method?.Value });
            }
            catch (OfflineException)
            {
                // Hors ligne, la répartition exacte est faite par le serveur à l'envoi : le total reste juste.
                if (version == previewVersion) { BalanceAfter = "calculé à l'envoi"; previewValid = true; }
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
            Hint = "Cochez au moins un mois à payer.";
            return;
        }
        if (IsLocked)
        {
            Hint = LockedBy;
            return;
        }
        Hint = null;
        ReceiptDto created;
        using var underLock = lockKey is { } key ? ScoliaClient.UnderLock(key) : null;
        try
        {
            created = await Shell.Api.SendAsync<ReceiptDto>(HttpMethod.Post, "api/paiements", new
        {
            enrollmentId = e.Id,
            amount = SelectedTotal,
            date = ToDay(Day),
            method = Method?.Value ?? "Especes",
            reference = string.IsNullOrWhiteSpace(Reference) ? NewReference(DateTime.Now) : Reference,
            payerPhone = string.IsNullOrWhiteSpace(PayerPhone) ? null : PayerPhone,
            note = string.IsNullOrWhiteSpace(Note) ? DefaultNote : Note.Trim(),
            feeItemIds = ids,
            idempotencyKey = Guid.NewGuid().ToString("N"),
        });
        }
        catch (OfflineQueuedException)
        {
            // Encaissement gardé sur l'appareil : les mois quittent la liste, le reçu sera numéroté à l'envoi.
            Reference = string.Empty;
            await ChooseAsync(new PayerRow(e.Id, Guid.Empty, StudentName, string.Empty));
            Hint = "Encaissement gardé sur cet appareil : le reçu sera numéroté par l'ordinateur principal à son retour.";
            return;
        }
        Reference = string.Empty;
        await ChooseAsync(new PayerRow(e.Id, created.StudentId, StudentName, string.Empty));
        Receipt = created;
        Raise(nameof(ReceiptText));
    }

    private void Reset()
    {
        Receipt = null;
        foreach (var month in Months) month.IsSelected = false;
        // Nouveau versement : nouvelle référence ; le téléphone reste celui de l'apprenant choisi.
        Reference = enrollment is null ? string.Empty : NewReference(DateTime.Now);
        Note = string.Empty;
        Day = DateTime.Now;
    }

    private sealed record EnrollmentChoice(Guid Id, decimal Balance);

    private string? lockKey;
    private string? lockedBy;

    /// <summary>Un autre poste encaisse déjà cet apprenant : l'encaissement est bloqué ici jusqu'à la fin du sien.</summary>
    public string? LockedBy { get => lockedBy; private set { if (Set(ref lockedBy, value)) { Raise(nameof(IsLocked)); Raise(nameof(CanPay)); } } }
    public bool IsLocked => lockedBy is not null;
    public bool CanPay => HasStudent && HasSelection && !IsLocked && previewValid && !previewing && !paying;

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

/// <summary>Un mois à payer : coché pour l'encaissement, réglé en entier à son montant dû.</summary>
public sealed class MonthRowVm(Guid id, string label, decimal amount, string amountText, bool overdue, Action changed) : Observable
{
    private bool selected;

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
        Action("Ouvrir le reçu", MaterialIconKind.ReceiptTextOutline, OpenSelectedAsync, open: true);
        Action("Excel", MaterialIconKind.FileExcelOutline, () => ExportAsync("xlsx"));
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
    public IReadOnlyList<ChoiceItem> Statuses { get; } = [new(string.Empty, "Tous les statuts"), new("Valide", "Validé"), new("Annule", "Annulé")];
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

        var page = await Shell.Api.GetAsync<PagedResult<ReceiptRowDto>>("api/paiements" + Filters(PageSize));
        CollectionSync.Apply(Items, page.Items.Select(r => new ReceiptRowVm(r.Id, r.Number, Format.Date(r.Date), r.Matricule, r.StudentName, r.PromotionCode, Money(r.Amount),
                Labels.Method(r.Method), Labels.Status(r.Status), ThemeBrush.Get(r.Status == "Valide" ? "ToneGreen" : "ToneRed"), r.Cashier)));

        var sum = page.Sum is { } s ? $" · total {Money(s)}" : string.Empty;
        subtitle = $"{Format.Count(page.Total, "reçu", "reçus")}{sum}" + (page.Total > PageSize ? $" · {PageSize} plus récents affichés" : string.Empty);
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
        if (Shell.Can(Perm.PaymentsCancel)) Action("Annuler le reçu", MaterialIconKind.Cancel, CancelAsync);
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
        receipt = await Shell.Api.GetAsync<ReceiptDto>($"api/paiements/{receiptId}");
        title = $"Reçu N° {receipt.Number}";
        subtitle = $"{receipt.StudentName} · {Labels.Status(receipt.Status)}";
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
        Details.Add(new InfoLine("Code de vérification", receipt.VerificationCode));
        Details.Add(new InfoLine("Observation", receipt.Note ?? "—"));

        Allocations.Clear();
        foreach (var a in receipt.Allocations)
            Allocations.Add(new AllocationRowVm(a.Label, Money(a.Amount), Money(a.RemainingAfter)));
    }

    public override Task PrintAsync() => Shell.Download($"api/paiements/{receiptId}/recu.pdf", $"recu-{receipt?.Number ?? receiptId.ToString()}.pdf");

    public async Task CancelAsync()
    {
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
        Files.Open(new Uri(Shell.Api.Server, $"verifier/{receipt.VerificationCode}").ToString());
    }
}
