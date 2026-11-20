using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

public sealed record StudentAccountRowVm(StudentAccountDto Dto, string Access, IBrush AccessBrush, string LastLogin);

/// <summary>Secrétariat d'une université : accès des étudiants à leurs résultats publiés.</summary>
public sealed class StudentAccountsPage : PageViewModel
{
    private ChoiceItem? promotion;
    private StudentAccountRowVm? selected;
    private string subtitle = string.Empty;
    private bool ready;

    public StudentAccountsPage(ShellViewModel shell) : base(shell)
    {
        Action("Créer les accès de la promotion", MaterialIconKind.AccountMultiplePlusOutline, IssuePromotionAsync, primary: true);
        Action("Créer l'accès", MaterialIconKind.AccountPlusOutline, IssueAsync, row: true, open: true);
        Action("Nouveau code provisoire", MaterialIconKind.KeyChange, ResetAsync, row: true);
        Action("Activer ou désactiver", MaterialIconKind.AccountCancelOutline, ToggleAsync, row: true, danger: true);
        ActionRefresh();
    }

    public override string Title => "Accès étudiants";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountKeyOutline;
    public ObservableCollection<StudentAccountRowVm> Items { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];
    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public StudentAccountRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: true);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }
        var query = Promotion?.Value is { Length: > 0 } p ? $"?promotionId={p}" : string.Empty;
        var list = await Shell.Api.GetAsync<List<StudentAccountDto>>("api/comptes-etudiants" + query);
        Items.Clear();
        foreach (var a in list)
            Items.Add(new StudentAccountRowVm(a,
                a.UserName is null ? "Aucun accès" : !a.Active ? $"{a.UserName} · désactivé" : a.MustChangePassword ? $"{a.UserName} · code provisoire" : a.UserName,
                ThemeBrush.Get(a.UserName is null ? "ToneNeutral" : !a.Active ? "ToneRed" : a.MustChangePassword ? "ToneOrange" : "ToneGreen"),
                a.LastLoginUtc is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : "—"));
        subtitle = $"{list.Count(a => a.Active)} accès actif(s) sur {list.Count} étudiant(s)";
        Raise(nameof(Subtitle));
    }

    /// <summary>Codes à remettre en main propre : affichés une seule fois.</summary>
    private async Task ShowCodesAsync(IReadOnlyList<StudentCredentialsDto> codes)
    {
        if (codes.Count == 0) { Shell.Notify("Tous les étudiants de la sélection ont déjà un accès actif."); return; }
        var text = string.Join("\n", codes.Select(c => $"{c.Matricule} · {c.FullName} — identifiant {c.UserName}, code {c.ProvisionalCode}"));
        await Shell.Confirm("Codes provisoires à remettre",
            text + "\n\nCes codes ne seront plus affichés. Chaque étudiant choisit son propre code à la première connexion.", "J'ai noté les codes");
        await RefreshAsync();
    }

    private async Task IssuePromotionAsync()
    {
        if (Promotion?.Value is not { Length: > 0 } id) { Shell.Notify("Choisissez d'abord une promotion."); return; }
        if (!await Shell.Confirm("Créer les accès", $"Un accès sera créé pour chaque étudiant de {Promotion.Label} qui n'en a pas.", "Créer")) return;
        await ShowCodesAsync(await Shell.Api.SendAsync<List<StudentCredentialsDto>>(HttpMethod.Post, $"api/comptes-etudiants/promotions/{id}", new { }));
    }

    private async Task IssueAsync()
    {
        if (Selected?.Dto is not { } a) return;
        if (a is { Active: true }) { Shell.Notify("Cet étudiant a déjà un accès : utilisez « Nouveau code provisoire »."); return; }
        await ShowCodesAsync([await Shell.Api.SendAsync<StudentCredentialsDto>(HttpMethod.Post, $"api/comptes-etudiants/{a.StudentId}", new { })]);
    }

    private async Task ResetAsync()
    {
        if (Selected?.Dto is not { UserId: { } userId, Active: true }) { Shell.Notify("Cet étudiant n'a pas d'accès actif."); return; }
        await ShowCodesAsync([await Shell.Api.SendAsync<StudentCredentialsDto>(HttpMethod.Post, $"api/comptes-etudiants/acces/{userId}/code-provisoire", new { })]);
    }

    private async Task ToggleAsync()
    {
        if (Selected?.Dto is not { UserId: { } userId } a) { Shell.Notify("Cet étudiant n'a pas d'accès."); return; }
        if (!await Shell.Confirm(a.Active ? "Désactiver l'accès" : "Réactiver l'accès",
                a.Active ? $"{a.FullName} ne pourra plus consulter ses résultats. Sa session ouverte se ferme." : $"{a.FullName} retrouve l'accès à ses résultats.",
                a.Active ? "Désactiver" : "Réactiver", danger: a.Active)) return;
        await Shell.Api.SendAsync(HttpMethod.Put, $"api/comptes-etudiants/acces/{userId}/statut", new { active = !a.Active });
        await RefreshAsync();
    }
}

public sealed record StudentUeRowVm(string Semester, string Code, string Name, string Credits, string Average, string Status,
    IBrush StatusBrush, string Elements);

