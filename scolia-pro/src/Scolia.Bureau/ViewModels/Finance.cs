using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Ligne du recouvrement : un apprenant en retard, avec ses liens de relance.</summary>
public sealed record DebtorRowVm(Guid StudentId, string Matricule, string Name, string Promotion, string Balance, string Overdue, string Count, string Periods, string Bucket, string Guardian, string Phone, string? WhatsAppLink, string? SmsLink);

/// <summary>Recouvrement : retards classés par ancienneté, filtrables, avec relances WhatsApp et SMS.</summary>
public sealed class DebtorsPage : PageViewModel
{
    private string search = string.Empty;
    private ChoiceItem? promotion;
    private ChoiceItem? bucket;
    private string minMonths = string.Empty;
    private DebtorRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private DispatcherTimer? timer;

    public DebtorsPage(ShellViewModel shell) : base(shell)
    {
        Action("Relancer par WhatsApp", MaterialIconKind.Whatsapp, () => OpenLinkAsync(r => r.WhatsAppLink), row: true);
        Action("Relancer par SMS", MaterialIconKind.MessageTextOutline, () => OpenLinkAsync(r => r.SmsLink), row: true);
        Action("Excel", MaterialIconKind.FileExcelOutline, () => ExportAsync("xlsx"));
        Action("PDF", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        ActionRefresh();
        MenuAction("Aperçu rapide", MaterialIconKind.EyeOutline, () => WithSelected(id => QuickView.StudentAsync(Shell, id)));
        MenuAction("Ouvrir la fiche", MaterialIconKind.AccountOutline, () => WithSelected(id => Shell.Open(() => new StudentDetailPage(Shell, id))), open: true);
        if (Shell.Can(Perm.PaymentsCreate)) MenuAction("Encaisser", MaterialIconKind.HandCoin, () => WithSelected(id => Shell.Open(() => new CashierPage(Shell, id))));
    }

    private Task WithSelected(Func<Guid, Task> work)
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return Task.CompletedTask;
        }
        return work(Selected.StudentId);
    }

    public override string Title => "Recouvrement";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ChartBar;

    public ObservableCollection<DebtorRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Buckets { get; } = Labels.Buckets;

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

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Bucket { get => bucket; set { if (Set(ref bucket, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public string MinMonths { get => minMonths; set { if (Set(ref minMonths, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public DebtorRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            bucket = Buckets[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            Raise(nameof(Bucket));
            ready = true;
        }

        var months = int.TryParse(MinMonths, out var m) ? m : (int?)null;
        var rows = await Shell.Api.GetAsync<List<DebtorDto>>("api/rapports/debiteurs" + ScoliaClient.Query(
            ("promotionId", Promotion?.Value), ("minOverdueMonths", months), ("bucket", Bucket?.Value), ("search", Search.Trim())));

        Items.Clear();
        foreach (var d in rows)
            Items.Add(new DebtorRowVm(d.StudentId, d.Matricule, d.FullName, d.PromotionCode, Money(d.Balance), Money(d.Overdue),
                $"{d.OverdueCount}", d.Periods, d.AgingBucket, d.GuardianName ?? "—", d.GuardianPhone ?? "—", d.WhatsAppLink, d.SmsLink));

        subtitle = $"{Format.Count(rows.Count, "dossier en retard", "dossiers en retard")} · reste exigible {Money(rows.Sum(r => r.Overdue))}";
        Raise(nameof(Subtitle));
    }

    private async Task ExportAsync(string format)
    {
        var months = int.TryParse(MinMonths, out var m) ? m : (int?)null;
        var query = ScoliaClient.Query(("promotionId", Promotion?.Value), ("minOverdueMonths", months), ("bucket", Bucket?.Value),
            ("search", Search.Trim()), ("format", format));
        await Shell.Download($"api/rapports/debiteurs/export{query}", $"debiteurs.{format}");
    }

    private Task OpenLinkAsync(Func<DebtorRowVm, string?> link)
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un dossier dans la liste.");
            return Task.CompletedTask;
        }
        var url = link(Selected);
        if (string.IsNullOrEmpty(url))
        {
            Shell.Notify("Ce garant n'a pas de numéro de téléphone enregistré.");
            return Task.CompletedTask;
        }
        Files.Open(url);
        return Task.CompletedTask;
    }
}

/// <summary>Ligne des prises en charge : bourses, sponsors et exonérations.</summary>
public sealed record SponsorshipRowVm(Guid Id, Guid EnrollmentId, string Matricule, string Name, string Promotion, string Sponsor, string Reason, string Since, string Covered, string Status, IBrush StatusBrush);

/// <summary>Prises en charge de l'année : liste et fin d'une prise en charge.</summary>
public sealed class SponsorshipsPage : PageViewModel
{
    private SponsorshipRowVm? selected;
    private string subtitle = string.Empty;

    public SponsorshipsPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.PaymentsRead)) Action("Terminer la prise en charge", MaterialIconKind.ShieldOffOutline, EndSelectedAsync, row: true);
        ActionRefresh();
    }

    public override string Title => "Prises en charge";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ShieldCheckOutline;

    public ObservableCollection<SponsorshipRowVm> Items { get; } = [];
    public SponsorshipRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var rows = await Shell.Api.GetAsync<List<SponsorshipListItem>>("api/prises-en-charge");
        Items.Clear();
        foreach (var s in rows)
            Items.Add(new SponsorshipRowVm(s.Id, s.EnrollmentId, s.Matricule, s.FullName, s.Promotion, s.SponsorName, s.Reason,
                Format.Date(s.EffectiveFrom), Money(s.Covered), s.IsActive ? "Active" : "Terminée", ThemeBrush.Get(s.IsActive ? "ToneGreen" : "ToneNeutral")));
        subtitle = $"{Format.Count(rows.Count, "prise en charge", "prises en charge")} · {rows.Count(r => r.IsActive)} active(s)";
        Raise(nameof(Subtitle));
    }

    private async Task EndSelectedAsync()
    {
        if (Selected is null || Selected.Status != "Active")
        {
            Shell.Notify("Sélectionnez une prise en charge active.");
            return;
        }
        var row = Selected;
        var form = new FormViewModel("Terminer la prise en charge", $"{row.Name} · {row.Sponsor}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/inscriptions/{row.EnrollmentId}/prises-en-charge/{row.Id}/fin", new { reason = f.Text("motif") });
            Shell.Notify("Prise en charge terminée. Son historique est conservé.");
            return true;
        })
        { SubmitText = "Terminer", Danger = true };
        form.Add("motif", "Motif de fin", required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}

