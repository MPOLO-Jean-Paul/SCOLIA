using System.Collections.ObjectModel;
using System.Windows.Input;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Bouton d'en-tête de page : Nouveau, Actualiser, Exporter.</summary>
public sealed record ActionVm(string Label, MaterialIconKind Icon, ICommand Command, bool Primary = false, bool Danger = false)
{
    /// <summary>Bouton bleu : action principale qui n'est pas destructrice.</summary>
    public bool IsPrimary => Primary && !Danger;
}

/// <summary>Écran de l'application. Chaque écran charge ses données et publie ses actions dans l'en-tête.</summary>
public abstract class PageViewModel : Observable
{
    /// <summary>Boutons visibles au plus dans l'en-tête : au-delà, les actions passent dans le menu « Plus ».</summary>
    private const int MaxVisible = 3;
    /// <summary>Nombre de boutons fixé par la largeur de la fenêtre ; -1 tant qu'elle ne l'a pas mesuré.</summary>
    private int visibleCount = -1;
    private bool busy;

    protected PageViewModel(ShellViewModel shell)
    {
        Shell = shell;
        Actions.CollectionChanged += (_, _) =>
        {
            Raise(nameof(VisibleActions));
            Raise(nameof(MoreActions));
            Raise(nameof(HasMoreActions));
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
    public IReadOnlyList<ActionVm> VisibleActions => Actions.Take(Shown).ToList();
    /// <summary>Les autres actions, rangées dans le menu « Plus » pour garder un en-tête net.</summary>
    public IReadOnlyList<ActionVm> MoreActions => Actions.Skip(Shown).ToList();
    public bool HasMoreActions => Shown < Actions.Count;

    /// <summary>Nombre de boutons affichés : la place mesurée, ou par défaut trois boutons et le menu pour les autres.</summary>
    private int Shown => visibleCount >= 0 ? Math.Min(visibleCount, Actions.Count)
        : Actions.Count <= MaxVisible + 1 ? Actions.Count : MaxVisible;

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

    /// <summary>Charge les données depuis le serveur. Chaque écran le redéfinit.</summary>
    public virtual Task LoadAsync() => Task.CompletedTask;

    /// <summary>Recharge l'écran et signale les erreurs sans interrompre l'interface.</summary>
    public async Task RefreshAsync()
    {
        Busy = true;
        try { await LoadAsync(); }
        catch (ApiException ex) { Shell.Notify(ex.Message); }
        catch (HttpRequestException) { Shell.Notify("Le serveur SCOLIA ne répond pas."); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Shell.Notify($"Erreur inattendue : {ex.Message}"); }
        finally { Busy = false; }
    }

    /// <summary>Imprime le document affiché, quand l'écran en a un.</summary>
    public virtual Task PrintAsync()
    {
        Shell.Notify("Rien à imprimer sur cette page.");
        return Task.CompletedTask;
    }

    protected string Currency => Shell.Session?.CurrencySymbol ?? string.Empty;

    protected string Money(decimal value) => Format.Money(value, Currency);

    protected void Action(string label, MaterialIconKind icon, Func<Task> work, bool primary = false, bool danger = false) =>
        Actions.Add(new ActionVm(label, icon, new AsyncCommand(() => Shell.Run(work)), primary, danger));

    protected void ActionRefresh() =>
        Actions.Add(new ActionVm("Actualiser", MaterialIconKind.Refresh, new AsyncCommand(RefreshAsync)));

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
