using System.Collections.ObjectModel;
using System.Windows.Input;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Bouton d'en-tête de page : Nouveau, Actualiser, Exporter. Une action de ligne agit sur la ligne choisie du tableau :
/// elle apparaît aussi au clic droit sur cette ligne, et l'action par défaut part d'un double-clic.
/// </summary>
public sealed record ActionVm(string Label, MaterialIconKind Icon, ICommand Command, bool Primary = false, bool Danger = false,
    bool Row = false, bool Default = false, bool MenuOnly = false)
{
    /// <summary>Bouton bleu : action principale qui n'est pas destructrice.</summary>
    public bool IsPrimary => Primary && !Danger;

    /// <summary>Info-bulle : ce que fait l'action, sur quoi elle agit, et ce qui protège une action définitive.</summary>
    public string Hint => Label switch
    {
        "Actualiser" => "Recharge les données de la page depuis le serveur.",
        "Tout cocher" => "Coche toutes les lignes affichées.",
        "Tout décocher" => "Décoche toutes les lignes.",
        "Excel" => "Enregistre la liste affichée en Excel dans Téléchargements.",
        "PDF" => "Enregistre la liste affichée en PDF dans Téléchargements.",
        "Imprimer" => "Prépare le document de la page pour l'impression.",
        _ => Row
            ? $"{Label} : agit sur la ligne choisie du tableau. Même commande au clic droit sur la ligne{(Default ? ", ou double-clic" : string.Empty)}."
            : Danger ? $"{Label} : action définitive. Un récapitulatif demande votre confirmation."
            : $"{Label}.",
    };
}

/// <summary>Écran de l'application. Chaque écran charge ses données et publie ses actions dans l'en-tête.</summary>
public abstract class PageViewModel : Observable
{
    /// <summary>Boutons visibles au plus dans l'en-tête : au-delà, les actions passent dans le menu « Plus ».</summary>
    private const int MaxVisible = 3;
    /// <summary>Nombre de boutons fixé par la largeur de la fenêtre ; -1 tant qu'elle ne l'a pas mesuré.</summary>
    private int visibleCount = -1;
    private bool busy;
    private TaskCompletionSource? refreshCompletion;

    protected PageViewModel(ShellViewModel shell)
    {
        Shell = shell;
        Actions.CollectionChanged += (_, _) =>
        {
            Raise(nameof(VisibleActions));
            Raise(nameof(MoreActions));
            Raise(nameof(HasMoreActions));
            Raise(nameof(HasHeaderActions));
        };
    }

    protected ShellViewModel Shell { get; }

    public abstract string Title { get; }
    /// <summary>Titre affiché en tête de page : le titre de l'écran, sauf l'accueil qui salue l'utilisateur.</summary>
    public virtual string Heading => Title;
    public virtual string Subtitle => string.Empty;
    public virtual MaterialIconKind Glyph => MaterialIconKind.FileDocumentOutline;
    public ObservableCollection<ActionVm> Actions { get; } = [];
    /// <summary>L'action principale et les plus courantes, affichées en boutons.</summary>
    public IReadOnlyList<ActionVm> VisibleActions => HeaderActions.Take(Shown).ToList();
    /// <summary>Les autres actions, rangées dans le menu « Plus » pour garder un en-tête net.</summary>
    public IReadOnlyList<ActionVm> MoreActions => HeaderActions.Skip(Shown).ToList();
    public bool HasMoreActions => Shown < HeaderActions.Count;

    /// <summary>Actions de l'en-tête : toutes, sauf celles réservées au clic droit sur une ligne.</summary>
    public IReadOnlyList<ActionVm> HeaderActions => Actions.Where(a => !a.MenuOnly).ToList();
    public bool HasHeaderActions => Actions.Any(a => !a.MenuOnly);

    /// <summary>Nombre de boutons affichés : la place mesurée, ou par défaut trois boutons et le menu pour les autres.</summary>
    private int Shown
    {
        get
        {
            var count = Actions.Count(a => !a.MenuOnly);
            return visibleCount >= 0 ? Math.Min(visibleCount, count) : count <= MaxVisible + 1 ? count : MaxVisible;
        }
    }

