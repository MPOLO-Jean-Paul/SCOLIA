using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Un mois de l'année scolaire : dû, réglé, reste, et l'argent entré pendant ce mois.</summary>
public sealed record PromoterPeriodRowVm(string Period, string Due, string Paid, string Remaining, double PaidShare,
    string Collected, bool HasCollected, bool IsCurrent, bool IsSettled);

public sealed record PromoterPromotionRowVm(string Code, string Name, string Students, string LateDebtors, bool HasLate,
    string CollectedToday, string CollectedMonth, string CollectedYear, string Outstanding, string Overdue, bool HasOverdue,
    double YearShare, string YearShareText, IReadOnlyList<PromoterPeriodRowVm> Periods);

public sealed record PromoterKpiVm(string Label, string Value, string Detail, MaterialIconKind Icon, IBrush Brush);

/// <summary>
/// Pilotage réservé au Promoteur : aucun reçu, numéro de téléphone, nom d'apprenant,
/// bouton d'écriture ni chemin vers les dossiers individuels. Les chiffres se mettent à jour seuls
/// quand un autre poste enregistre un reçu.
/// </summary>
public sealed class PromoterDashboardPage : PageViewModel
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private string subtitle = "Consultation uniquement";
    private string asOf = string.Empty;
    private string attention = string.Empty;

    public PromoterDashboardPage(ShellViewModel shell) : base(shell)
    {
        ActionRefresh();
    }

    public override string Title => "Vue d'ensemble";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboardOutline;

    public string AsOf { get => asOf; private set => Set(ref asOf, value); }
    /// <summary>Ce qui demande l'attention du promoteur, en une phrase ; vide quand tout est à jour.</summary>
    public string Attention { get => attention; private set { if (Set(ref attention, value)) Raise(nameof(HasAttention)); } }
    public bool HasAttention => attention.Length > 0;
    public ObservableCollection<PromoterKpiVm> Kpis { get; } = [];
    public ObservableCollection<PromoterPromotionRowVm> Promotions { get; } = [];
    /// <summary>Tout l'établissement, mois par mois : la somme des promotions.</summary>
    public ObservableCollection<PromoterPeriodRowVm> SchoolPeriods { get; } = [];
    public bool HasPromotions => Promotions.Count > 0;

    public override async Task LoadAsync()
    {
        if (!Shell.Can(Ribbon.Perm.PilotageRead)) return;
        var data = await Shell.Api.GetAsync<PromoterDashboardDto>("api/pilotage/synthese");
        var currency = Shell.Session?.CurrencySymbol ?? "FC";
        string Money(decimal amount) => amount.ToString("N0", French) + " " + currency;
        string Count(int count) => count.ToString("N0", French);
        var currentLabel = $"{French.TextInfo.ToTitleCase(data.AsOf.ToString("MMMM", French))} {data.AsOf.Year}";

        PromoterPeriodRowVm Period(string label, decimal due, decimal paid, decimal remaining, decimal collected) =>
            new(label, Money(due), Money(paid), Money(remaining), due > 0 ? (double)Math.Clamp(paid / due, 0, 1) : 0,
                Money(collected), collected > 0, string.Equals(label, currentLabel, StringComparison.OrdinalIgnoreCase),
                due > 0 && remaining <= 0);

        static double Share(IEnumerable<PromoterPeriodDto> periods)
        {
            var list = periods.ToList();
            var due = list.Sum(p => p.DueNet);
            return due > 0 ? (double)Math.Clamp(list.Sum(p => p.PaidAgainstDue) / due, 0, 1) : 0;
        }

        var allPeriods = data.Promotions.SelectMany(p => p.Periods).ToList();
        var yearShare = Share(allPeriods);
        var latePart = data.ActiveStudents > 0 ? (double)data.StudentsOverdue / data.ActiveStudents : 0;

        var nextKpis = new[]
        {
            new PromoterKpiVm("Apprenants actifs", Count(data.ActiveStudents),
                Format.Count(data.Promotions.Count, "promotion", "promotions"), MaterialIconKind.AccountGroup, ThemeBrush.Get("ToneIndigo")),
            new PromoterKpiVm("En retard de paiement", Count(data.StudentsOverdue),
                data.StudentsOverdue == 0 ? "Tout le monde est à jour" : $"{latePart:P0} des apprenants · {Money(data.Overdue)} exigibles",
                MaterialIconKind.AlertCircleOutline, ThemeBrush.Get(data.StudentsOverdue == 0 ? "ToneGreen" : "ToneOrange")),
            new PromoterKpiVm("Encaissé aujourd'hui", Money(data.CollectedToday), "Reçus validés du jour",
                MaterialIconKind.HandCoin, ThemeBrush.Get("ToneGreen")),
            new PromoterKpiVm("Encaissé ce mois", Money(data.CollectedThisMonth), currentLabel,
                MaterialIconKind.CalendarMonth, ThemeBrush.Get("ToneBlue")),
            new PromoterKpiVm("Encaissé depuis la rentrée", Money(data.CollectedTotal), $"Année {data.AcademicYear}",
                MaterialIconKind.Finance, ThemeBrush.Get("ToneTeal")),
            new PromoterKpiVm("Reste à percevoir", Money(data.Outstanding), $"Année réglée à {yearShare:P0}",
                MaterialIconKind.ChartDonut, ThemeBrush.Get("TonePurple")),
        };

        var nextPromotions = data.Promotions.Select(p =>
        {
            var share = Share(p.Periods);
            return new PromoterPromotionRowVm(p.Code, p.Name, Count(p.ActiveStudents),
                p.StudentsOverdue == 0 ? "Aucun retard" : Format.Count(p.StudentsOverdue, "en retard", "en retard"), p.StudentsOverdue > 0,
                Money(p.CollectedToday), Money(p.CollectedThisMonth), Money(p.CollectedTotal),
                Money(p.Outstanding), Money(p.Overdue), p.Overdue > 0, share, $"Année réglée à {share:P0}",
                p.Periods.Select(m => Period(m.Label, m.DueNet, m.PaidAgainstDue, m.RemainingDue, m.CollectedOnDates)).ToArray());
        }).ToArray();

        // Établissement entier : les mois dans l'ordre de l'année, additionnés sur toutes les promotions.
        var nextSchool = allPeriods.GroupBy(m => m.PeriodId)
            .Select(g => Period(g.First().Label, g.Sum(m => m.DueNet), g.Sum(m => m.PaidAgainstDue), g.Sum(m => m.RemainingDue), g.Sum(m => m.CollectedOnDates)))
            .ToArray();

        var worst = data.Promotions.Where(p => p.Overdue > 0).OrderByDescending(p => p.Overdue).FirstOrDefault();
        Attention = worst is null
            ? string.Empty
            : $"{Money(data.Overdue)} déjà exigibles ne sont pas réglés. La promotion {worst.Code} en porte le plus : {Money(worst.Overdue)}.";

        Replace(Kpis, nextKpis);
        Replace(Promotions, nextPromotions);
        Replace(SchoolPeriods, nextSchool);
        Raise(nameof(HasPromotions));
        subtitle = $"Année {data.AcademicYear}";
        Raise(nameof(Subtitle));
        AsOf = $"Situation au {data.AsOf:dd/MM/yyyy} à {DateTime.Now:HH:mm} · mise à jour automatique";
    }

    /// <summary>Remplace le contenu d'une liste sans la vider quand rien n'a changé : pas de clignotement au rafraîchissement.</summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items)) return;
        for (var i = 0; i < items.Count; i++)
        {
            if (i < target.Count) { if (!Equals(target[i], items[i])) target[i] = items[i]; }
            else target.Add(items[i]);
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }
}
