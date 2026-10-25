using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Ligne du lot : un apprenant, les mois choisis selon le mode, le montant calculé et le résultat de l'enregistrement.
/// </summary>
public sealed class BatchRowVm : Observable
{
    private readonly Action changed;
    private bool selected;
    private string reference = string.Empty;
    private string plan = string.Empty;
    private string amount = string.Empty;
    private string result = string.Empty;
    private bool failed;
    private decimal total;
    private IReadOnlyList<Guid> feeItemIds = [];

    public BatchRowVm(BatchCandidateDto candidate, string balance, string overdue, Action changed)
    {
        Candidate = candidate;
        Balance = balance;
        Overdue = overdue;
        this.changed = changed;
    }

    public BatchCandidateDto Candidate { get; }
    public Guid EnrollmentId => Candidate.EnrollmentId;
    public string Matricule => Candidate.Matricule;
    public string Name => Candidate.FullName;
    public string Promotion => Candidate.PromotionCode;
    public string Balance { get; }
    public string Overdue { get; }

    public bool Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) changed(); }
    }

    public string Reference { get => reference; set => Set(ref reference, value); }
    public string Plan { get => plan; private set => Set(ref plan, value); }
    public string Amount { get => amount; private set => Set(ref amount, value); }
    public string Result { get => result; private set => Set(ref result, value); }
    public bool Failed { get => failed; private set => Set(ref failed, value); }
    public IReadOnlyList<Guid> FeeItemIds => feeItemIds;
    public decimal Total => total;

    /// <summary>Retient les mois du mode choisi et le montant qui en résulte.</summary>
    public void ApplyMonths(IReadOnlyList<BatchMonthDto> chosen, Func<decimal, string> money)
    {
        feeItemIds = chosen.Select(m => m.FeeItemId).ToList();
        total = chosen.Sum(m => m.Remaining);
        Plan = chosen.Count switch
        {
            0 => "Aucun mois ouvert",
            1 => chosen[0].Label,
            _ => $"{chosen.Count} mois : {string.Join(", ", chosen.Select(m => m.Label))}",
        };
        Amount = chosen.Count == 0 ? "—" : money(total);
    }

    /// <summary>Contrôle sans écriture : la ligne montre ce qui bloque, ou les reçus semblables à vérifier.</summary>
    public void ShowCheck(BatchPreviewLineDto line)
    {
        Failed = line.Error is not null;
        Result = line.Error is not null ? $"À corriger · {line.Error}"
            : line.Duplicates.Count > 0 ? $"Semblable à {string.Join(", ", line.Duplicates)}"
            : string.Empty;
    }

    /// <summary>Après enregistrement : la ligne réussie est décochée, la ligne en erreur reste cochée pour correction.</summary>
    public void ShowResult(BatchPaymentLineDto line)
    {
        Failed = !line.Ok;
        Result = line.Ok ? $"OK · {line.ReceiptNumber}" : $"Erreur · {line.Error}";
        selected = !line.Ok;
        Raise(nameof(Selected));
        changed();
    }
}

/// <summary>Encaissement par lot : plusieurs apprenants et plusieurs mois en une opération, un reçu numéroté par apprenant.</summary>
public sealed class BatchPaymentPage : PageViewModel
{
    private readonly Dictionary<Guid, BatchPaymentLineDto> results = [];
    private string search = string.Empty;
    private ChoiceItem? promotion;
    private ChoiceItem? mode;
    private ChoiceItem? method;
    private DateTime? day = DateTime.Now;
    private string subtitle = string.Empty;
    private string selectionSummary = string.Empty;
    private bool ready;
    private DispatcherTimer? timer;

    public BatchPaymentPage(ShellViewModel shell) : base(shell)
    {
        mode = Modes[0];
        method = Labels.Methods[0];
        if (Shell.Can(Perm.PaymentsCreate))
        {
            Action("Vérifier le lot", MaterialIconKind.ClipboardCheckOutline, async () => { await CheckAsync(); });
            Action("Enregistrer le lot", MaterialIconKind.CashCheck, PayAsync, primary: true);
        }
        Action("Tout cocher", MaterialIconKind.CheckboxMultipleMarkedOutline, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxMultipleBlankOutline, () => { SetAll(false); return Task.CompletedTask; });
        ActionRefresh();
    }

    public override string Title => "Encaissement par lot";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.FormatListChecks;

    public ObservableCollection<BatchRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Modes { get; } =
    [
        new("late", "Mois en retard"),
        new("next", "Prochain mois"),
        new("all", "Tous les mois ouverts"),
    ];
    public IReadOnlyList<ChoiceItem> Methods { get; } = Labels.Methods;

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

