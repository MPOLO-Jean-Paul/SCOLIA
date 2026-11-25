using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
using Scolia.Domain.Common;

namespace Scolia.Bureau.ViewModels;

/// <summary>Ligne du suivi mensuel : un apprenant face au mois choisi.</summary>
public sealed record MonthlyRowVm(Guid StudentId, string Matricule, string Name, string Promotion, string Amount, string Paid, string Remaining,
    string Status, IBrush StatusBrush, string Receipt, string PaidOn, string? WhatsAppLink, string? SmsLink, Guid? FeeItemId = null);

/// <summary>
/// Suivi des paiements par mois : pour le mois choisi, qui a payé et qui reste à payer, pour une promotion ou pour
/// tout l'établissement. Les totaux portent sur toute la promotion, l'état et la recherche ne filtrent que la liste.
/// </summary>
public sealed class MonthlyPage : PageViewModel
{
    /// <summary>États du filtre, dans l'ordre où l'on suit un mois : d'abord ceux qui restent à payer.</summary>
    public static readonly IReadOnlyList<ChoiceItem> States =
    [
        new(string.Empty, "Tous les états"), new("nonpaye", "Non payés"), new("retard", "En retard"),
        new("paye", "Payés"), new("couvert", "Pris en charge"),
    ];

    private ChoiceItem? month;
    private ChoiceItem? promotion;
    private ChoiceItem? state;
    private string search = string.Empty;
    private MonthlyRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;
    private string? loadedFilters;
    private DispatcherTimer? timer;

