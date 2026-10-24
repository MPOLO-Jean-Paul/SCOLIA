using System.Collections.ObjectModel;
using System.Net.Http;
using Avalonia.Threading;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Valeurs de portée attendues par le serveur : les apprenants cochés, ou tous.</summary>
public static class ErasureScope
{
    public const string Selection = "selection";
    public const string All = "tous";
}

/// <summary>Apprenant proposé à l'effacement, avec sa case et son solde.</summary>
public sealed class EraseRowVm : Observable
{
    private readonly Action<EraseRowVm> changed;
    private bool selected;

    public EraseRowVm(StudentListItem student, string balance, Action<EraseRowVm> changed)
    {
        Student = student;
        Balance = balance;
        this.changed = changed;
    }

    public StudentListItem Student { get; }
    public string Matricule => Student.Matricule;
    public string Name => Format.FullName(Student.LastName, Student.MiddleName, Student.FirstName);
    public string Promotion => Student.PromotionCode ?? "—";
    public string Status => Labels.Status(Student.Status ?? string.Empty);
    public string Balance { get; }

    public bool Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) changed(this); }
    }
}

/// <summary>
/// Effacement volontaire, depuis Données : les paiements, ou les dossiers des apprenants. Portée : les apprenants
/// cochés, ou tous. Le mot de passe du compte connecté confirme ; le serveur fait une sauvegarde avant d'effacer.
/// </summary>
public sealed class EraseDataPage : PageViewModel
{
    private readonly bool payments;
    private readonly HashSet<Guid> chosen = [];
    private string search = string.Empty;
    private ChoiceItem? scope;
    private string password = string.Empty;
    private string selectionSummary = string.Empty;
    private string subtitle = string.Empty;
    private DispatcherTimer? timer;

    public EraseDataPage(ShellViewModel shell, bool payments) : base(shell)
    {
        this.payments = payments;
        scope = Scopes[0];
        if (Shell.Can(Perm.BackupManage))
            Action(payments ? "Effacer définitivement les paiements" : "Effacer définitivement les apprenants", MaterialIconKind.DeleteForeverOutline, EraseAsync, primary: true, danger: true);
        Action("Tout cocher", MaterialIconKind.CheckboxMultipleMarkedOutline, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxMultipleBlankOutline, () => { SetAll(false); return Task.CompletedTask; });
        ActionRefresh();
    }

    public override string Title => payments ? "Effacer les paiements" : "Effacer tous les apprenants";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.DeleteSweepOutline;

    public ObservableCollection<EraseRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Scopes { get; } =
    [
        new(ErasureScope.Selection, "Apprenants cochés"),
        new(ErasureScope.All, "Tous les apprenants"),
    ];

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

    public ChoiceItem? Scope
    {
        get => scope;
        set
        {
            if (!Set(ref scope, value)) return;
            Raise(nameof(IsSelectionScope));
            SelectionChanged();
        }
    }

    public bool IsSelectionScope => Scope?.Value == ErasureScope.Selection;

    /// <summary>Mot de passe du compte connecté : c'est lui qui confirme l'effacement.</summary>
    public string Password { get => password; set => Set(ref password, value); }
    public string SelectionSummary { get => selectionSummary; private set => Set(ref selectionSummary, value); }

    public override async Task LoadAsync()
    {
        var page = await Shell.Api.GetAsync<PagedResult<StudentListItem>>("api/apprenants" + ScoliaClient.Query(("q", Search.Trim()), ("taille", 500)));
        Items.Clear();
        foreach (var student in page.Items)
        {
            var row = new EraseRowVm(student, Money(student.Balance), RowChanged);
            row.Selected = chosen.Contains(student.Id);
            Items.Add(row);
        }
        subtitle = payments
            ? "Efface les reçus et les paiements des apprenants choisis. Les dossiers restent, et chaque solde revient à son montant dû."
            : "Efface les dossiers des apprenants choisis, avec leurs inscriptions, reçus, notes, présences, discipline et prises en charge.";
        if (page.Items.Count < page.Total) subtitle += $" Liste limitée à {page.Items.Count} apprenant(s) sur {page.Total} : utilisez la recherche.";
        Raise(nameof(Subtitle));
        SelectionChanged();
    }

    private void RowChanged(EraseRowVm row)
    {
        if (row.Selected) chosen.Add(row.Student.Id);
        else chosen.Remove(row.Student.Id);
        SelectionChanged();
    }

    private void SetAll(bool value)
    {
        foreach (var row in Items) row.Selected = value;
    }

    private void SelectionChanged()
    {
        SelectionSummary = !IsSelectionScope ? "Portée : tous les apprenants"
            : chosen.Count == 0 ? "Aucun apprenant coché"
            : Format.Count(chosen.Count, "apprenant coché", "apprenants cochés");
    }

    private async Task EraseAsync()
    {
        var all = !IsSelectionScope;
        if (!all && chosen.Count == 0)
        {
            Shell.Notify("Cochez au moins un apprenant, ou choisissez « Tous les apprenants ».");
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            Shell.Notify("Saisissez le mot de passe de votre compte pour confirmer.");
            return;
        }

        var body = new { scope = all ? ErasureScope.All : ErasureScope.Selection, studentIds = all ? null : chosen.ToList(), password = Password };
        var summary = await Shell.Api.SendAsync<ErasureSummaryDto>(HttpMethod.Post, payments ? "api/effacement/paiements" : "api/effacement/apprenants", body);
        Password = string.Empty;
        chosen.Clear();
        Shell.Notify(payments
            ? $"{Format.Count(summary.Receipts, "reçu effacé", "reçus effacés")} · {summary.Allocations} imputation(s). Une sauvegarde a été faite avant."
            : $"{Format.Count(summary.Students, "dossier effacé", "dossiers effacés")} · {summary.Enrollments} inscription(s). Une sauvegarde a été faite avant.");
        await RefreshAsync();
    }
}
