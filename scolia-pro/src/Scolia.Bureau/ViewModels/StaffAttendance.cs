using System.Collections.ObjectModel;
using System.Net.Http;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Membre du personnel avec sa journée du jour.</summary>
public sealed record StaffAttendanceRowVm(Guid Id, string Name, string Function, string Entry, string Exit, string Today);

/// <summary>Bilan d'un membre : ce qui sert à préparer la paie.</summary>
public sealed record StaffBilanRowVm(string Name, string Present, string Late, string Absent, string Justified, string Conversions, string Deductible);

/// <summary>
/// Pointage du personnel (version 79) : signer l'entrée et la sortie, justifier une absence, et lire le bilan
/// de la période. Une entrée strictement après 08:00:00 est signalée en retard.
/// </summary>
public sealed class StaffAttendancePage : PageViewModel
{
    private StaffAttendanceRowVm? selected;
    private bool previousMonth;
    private string subtitle = string.Empty;

    public StaffAttendancePage(ShellViewModel shell) : base(shell)
    {
        if (Shell.Can(Perm.StaffManage))
        {
            Action("Entrée", MaterialIconKind.LoginVariant, () => MarkAsync("Entree"), primary: true, row: true);
            Action("Sortie", MaterialIconKind.LogoutVariant, () => MarkAsync("Sortie"), row: true);
            Action("Justifier une absence", MaterialIconKind.FileCheck, JustifyAsync, row: true);
        }
        Action("Mois précédent ou en cours", MaterialIconKind.CalendarRange, async () =>
        {
            previousMonth = !previousMonth;
            await RefreshAsync();
            Shell.Notify(previousMonth ? "Pointage du mois précédent affiché." : "Pointage du mois en cours affiché.");
        });
        ActionRefresh();
    }

    public override string Title => "Pointage du personnel";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ClockCheckOutline;

    public ObservableCollection<StaffAttendanceRowVm> Members { get; } = [];
    public ObservableCollection<StaffBilanRowVm> Bilan { get; } = [];
    public StaffAttendanceRowVm? Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>
    /// Journée de chaque membre en une seule requête. Un ordinateur principal pas encore mis à jour ne la connaît pas :
    /// on revient alors à une requête par membre, six à la fois.
    /// </summary>
    private async Task<Dictionary<Guid, StaffDayDto?>> DaysOfAllAsync(IReadOnlyList<StaffDto> staff, DateOnly today)
    {
        try
        {
            var all = await Shell.Api.GetAsync<List<StaffTodayDto>>("api/personnel/pointage/jour" + ScoliaClient.Query(("date", today)));
            return all.ToDictionary(d => d.StaffId, d => d.Day);
        }
        catch (ApiException ex) when (ex.Status is 404 or 405) { }
        using var gate = new SemaphoreSlim(6);
        var pairs = await Task.WhenAll(staff.Select(async s =>
        {
            await gate.WaitAsync();
            try
            {
                var list = await Shell.Api.GetAsync<List<StaffDayDto>>($"api/personnel/{s.Id}/pointage" + ScoliaClient.Query(("du", today), ("au", today)));
                return (s.Id, Day: list.FirstOrDefault());
            }
            finally { gate.Release(); }
        }));
        return pairs.ToDictionary(p => p.Id, p => p.Day);
    }

    public override async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = PeriodOf(today);
        // Fluidité : le pointage du jour de chaque membre et le bilan partent ensemble, au lieu d'une requête après l'autre.
        var staff = (await Shell.Api.GetAsync<List<StaffDto>>("api/personnel")).Where(s => s.IsActive).ToList();
        var bilanTask = Shell.Api.GetAsync<List<StaffBilanDto>>("api/personnel/pointage/bilan" + ScoliaClient.Query(("du", from), ("au", to)));
        var days = await DaysOfAllAsync(staff, today);
        await bilanTask;

        var members = new List<StaffAttendanceRowVm>();
        for (var i = 0; i < staff.Count; i++)
        {
            var s = staff[i];
            var day = days.GetValueOrDefault(s.Id);
            var name = string.Join(' ', new[] { s.LastName, s.MiddleName, s.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            members.Add(new StaffAttendanceRowVm(s.Id, name, s.Function,
                day?.Entry is { } entry ? entry + (day.Late ? " (retard)" : string.Empty) : "—", day?.Exit ?? "—", day?.Status ?? "—"));
        }
        Members.Clear();
        foreach (var member in members) Members.Add(member);

        Bilan.Clear();
        foreach (var b in bilanTask.Result)
            Bilan.Add(new StaffBilanRowVm(b.Name, b.Present.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Late.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.Absent.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Justified.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.Conversions.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Deductible.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        subtitle = $"{Format.Count(Members.Count, "membre actif", "membres actifs")} · du {Format.Date(from)} au {Format.Date(to)}";
        Raise(nameof(Subtitle));
        Selected = null;
    }

    private (DateOnly From, DateOnly To) PeriodOf(DateOnly today)
    {
        if (!previousMonth) return (new DateOnly(today.Year, today.Month, 1), today);
        var firstOfPrevious = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return (firstOfPrevious, firstOfPrevious.AddMonths(1).AddDays(-1));
    }

    private async Task MarkAsync(string kind)
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez un membre du personnel.");
            return;
        }
        var mark = await Shell.Api.SendAsync<StaffMarkDto>(HttpMethod.Post, $"api/personnel/{row.Id}/signature/{kind}");
        Shell.Notify($"{(kind == "Entree" ? "Entrée" : "Sortie")} de {row.Name} signée à {mark.Time}{(mark.IsLate ? " : en retard" : string.Empty)}.");
        await RefreshAsync();
    }

    private async Task JustifyAsync()
    {
        if (Selected is not { } row)
        {
            Shell.Notify("Sélectionnez un membre du personnel.");
            return;
        }
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        var form = new FormViewModel($"Justifier une absence : {row.Name}", "Le motif est obligatoire. L'annulation garde la trace de la justification.", async f =>
        {
            await Shell.Api.SendAsync(HttpMethod.Post, $"api/personnel/{row.Id}/justifications", new { date = f.Day("date") ?? yesterday, reason = f.Text("motif") });
            Shell.Notify("Journée justifiée.");
            return true;
        });
        form.AddDay("date", "Date de l'absence", yesterday, required: true);
        form.Add("motif", "Motif", FieldKind.Multiline, required: true);
        if (await Shell.Ask(form)) await RefreshAsync();
    }
}