/// <summary>Ligne de mouvement de caisse.</summary>
public sealed record MovementRowVm(string Date, string Type, string Number, string Label, string Method, string In, string Out);

/// <summary>Ligne d'arrêté de caisse passé.</summary>
public sealed record ClosingRowVm(string Date, string Expected, string Counted, string Difference, IBrush DifferenceBrush, string ClosedBy, string Note);

/// <summary>Caisse : situation de la journée, mouvements et historique des arrêtés.</summary>
public sealed class CashPage : PageViewModel
{
    private DateTime? day = DateTime.Now;
    private bool ready;
    private string subtitle = string.Empty;
    private CashSituationDto? situation;

    public CashPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.CashClose)) Action("Arrêter la caisse", MaterialIconKind.LockOutline, () => ClosingAsync(Shell), primary: true);
        ActionRefresh();
    }

    public override string Title => "Caisse";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CashMultiple;

    public ObservableCollection<KpiVm> Figures { get; } = [];
    public ObservableCollection<MovementRowVm> Movements { get; } = [];
    public ObservableCollection<ClosingRowVm> Closings { get; } = [];

    public DateTime? Day
    {
        get => day;
        set { if (Set(ref day, value) && ready) _ = Shell.Run(RefreshAsync); }
    }

    public override async Task LoadAsync()
    {
        ready = true;
        var date = ToDay(Day);
        situation = await Shell.Api.GetAsync<CashSituationDto>("api/caisse/situation" + ScoliaClient.Query(("date", date)));

        Figures.Clear();
        Figures.Add(new KpiVm("Solde d'ouverture", Money(situation.OpeningBalance), $"Période depuis le {Format.Date(situation.PeriodStart)}", ThemeBrush.Get("ToneNeutral"), MaterialIconKind.Safe));
        Figures.Add(new KpiVm("Recettes en espèces", Money(situation.CashReceipts), $"Autres recettes : {Money(situation.OtherReceipts)}", ThemeBrush.Get("ToneGreen"), MaterialIconKind.CashPlus));
        Figures.Add(new KpiVm("Dépenses en espèces", Money(situation.CashExpenses), "Sorties de caisse de la période", ThemeBrush.Get("ToneRed"), MaterialIconKind.CashMinus));
        Figures.Add(new KpiVm("Espèces attendues", Money(situation.ExpectedCash), $"Au {Format.Date(date)}", ThemeBrush.Get("ToneBlue"), MaterialIconKind.CashCheck));

        Movements.Clear();
        foreach (var m in situation.Movements)
            Movements.Add(new MovementRowVm(Format.Date(m.Date), m.Type, m.Number, m.Label, Labels.Method(m.Method),
                m.In == 0 ? "—" : Money(m.In), m.Out == 0 ? "—" : Money(m.Out)));

        var closings = await Shell.Api.GetAsync<List<CashClosingDto>>("api/caisse/arretes");
        Closings.Clear();
        foreach (var c in closings)
            Closings.Add(new ClosingRowVm(Format.Date(c.Date), Money(c.ExpectedCash), Money(c.CountedCash), Money(c.Difference),
                ThemeBrush.Get(c.Difference == 0 ? "ToneGreen" : "ToneRed"), c.ClosedBy, c.Note ?? "—"));

        subtitle = situation.LastClosingDate is { } last ? $"Dernier arrêté le {Format.Date(last)}" : "Aucun arrêté de caisse pour l'instant";
        Raise(nameof(Subtitle));
    }

    /// <summary>Compte les espèces et verrouille la période.</summary>
    public static async Task ClosingAsync(ShellViewModel shell)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var situation = await shell.Api.GetAsync<CashSituationDto>("api/caisse/situation" + ScoliaClient.Query(("date", today)));
        var money = shell.Session?.CurrencySymbol ?? string.Empty;
        var form = new FormViewModel("Arrêter la caisse", $"Espèces attendues en caisse : {Format.Money(situation.ExpectedCash, money)}", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/caisse/arretes", new
            {
                date = f.Day("date"),
                countedCash = f.Amount("compte"),
                note = f.OptionalText("note"),
            });
            shell.Notify("Caisse arrêtée. La période est verrouillée.");
            return true;
        })
        { SubmitText = "Arrêter la caisse", Danger = true };
        form.AddDay("date", "Date de l'arrêté", today, required: true);
        form.Add("compte", "Espèces comptées", FieldKind.Amount, required: true);
        form.Add("note", "Observation (obligatoire en cas d'écart)", FieldKind.Multiline);
        if (await shell.Ask(form) && shell.Current is CashPage page) await page.RefreshAsync();
    }
}

