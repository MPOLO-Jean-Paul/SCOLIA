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
    private readonly Action<BatchRowVm> changed;
    private bool selected;
    private string reference = string.Empty;
    private string plan = string.Empty;
    private string amount = string.Empty;
    private string result = string.Empty;
    private bool failed;
    private decimal total;
    private IReadOnlyList<Guid> feeItemIds = [];

    public BatchRowVm(BatchCandidateDto candidate, string balance, string overdue, Action<BatchRowVm> changed)
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
        set { if (Set(ref selected, value)) changed(this); }
    }

    public string Reference { get => reference; set => Set(ref reference, value); }
    public string Plan { get => plan; private set => Set(ref plan, value); }
    public string Amount { get => amount; private set => Set(ref amount, value); }
    public string Result { get => result; private set => Set(ref result, value); }
    public bool Failed { get => failed; private set => Set(ref failed, value); }
    public IReadOnlyList<Guid> FeeItemIds => feeItemIds;
    public decimal Total => total;

    /// <summary>Retient les mois du mode choisi et le montant qui en résulte.</summary>
    public void ApplyMonths(IReadOnlyList<BatchMonthDto> chosen, Func<decimal, string> money, bool lateOnly = false)
    {
        feeItemIds = chosen.Select(m => m.FeeItemId).ToList();
        total = chosen.Sum(m => m.Remaining);
        Plan = chosen.Count switch
        {
            // En mode « Mois en retard », un apprenant à jour n'a rien à régler : on le dit tel quel.
            0 => lateOnly ? "À jour" : "Aucun mois à payer",
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
        // Une erreur de validation ne sélectionne jamais une ligne retirée manuellement.
        // Une réussite retire la ligne du lot et évite un deuxième encaissement.
        if (line.Ok) Selected = false;
    }
}

