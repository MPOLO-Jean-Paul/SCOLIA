using System.Collections.ObjectModel;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Apprenant dans la liste des cartes : la case Carte choisit les impressions.</summary>
public sealed class CardRowVm(CardCandidateDto source, string diagnostic) : Observable
{
    private bool selected;

    public Guid EnrollmentId { get; } = source.EnrollmentId;
    public string Matricule { get; } = source.Matricule;
    public string Name { get; } = source.FullName;
    public string Promotion { get; } = source.PromotionCode;
    public string Status { get; } = source.Status;
    public string Diagnostic { get; } = diagnostic;
    public bool Selectable { get; } = source.Eligible && source.IdentityComplete && !source.DuplicateMatricule;

    public bool Selected { get => selected; set => Set(ref selected, value); }
}

/// <summary>Cartes d'accès aux examens (V79 : Scolarité > Cartes) : choix des apprenants, session, exemplaires et export PDF.</summary>
public sealed class CardsPage : PageViewModel
{
    private static readonly IReadOnlyList<ChoiceItem> MonthChoices =
    [
        new("1", "Janvier"), new("2", "Février"), new("3", "Mars"), new("4", "Avril"), new("5", "Mai"), new("6", "Juin"),
        new("7", "Juillet"), new("8", "Août"), new("9", "Septembre"), new("10", "Octobre"), new("11", "Novembre"), new("12", "Décembre"),
    ];

    private static readonly IReadOnlyList<ChoiceItem> CopyChoices = Enumerable.Range(1, 5).Select(n => new ChoiceItem(n.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{n} exemplaire{(n > 1 ? "s" : string.Empty)}")).ToList();

    private static readonly IReadOnlyList<ChoiceItem> FormatChoices = [new("pdf", "PDF, prêt à imprimer"), new("docx", "Word, modifiable")];

    private ChoiceItem? promotion;
    private ChoiceItem? format;
    private ChoiceItem? month;
    private ChoiceItem? year;
    private ChoiceItem? copies;
    private string search = string.Empty;
    private bool includeLeft;
    private bool ready;
    private string subtitle = string.Empty;

    public CardsPage(ShellViewModel shell) : base(shell)
    {
        var today = DateTime.Today;
        month = MonthChoices[today.Month - 1];
        year = YearChoices()[1];
        copies = CopyChoices[0];
        format = FormatChoices[0];
        if (Shell.Can(Perm.StudentsWrite))
        {
            Action("Exporter les cartes", MaterialIconKind.FilePdfBox, ExportAsync, primary: true);
        }
        Action("Tout cocher", MaterialIconKind.CheckboxMarkedOutline, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxBlankOutline, () => { SetAll(false); return Task.CompletedTask; });
        Action("Session actuelle", MaterialIconKind.CalendarTodayOutline, () => { Month = MonthChoices[DateTime.Today.Month - 1]; Year = YearChoices()[1]; return Task.CompletedTask; });
        Action("Historique des lots", MaterialIconKind.History, ShowBatchesAsync);
        ActionRefresh();
    }

    public override string Title => "Cartes d'accès";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.CardAccountDetailsOutline;

    public ObservableCollection<CardRowVm> Rows { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Months => MonthChoices;
    public IReadOnlyList<ChoiceItem> Years => YearChoices();
    public IReadOnlyList<ChoiceItem> Copies => CopyChoices;
    public IReadOnlyList<ChoiceItem> Formats => FormatChoices;

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public string Search { get => search; set { if (Set(ref search, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public bool IncludeLeft { get => includeLeft; set { if (Set(ref includeLeft, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public ChoiceItem? Month { get => month; set => Set(ref month, value); }
    public ChoiceItem? Year { get => year; set => Set(ref year, value); }
    public ChoiceItem? Copy { get => copies; set => Set(ref copies, value); }
    public ChoiceItem? ExportFormat { get => format; set => Set(ref format, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        var query = ScoliaClient.Query(
            ("promotionId", Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : (Guid?)null),
            ("recherche", string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()),
            ("sortis", IncludeLeft ? "true" : null));
        var candidates = await Shell.Api.GetAsync<List<CardCandidateDto>>("api/cartes/candidats" + query);
        Rows.Clear();
        foreach (var c in candidates)
        {
            var diagnostic = !c.IdentityComplete ? "Identité incomplète" : c.DuplicateMatricule ? "Matricule répété" : !c.Eligible ? $"Exclu : {c.Status}" : string.Empty;
            Rows.Add(new CardRowVm(c, diagnostic));
        }
        subtitle = $"{Format.Count(Rows.Count, "apprenant", "apprenants")} · {Rows.Count(r => r.Selectable)} imprimable(s) · session {Month?.Label} {Year?.Value}";
        Raise(nameof(Subtitle));
    }

    private void SetAll(bool value)
    {
        foreach (var row in Rows.Where(r => r.Selectable)) row.Selected = value;
    }

    private static IReadOnlyList<ChoiceItem> YearChoices()
    {
        var current = DateTime.Today.Year;
        return [new((current - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (current - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(current.ToString(System.Globalization.CultureInfo.InvariantCulture), current.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new((current + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), (current + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }

    private async Task ExportAsync()
    {
        var ids = Rows.Where(r => r.Selected && r.Selectable).Select(r => r.EnrollmentId).ToList();
        if (ids.Count == 0)
        {
            Shell.Notify("Cochez au moins un apprenant imprimable.");
            return;
        }
        var month = int.Parse(Month?.Value ?? DateTime.Today.Month.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var year = int.Parse(Year?.Value ?? DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var copies = int.Parse(Copy?.Value ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        var format = ExportFormat?.Value ?? "pdf";
        var (bytes, skipped) = await Shell.Api.PostDocumentAsync("api/cartes/export", new { enrollmentIds = ids, month, year, copies, includeLeft = IncludeLeft, format });
        Files.Open(Files.Save(bytes, $"cartes-{year}-{month:00}.{format}"));
        Shell.Notify(skipped == 0
            ? "Cartes générées et enregistrées dans Téléchargements."
            : $"Cartes générées. {skipped} apprenant(s) écarté(s) : sortis, suspendus, identité incomplète ou matricule répété.");
        await RefreshAsync();
    }

    private async Task ShowBatchesAsync()
    {
        var batches = await Shell.Api.GetAsync<List<CardBatchDto>>("api/cartes/lots");
        var lines = batches.Select(b => $"{Format.DateTime(b.CreatedAtUtc)} · {b.Session} · {b.PromotionLabel} · {b.CardCount} carte(s), {b.PageCount} page(s) · {b.Format} · {b.CreatedBy}");
        var form = new FormViewModel("Historique des lots de cartes", Format.Count(batches.Count, "export", "exports"), _ => Task.FromResult(true))
        {
            Message = batches.Count == 0 ? "Aucun export pour l'instant." : string.Join(Environment.NewLine, lines),
            SubmitText = "Fermer",
            ShowCancel = false,
        };
        await Shell.Ask(form);
    }
}
