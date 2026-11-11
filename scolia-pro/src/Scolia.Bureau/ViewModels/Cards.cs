using System.Collections.ObjectModel;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Apprenant dans la liste des cartes : la case Carte choisit les impressions.</summary>
public sealed class CardRowVm(CardCandidateDto source, string diagnostic, Action<Guid, bool>? onSelectionChanged = null) : Observable
{
    private bool selected;

    public Guid EnrollmentId { get; } = source.EnrollmentId;
    public string Matricule { get; } = source.Matricule;
    public string Name { get; } = source.FullName;
    public string Promotion { get; } = source.PromotionCode;
    public string Status { get; } = source.Status;
    public string Diagnostic { get; } = diagnostic;
    public bool Selectable { get; } = source.Eligible && source.IdentityComplete && !source.DuplicateMatricule;

    public bool Selected
    {
        get => selected;
        set
        {
            // Une carte bloquée par un statut ou un matricule répété ne peut jamais entrer
            // dans un lot, même si la case de la grille reçoit un clic.
            if (value && !Selectable) return;
            if (Set(ref selected, value)) onSelectionChanged?.Invoke(EnrollmentId, value);
        }
    }
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
    private DispatcherTimer? searchTimer;
    private int refreshVersion;
    private bool includeLeft;
    private bool ready;
    private string subtitle = string.Empty;
    // Conserve le lot sélectionné lorsque l'on filtre ou parcourt plusieurs promotions.
    private readonly HashSet<Guid> selectedCardIds = [];
    public int SelectedCount => selectedCardIds.Count;
    private bool generatorChecked;
    private string generatorStatus = "Vérification du moteur de cartes de l'établissement…";
    public string GeneratorStatus { get => generatorStatus; private set => Set(ref generatorStatus, value); }