    /// <summary>
    /// Fenêtre étroite : la fenêtre indique combien de boutons tiennent à côté du titre. Les autres passent dans
    /// le menu « Plus », sans jamais recouvrir le titre de la page.
    /// </summary>
    public void FitActions(int count)
    {
        count = Math.Clamp(count, 0, MaxVisible + 1);
        if (count == visibleCount) return;
        visibleCount = count;
        Raise(nameof(VisibleActions));
        Raise(nameof(MoreActions));
        Raise(nameof(HasMoreActions));
    }
    public bool Busy { get => busy; protected set => Set(ref busy, value); }

    /// <summary>Écran de téléphone : certaines pages montrent alors une présentation en lignes au lieu d'un tableau large.</summary>
    public bool Phone => Shell.Phone;
    public bool NotPhone => !Shell.Phone;

    public void PhoneChanged()
    {
        Raise(nameof(Phone));
        Raise(nameof(NotPhone));
    }

    /// <summary>L'écran quitte l'affichage : il rend les actions qu'il avait réservées.</summary>
    public virtual void Left() { }

    /// <summary>L'écran revient à l'affichage, par exemple avec le bouton Retour.</summary>
    public virtual void Entered() { }

    /// <summary>
    /// Recharge après une écriture d'un autre poste : sans notification, et la ligne choisie reste choisie
    /// (retrouvée par son identifiant dans la nouvelle liste).
    /// </summary>
    public async Task RefreshQuietlyAsync()
    {
        var selected = GetType().GetProperty("Selected");
        var before = selected?.GetValue(this);
        var key = Identity(before);
        await RefreshCoreAsync(quiet: true);
        if (selected is not { CanWrite: true } || before is null || key is null) return;
        // Une autre ligne a été choisie pendant la requête : la décision du lecteur prime.
        if (selected.GetValue(this) is { } now && !ReferenceEquals(now, before)) return;
        foreach (var property in GetType().GetProperties())
        {
            if (property.PropertyType == typeof(string) || !typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0) continue;
            if (property.GetValue(this) is not System.Collections.IEnumerable items) continue;
            var match = items.Cast<object?>().FirstOrDefault(o => o is not null && o.GetType() == before.GetType() && Identity(o) == key);
            if (match is null) continue;
            selected.SetValue(this, match);
            return;
        }
    }

    private static string? Identity(object? row)
    {
        if (row is null) return null;
        foreach (var name in new[] { "Id", "EnrollmentId", "StudentId", "Number", "Matricule", "Key" })
            if (row.GetType().GetProperty(name)?.GetValue(row) is { } value) return value.ToString();
        return null;
    }

    /// <summary>Charge les données depuis le serveur. Chaque écran le redéfinit.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Recharge l'écran et signale les erreurs sans interrompre l'interface.</summary>
    public Task RefreshAsync() => RefreshCoreAsync(quiet: false);