/// <summary>Étudiant : ses résultats de l'étape la plus avancée publiée par le jury.</summary>
public sealed class StudentResultsPage : PageViewModel
{
    private ChoiceItem? year;
    private string subtitle = string.Empty;
    private string summary = string.Empty;
    private string decision = string.Empty;
    private string notice = string.Empty;
    private StudentPortalDto? portal;
    private StudentUeRowVm? selected;

    public StudentResultsPage(ShellViewModel shell) : base(shell)
    {
        Action("Mon relevé (PDF)", MaterialIconKind.FilePdfBox, TranscriptAsync, primary: true);
        ActionRefresh();
    }

    public override string Title => "Mes résultats";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.Certificate;
    public ObservableCollection<StudentUeRowVm> Items { get; } = [];
    public ObservableCollection<string> Debts { get; } = [];
    public IReadOnlyList<ChoiceItem> Years { get; private set; } = [];
    public ChoiceItem? Year { get => year; set { if (Set(ref year, value)) Show(); } }
    public StudentUeRowVm? Selected { get => selected; set => Set(ref selected, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string Decision { get => decision; private set { if (Set(ref decision, value)) Raise(nameof(HasDecision)); } }
    public bool HasDecision => !string.IsNullOrEmpty(decision);
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(notice);
    public bool HasDebts => Debts.Count > 0;

    public override async Task LoadAsync()
    {
        portal = await Shell.Api.GetAsync<StudentPortalDto>("api/etudiant/mes-resultats");
        Years = portal.Results.Select(r => new ChoiceItem(r.EnrollmentId.ToString(), $"{r.YearLabel} · {r.PromotionName}")).ToList();
        Raise(nameof(Years));
        year = Years.Count > 0 ? Years[0] : null;
        Raise(nameof(Year));
        Debts.Clear();
        foreach (var d in portal.Debts)
            Debts.Add($"{d.UeCode} · {d.UeName} ({ProfileFormat.N(d.Credits)} crédits, {d.OriginYear}) — " +
                (d.ClearedAtUtc is null ? "à apurer" : $"apurée avec {ProfileFormat.N(d.ClearedMark)}"));
        Raise(nameof(HasDebts));
        Show();
    }

    private void Show()
    {
        Items.Clear();
        var r = portal?.Results.FirstOrDefault(x => x.EnrollmentId.ToString() == Year?.Value);
        if (portal is null || r is null)
        {
            Notice = portal?.Notice ?? "Aucun résultat publié.";
            Summary = string.Empty;
            Decision = string.Empty;
            subtitle = portal is null ? string.Empty : $"{portal.FullName} · {portal.Matricule}";
            Raise(nameof(Subtitle));
            return;
        }
        Notice = r.Stage == "S1" ? "Résultats partiels : la décision de l'année est prise après la session du deuxième semestre." : string.Empty;
        foreach (var u in r.Result.Units)
            Items.Add(new StudentUeRowVm(u.Semester, u.Code, u.Name, ProfileFormat.N(u.Credits),
                u.Average is { } a ? $"{ProfileFormat.N(a)}/{ProfileFormat.N(r.MarkScale)}" : "—",
                u.Average is null ? "Non évaluée" : u.Acquired ? (u.ByCompensation ? "Acquise par compensation" : "Acquise") : "Non acquise",
                ThemeBrush.Get(u.Average is null ? "ToneNeutral" : u.Acquired ? "ToneGreen" : "ToneRed"),
                string.Join(" · ", u.Elements.Select(e => $"{e.Name} : {(e.Mark is { } m ? ProfileFormat.N(m) : "—")}" + (e.Session == 2 ? " (2e session)" : "")))));
        var res = r.Result;
        Summary = r.Stage == "S1"
            ? $"Premier semestre : moyenne {ProfileFormat.N(res.S1.Average)}/{ProfileFormat.N(r.MarkScale)} · {ProfileFormat.N(res.S1.CreditsAcquired)}/{ProfileFormat.N(res.S1.CreditsPlanned)} crédits" +
              (res.Rank is { } rk ? $" · rang {rk}/{r.Headcount}" : "")
            : $"Moyenne annuelle {ProfileFormat.N(res.Average)}/{ProfileFormat.N(r.MarkScale)} · {ProfileFormat.N(res.CreditsAcquired)}/{ProfileFormat.N(res.CreditsPlanned)} crédits" +
              (res.Rank is { } rank ? $" · rang {rank}/{r.Headcount}" : "");
        Decision = res.Decision is { } d ? "Décision du jury : " + d : string.Empty;
        subtitle = $"{r.StageLabel} · {r.PromotionName} ({r.Level}) · {r.YearLabel}" +
                   (r.PublishedAtUtc is { } at ? $" · publiés le {at.ToLocalTime():dd/MM/yyyy}" : "");
        Raise(nameof(Subtitle));
    }

    private Task TranscriptAsync() => Year?.Value is { Length: > 0 } id
        ? Shell.Download($"api/etudiant/releve.pdf?inscription={id}", "mon-releve.pdf")
        : Task.Run(() => Shell.Notify("Aucun résultat publié à imprimer."));
}
