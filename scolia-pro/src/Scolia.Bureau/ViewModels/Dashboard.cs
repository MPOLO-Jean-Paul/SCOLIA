using System.Collections.ObjectModel;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Tuile d'indicateur : libellé, valeur, précision et couleur d'accent.</summary>
public sealed record KpiVm(string Label, string Value, string Hint, IBrush Accent);

/// <summary>Ligne libellé et valeur d'une fiche.</summary>
public sealed record InfoLine(string Label, string Value);

/// <summary>Colonne de graphique, hauteur en pixels proportionnelle à la plus grande valeur.</summary>
public sealed record BarVm(string Label, string Value, double Height);

/// <summary>Ligne du tableau des promotions, avec sa barre de recouvrement.</summary>
public sealed record PromotionStatVm(string Code, string Name, string Students, string Expected, string Collected, string Outstanding, string Overdue, string Recovery, double RecoveryValue);

/// <summary>Reçu récent affiché sur le tableau de bord.</summary>
public sealed record ReceiptLineVm(Guid Id, string Number, string Date, string Amount, string Method);

/// <summary>Tableau de bord : indicateurs, encaissements par mois, retards par ancienneté, promotions et derniers reçus.</summary>
public sealed class DashboardPage : PageViewModel
{
    private const double ChartHeight = 150;
    private string subtitle = string.Empty;
    private double maleShare;
    private string genderText = string.Empty;

    public DashboardPage(ShellViewModel shell) : base(shell)
    {
        Action("Encaisser", MaterialIconKind.HandCoin, () => Shell.Open(() => new CashierPage(Shell)), primary: true);
        ActionRefresh();
    }

    public override string Title => "Tableau de bord";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboardOutline;

    public ObservableCollection<KpiVm> Kpis { get; } = [];
    public ObservableCollection<BarVm> Months { get; } = [];
    public ObservableCollection<BarVm> Aging { get; } = [];
    public ObservableCollection<PromotionStatVm> Promotions { get; } = [];
    public ObservableCollection<ReceiptLineVm> Latest { get; } = [];
    public string GenderText { get => genderText; private set => Set(ref genderText, value); }
    /// <summary>Part des garçons, de 0 à 100, pour la barre de répartition.</summary>
    public double MaleShare { get => maleShare; private set => Set(ref maleShare, value); }

    public override async Task LoadAsync()
    {
        var d = await Shell.Api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord");
        subtitle = $"Année scolaire {d.AcademicYear} · {Format.Count(d.ActiveStudents, "apprenant actif", "apprenants actifs")}";
        Raise(nameof(Subtitle));

        Kpis.Clear();
        Kpis.Add(new KpiVm("Apprenants actifs", Format.Amount(d.ActiveStudents), $"{d.StudentsUpToDate} à jour · {d.StudentsLate} en retard", ThemeBrush.Get("ToneBlue")));
        Kpis.Add(new KpiVm("Recettes de l'année", Money(d.CollectedTotal), $"Aujourd'hui : {Money(d.CollectedToday)}", ThemeBrush.Get("ToneGreen")));
        Kpis.Add(new KpiVm("Recettes du mois", Money(d.CollectedThisMonth), $"Dépenses du mois : {Money(d.ExpensesThisMonth)}", ThemeBrush.Get("ToneGreen")));
        Kpis.Add(new KpiVm("Reste exigible", Money(d.Overdue), $"{Format.Count(d.StudentsLate, "apprenant en retard", "apprenants en retard")}", ThemeBrush.Get("ToneOrange")));
        Kpis.Add(new KpiVm("Reste à percevoir", Money(d.Outstanding), $"Attendu à ce jour : {Money(d.ExpectedToDate)}", ThemeBrush.Get("ToneNeutral")));
        Kpis.Add(new KpiVm("Taux de recouvrement", Format.Percent(d.RecoveryRate), $"{d.NewThisMonth} nouvelle(s) inscription(s) ce mois", ThemeBrush.Get("ToneBlue")));

        Months.Clear();
        var monthMax = d.MonthlyCollections.Select(m => m.Value).DefaultIfEmpty(1m).Max();
        foreach (var m in d.MonthlyCollections)
            Months.Add(new BarVm(m.Label, Money(m.Value), Scale(m.Value, monthMax)));

        Aging.Clear();
        var agingMax = d.Aging.Select(a => a.Value).DefaultIfEmpty(1m).Max();
        foreach (var a in d.Aging)
            Aging.Add(new BarVm(a.Label, Money(a.Value), Scale(a.Value, agingMax)));

        Promotions.Clear();
        foreach (var p in d.Promotions)
            Promotions.Add(new PromotionStatVm(p.Code, p.Name, Format.Amount(p.Students), Money(p.Expected), Money(p.Collected),
                Money(p.Outstanding), Money(p.Overdue), Format.Percent(p.RecoveryRate), (double)Math.Clamp(p.RecoveryRate, 0, 100)));

        Latest.Clear();
        foreach (var r in d.LatestReceipts)
            Latest.Add(new ReceiptLineVm(r.Id, r.Number, Format.Date(r.Date), Money(r.Amount), Labels.Method(r.Method)));

        var total = d.GenderMale + d.GenderFemale;
        MaleShare = total == 0 ? 0 : 100.0 * d.GenderMale / total;
        GenderText = $"Garçons {Format.Amount(d.GenderMale)} · Filles {Format.Amount(d.GenderFemale)}";
    }

    private static double Scale(decimal value, decimal max) => max <= 0 ? 0 : (double)(value / max) * ChartHeight;
}
