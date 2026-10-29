using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Ligne du suivi mensuel : un apprenant face au mois choisi.</summary>
public sealed record MonthlyRowVm(Guid StudentId, string Matricule, string Name, string Promotion, string Amount, string Paid, string Remaining,
    string Status, IBrush StatusBrush, string Receipt, string PaidOn, string? WhatsAppLink, string? SmsLink);

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
    private DispatcherTimer? timer;

    public MonthlyPage(ShellViewModel shell, string? initialState = null) : base(shell)
    {
        state = States.FirstOrDefault(s => s.Value == (initialState ?? string.Empty)) ?? States[0];
        if (Shell.Can(Perm.PaymentsCreate)) Action("Encaisser", MaterialIconKind.HandCoin, EncashAsync, primary: true, row: true);
        Action("Ouvrir la fiche", MaterialIconKind.AccountOutline, OpenSelectedAsync, open: true);
        Action("Relancer par WhatsApp", MaterialIconKind.Whatsapp, () => OpenLinkAsync(r => r.WhatsAppLink), row: true);
        Action("Relancer par SMS", MaterialIconKind.MessageTextOutline, () => OpenLinkAsync(r => r.SmsLink), row: true);
        Action("Excel", MaterialIconKind.FileExcelOutline, () => ExportAsync("xlsx"));
        Action("PDF", MaterialIconKind.FilePdfBox, () => ExportAsync("pdf"));
        ActionRefresh();
        MenuAction("Aperçu rapide", MaterialIconKind.EyeOutline, () =>
        {
            if (Selected is not null) return QuickView.StudentAsync(Shell, Selected.StudentId);
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return Task.CompletedTask;
        });
    }

    public override string Title => "Suivi mensuel";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CalendarCheck;

    public ObservableCollection<MonthlyRowVm> Items { get; } = [];
    public ObservableCollection<KpiVm> Totals { get; } = [];
    public IReadOnlyList<ChoiceItem> Months { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> StateChoices => States;

    public ChoiceItem? Month { get => month; set { if (Set(ref month, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? State { get => state; set { if (Set(ref state, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public MonthlyRowVm? Selected { get => selected; set => Set(ref selected, value); }

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

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
        }

        var d = await Shell.Api.GetAsync<MonthlyFollowUpDto>("api/rapports/suivi-mensuel" + Filters());
        if (!ready)
        {
            // Le mois en cours est choisi d'office ; les autres mois de l'année restent à un clic.
            Months = d.Periods.Select(p => new ChoiceItem(p.Id.ToString(), p.Name)).ToList();
            month = Months.FirstOrDefault(m => m.Value == d.PeriodId.ToString());
            Raise(nameof(Months));
            Raise(nameof(Month));
            ready = true;
        }

        CollectionSync.Apply(Totals, new[] {
            new KpiVm("Payés", Format.Amount(d.PaidCount), $"sur {Format.Count(d.PaidCount + d.UnpaidCount + d.CoveredCount, "apprenant", "apprenants")}", ThemeBrush.Get("ToneGreen")),
            new KpiVm("Non payés", Format.Amount(d.UnpaidCount), Format.Count(d.LateCount, "en retard", "en retard"), ThemeBrush.Get("ToneOrange")),
            new KpiVm("Encaissé sur le mois", Money(d.Collected), $"Attendu : {Money(d.Expected)}", ThemeBrush.Get("ToneGreen")),
            new KpiVm("Reste à percevoir", Money(d.Remaining), d.CoveredCount > 0 ? Format.Count(d.CoveredCount, "pris en charge", "pris en charge") : "Aucun pris en charge", ThemeBrush.Get("ToneRed")),
        });

        CollectionSync.Apply(Items, d.Rows.Select(r => new MonthlyRowVm(r.StudentId, r.Matricule, r.FullName, r.PromotionCode, Money(r.Amount), Money(r.Paid), Money(r.Remaining),
                r.Status, ThemeBrush.Get(Tone(r.Status)), r.ReceiptNumber ?? "—", r.PaidOn is { } on ? Format.Date(on) : "—", r.WhatsAppLink, r.SmsLink)));

        var scope = Promotion is { Value.Length: > 0 } p ? p.Label : "toutes les promotions";
        subtitle = $"{d.Period} · {scope} · {Format.Count(d.Rows.Count, "apprenant affiché", "apprenants affichés")}";
        Raise(nameof(Subtitle));
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

    private Task ExportAsync(string format) =>
        Shell.Download("api/rapports/suivi-mensuel/export" + Filters(format), $"suivi-mensuel.{format}");

    private async Task EncashAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return;
        }
        await Shell.Open(() => new CashierPage(Shell, Selected.StudentId));
    }

    private async Task OpenSelectedAsync()
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
            return;
        }
        await Shell.Open(() => new StudentDetailPage(Shell, Selected.StudentId));
    }

    private Task OpenLinkAsync(Func<MonthlyRowVm, string?> link)
    {
        if (Selected is null)
        {
            Shell.Notify("Sélectionnez un apprenant dans la liste.");
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
