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
/// Effacement volontaire, depuis Données, en trois étapes : choisir quoi effacer (les paiements, ou les dossiers),
/// choisir les apprenants (cochés, ou tous), puis confirmer avec le mot de passe du compte connecté.
/// Un récapitulatif demande la confirmation ; le serveur fait une sauvegarde avant d'effacer.
/// </summary>
public sealed class EraseDataPage : PageViewModel
{
    private readonly HashSet<Guid> chosen = [];
    private bool payments;
    private string search = string.Empty;
    private ChoiceItem? scope;
    private string password = string.Empty;
    private string selectionSummary = string.Empty;
    private string subtitle = string.Empty;
    private int total;
    private DispatcherTimer? timer;

    public EraseDataPage(ShellViewModel shell, bool payments) : base(shell)
    {
        this.payments = payments;
        scope = Scopes[0];
        Erase = new AsyncCommand(() => Shell.Run(EraseAsync));
        RebuildActions();
    }

    private void RebuildActions()
    {
        Actions.Clear();
        if (Shell.Can(Perm.BackupManage))
            Action(payments ? "Effacer les paiements" : "Effacer les dossiers", MaterialIconKind.DeleteForeverOutline, EraseAsync, primary: true, danger: true);
        Action("Tout cocher", MaterialIconKind.CheckboxMultipleMarkedOutline, () => { SetAll(true); return Task.CompletedTask; });
        Action("Tout décocher", MaterialIconKind.CheckboxMultipleBlankOutline, () => { SetAll(false); return Task.CompletedTask; });
        ActionRefresh();
    }

    public override string Title => "Effacement des données";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.DeleteSweepOutline;

    /// <summary>Étape 1 : effacer les paiements (les dossiers restent) ou les dossiers entiers.</summary>
    public bool ErasePayments
    {
        get => payments;
        set
        {
            if (!value || payments) return;
            payments = true;
            KindChanged();
        }
    }

    public bool EraseStudents
    {
        get => !payments;
        set
        {
            if (!value || !payments) return;
            payments = false;
            KindChanged();
        }
    }

    private void KindChanged()
    {
        Raise(nameof(ErasePayments)); Raise(nameof(EraseStudents)); Raise(nameof(KindHint)); Raise(nameof(EraseText));
        RebuildActions();
        SelectionChanged();
    }

    public string KindHint => payments
        ? "Les reçus et les paiements des apprenants choisis sont effacés. Les dossiers restent, et chaque solde revient à son montant dû."
        : "Les dossiers des apprenants choisis sont effacés, avec leurs inscriptions, reçus, notes, présences, discipline et prises en charge.";

    public string EraseText => payments ? "Effacer les paiements" : "Effacer les dossiers";
    public bool CanErase => Shell.Can(Perm.BackupManage);
    public AsyncCommand Erase { get; }

    public ObservableCollection<EraseRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Scopes { get; } =
    [
        new(ErasureScope.Selection, "Les apprenants cochés"),
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
            Raise(nameof(ScopeAll));
            Raise(nameof(ScopeSelection));
            SelectionChanged();
        }
    }

    public bool ScopeSelection { get => IsSelectionScope; set { if (value) Scope = Scopes[0]; } }
    public bool ScopeAll { get => !IsSelectionScope; set { if (value) Scope = Scopes[1]; } }
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
        total = page.Total;
        subtitle = "Une sauvegarde est faite avant chaque effacement : il se rattrape depuis Sauvegardes.";
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
        if (value && !IsSelectionScope) Scope = Scopes[0];
        foreach (var row in Items) row.Selected = value;
        Shell.Notify(value ? $"{Format.Count(Items.Count, "apprenant coché", "apprenants cochés")}." : "Aucun apprenant coché.");
    }

    private void SelectionChanged()
    {
        SelectionSummary = !IsSelectionScope ? $"Tous les apprenants seront concernés{(total > 0 ? $" ({Format.Count(total, "dossier", "dossiers")})" : string.Empty)}."
            : chosen.Count == 0 ? "Aucun apprenant coché : cochez les lignes concernées, ou choisissez « Tous les apprenants »."
            : $"{Format.Count(chosen.Count, "apprenant coché", "apprenants cochés")}.";
    }

    private async Task EraseAsync()
    {
        var all = !IsSelectionScope;
        if (!all && chosen.Count == 0)
        {
            Shell.Notify("Cochez au moins un apprenant, ou choisissez « Tous les apprenants ».", SoundKind.Error);
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            Shell.Notify("Saisissez le mot de passe de votre compte pour confirmer.", SoundKind.Error);
            return;
        }
        var who = all ? "de tous les apprenants" : $"de {Format.Count(chosen.Count, "apprenant coché", "apprenants cochés")}";
        var what = payments ? $"Les reçus et paiements {who} seront effacés. Les dossiers restent." : $"Les dossiers {who} seront effacés, avec tout leur historique.";
        if (!await Shell.Confirm(payments ? "Effacer les paiements ?" : "Effacer les dossiers ?",
                $"{what}{Environment.NewLine}Une sauvegarde est faite juste avant : l'effacement se rattrape depuis Sauvegardes.", EraseText, danger: true))
            return;

        var body = new { scope = all ? ErasureScope.All : ErasureScope.Selection, studentIds = all ? null : chosen.ToList(), password = Password };
        var summary = await Shell.Api.SendAsync<ErasureSummaryDto>(HttpMethod.Post, payments ? "api/effacement/paiements" : "api/effacement/apprenants", body);
        Password = string.Empty;
        chosen.Clear();
        Shell.Notify(payments
            ? $"{Format.Count(summary.Receipts, "reçu effacé", "reçus effacés")} · {summary.Allocations} imputation(s). Une sauvegarde a été faite avant."
            : $"{Format.Count(summary.Students, "dossier effacé", "dossiers effacés")} · {summary.Enrollments} inscription(s). Une sauvegarde a été faite avant.",
            SoundKind.Success);
        await RefreshAsync();
    }
}