/// <summary>Ligne de dépense : sortie de caisse ou de banque.</summary>
public sealed record ExpenseRowVm(Guid Id, string Number, string Date, string Category, string Beneficiary, string Description, string Amount, string Method, string Status, IBrush StatusBrush, string? Reference, string RecordedBy);

/// <summary>Dépenses : filtres par période et catégorie, création et annulation motivée.</summary>
public sealed class ExpensesPage : PageViewModel
{
    private string search = string.Empty;
    private DateTime? from;
    private DateTime? to;
    private ChoiceItem? category;
    private ExpenseRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private DispatcherTimer? timer;

    public ExpensesPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.ExpensesWrite)) Action("Nouvelle dépense", MaterialIconKind.CashMinus, () => NewExpenseAsync(Shell), primary: true);
        if (Shell.Can(Perm.ExpensesWrite)) Action("Annuler la dépense", MaterialIconKind.Cancel, CancelSelectedAsync, row: true);
        ActionRefresh();
    }

    public override string Title => "Dépenses";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.WalletOutline;

    public ObservableCollection<ExpenseRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Categories { get; private set; } = [];

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
    public ChoiceItem? Category { get => category; set { if (Set(ref category, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ExpenseRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            var names = await Shell.Api.GetAsync<List<string>>("api/depenses/categories");
            Categories = new[] { new ChoiceItem(string.Empty, "Toutes les catégories") }.Concat(Choices.Categories(names)).ToList();
            category = Categories[0];
            Raise(nameof(Categories));
            Raise(nameof(Category));
            ready = true;
        }

        var page = await Shell.Api.GetAsync<PagedResult<ExpenseDto>>("api/depenses" + ScoliaClient.Query(
            ("du", ToDayOrNull(From)), ("au", ToDayOrNull(To)), ("categorie", Category?.Value), ("q", Search.Trim()), ("page", 1), ("taille", 200)));

        Items.Clear();
        foreach (var e in page.Items)
            Items.Add(new ExpenseRowVm(e.Id, e.Number, Format.Date(e.Date), e.Category, e.Beneficiary, e.Description, Money(e.Amount),
                Labels.Method(e.Method), Labels.Status(e.Status), ThemeBrush.Get(e.Status == "Valide" ? "ToneGreen" : "ToneRed"), e.Reference, e.RecordedBy));

        var total = page.Items.Where(e => e.Status == "Valide").Sum(e => e.Amount);
        subtitle = $"{Format.Count(page.Total, "dépense", "dépenses")} · total valide {Money(total)}";
        Raise(nameof(Subtitle));
    }

    private static DateOnly? ToDayOrNull(DateTime? value) => value is null ? null : ToDay(value);

    private async Task CancelSelectedAsync()
    {
        if (Selected is null || Selected.Status == "Annulé")
        {
            Shell.Notify("Sélectionnez une dépense valide à annuler.");
            return;
        }
        var row = Selected;
        var form = new FormViewModel("Annuler la dépense", $"{row.Number} · {row.Beneficiary} · {row.Amount}", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/depenses/{row.Id}/annulation", new { reason = f.Text("motif") });
            Shell.Notify("Dépense annulée. Elle reste dans le journal.");
            return true;
        })
        { SubmitText = "Annuler la dépense", Danger = true };
        form.Add("motif", "Motif de l'annulation", FieldKind.Multiline, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    /// <summary>Formulaire d'une nouvelle dépense, numérotée à l'enregistrement.</summary>
    public static async Task NewExpenseAsync(ShellViewModel shell)
    {
        var categories = await shell.Api.GetAsync<List<string>>("api/depenses/categories");
        var form = new FormViewModel("Nouvelle dépense", "Chaque dépense reçoit un numéro et reste dans le journal.", async f =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, "api/depenses", new
            {
                date = f.Day("date"),
                category = f.ChoiceValue("categorie"),
                beneficiary = f.Text("beneficiaire"),
                description = f.Text("description"),
                amount = f.Amount("montant"),
                method = f.ChoiceValue("mode"),
                reference = f.OptionalText("reference"),
            });
            shell.Notify("Dépense enregistrée.");
            return true;
        });
        form.AddDay("date", "Date", DateOnly.FromDateTime(DateTime.Today), required: true);
        form.AddChoice("categorie", "Catégorie", Choices.Categories(categories), null, required: true);
        form.Add("beneficiaire", "Bénéficiaire", required: true);
        form.Add("description", "Description", FieldKind.Multiline, required: true);
        form.Add("montant", "Montant", FieldKind.Amount, required: true);
        form.AddChoice("mode", "Mode", Labels.Methods, "Especes", required: true);
        form.Add("reference", "Référence / pièce");
        if (await shell.Ask(form) && shell.Current is ExpensesPage page) await page.RefreshAsync();
    }
}

