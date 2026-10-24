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
/// de la période. Une entrée à 08:00:00 ou après est signalée en retard.
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
            Action("Entrée", MaterialIconKind.LoginVariant, () => MarkAsync("Entree"), primary: true);
            Action("Sortie", MaterialIconKind.LogoutVariant, () => MarkAsync("Sortie"));
            Action("Justifier une absence", MaterialIconKind.FileCheckOutline, JustifyAsync);
        }
        Action("Mois précédent ou en cours", MaterialIconKind.CalendarRangeOutline, () => { previousMonth = !previousMonth; return RefreshAsync(); });
        ActionRefresh();
    }

    public override string Title => "Pointage du personnel";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ClockCheckOutline;

    public ObservableCollection<StaffAttendanceRowVm> Members { get; } = [];
    public ObservableCollection<StaffBilanRowVm> Bilan { get; } = [];
    public StaffAttendanceRowVm? Selected { get => selected; set => Set(ref selected, value); }

    public override async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (from, to) = PeriodOf(today);
        var staff = await Shell.Api.GetAsync<List<StaffDto>>("api/personnel");
        Members.Clear();
        foreach (var s in staff.Where(s => s.IsActive))
        {
            var days = await Shell.Api.GetAsync<List<StaffDayDto>>($"api/personnel/{s.Id}/pointage" + ScoliaClient.Query(("du", today), ("au", today)));
            var day = days.FirstOrDefault();
            var name = string.Join(' ', new[] { s.LastName, s.MiddleName, s.FirstName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            Members.Add(new StaffAttendanceRowVm(s.Id, name, s.Function,
                day?.Entry is { } entry ? entry + (day.Late ? " (retard)" : string.Empty) : "—", day?.Exit ?? "—", day?.Status ?? "—"));
        }

        Bilan.Clear();
        foreach (var b in await Shell.Api.GetAsync<List<StaffBilanDto>>("api/personnel/pointage/bilan" + ScoliaClient.Query(("du", from), ("au", to))))
            Bilan.Add(new StaffBilanRowVm(b.Name, b.Present.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Late.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.Absent.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Justified.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.Conversions.ToString(System.Globalization.CultureInfo.InvariantCulture), b.Deductible.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        subtitle = $"{Format.Count(Members.Count, "membre actif", "membres actifs")} · bilan du {Format.Date(from)} au {Format.Date(to)}";
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
