using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Module du catalogue à l'écran : il s'ouvre et se referme d'un toucher, comme une rubrique de réglages.</summary>
public sealed class CatalogModuleVm(CatalogModule module, IReadOnlyList<CatalogEntry> entries, bool expanded) : Observable
{
    private bool isExpanded = expanded;
    public string Title => module.Title;
    public string Summary => module.Summary;
    public MaterialIconKind Icon => Enum.TryParse<MaterialIconKind>(module.Icon, out var kind) ? kind : MaterialIconKind.InformationOutline;
    public IBrush Brush => ThemeBrush.Get(module.Tone);
    public IReadOnlyList<CatalogEntry> Entries => entries;
    public string CountText => Format.Count(entries.Count, "fonction", "fonctions");
    public bool IsExpanded { get => isExpanded; set { if (Set(ref isExpanded, value)) Raise(nameof(Chevron)); } }
    public MaterialIconKind Chevron => isExpanded ? MaterialIconKind.ChevronUp : MaterialIconKind.ChevronDown;
    public RelayCommand Toggle => toggle ??= new RelayCommand(() => IsExpanded = !IsExpanded);
    private RelayCommand? toggle;
}

/// <summary>
/// « Info sur l'application » : le catalogue du fonctionnement général de SCOLIA, module par module, avec une recherche.
/// Ouvert depuis Aide, juste après « À propos ».
/// </summary>
public sealed class AppInfoViewModel : Observable
{
    private string query = string.Empty;

    public AppInfoViewModel(ShellViewModel shell)
    {
        Close = new RelayCommand(shell.CloseDialog);
        ClearQuery = new RelayCommand(() => Query = string.Empty);
        Refresh();
    }

    public string Title => "Info sur l'application";
    public string Subtitle => $"SCOLIA Pro {AboutInfo.Version} · fonctionnement général, module par module";
    public string Totals => $"{AppCatalog.Modules.Count} modules · {AppCatalog.Modules.Sum(m => m.Entries.Count)} fonctions décrites";
    public RelayCommand Close { get; }
    public RelayCommand ClearQuery { get; }
    public List<CatalogModuleVm> Modules { get; private set; } = [];
    public bool HasModules => Modules.Count > 0;
    public bool IsEmpty => Modules.Count == 0;
    public bool HasQuery => query.Length > 0;

    /// <summary>Micro du champ, comme la recherche de l'accueil.</summary>
    public bool VoiceAvailable => VoiceInput.Available;
    private bool listening;
    public bool Listening { get => listening; private set { if (Set(ref listening, value)) Raise(nameof(Placeholder)); } }
    public string Placeholder => listening ? "Parlez maintenant…" : "Chercher une fonction : reçu, empreinte, sauvegarde…";
    public AsyncCommand Listen => listen ??= new AsyncCommand(async () =>
    {
        Listening = true;
        try { if (await VoiceInput.ListenAsync() is { } text) Query = text; }
        finally { Listening = false; }
    });
    private AsyncCommand? listen;

    /// <summary>Recherche dans tout le catalogue : les modules concernés s'ouvrent sur les fonctions trouvées.</summary>
    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value ?? string.Empty)) return;
            Raise(nameof(HasQuery));
            Refresh();
        }
    }

    private void Refresh()
    {
        var text = query.Trim();
        Modules = AppCatalog.Modules
            .Select((m, i) => (m, i, entries: text.Length == 0 ? m.Entries : m.Entries.Where(e => AppCatalog.Matches(text, m, e)).ToList()))
            .Where(x => x.entries.Count > 0)
            .Select(x => new CatalogModuleVm(x.m, x.entries, expanded: text.Length > 0 || x.i == 0))
            .ToList();
        Raise(nameof(Modules));
        Raise(nameof(HasModules));
        Raise(nameof(IsEmpty));
    }
}