/// <summary>Ligne de réinscription : un apprenant à reconduire, avec sa promotion cible.</summary>
public sealed class ReEnrollRowVm : Observable
{
    private bool selected;
    private ChoiceItem? target;

    public ReEnrollRowVm(Guid studentId, string matricule, string name, string fromPromotion, string previousBalance, bool selected,
        ChoiceItem? target, IReadOnlyList<ChoiceItem> choices)
    {
        StudentId = studentId;
        Matricule = matricule;
        Name = name;
        FromPromotion = fromPromotion;
        PreviousBalance = previousBalance;
        this.selected = selected;
        this.target = target;
        Choices = choices;
    }

    /// <summary>Promotions proposées pour la ligne : celles de l'année cible.</summary>
    public IReadOnlyList<ChoiceItem> Choices { get; }
    public Guid StudentId { get; }
    public string Matricule { get; }
    public string Name { get; }
    public string FromPromotion { get; }
    public string PreviousBalance { get; }
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public ChoiceItem? Target { get => target; set => Set(ref target, value); }
}

/// <summary>Réinscriptions : reconduit les apprenants vers la promotion suivante de l'année choisie.</summary>
public sealed class ReEnrollmentPage : PageViewModel
{
    private ChoiceItem? year;
    private string subtitle = string.Empty;
    private bool ready;
    private IReadOnlyList<ChoiceItem> promotions = [];

