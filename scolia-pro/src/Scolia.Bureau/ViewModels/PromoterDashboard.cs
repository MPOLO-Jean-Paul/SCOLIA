using System.Collections.ObjectModel;
using System.Globalization;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Totaux d'une promotion, sans informations identifiant les apprenants.</summary>
public sealed record PromoterPeriodRowVm(string Period, string Collected, string Due, string Paid, string Remaining);
public sealed record PromoterPromotionRowVm(string Code, string Name, string Students, string Debtors, string LateDebtors,
    string CollectedToday, string CollectedMonth, string CollectedYear, string Outstanding, string Overdue,
    IReadOnlyList<PromoterPeriodRowVm> Periods);
public sealed record PromoterKpiVm(string Label, string Value, string Detail);

/// <summary>
/// Pilotage réservé au Promoteur : aucun reçu, numéro de téléphone, nom d'apprenant,
/// bouton d'écriture ni chemin vers les dossiers individuels.
/// </summary>
public sealed class PromoterDashboardPage : PageViewModel
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private string subtitle = "Consultation uniquement";
    private string asOf = string.Empty;

    public PromoterDashboardPage(ShellViewModel shell) : base(shell)
    {
        ActionRefresh();
    }

    public override string Title => "Vue d'ensemble";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboardOutline;

    public string AsOf { get => asOf; private set => Set(ref asOf, value); }
    public ObservableCollection<PromoterKpiVm> Kpis { get; } = [];
    public ObservableCollection<PromoterPromotionRowVm> Promotions { get; } = [];

    public override async Task LoadAsync()
    {
        if (!Shell.Can(Ribbon.Perm.PilotageRead)) return;
        var data = await Shell.Api.GetAsync<PromoterDashboardDto>("api/pilotage/synthese");
        var currency = Shell.Session?.CurrencySymbol ?? "FC";
        string Money(decimal amount) => amount.ToString("N0", French) + " " + currency;
        string Count(int count) => count.ToString("N0", French);

        var nextKpis = new[]
        {
            new PromoterKpiVm("Apprenants actifs", Count(data.ActiveStudents), "Effectif total"),
            new PromoterKpiVm("Dossiers débiteurs", Count(data.StudentsWithBalance),
                $"{Count(data.StudentsOverdue)} en retard de paiement"),
            new PromoterKpiVm("Recettes aujourd'hui", Money(data.CollectedToday), "Reçus validés du jour"),
            new PromoterKpiVm("Recettes du mois", Money(data.CollectedThisMonth), "Reçus validés du mois"),
            new PromoterKpiVm("Recettes cumulées", Money(data.CollectedTotal), "Année scolaire en cours"),
            new PromoterKpiVm("Reste à percevoir", Money(data.Outstanding),
                $"Dont {Money(data.Overdue)} déjà exigibles"),
        };
        var nextPromotions = data.Promotions.Select(p => new PromoterPromotionRowVm(
            p.Code, p.Name, Count(p.ActiveStudents), Count(p.StudentsWithBalance), Count(p.StudentsOverdue),
            Money(p.CollectedToday), Money(p.CollectedThisMonth), Money(p.CollectedTotal),
            Money(p.Outstanding), Money(p.Overdue),
            p.Periods.Select(m => new PromoterPeriodRowVm(m.Label, Money(m.CollectedOnDates),
                Money(m.DueNet), Money(m.PaidAgainstDue), Money(m.RemainingDue))).ToArray())).ToArray();

        Kpis.Clear();
        foreach (var item in nextKpis) Kpis.Add(item);
        Promotions.Clear();
        foreach (var item in nextPromotions) Promotions.Add(item);
        subtitle = $"Année {data.AcademicYear} · lecture seule";
        Raise(nameof(Subtitle));
        AsOf = $"Situation au {data.AsOf:dd/MM/yyyy} · poste principal";
    }
}
