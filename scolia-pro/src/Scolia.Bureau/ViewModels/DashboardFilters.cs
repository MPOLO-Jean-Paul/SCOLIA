using System.Collections.ObjectModel;
using Avalonia.Threading;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Un seul périmètre pour les chiffres, les lignes et les fichiers exportés des tableaux de bord.</summary>
public sealed class DashboardFiltersVm : Observable
{
    private bool initialized, supported, updatingChoices;
    private ChoiceItem? section, promotion, gender, state, sort;
    private string search = string.Empty;
    private bool expanded = !Preferences.MobileLayout;
    public bool Expanded { get => expanded; set => Set(ref expanded, value); }
    private DashboardFilterOptions? options;
    private DispatcherTimer? debounce;
    public event Action? Changed;
    public RelayCommand Reset { get; }
    public DashboardFiltersVm() => Reset = new RelayCommand(ResetSelection);
    public bool HasSelection => Supported && (HasScope || Sort?.Value != "code");
    public int ActiveCount => new[] { !string.IsNullOrEmpty(Section?.Value), !string.IsNullOrEmpty(Promotion?.Value),
        !string.IsNullOrEmpty(Gender?.Value), !string.IsNullOrEmpty(State?.Value), !string.IsNullOrWhiteSpace(Search) }.Count(value => value);
    public string Summary => !initialized ? "Préparation de la sélection…" : !Supported ? "Mise à jour du serveur requise" :
        (ActiveCount == 0 ? "Vue complète" : $"{ActiveCount} filtre{(ActiveCount > 1 ? "s" : "")} actif{(ActiveCount > 1 ? "s" : "")}") + " · " + Sort!.Label;
    private bool refreshing, refreshQueued;
    public async Task RefreshLatestAsync(Func<Task> refresh, Func<bool> selectionIsCurrent)
    {
        refreshQueued = true;
        if (refreshing) return;
        refreshing = true;
        try
        {
            do
            {
                refreshQueued = false;
                await refresh();
            } while (refreshQueued && !selectionIsCurrent());
        }
        finally { refreshing = false; }
    }