    /// <summary>Le mode choisit les mois de chaque apprenant : en retard, prochain mois ou tous les mois ouverts.</summary>
    public ChoiceItem? Mode { get => mode; set { if (Set(ref mode, value) && ready) ApplyMode(); } }
    public ChoiceItem? Method { get => method; set => Set(ref method, value); }
    public DateTime? Day { get => day; set => Set(ref day, value); }
    public string SelectionSummary { get => selectionSummary; private set => Set(ref selectionSummary, value); }

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

        var candidates = await Shell.Api.GetAsync<List<BatchCandidateDto>>("api/paiements/lot/candidats" + ScoliaClient.Query(
            ("promotionId", Promotion?.Value), ("q", Search.Trim())));

        Items.Clear();
        foreach (var candidate in candidates)
        {
            var row = new BatchRowVm(candidate, Money(candidate.Balance), Money(candidate.Overdue), SelectionChanged);
            row.ApplyMonths(PlanFor(candidate), Money);
            if (results.TryGetValue(candidate.EnrollmentId, out var previous)) row.ShowResult(previous);
            Items.Add(row);
        }
        subtitle = $"{Format.Count(candidates.Count, "apprenant à régler", "apprenants à régler")} · reste total {Money(candidates.Sum(c => c.Balance))}";
        Raise(nameof(Subtitle));
        SelectionChanged();
    }

    private IReadOnlyList<BatchMonthDto> PlanFor(BatchCandidateDto candidate) => Mode?.Value switch
    {
        "late" => candidate.Months.Where(m => m.IsOverdue).ToList(),
        "next" => candidate.Months.Take(1).ToList(),
        _ => candidate.Months.ToList(),
    };

    private void ApplyMode()
    {
        foreach (var row in Items) row.ApplyMonths(PlanFor(row.Candidate), Money);
        SelectionChanged();
    }

    private void SetAll(bool value)
    {
        foreach (var row in Items) row.Selected = value;
        SelectionChanged();
        Shell.Notify(value ? $"{Format.Count(Items.Count, "apprenant coché", "apprenants cochés")}." : "Aucun apprenant coché.");
    }

    private void SelectionChanged()
    {
        var chosen = Items.Where(i => i.Selected).ToList();
        SelectionSummary = chosen.Count == 0 ? "Aucun apprenant coché"
            : $"{Format.Count(chosen.Count, "apprenant coché", "apprenants cochés")} · total {Money(chosen.Sum(i => i.Total))}";
    }

    private List<BatchRowVm> Chosen() => Items.Where(i => i.Selected).ToList();

    private object BuildRequest(IReadOnlyList<BatchRowVm> rows, string? key) => new
    {
        date = ToDay(Day),
        idempotencyKey = key,
        items = rows.Select(r => new
        {
            enrollmentId = r.EnrollmentId,
            feeItemIds = r.FeeItemIds,
            method = Method?.Value ?? "Especes",
            reference = string.IsNullOrWhiteSpace(r.Reference) ? null : r.Reference.Trim(),
        }).ToList(),
    };

    /// <summary>Contrôle le lot sans rien écrire. Chaque ligne affiche son montant, son erreur ou ses doublons.</summary>
    private async Task<BatchPreviewDto?> CheckAsync()
    {
        var rows = Chosen();
        if (rows.Count == 0)
        {
            Shell.Notify("Cochez au moins un apprenant.");
            return null;
        }
        var preview = await Shell.Api.SendAsync<BatchPreviewDto>(HttpMethod.Post, "api/paiements/lot/apercu", BuildRequest(rows, null));
        for (var i = 0; i < rows.Count && i < preview.Lines.Count; i++) rows[i].ShowCheck(preview.Lines[i]);
        return preview;
    }

    private async Task PayAsync()
    {
        var preview = await CheckAsync();
        if (preview is null) return;
        if (preview.Errors > 0)
        {
            Shell.Notify($"{Format.Count(preview.Errors, "ligne à corriger", "lignes à corriger")} avant l'enregistrement.");
            return;
        }

        var question = $"Enregistrer {Format.Count(preview.Count, "paiement séparé", "paiements séparés")} pour un total de {Money(preview.Total)} à la date du {Format.Date(ToDay(Day))} ?";
        if (preview.DuplicateCount > 0)
            question += $"\n\n{Format.Count(preview.DuplicateCount, "reçu semblable existe déjà", "reçus semblables existent déjà")} à cette date : les lignes signalées sont à vérifier.";
        if (!await Shell.Confirm("Enregistrer le lot", question, "Enregistrer")) return;

        // La clé d'idempotence est propre à ce clic : un envoi répété après une coupure ne double pas les reçus.
        var result = await Shell.Api.SendAsync<BatchPaymentResultDto>(HttpMethod.Post, "api/paiements/lot",
            BuildRequest(Chosen(), Guid.NewGuid().ToString("N")));
        foreach (var line in result.Lines) results[line.EnrollmentId] = line;
        Shell.Notify($"{Format.Count(result.Succeeded, "paiement enregistré", "paiements enregistrés")} · {result.Failed} erreur(s).");
        await RefreshAsync();
    }
}