    public MonthlyPage(ShellViewModel shell, string? initialState = null) : base(shell)
    {
        state = States.FirstOrDefault(s => s.Value == (initialState ?? string.Empty)) ?? States[0];
        if (Shell.Can(Perm.PaymentsCreate)) Action("Encaisser", MaterialIconKind.HandCoin, EncashAsync, primary: true, row: true);
        Action("Ouvrir la fiche", MaterialIconKind.Account, OpenSelectedAsync, open: true);
        Action("Relancer par WhatsApp", MaterialIconKind.Whatsapp, () => OpenLinkAsync(r => r.WhatsAppLink), row: true);
        Action("Relancer par SMS", MaterialIconKind.MessageText, () => OpenLinkAsync(r => r.SmsLink), row: true);
        Action("Excel", MaterialIconKind.FileExcel, () => ExportAsync("xlsx"));
        Action("PDF", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        Action("Word", MaterialIconKind.FileDocumentOutline, () => ExportAsync("docx"));
        ActionRefresh();
        MenuAction("Aperçu rapide", MaterialIconKind.Eye, () =>
        {
            if (Selected is not null) return QuickView.StudentAsync(Shell, Selected.StudentId);
            Shell.Notify($"Sélectionnez un {Shell.StudentTerms.Singular} dans la liste.");
            return Task.CompletedTask;
        });
    }

    private string receiptActivity = string.Empty;
    public string ReceiptActivity { get => receiptActivity; private set => Set(ref receiptActivity, value); }
    public override string Title => "Suivi mensuel";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CalendarCheck;

    public ObservableCollection<MonthlyRowVm> Items { get; } = [];
    public ObservableCollection<KpiVm> Totals { get; } = [];
    public IReadOnlyList<ChoiceItem> Months { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> StateChoices => States;
    public System.Windows.Input.ICommand ExportExcel => Actions.First(a => a.Label == "Excel").Command;
    public System.Windows.Input.ICommand ExportPdf => Actions.First(a => a.Label == "PDF").Command;
    public System.Windows.Input.ICommand ExportWord => Actions.First(a => a.Label == "Word").Command;
    public string ExportScope => $"Export : {Month?.Label ?? "mois à choisir"} · {Promotion?.Label ?? "promotion à choisir"} · {State?.Label ?? "Tous les états"} · " +
        (string.Equals(loadedFilters, Filters(), StringComparison.Ordinal) && loadedFilters is not null
            ? $"{Items.Count} ligne(s)" : "liste à actualiser");

    public ChoiceItem? Month { get => month; set { if (Set(ref month, value)) FilterChanged(); } }
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value)) FilterChanged(); } }
    public ChoiceItem? State { get => state; set { if (Set(ref state, value)) FilterChanged(); } }
    private void FilterChanged()
    {
        loadedFilters = null;
        Raise(nameof(ExportScope));
        if (ready) _ = Shell.Run(RefreshAsync);
    }
    public MonthlyRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            loadedFilters = null;
            Raise(nameof(ExportScope));
            timer?.Stop();
            var scheduled = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer = scheduled;
            scheduled.Tick += async (_, _) =>
            {
                scheduled.Stop();
                if (!ReferenceEquals(timer, scheduled)) return;
                timer = null;
                await Shell.Run(RefreshAsync);
            };
            scheduled.Start();
        }
    }

    // Instance isolée pour Flutter : filtres atomiques, sans minuteries ni appels concurrents.
    internal async Task LoadForFlutterAsync(string? period, string? promotionId, string? status, string query)
    {
        if (!ready) await LoadAsync();
        month = period is null ? month : Months.FirstOrDefault(x => x.Value == period)
            ?? throw new ArgumentException("Période inconnue.");
        promotion = promotionId is null ? promotion : Promotions.FirstOrDefault(x => x.Value == promotionId)
            ?? throw new ArgumentException("Promotion inconnue.");
        state = status is null ? state : States.FirstOrDefault(x => x.Value == status)
            ?? throw new ArgumentException("État inconnu.");
        search = query;
        loadedFilters = null;
        if (month is null || promotion is null) throw new InvalidOperationException("Sélection indisponible.");
        await LoadAsync();
        if (!string.Equals(loadedFilters, Filters(), StringComparison.Ordinal))
            throw new InvalidOperationException("La sélection n’a pas été confirmée.");
    }

    internal Task ExportForFlutterAsync(string format) => ExportAsync(format, strict: true);

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            Raise(nameof(Promotions));
            promotion = Promotions[0];
            Raise(nameof(Promotion));
        }

        if (ready && (Promotion is null || !Guid.TryParse(Month?.Value, out _))) return;
        var requestedFilters = Filters();
        var d = await Shell.Api.GetAsync<MonthlyFollowUpDto>("api/rapports/suivi-mensuel" + requestedFilters);
        // Ne jamais appliquer une réponse ancienne après un changement de filtre.
        var changed = !string.Equals(requestedFilters, Filters(), StringComparison.Ordinal);
        if (ready && (changed || !Guid.TryParse(Month?.Value, out var selectedPeriod) || selectedPeriod != d.PeriodId)) return;
        if (!ready)
        {
            // Le mois en cours est choisi d'office ; les autres mois de l'année restent à un clic.
            Months = d.Periods.Select(p => new ChoiceItem(p.Id.ToString(), p.Name)).ToList();
            Raise(nameof(Months));
            month = Months.FirstOrDefault(m => m.Value == d.PeriodId.ToString());
            Raise(nameof(Month));
            ready = true;
            if (changed) { await LoadAsync(); return; }
        }

        CollectionSync.Apply(Totals, new[] {
            // Les pris en charge ne sont pas des payants : ils ont leur propre total.
            new KpiVm("Mois soldé", Format.Amount(d.PaidCount), $"sur {Format.Count(d.PaidCount + d.UnpaidCount, "payant", "payants")}", ThemeBrush.Get("ToneGreen")),
            new KpiVm("Reste dû", Format.Amount(d.UnpaidCount), Format.Count(d.LateCount, "en retard", "en retard"), ThemeBrush.Get("ToneOrange")),
            new KpiVm("Imputé au mois", Money(d.Collected), $"Attendu : {Money(d.Expected)}", ThemeBrush.Get("ToneGreen")),
            new KpiVm("Reste à percevoir", Money(d.Remaining), "Sur les payants de ce mois", ThemeBrush.Get("ToneRed")),
            new KpiVm("Pris en charge", Format.Amount(d.CoveredCount), "Hors payants pour ce mois", ThemeBrush.Get("TonePurple")),
        });

        ReceiptActivity = d.ReceiptCount is { } count && d.PayingStudentsByReceiptDate is { } people && d.CashReceived is { } cashReceived
            ? $"Reçus validés émis pendant {d.Period} : {count} · {people} apprenant(s) distinct(s) · {Money(cashReceived)} reçus, tous frais et mois confondus."
            : "Le nombre de reçus par date d’encaissement est indisponible. Actualisez sur le serveur.";
        if (!Shell.Api.IsOnline)
            ReceiptActivity += " · Dernière situation confirmée disponible localement. Les versements non synchronisés ne sont pas encore inclus.";
        CollectionSync.Apply(Items, d.Rows.Select(r => new MonthlyRowVm(r.StudentId, r.Matricule, r.FullName, r.PromotionCode, Money(r.Amount), Money(r.Paid), Money(r.Remaining),
                r.Status + (r.Sponsorship == "Prise en charge partielle" ? " · PEC partielle" : string.Empty), ThemeBrush.Get(Tone(r.Status)), r.ReceiptNumber ?? "—", r.PaidOn is { } on ? Format.Date(on) : "—", r.WhatsAppLink, r.SmsLink, r.FeeItemId)));

        // Sous-titre court, lisible sur deux lignes d'un téléphone : la promotion n'est citée que si elle est choisie.
        var scope = Promotion is { Value.Length: > 0 } p ? $" · {p.Label}" : string.Empty;
        subtitle = $"{d.Period}{scope} · {Format.Count(d.Rows.Count, Shell.StudentTerms.Singular, Shell.StudentTerms.Plural)}";
        Raise(nameof(Subtitle));
        loadedFilters = Filters();
        Raise(nameof(ExportScope));
    }

    /// <summary>Couleur du point d'état : vert payé, sarcelle pris en charge, orange partiel ou non payé, rouge en retard.</summary>
    private static string Tone(string status) => status switch
    {
        "Payé" => "ToneGreen",
        "Pris en charge" => "ToneTeal",
        "En retard" => "ToneRed",
        _ => "ToneOrange",
    };

    private string Filters(string? format = null) => ScoliaClient.Query(
        ("periodeId", ready ? Month?.Value : null), ("promotionId", Promotion?.Value is { Length: > 0 } id ? id : null),
        ("etat", State?.Value is { Length: > 0 } e ? e : null), ("q", Search.Trim()), ("format", format));

    private async Task ExportAsync(string format, bool strict = false)
    {
        if (!ready || !Guid.TryParse(Month?.Value, out var periodId) || Promotion is null ||
            (Promotion.Value.Length > 0 && !Guid.TryParse(Promotion.Value, out _)))
        {
            if (strict) throw new InvalidOperationException("Sélection d’export indisponible.");
            Shell.Notify("Choisissez le mois et la promotion avant d’exporter.");
            return;
        }
        var requested = Filters();
        if (timer is not null) { timer.Stop(); timer = null; }
        if (Busy || !string.Equals(loadedFilters, requested, StringComparison.Ordinal))
        {
            if (strict) throw new InvalidOperationException("La sélection doit être actualisée.");
            await RefreshAsync();
        }
        if (Promotion is not { } selectedPromotion || !Guid.TryParse(Month?.Value, out var currentPeriod) || currentPeriod != periodId ||
            !string.Equals(requested, Filters(), StringComparison.Ordinal) ||
            !string.Equals(loadedFilters, requested, StringComparison.Ordinal))
        {
            if (strict) throw new InvalidOperationException("La sélection a changé.");
            Shell.Notify("Les filtres ont changé ou la liste n’est pas actualisée. Vérifiez la sélection puis réessayez.");
            return;
        }
        var promotionScope = selectedPromotion.Value.Length == 0 ? "all" : selectedPromotion.Value;
        var fingerprint = MonthlyExportSelection.Fingerprint(Items.Select(r => (r.StudentId, r.FeeItemId)));
        var query = ScoliaClient.Query(("periodeId", periodId), ("promotionScope", promotionScope),
            ("etat", State?.Value), ("q", Search.Trim()), ("expectedCount", Items.Count), ("expectedSelection", fingerprint), ("format", format));
        static string Safe(string text) => string.Concat(text.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        var name = $"suivi-mensuel-{Safe(Month?.Label ?? "mois")}-{Safe(selectedPromotion.Label)}.{format}";
        await Shell.Download("api/rapports/suivi-mensuel/export/selection" + query, name);
    }

    public override Task PrintAsync() => ExportAsync("pdf");

    private async Task EncashAsync()
    {
        if (Selected is null)
        {
            Shell.Notify($"Sélectionnez un {Shell.StudentTerms.Singular} dans la liste.");
            return;
        }
        var row = Selected;
        if (row.FeeItemId is not { } feeId)
        {
            Shell.Notify("Actualisez le suivi mensuel sur le serveur pour choisir la mensualité exacte.");
            return;
        }
        await Shell.Open(() => new CashierPage(Shell, row.StudentId, feeId));
    }

    private async Task OpenSelectedAsync()
    {
        if (Selected is null)
        {
            Shell.Notify($"Sélectionnez un {Shell.StudentTerms.Singular} dans la liste.");
            return;
        }
        await Shell.Open(() => new StudentDetailPage(Shell, Selected.StudentId));
    }

    private Task OpenLinkAsync(Func<MonthlyRowVm, string?> link)
    {
        if (Selected is null)
        {
            Shell.Notify($"Sélectionnez un {Shell.StudentTerms.Singular} dans la liste.");
            return Task.CompletedTask;
        }
        var url = link(Selected);
        if (string.IsNullOrEmpty(url))
        {
            Shell.Notify(Selected.Status is "Payé" or "Pris en charge" ? "Ce mois est réglé : rien à relancer." : "Ce garant n'a pas de numéro de téléphone enregistré.");
            return Task.CompletedTask;
        }
        Files.Open(url);
        return Task.CompletedTask;
    }
}