    private void ResetSelection()
    {
        debounce?.Stop();
        section = Sections.FirstOrDefault();
        RebuildPromotions();
        promotion = Promotions.FirstOrDefault();
        gender = Genders[0]; state = States[0]; sort = Sorts[0]; search = string.Empty;
        foreach (var property in new[] { nameof(Section), nameof(Promotion), nameof(Gender), nameof(State), nameof(Sort), nameof(Search) }) Raise(property);
        Notify();
    }
    public ObservableCollection<ChoiceItem> Sections { get; } = [];
    public ObservableCollection<ChoiceItem> Promotions { get; } = [];
    public IReadOnlyList<ChoiceItem> Genders { get; } = [new("", "Tous les sexes"), new("M", "Masculin"), new("F", "Féminin")];
    public IReadOnlyList<ChoiceItem> States { get; } = [new("", "Toutes les situations"), new("paying", "Payants"), new("uptodate", "Payants à jour"), new("late", "Payants en retard"), new("balance", "Solde restant"), new("sponsored", "Pris en charge"), new("partial", "Prise en charge partielle")];
    public IReadOnlyList<ChoiceItem> Sorts { get; } = [new("code", "Promotion croissante"), new("code-desc", "Promotion décroissante"), new("students", "Effectif décroissant"), new("collected", "Encaissements décroissants"), new("outstanding", "Reste décroissant"), new("overdue", "Retard décroissant")];
    public bool Supported { get => supported; private set { if (Set(ref supported, value)) Raise(nameof(SupportMessage)); } }
    public string SupportMessage => !initialized ? "Chargement des filtres…" : Supported ? "Les totaux et les exports suivent tous les filtres sélectionnés."
        : "Les filtres et les exports nécessitent la mise à jour du serveur de l'établissement.";
    public ChoiceItem? Section { get => section; set { if (!Set(ref section, value)) return; RebuildPromotions(); Notify(); } }
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value)) Notify(); } }
    public ChoiceItem? Gender { get => gender ?? Genders[0]; set { if (Set(ref gender, value)) Notify(); } }
    public ChoiceItem? State { get => state ?? States[0]; set { if (Set(ref state, value)) Notify(); } }
    public ChoiceItem? Sort { get => sort ?? Sorts[0]; set { if (Set(ref sort, value)) Notify(); } }
    public string Search { get => search; set
    {
        if (!Set(ref search, value ?? string.Empty)) return;
        debounce?.Stop();
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        debounce.Tick += (_, _) => { debounce?.Stop(); Notify(); };
        debounce.Start();
    } }
    public bool HasScope => !string.IsNullOrEmpty(Section?.Value) || !string.IsNullOrEmpty(Promotion?.Value)
        || !string.IsNullOrEmpty(Gender?.Value) || !string.IsNullOrEmpty(State?.Value) || !string.IsNullOrWhiteSpace(Search);
    public string Query => !Supported ? string.Empty : ScoliaClient.Query(("sectionId", Id(Section)), ("promotionId", Id(Promotion)),
        ("gender", Gender?.Value), ("state", State?.Value), ("search", Search.Trim()), ("sort", Sort?.Value));
    private static Guid? Id(ChoiceItem? item) => string.IsNullOrEmpty(item?.Value) ? null :
        Guid.TryParse(item.Value, out var id) ? id : throw new InvalidOperationException("Sélection du tableau de bord invalide.");
    private void Notify() { Raise(nameof(HasScope)); Raise(nameof(HasSelection)); Raise(nameof(ActiveCount)); Raise(nameof(Summary)); if (initialized && Supported && !updatingChoices) Changed?.Invoke(); }

    private void RebuildPromotions()
    {
        var keep = promotion?.Value;
        var wasUpdating = updatingChoices;
        updatingChoices = true;
        try
        {
            Promotions.Clear();
            Promotions.Add(new ChoiceItem("", "Toutes les promotions"));
            var selectedSection = section?.Value;
            foreach (var p in options?.Promotions ?? [])
                if (string.IsNullOrEmpty(selectedSection) || p.SectionId?.ToString() == selectedSection)
                    Promotions.Add(new ChoiceItem(p.Id.ToString(), p.Code + " · " + p.Name));
            promotion = Promotions.FirstOrDefault(p => p.Value == keep) ?? Promotions[0];
            Raise(nameof(Promotion));
        }
        finally { updatingChoices = wasUpdating; }
    }

    public async Task EnsureAsync(ScoliaClient api, bool promoter)
    {
        DashboardFilterOptions? next = null;
        try
        {
            next = await api.GetAsync<DashboardFilterOptions>(promoter ? "api/pilotage/filtres" : "api/rapports/tableau-de-bord/filtres");
        }
        catch (ApiException ex) when (ex.Status == 404) { }
        if (initialized && next?.Version == options?.Version
            && (next?.Sections ?? []).SequenceEqual(options?.Sections ?? [])
            && (next?.Promotions ?? []).SequenceEqual(options?.Promotions ?? [])) return;
        var keepSection = section?.Value;
        options = next;
        updatingChoices = true;
        try
        {
            Supported = options?.Version >= 1;
            Sections.Clear();
            Sections.Add(new ChoiceItem("", "Toutes les sections"));
            foreach (var item in options?.Sections ?? []) Sections.Add(new ChoiceItem(item.Id.ToString(), item.Label));
            section = Sections.FirstOrDefault(item => item.Value == keepSection) ?? Sections[0];
            Raise(nameof(Section));
            RebuildPromotions();
        }
        finally { updatingChoices = false; }
        initialized = true;
        foreach (var property in new[] { nameof(Summary), nameof(HasScope), nameof(HasSelection), nameof(ActiveCount), nameof(SupportMessage) }) Raise(property);
    }

    public async Task ExportAsync(ShellViewModel shell, bool promoter, string? loadedQuery, string? selectionKey)
    {
        if (!Supported)
        {
            shell.Notify("Mettez à jour le serveur de l'établissement pour exporter une sélection filtrée.");
            return;
        }
        if (loadedQuery != Query || string.IsNullOrEmpty(selectionKey))
        {
            shell.Notify("Attendez la fin du chargement ou actualisez le tableau de bord avant l'export.");
            return;
        }
        var query = loadedQuery;
        var key = selectionKey;
        var form = new FormViewModel("Exporter la sélection", "Le fichier reprend les promotions, les chiffres et le tri du tableau affiché.", async f =>
        {
            if (Query != query) throw new InvalidOperationException("Les filtres ont changé. Fermez cette fenêtre et relancez l'export.");
            var format = f.ChoiceValue("format")!;
            var path = promoter ? "api/pilotage/export" : "api/rapports/tableau-de-bord/export";
            await shell.Download(path + query + (query.Length == 0 ? "?" : "&") + "expectedSelection=" + Uri.EscapeDataString(key) + "&format=" + format,
                $"{(promoter ? "pilotage" : "tableau-bord")}-selection-{DateTime.Today:yyyyMMdd}.{format}");
            return true;
        }) { SubmitText = "Exporter" };
        form.AddChoice("format", "Format du fichier", [new("xlsx", "Excel · synthèse et détails"), new("pdf", "PDF · synthèse par promotion")], "xlsx", required: true);
        await shell.Ask(form);
    }
}
