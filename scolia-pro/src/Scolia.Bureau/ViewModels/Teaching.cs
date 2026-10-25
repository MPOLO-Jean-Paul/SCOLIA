using System.Collections.ObjectModel;
using System.Net.Http;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Cours dans la page d'attribution : numéro, volume, enseignant et état.</summary>
public sealed record TeachingCourseRowVm(Guid Id, string Promotion, string Number, string Name, string Hours, string MaxScore, string Teacher,
    string Status, bool IsActive, bool IsAssigned, CourseDto Source);

/// <summary>Charge pédagogique prévue d'un enseignant.</summary>
public sealed record TeacherChargeRowVm(Guid TeacherId, string Name, string Courses, string Promotions, string Hours);

/// <summary>
/// Attributions et charges des enseignants (V79 : Attribution des cours, Charges des enseignants).
/// Un cours a une seule attribution active ; réattribuer et retirer gardent l'historique.
/// </summary>
public sealed class TeachingPage : PageViewModel
{
    private ChoiceItem? promotion;
    private TeachingCourseRowVm? selected;
    private bool showArchived;
    private bool ready;
    private string subtitle = string.Empty;

    public TeachingPage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.AcademicsWrite))
        {
            Action("Nouveau cours", MaterialIconKind.FileDocumentPlusOutline, () => GradesPage.NewCourseAsync(Shell, PromotionId));
            Action("Modifier le cours", MaterialIconKind.PencilOutline, EditCourseAsync, open: true);
            Action("Attribuer", MaterialIconKind.AccountArrowRightOutline, AssignAsync, primary: true, row: true);
            Action("Retirer l'attribution", MaterialIconKind.AccountMinusOutline, UnassignAsync, row: true);
            Action("Archiver ou réactiver", MaterialIconKind.ArchiveOutline, ToggleArchiveAsync, row: true);
        }
        Action("Historique des attributions", MaterialIconKind.History, ShowHistoryAsync);
        ActionRefresh();
    }

    public override string Title => "Attributions et charges";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.AccountSchoolOutline;

    public ObservableCollection<TeachingCourseRowVm> Courses { get; } = [];
    public ObservableCollection<TeacherChargeRowVm> Charges { get; } = [];
    public IReadOnlyList<ChoiceItem> Promotions { get; private set; } = [];

    public ChoiceItem? Promotion { get => promotion; set { if (Set(ref promotion, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public bool ShowArchived { get => showArchived; set { if (Set(ref showArchived, value) && ready) _ = Shell.Run(RefreshAsync); } }
    public TeachingCourseRowVm? Selected { get => selected; set => Set(ref selected, value); }

    private Guid? PromotionId => Promotion?.Value is { Length: > 0 } v ? Guid.Parse(v) : null;

    public override async Task LoadAsync()
    {
        if (!ready)
        {
            Promotions = await PromotionChoicesAsync(withAll: false);
            promotion = Promotions.Count > 0 ? Promotions[0] : null;
            Raise(nameof(Promotions));
            Raise(nameof(Promotion));
            ready = true;
        }

        Courses.Clear();
        if (PromotionId is { } pid)
        {
            var courses = await Shell.Api.GetAsync<List<CourseDto>>("api/scolarite/cours" + ScoliaClient.Query(
                ("promotionId", pid), ("archives", ShowArchived ? "true" : null)));
            foreach (var c in courses)
                Courses.Add(new TeachingCourseRowVm(c.Id, c.PromotionCode, c.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Name,
                    $"{c.Hours:0.##} h", c.MaxScore.ToString("0.##"), c.TeacherName ?? "Non attribué", c.IsActive ? "Actif" : "Archivé",
                    c.IsActive, c.TeacherId is not null, c));
            var unassigned = courses.Count(c => c.IsActive && c.TeacherId is null);
            subtitle = $"{Format.Count(courses.Count, "cours", "cours")} · {unassigned} sans enseignant · {Promotion?.Label}";
        }
        else subtitle = "Aucune promotion";
        Raise(nameof(Subtitle));

        Charges.Clear();
        var charges = await Shell.Api.GetAsync<List<TeacherChargeDto>>("api/scolarite/enseignants/charges");
        foreach (var t in charges)
            Charges.Add(new TeacherChargeRowVm(t.TeacherId, t.Name, t.Courses.ToString(System.Globalization.CultureInfo.InvariantCulture),
                t.Promotions.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{t.Hours:0.##} h"));
        Selected = null;
    }

    private async Task EditCourseAsync()
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez un cours à modifier.");
            return;
        }
        var c = row.Source;
        var form = new FormViewModel("Modifier le cours", $"Promotion {c.PromotionCode}. La promotion d'un cours enregistré est fixe.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/scolarite/cours/{row.Id}", new
            {
                promotionId = c.PromotionId, number = (int)f.Amount("numero"), name = f.Text("intitule"), coefficient = f.Amount("coefficient"),
                hours = f.Amount("heures"), maxScore = f.Amount("cotation"),
            });
            Shell.Notify("Cours modifié. Les charges sont actualisées.");
            return true;
        });
        form.Add("numero", "Numéro", FieldKind.Amount, c.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), required: true);
        form.Add("intitule", "Intitulé", value: c.Name, required: true);
        form.Add("coefficient", "Coefficient", FieldKind.Amount, c.Coefficient.ToString("0.##"), required: true);
        form.Add("heures", "Volume horaire annuel", FieldKind.Amount, c.Hours.ToString("0.##"), required: true);
        form.Add("cotation", "Cotation maximale (points)", FieldKind.Amount, c.MaxScore.ToString("0.##"), required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task AssignAsync()
    {
        if (Selected is not { IsActive: true } row)
        {
            Shell.Notify("Sélectionnez un cours actif à attribuer.");
            return;
        }
        var teachers = await Shell.Api.GetAsync<List<TeacherDto>>("api/enseignants");
        var form = new FormViewModel($"Attribuer : {row.Name}", $"Promotion {row.Promotion}, cours {row.Number}. Seuls les enseignants en activité sur l'année sont proposés.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Put, $"api/scolarite/cours/{row.Id}/attribution", new
            {
                teacherId = Guid.Parse(f.ChoiceValue("enseignant")!),
                confirmReplace = row.IsAssigned && f.Flag("confirme"),
            });
            Shell.Notify("Attribution enregistrée.");
            return true;
        });
        form.AddChoice("enseignant", "Enseignant", teachers.Select(t => new ChoiceItem(t.Id.ToString(), $"{t.Nom} · {t.Function}")).ToList(), null, required: true);
        if (row.IsAssigned)
            form.AddFlag("confirme", $"Je confirme la réattribution (actuellement : {row.Teacher})", false);
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task UnassignAsync()
    {
        if (Selected is not { IsAssigned: true } row)
        {
            Shell.Notify("Ce cours n'a pas d'enseignant attribué.");
            return;
        }
        var form = new FormViewModel("Retirer l'attribution", $"{row.Name} · {row.Teacher}. L'historique est conservé et le cours redevient disponible.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/scolarite/cours/{row.Id}/retrait-attribution", new { reason = f.OptionalText("motif") });
            Shell.Notify("Attribution retirée.");
            return true;
        });
        form.Add("motif", "Motif (facultatif)");
        if (await Shell.Ask(form)) await RefreshAsync();
    }

    private async Task ToggleArchiveAsync()
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez un cours.");
            return;
        }
        var archive = row.IsActive;
        await Shell.Api.SendAsync(HttpMethod.Post, $"api/scolarite/cours/{row.Id}/{(archive ? "archivage" : "reactivation")}");
        Shell.Notify(archive ? "Cours archivé. Son historique reste consultable." : "Cours réactivé.");
        await RefreshAsync();
    }

    private async Task ShowHistoryAsync()
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez un cours pour voir ses attributions.");
            return;
        }
        var history = await Shell.Api.GetAsync<List<AssignmentHistoryDto>>($"api/scolarite/cours/{row.Id}/attributions");
        var lines = history.Select(h => $"{Format.Date(h.StartedOn)} au {(h.EndedOn is { } end ? Format.Date(end) : "aujourd'hui")} · {h.TeacherName}"
            + (h.EndReason is null ? string.Empty : $" · {h.EndReason}") + (h.IsCurrent ? " · en cours" : string.Empty));
        var form = new FormViewModel($"Historique : {row.Name}", Format.Count(history.Count, "attribution", "attributions"), _ => Task.FromResult(true))
        {
            Message = history.Count == 0 ? "Ce cours n'a jamais été attribué." : string.Join(Environment.NewLine, lines),
            SubmitText = "Fermer",
            ShowCancel = false,
        };
        await Shell.Ask(form);
    }
}