    private async Task<ServerHealthDto?> CheckGeneratorAsync()
    {
        try
        {
            var health = await Shell.Api.GetAsync<ServerHealthDto>("api/sante");
            GeneratorStatus = CardServerCompatibility.SupportsCurrentGenerator(health.CardGenerator)
                ? $"Moteur PDF et Word vérifié · {health.CardGenerator} · serveur {health.Version}"
                : $"Générateur ancien sur le serveur {health.Version ?? "inconnu"} : mise à jour du poste serveur nécessaire.";
            return health;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            GeneratorStatus = "Générateur indisponible : vérifiez la connexion au serveur avant l'export.";
            return null;
        }
    }

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
        Action("Tout cocher", MaterialIconKind.CheckboxMultipleMarked, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxMultipleBlank, () => { SetAll(false); return Task.CompletedTask; });
        Action("Session actuelle", MaterialIconKind.CalendarToday, () =>
        {
            Month = MonthChoices[DateTime.Today.Month - 1];
            Year = YearChoices()[1];
            Shell.Notify($"Session choisie pour les cartes : {Month.Label.ToLowerInvariant()} {Year.Label}.");
            return Task.CompletedTask;
        });
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

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) { refreshVersion++; _ = Shell.Run(RefreshAsync); } } }
    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            refreshVersion++;
            searchTimer?.Stop();
            if (!ready) return;
            // Une recherche réseau par pause de saisie, pas une requête par caractère.
            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            searchTimer.Tick += async (_, _) =>
            {
                searchTimer?.Stop();
                if (Shell.Current == this) await Shell.Run(RefreshAsync);
            };
            searchTimer.Start();
        }
    }
    public bool IncludeLeft { get => includeLeft; set { if (Set(ref includeLeft, value) && ready) { refreshVersion++; _ = Shell.Run(RefreshAsync); } } }
    public ChoiceItem? Month { get => month; set => Set(ref month, value); }
    public ChoiceItem? Year { get => year; set => Set(ref year, value); }
    public ChoiceItem? Copy { get => copies; set => Set(ref copies, value); }
    public ChoiceItem? ExportFormat { get => format; set => Set(ref format, value); }

    public override async Task LoadAsync()
    {
        var request = ++refreshVersion;
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions[0];
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        if (!generatorChecked)
        {
            generatorChecked = true;
            await CheckGeneratorAsync();
        }
        var query = ScoliaClient.Query(
            ("promotionId", Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : (Guid?)null),
            ("recherche", string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()),
            ("sortis", IncludeLeft ? "true" : null));
        var candidates = await Shell.Api.GetAsync<List<CardCandidateDto>>("api/cartes/candidats" + query);
        // Ignorer toute ancienne réponse arrivée après un filtre plus récent.
        if (request != refreshVersion) return;
        Rows.Clear();
        foreach (var c in candidates)
        {
            var diagnostic = !c.IdentityComplete ? "Identité incomplète" : c.DuplicateMatricule ? "Matricule répété" : !c.Eligible ? $"Exclu : {c.Status}" : string.Empty;
            var row = new CardRowVm(c, diagnostic, SelectionChanged)
            {
                Selected = c.Eligible && c.IdentityComplete && !c.DuplicateMatricule && selectedCardIds.Contains(c.EnrollmentId),
            };
            Rows.Add(row);
        }
        // Sous-titre court : il tient sur deux lignes d'un téléphone. Le nombre d'apprenants n'est rappelé que s'il diffère.
        var printable = Rows.Count(r => r.Selectable);
        subtitle = (printable == Rows.Count
            ? Format.Count(printable, "carte à imprimer", "cartes à imprimer")
            : $"{Format.Count(printable, "carte à imprimer", "cartes à imprimer")} sur {Rows.Count}")
            + $" · {Month?.Label} {Year?.Value}";
        Raise(nameof(Subtitle));
    }

    private void SelectionChanged(Guid enrollmentId, bool isSelected)
    {
        if (isSelected) selectedCardIds.Add(enrollmentId);
        else selectedCardIds.Remove(enrollmentId);
        Raise(nameof(SelectedCount));
    }

    private void SetAll(bool value)
    {
        // « Tout décocher » concerne le lot entier, y compris les autres promotions.
        if (!value) selectedCardIds.Clear();
        var rows = Rows.Where(r => r.Selectable).ToList();
        foreach (var row in rows) row.Selected = value;
        Raise(nameof(SelectedCount));
        Shell.Notify(value ? $"{Format.Count(selectedCardIds.Count, $"{Shell.StudentTerms.Singular} coché", $"{Shell.StudentTerms.Plural} cochés")} pour les cartes."
            : "Sélection du lot annulée.");
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
        var ids = selectedCardIds.ToList();
        if (ids.Count == 0)
        {
            Shell.Notify($"Cochez au moins un {Shell.StudentTerms.Singular} imprimable.");
            return;
        }
        var month = int.Parse(Month?.Value ?? DateTime.Today.Month.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var year = int.Parse(Year?.Value ?? DateTime.Today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        var copies = int.Parse(Copy?.Value ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        var format = ExportFormat?.Value ?? "pdf";
        // Le PDF/Word est fabrique par le serveur, pas par le client Android :
        // une application récente reliée à un ancien serveur produirait l'ancien modèle.
        var health = await CheckGeneratorAsync();
        if (health is null)
        {
            Shell.Notify("Impossible de joindre le moteur de cartes du serveur. Aucune carte n'a été générée.");
            return;
        }
        if (!CardServerCompatibility.SupportsCurrentGenerator(health.CardGenerator))
        {
            Shell.Notify(CardServerCompatibility.OutdatedServerMessage(health.Version, health.CardGenerator));
            return;
        }
        var (bytes, skipped) = await Shell.Api.PostDocumentAsync("api/cartes/export", new { enrollmentIds = ids, month, year, copies, includeLeft = IncludeLeft, format });
        Files.Open(Files.Save(bytes, $"cartes-{year}-{month:00}.{format}"));
        Shell.Notify(skipped == 0
            ? "Cartes générées et enregistrées dans Téléchargements."
            : $"Cartes générées. {skipped} {Shell.StudentTerms.Plural} écartés : sortis, suspendus, identité incomplète ou matricule répété.");
        selectedCardIds.Clear();
        Raise(nameof(SelectedCount));
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
