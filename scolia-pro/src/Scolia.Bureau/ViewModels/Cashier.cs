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
    private PayerRow? selected;
    private EnrollmentChoice? enrollment;
    private DateTime? day = DateTime.Now;
    private ChoiceItem? method;
    private string reference = string.Empty;
    private string payerName = string.Empty;
    private string payerPhone = string.Empty;
    private string note = string.Empty;
    private string studentName = string.Empty;
    private string studentInfo = string.Empty;
    private string balance = string.Empty;
    private string overdue = string.Empty;
    private string balanceAfter = string.Empty;
    private string? hint;
    private ReceiptDto? receipt;

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
    public override string Subtitle => "Choisissez les mois payés : chaque mois se règle à son montant, puis reçu numéroté";
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
            searchTimer?.Stop();
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += async (_, _) => { searchTimer?.Stop(); await Shell.Run(SearchAsync); };
            searchTimer.Start();
        }
    }

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
    public string Total => Money(SelectedTotal);
    public string TotalCaption => SelectedCount == 0 ? "Aucun mois choisi" : $"{SelectedCount} mois · total {Money(SelectedTotal)}";

    /// <summary>Un mois coché ou décoché met à jour le total et l'aperçu de répartition.</summary>
    private void MonthsChanged()
    {
        Raise(nameof(Total));
        Raise(nameof(TotalCaption));
        Raise(nameof(HasSelection));
        previewTimer?.Stop();
        previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        previewTimer.Tick += async (_, _) => { previewTimer?.Stop(); await Shell.Run(PreviewAsync); };
        previewTimer.Start();
    }

    private List<Guid> SelectedIds() => Months.Where(m => m.IsSelected).Select(m => m.Id).ToList();

    public DateTime? Day { get => day; set => Set(ref day, value); }
    public ChoiceItem? Method { get => method; set => Set(ref method, value); }
    public string Reference { get => reference; set => Set(ref reference, value); }
    public string PayerName { get => payerName; set => Set(ref payerName, value); }
    public string PayerPhone { get => payerPhone; set => Set(ref payerPhone, value); }
    public string Note { get => note; set => Set(ref note, value); }

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
        Results.Clear();
        if (search.Trim().Length < 2) return;
        var page = await Shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + ScoliaClient.Query(("q", search.Trim()), ("taille", 12)));
        foreach (var s in page.Items)
            Results.Add(new PayerRow(s.EnrollmentId, s.Id, $"{s.Matricule} · {Format.FullName(s.LastName, s.MiddleName, s.FirstName)}",
                $"{s.PromotionCode ?? "sans promotion"} · {Labels.Status(s.Status ?? string.Empty)}"));
    }

    public async Task ChooseAsync(PayerRow row)
    {
        if (row.EnrollmentId is not { } eid)
        {
            Hint = "Cet apprenant n'a pas d'inscription active : encaissement impossible.";
            return;
        }
        var account = await Shell.Api.GetAsync<AccountDto>($"api/inscriptions/{eid}/compte");
        enrollment = new EnrollmentChoice(eid, account.Balance);
        Months.Clear();
        // Seule la scolarité se règle par mois : inscription et examen ne figurent pas dans cette liste.
        foreach (var line in account.Lines.Where(l => l.Remaining > 0 && l.Kind == "Mensualite"))
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
        Hint = null;
        Raise(nameof(HasStudent));
        Receipt = null;
        await PreviewAsync();
    }

    public async Task PreviewAsync()
    {
        Allocations.Clear();
        BalanceAfter = string.Empty;
        if (enrollment is not { } e || !HasSelection) return;
        var preview = await Shell.Api.SendAsync<PaymentPreviewDto>(HttpMethod.Post, "api/paiements/apercu",
            new { enrollmentId = e.Id, amount = SelectedTotal, feeItemIds = SelectedIds(), method = Method?.Value });
        foreach (var a in preview.Allocations)
            Allocations.Add(new AllocationRowVm(a.Label, Money(a.Amount), Money(a.RemainingAfter)));
        BalanceAfter = Money(preview.BalanceAfter);
    }

    /// <summary>Enregistre le versement : crée le reçu puis affiche sa confirmation.</summary>
    public async Task PayAsync()
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
        Hint = null;
        var created = await Shell.Api.SendAsync<ReceiptDto>(HttpMethod.Post, "api/paiements", new
        {
            enrollmentId = e.Id,
            amount = SelectedTotal,
            date = ToDay(Day),
            method = Method?.Value ?? "Especes",
            reference = string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim(),
            payerName = string.IsNullOrWhiteSpace(PayerName) ? null : PayerName.Trim(),
            payerPhone = string.IsNullOrWhiteSpace(PayerPhone) ? null : PayerPhone.Trim(),
            note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            feeItemIds = ids,
            idempotencyKey = Guid.NewGuid().ToString("N"),
        });
        Reference = string.Empty;
        await ChooseAsync(new PayerRow(e.Id, created.StudentId, StudentName, string.Empty));
        Receipt = created;
        Raise(nameof(ReceiptText));
        Shell.Notify($"Reçu N° {created.Number} enregistré.");
    }

    private void Reset()
    {
        Receipt = null;
        foreach (var month in Months) month.IsSelected = false;
        Reference = string.Empty;
        PayerName = string.Empty;
        PayerPhone = string.Empty;
        Note = string.Empty;
        Day = DateTime.Now;
    }

    private sealed record EnrollmentChoice(Guid Id, decimal Balance);
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
        Items.Clear();
        foreach (var r in page.Items)
            Items.Add(new ReceiptRowVm(r.Id, r.Number, Format.Date(r.Date), r.Matricule, r.StudentName, r.PromotionCode, Money(r.Amount),
                Labels.Method(r.Method), Labels.Status(r.Status), ThemeBrush.Get(r.Status == "Valide" ? "ToneGreen" : "ToneRed"), r.Cashier));

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
        Details.Add(new InfoLine("Caissier", receipt.Cashier));
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