    private async Task RefreshCoreAsync(bool quiet)
    {
        // Les événements du direct et le contrôle périodique peuvent arriver ensemble.
        // Un seul chargement à la fois ; une mise à jour silencieuse déjà couverte n'en relance pas un autre.
        while (refreshCompletion is { } pending)
        {
            if (quiet) return;
            await pending.Task;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refreshCompletion = completion;
        if (!quiet) Busy = true;
        try { await LoadAsync(); }
        catch (ApiException ex) { if (!quiet) Shell.Notify(ex.Message); }
        catch (HttpRequestException) { if (!quiet) Shell.Notify("Le serveur SCOLIA ne répond pas."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (!quiet) Shell.Notify($"Erreur inattendue : {ex.Message}"); }
        finally
        {
            if (!quiet) Busy = false;
            refreshCompletion = null;
            completion.TrySetResult();
        }
    }

    /// <summary>Imprime le document affiché, quand l'écran en a un.</summary>
    public virtual Task PrintAsync()
    {
        Shell.Notify("Rien à imprimer sur cette page.");
        return Task.CompletedTask;
    }

    protected string Currency => Shell.Session?.CurrencySymbol ?? string.Empty;

    protected string Money(decimal value) => Format.Money(value, Currency);

    protected void Action(string label, MaterialIconKind icon, Func<Task> work, bool primary = false, bool danger = false, bool row = false, bool open = false) =>
        Actions.Add(new ActionVm(label, icon, new AsyncCommand(() => Shell.Run(row || open ? NeedsRow(label, work) : work)), primary, danger, row || open, open));

    /// <summary>Action de ligne proposée seulement au clic droit : elle n'encombre pas l'en-tête.</summary>
    protected void MenuAction(string label, MaterialIconKind icon, Func<Task> work, bool open = false, bool danger = false) =>
        Actions.Add(new ActionVm(label, icon, new AsyncCommand(() => Shell.Run(NeedsRow(label, work))), Danger: danger, Row: true, Default: open, MenuOnly: true));

    /// <summary>
    /// Ligne choisie dans le tableau de l'écran, ou null. Par défaut la propriété « Selected » de l'écran ;
    /// un écran à plusieurs tableaux indique celui de sa rubrique ouverte.
    /// </summary>
    public virtual object? SelectedRow => GetType().GetProperty("Selected")?.GetValue(this);

    /// <summary>Vrai quand l'écran a un tableau dont on choisit une ligne.</summary>
    protected virtual bool HasRows => GetType().GetProperty("Selected") is not null;

    /// <summary>
    /// Une action de ligne lancée sans ligne choisie l'explique, au lieu de ne rien faire :
    /// l'utilisateur sait qu'il doit d'abord cliquer sur une ligne.
    /// </summary>
    private Func<Task> NeedsRow(string label, Func<Task> work) => () =>
    {
        if (HasRows && SelectedRow is null)
        {
            Shell.Notify(PhoneLayout.Active
                ? $"« {label} » agit sur un élément : touchez « … » sur sa carte, ou appuyez longuement dessus."
                : $"Choisissez d'abord une ligne du tableau, puis « {label} ». Astuce : clic droit sur la ligne.", SoundKind.Error);
            return Task.CompletedTask;
        }
        return work();
    };

    /// <summary>Actions proposées au clic droit sur une ligne du tableau.</summary>
    public IReadOnlyList<ActionVm> RowActions => Actions.Where(a => a.Row).ToList();

    /// <summary>Actions de la page elle-même, proposées au clic droit hors des lignes.</summary>
    public IReadOnlyList<ActionVm> PageActions => Actions.Where(a => !a.Row).ToList();

    /// <summary>Action lancée par un double-clic sur une ligne : celle marquée « ouvrir », sinon la première action de ligne.</summary>
    public ActionVm? DefaultRowAction => Actions.FirstOrDefault(a => a.Default) ?? Actions.FirstOrDefault(a => a.Row);

    /// <summary>Recharge les données depuis le serveur, puis le confirme : la commande ne paraît jamais sans effet.</summary>
    protected void ActionRefresh() =>
        Actions.Add(new ActionVm("Actualiser", MaterialIconKind.Refresh, new AsyncCommand(async () =>
        {
            await RefreshAsync();
            Shell.Notify($"{Title} : données à jour.");
        })));

    /// <summary>Date d'un sélecteur de calendrier, ou aujourd'hui.</summary>
    protected static DateOnly ToDay(DateTime? value) =>
        value is null ? DateOnly.FromDateTime(DateTime.Today) : DateOnly.FromDateTime(value.Value);

    protected static DateTime? ToCalendar(DateOnly? day) =>
        day is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;

    /// <summary>Lit une liste de promotions de l'année courante, pour les filtres et les formulaires.</summary>
    protected async Task<List<PromotionDto>> PromotionsAsync() =>
        await Shell.Api.GetAsync<List<PromotionDto>>("api/etablissement/promotions");

    protected async Task<List<ChoiceItem>> PromotionChoicesAsync(bool withAll)
    {
        var promotions = await PromotionsAsync();
        var choices = promotions.Select(Choices.Promotion).ToList();
        if (withAll) choices.Insert(0, new ChoiceItem(string.Empty, "Toutes les promotions"));
        return choices;
    }
}

/// <summary>Listes de choix partagées par les écrans et les formulaires.</summary>
public static class Choices
{
    public static ChoiceItem Promotion(PromotionDto p) => new(p.Id.ToString(), $"{p.Code} · {p.Name}");

    public static ChoiceItem Student(StudentListItem s) =>
        new(s.EnrollmentId?.ToString() ?? string.Empty, $"{s.Matricule} · {Infrastructure.Format.FullName(s.LastName, s.MiddleName, s.FirstName)}");

    public static IReadOnlyList<ChoiceItem> Categories(IEnumerable<string> names) =>
        names.Select(n => new ChoiceItem(n, n)).ToList();
}