/// <summary>Encaissement par lot : plusieurs apprenants et plusieurs mois en une opération, un reçu numéroté par apprenant.</summary>
public sealed class BatchPaymentPage : PageViewModel
{
    private readonly Dictionary<Guid, BatchPaymentLineDto> results = [];
    // Une référence par inscription et par tentative : l'aperçu et l'encaissement
    // portent exactement la même transaction, même si la recherche change entre-temps.
    private readonly Dictionary<Guid, string> autoReferences = [];
    // Le lot appartient à l'utilisateur ; la recherche ne le réinitialise jamais.
    private readonly Dictionary<Guid, BatchRowVm> selectedRows = [];
    private int queryVersion;
    private bool restoringRows;
    private bool showSelectedOnly;
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
            Action("Vérifier le lot", MaterialIconKind.ClipboardCheck, async () => { await CheckAsync(); });
            Action("Enregistrer le lot", MaterialIconKind.CashCheck, PayAsync, primary: true);
            // Le total coché et « Enregistrer le lot » restent en bas de l'écran pendant qu'on coche.
            Sticky = new StickyBarVm("Enregistrer le lot", new AsyncCommand(() => Shell.Run(PayAsync))) { Title = "Total du lot" };
        }
        Action("Tout cocher", MaterialIconKind.CheckboxMultipleMarked, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxMultipleBlank, () => { SetAll(false); return Task.CompletedTask; });
        ActionRefresh();
    }

    public override string Title => "Encaissement par lot";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.FormatListChecks;

    public ObservableCollection<BatchRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public IReadOnlyList<ChoiceItem> Modes { get; } =
    [
        new("first_late", "Premier mois en retard"),
        new("next", "Mois prochain (2e retard)"),
        new("late", "Tous les mois en retard"),
        new("all", "Tous les mois ouverts"),
    ];
    public IReadOnlyList<ChoiceItem> Methods { get; } = Labels.Methods;

    public string Search
    {
        get => search;
        set
        {
            if (!Set(ref search, value)) return;
            var version = ++queryVersion;
            timer?.Stop();
            if (!ready) return;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += async (_, _) =>
            {
                timer?.Stop();
                if (version == queryVersion && ReferenceEquals(Shell.Current, this))
                    await Shell.Run(RefreshAsync);
            };
            timer.Start();
        }
    }

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) { queryVersion++; timer?.Stop(); _ = Shell.Run(RefreshAsync); } } }

    /// <summary>Le mode choisit un premier retard, un deuxième retard, tous les retards ou tous les mois ouverts.</summary>
    public ChoiceItem? Mode
    {
        get => mode;
        set
        {
            if (!Set(ref mode, value)) return;
            Raise(nameof(MonthModeHint));
            if (ready) ApplyMode();
        }
    }

    public string MonthModeHint => Mode?.Value switch
    {
        "first_late" => "Le premier mois en retard non soldé de chaque apprenant.",
        "next" => "Uniquement le deuxième mois en retard, après le premier.",
        "late" => "Tous les mois en retard non soldés, du plus ancien au plus récent.",
        "all" => "Tous les mois à régler, y compris ceux qui ne sont pas encore en retard.",
        _ => string.Empty,
    };
    public ChoiceItem? Method { get => method; set => Set(ref method, value); }
    public DateTime? Day { get => day; set => Set(ref day, value); }
    public string SelectionSummary { get => selectionSummary; private set => Set(ref selectionSummary, value); }
    public int SelectedCount => selectedRows.Count;
    public bool HasSelected => selectedRows.Count > 0;
    public string SelectedViewTitle => showSelectedOnly ? "Revenir aux résultats" : "Voir le lot sélectionné";
    public RelayCommand ToggleSelectedView => toggleSelectedView ??= new RelayCommand(() =>
    {
        showSelectedOnly = !showSelectedOnly;
        Raise(nameof(SelectedViewTitle));
        ShowRows();
    });
    private RelayCommand? toggleSelectedView;


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

        // Lors du premier chargement, un filtre saisi avant la réponse des promotions
        // doit être pris en compte et ne doit pas rendre la liste définitivement vide.
        var version = ++queryVersion;
        var candidates = await Shell.Api.GetAsync<List<BatchCandidateDto>>("api/paiements/lot/candidats" + ScoliaClient.Query(
            ("promotionId", Promotion?.Value), ("q", Search.Trim())));

        // Une ancienne recherche arrivée en retard ne remplace jamais les lignes récentes.
        if (version != queryVersion) return;
        var fresh = new List<BatchRowVm>(candidates.Count);
        restoringRows = true;
        try
        {
            foreach (var candidate in candidates)
            {
                var row = new BatchRowVm(candidate, Money(candidate.Balance), Money(candidate.Overdue), SelectionChanged);
                row.ApplyMonths(PlanFor(candidate), Money, LateOnly);
                if (selectedRows.TryGetValue(candidate.EnrollmentId, out var previouslySelected))
                {
                    row.Reference = previouslySelected.Reference;
                    if (row.FeeItemIds.Count > 0) row.Selected = true;
                    else selectedRows.Remove(candidate.EnrollmentId);
                }
                if (results.TryGetValue(candidate.EnrollmentId, out var previousResult)) row.ShowResult(previousResult);
                fresh.Add(row);
            }
            allRows.Clear();
            allRows.AddRange(fresh);
        }
        finally { restoringRows = false; }
        ShowRows();
        UpdateSelectionSummary();
    }

    /// <summary>Tous les apprenants chargés ; la liste n'affiche que ceux qui ont à régler dans le mode choisi.</summary>
    private readonly List<BatchRowVm> allRows = [];

    /// <summary>
    /// Seuls les apprenants qui ont des mois à régler dans le mode choisi apparaissent : la liste ne commence
    /// pas par des lignes « À jour » sans rien à encaisser. Une ligne déjà traitée reste pour montrer son résultat.
    /// </summary>
    private void ShowRows()
    {
        Items.Clear();
        var visible = showSelectedOnly
            ? selectedRows.Values.Where(r => r.Selected && r.FeeItemIds.Count > 0)
            : allRows.Where(r => r.FeeItemIds.Count > 0 || r.Result.Length > 0);
        foreach (var row in visible.OrderBy(r => r.Promotion).ThenBy(r => r.Name)) Items.Add(row);
        var due = Items.Where(r => r.FeeItemIds.Count > 0).ToList();
        subtitle = due.Count == 0
            ? (LateOnly ? "Aucun apprenant n'a de mois en retard" : "Aucun mois à régler")
            : $"{Format.Count(due.Count, "apprenant à régler", "apprenants à régler")} · {Money(due.Sum(r => r.Total))}";
        Raise(nameof(Subtitle));
        Raise(nameof(IsEmpty));
    }

    /// <summary>Rien à régler dans ce mode : la page le dit au lieu de montrer une liste vide.</summary>
    public bool IsEmpty => Items.Count == 0;

    private bool LateOnly => Mode?.Value is "first_late" or "next" or "late";

    /// <summary>
    /// Un retard correspond à une mensualité due et non soldée. Les trois
    /// modes de retard utilisent exactement la même chronologie de date,
    /// sans compter les mois non encore exigibles. Si un second retard
    /// n'existe pas, la sélection est vide (ne jamais encaisser le premier deux fois).
    /// </summary>
    public static IReadOnlyList<BatchMonthDto> MonthsForMode(BatchCandidateDto candidate, string? mode)
    {
        var due = candidate.Months.Where(m => m.Remaining > 0)
            .OrderBy(m => m.DueDate).ThenBy(m => m.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var late = due.Where(m => m.IsOverdue).ToList();
        return mode switch
        {
            "first_late" => late.Take(1).ToList(),
            "next" => late.Skip(1).Take(1).ToList(),
            "late" => late,
            "all" => due,
            _ => late.Take(1).ToList(),
        };
    }

    private IReadOnlyList<BatchMonthDto> PlanFor(BatchCandidateDto candidate) =>
        MonthsForMode(candidate, Mode?.Value);

    private void ApplyMode()
    {
        // La nouvelle règle de mois s'applique au lot entier, même aux lignes masquées.
        foreach (var row in allRows.Concat(selectedRows.Values).Distinct().ToList())
        {
            row.ApplyMonths(PlanFor(row.Candidate), Money, LateOnly);
            if (row.FeeItemIds.Count == 0) row.Selected = false;
        }
        ShowRows();
        UpdateSelectionSummary();
    }

    private void SetAll(bool value)
    {
        // Tout cocher ajoute les lignes visibles. Tout décocher efface l'ensemble du lot.
        if (!value)
            foreach (var row in selectedRows.Values.ToList()) row.Selected = false;
        foreach (var row in Items.ToList()) row.Selected = value && row.FeeItemIds.Count > 0;
        UpdateSelectionSummary();
        var count = selectedRows.Count;
        Shell.Notify(value ? (count == 0 ? "Aucun apprenant n'a de mois à régler dans ce mode." : $"{Format.Count(count, "apprenant coché", "apprenants cochés")} dans le lot.") : "Tout le lot a été décoché.");
    }

    /// <summary>Actualise le lot indépendant des résultats visibles de recherche.</summary>
    public void SelectionChanged(BatchRowVm row)
    {
        if (row.Selected && row.FeeItemIds.Count > 0) selectedRows[row.EnrollmentId] = row;
        else selectedRows.Remove(row.EnrollmentId);
        if (!restoringRows)
        {
            UpdateSelectionSummary();
            if (showSelectedOnly && !row.Selected)
                Dispatcher.UIThread.Post(ShowRows); // Ne pas modifier Items pendant le clic DataGrid.
        }
    }

    private void UpdateSelectionSummary()
    {
        var chosen = Chosen();
        SelectionSummary = chosen.Count == 0 ? "Aucun apprenant coché"
            : $"{Format.Count(chosen.Count, "apprenant coché", "apprenants cochés")} · total {Money(chosen.Sum(i => i.Total))}";
        if (Sticky is { } bar)
        {
            bar.IsVisible = chosen.Count > 0;
            bar.Value = Money(chosen.Sum(i => i.Total));
            bar.Detail = Format.Count(chosen.Count, "apprenant coché", "apprenants cochés");
            bar.CanRun = chosen.Count > 0;
        }
        Raise(nameof(SelectedCount));
        Raise(nameof(HasSelected));
    }

    private List<BatchRowVm> Chosen() => selectedRows.Values
        .Where(r => r.Selected && r.FeeItemIds.Count > 0)
        .OrderBy(r => r.Promotion).ThenBy(r => r.Name).ToList();

    private object BuildRequest(IReadOnlyList<BatchRowVm> rows, string? key) => new
    {
        date = ToDay(Day),
        idempotencyKey = key,
        items = rows.Select(r => new
        {
            enrollmentId = r.EnrollmentId,
            feeItemIds = r.FeeItemIds,
            method = Method?.Value ?? "Especes",
            // Comme à l'encaissement simple : sans référence saisie, SCOLIA en donne une.
            reference = ReferenceFor(r),
        }).ToList(),
    };

    private string ReferenceFor(BatchRowVm row)
    {
        if (!string.IsNullOrWhiteSpace(row.Reference)) return row.Reference.Trim();
        if (autoReferences.TryGetValue(row.EnrollmentId, out var reference)) return reference;
        reference = CashierPage.NewReference(DateTime.Now);
        autoReferences[row.EnrollmentId] = reference;
        return reference;
    }

    /// <summary>Contrôle le lot sans rien écrire. Chaque ligne affiche son montant, son erreur ou ses doublons.</summary>
    private async Task<BatchPreviewDto?> CheckAsync(IReadOnlyList<BatchRowVm>? fixedSelection = null)
    {
        var rows = fixedSelection?.ToList() ?? Chosen();
        if (rows.Count == 0)
        {
            Shell.Notify("Cochez au moins un apprenant.");
            return null;
        }
        var preview = await Shell.Api.SendAsync<BatchPreviewDto>(HttpMethod.Post, "api/paiements/lot/apercu", BuildRequest(rows, null));
        // Chaque diagnostic retourne sur le bon apprenant, même si l'API réordonne les lignes.
        var byEnrollment = rows.ToDictionary(r => r.EnrollmentId);
        foreach (var line in preview.Lines)
            if (byEnrollment.TryGetValue(line.EnrollmentId, out var row)) row.ShowCheck(line);
        return preview;
    }

    private async Task PayAsync()
    {
        // On vérifie et confirme le même instantané de sélection, y compris les dossiers filtrés.
        var rows = Chosen();
        var preview = await CheckAsync(rows);
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
            BuildRequest(rows, Guid.NewGuid().ToString("N")));
        foreach (var line in result.Lines)
        {
            results[line.EnrollmentId] = line;
            if (line.Ok)
            {
                // Ne plus afficher un paiement terminé dans la sélection prête à encaisser.
                if (selectedRows.TryGetValue(line.EnrollmentId, out var paidRow)) paidRow.ShowResult(line);
                selectedRows.Remove(line.EnrollmentId);
                autoReferences.Remove(line.EnrollmentId);
            }
            else if (selectedRows.TryGetValue(line.EnrollmentId, out var invalidRow)) invalidRow.ShowResult(line);
        }
        UpdateSelectionSummary();
        Shell.Notify($"{Format.Count(result.Succeeded, "paiement enregistré", "paiements enregistrés")} · {result.Failed} erreur(s).");
        await RefreshAsync();
    }
}