    public ReEnrollmentPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.StudentsWrite)) Action("Lancer les réinscriptions", MaterialIconKind.SchoolOutline, ApplyAsync, primary: true);
        if (Shell.Can(Perm.SettingsWrite)) Action("Nouvelle année", MaterialIconKind.CalendarPlusOutline, async () =>
        {
            await SettingsPage.NewYearAsync(Shell);
            ready = false;
            await RefreshAsync();
        });
        ActionRefresh();
    }

    private string emptyHint = "Les dossiers à reconduire apparaissent ici une fois l'année précédente clôturée.";

    /// <summary>Explication de la liste vide : elle dit quoi faire, sans message d'erreur.</summary>
    public string EmptyHint { get => emptyHint; private set => Set(ref emptyHint, value); }

    public override string Title => "Réinscriptions";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.SchoolOutline;

    public ObservableCollection<ReEnrollRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Years { get; private set; } = [];

    public ChoiceItem? Year
    {
        get => year;
        set { if (Set(ref year, value) && ready) _ = Shell.Run(RefreshAsync); }
    }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            var years = await Shell.Api.GetAsync<List<AcademicYearDto>>("api/etablissement/annees");
            Years = years.Select(y => new ChoiceItem(y.Id.ToString(), $"{y.Label}{(y.IsCurrent ? " (courante)" : string.Empty)}")).ToList();
            Raise(nameof(Years));
            // L'année cible est la suivante : par défaut, la première qui n'est pas la courante.
            year = Years.FirstOrDefault(y => !y.Label.EndsWith("(courante)")) ?? (Years.Count > 0 ? Years[0] : null);
            Raise(nameof(Year));
            ready = true;
        }
        if (Year is null) return;

        // Première année de l'établissement : rien à reconduire tant que l'année suivante n'existe pas.
        if (Years.Count < 2)
        {
            Items.Clear();
            EmptyHint = "Une seule année scolaire existe. Créez l'année suivante avec « Nouvelle année », puis revenez ici pour reconduire les apprenants.";
            subtitle = "Aucune année suivante pour le moment";
            Raise(nameof(Subtitle));
            return;
        }
        EmptyHint = "Les dossiers à reconduire apparaissent ici une fois l'année précédente clôturée.";

        promotions = await PromotionChoicesFor(Guid.Parse(Year.Value));
        var plan = await Shell.Api.GetAsync<List<ReEnrollmentItemDto>>("api/reinscriptions/plan" + ScoliaClient.Query(("anneeId", Year.Value)));
        Items.Clear();
        foreach (var p in plan)
        {
            // Le libellé d'une promotion commence par son code : on retrouve la promotion suivante par ce code.
            var target = promotions.FirstOrDefault(c => p.ToPromotion is not null && c.Label.StartsWith($"{p.ToPromotion} ·", StringComparison.Ordinal))
                ?? (promotions.Count > 0 ? promotions[0] : null);
            Items.Add(new ReEnrollRowVm(p.StudentId, p.Matricule, p.FullName, p.FromPromotion, Money(p.PreviousBalance), p.Selected, target, promotions));
        }
        subtitle = $"{Format.Count(plan.Count, "apprenant à reconduire", "apprenants à reconduire")} · {Items.Count(i => i.Selected)} sélectionné(s)";
        Raise(nameof(Subtitle));
    }

    private async Task<IReadOnlyList<ChoiceItem>> PromotionChoicesFor(Guid yearId)
    {
        var list = await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions" + ScoliaClient.Query(("anneeId", yearId)));
        return list.Select(Choices.Promotion).ToList();
    }

    private async Task ApplyAsync()
    {
        if (Year is null) return;
        var choices = Items.Where(i => i.Selected && i.Target is not null)
            .Select(i => new { studentId = i.StudentId, promotionId = Guid.Parse(i.Target!.Value) })
            .ToList();
        if (choices.Count == 0)
        {
            Shell.Notify("Sélectionnez au moins un apprenant et sa promotion cible.");
            return;
        }
        var confirmed = await Shell.Confirm("Lancer les réinscriptions",
            $"{Format.Count(choices.Count, "apprenant sera inscrit", "apprenants seront inscrits")} dans l'année {Year.Label}. Les soldes restent reportés sur le compte.",
            "Réinscrire");
        if (!confirmed) return;
        var created = await Shell.Api.SendAsync<int>(HttpMethod.Post, "api/reinscriptions", new { targetYearId = Guid.Parse(Year.Value), choices });
        Shell.Notify($"{created} réinscription(s) enregistrée(s).");
        await RefreshAsync();
    }
}
