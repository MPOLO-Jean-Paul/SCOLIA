using System.Collections.ObjectModel;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Tuile d'indicateur : libellé, valeur, précision, icône et couleur d'accent.</summary>
public sealed record KpiVm(string Label, string Value, string Hint, IBrush Accent, MaterialIconKind Icon = MaterialIconKind.ChartLine);

/// <summary>Ligne libellé et valeur d'une fiche.</summary>
public sealed record InfoLine(string Label, string Value);

/// <summary>Colonne de graphique, hauteur en pixels proportionnelle à la plus grande valeur.</summary>
public sealed record BarVm(string Label, string Value, double Height, double Track = 0);

/// <summary>Ligne du tableau des promotions, avec sa barre de recouvrement.</summary>
public sealed record PromotionStatVm(string Code, string Name, string Students, string Expected, string Collected, string Outstanding, string Overdue, string Recovery, double RecoveryValue);

/// <summary>Reçu récent affiché sur le tableau de bord.</summary>
public sealed record ReceiptLineVm(Guid Id, string Number, string Date, string Amount, string Method);

/// <summary>Repère de la journée : caisse, dépenses, appel, renvois.</summary>
public sealed record TodayStatVm(string Label, string Value, string Hint, MaterialIconKind Icon, IBrush Brush);

/// <summary>Apprenant cité sur l'accueil (convocation ou anniversaire), qui ouvre sa fiche.</summary>
public sealed record TodayPersonVm(string Name, string Promotion, string Detail, string Initials, AsyncCommand Open);

/// <summary>
/// Tableau de bord : journée en cours, indicateurs, encaissements par mois, retards par ancienneté,
/// promotions et derniers reçus.
/// </summary>
public sealed class DashboardPage : PageViewModel
{
    private const double ChartHeight = 150;
    private static readonly System.Globalization.CultureInfo Fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
    private string subtitle = string.Empty;
    private double maleShare;
    private string genderText = string.Empty;
    private string todayTitle = string.Empty;
    private bool hasToday;
    private string backupLine = string.Empty;

    public DashboardPage(ShellViewModel shell) : base(shell)
    {
        Action("Encaisser", MaterialIconKind.HandCoin, () => Shell.Open(() => new CashierPage(Shell)), primary: true);
        ActionRefresh();
    }

    public override string Title => "Tableau de bord";
    public override string Subtitle => subtitle;
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboardOutline;

    public ObservableCollection<TodayStatVm> TodayStats { get; } = [];
    public ObservableCollection<TodayPersonVm> Appointments { get; } = [];
    public ObservableCollection<TodayPersonVm> Birthdays { get; } = [];
    public ObservableCollection<KpiVm> Kpis { get; } = [];
    public ObservableCollection<BarVm> Months { get; } = [];
    public ObservableCollection<BarVm> Aging { get; } = [];
    public ObservableCollection<PromotionStatVm> Promotions { get; } = [];
    public ObservableCollection<ReceiptLineVm> Latest { get; } = [];
    public string GenderText { get => genderText; private set => Set(ref genderText, value); }
    /// <summary>Part des garçons, de 0 à 100, pour la barre de répartition.</summary>
    public double MaleShare { get => maleShare; private set => Set(ref maleShare, value); }
    public string TodayTitle { get => todayTitle; private set => Set(ref todayTitle, value); }
    public bool HasToday { get => hasToday; private set => Set(ref hasToday, value); }
    public bool HasAppointments => Appointments.Count > 0;
    public bool HasBirthdays => Birthdays.Count > 0;
    public bool HasPeople => HasAppointments || HasBirthdays;

    /// <summary>Rappel affiché quand la dernière sauvegarde date de plus d'un jour, pour les comptes qui gèrent les sauvegardes.</summary>
    public string BackupLine { get => backupLine; private set { if (Set(ref backupLine, value)) Raise(nameof(HasBackupLine)); } }
    public bool HasBackupLine => backupLine.Length > 0;
    public AsyncCommand BackupNow => backupNow ??= new AsyncCommand(() => Shell.Run(async () =>
    {
        await DataPage.BackupNowAsync(Shell);
        await CheckBackupAsync();
    }));
    private AsyncCommand? backupNow;

    public override async Task LoadAsync()
    {
        var d = await Shell.Api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord");
        subtitle = $"{Capitalize(DateTime.Today.ToString("dddd d MMMM yyyy", Fr))} · année scolaire {d.AcademicYear}";
        Raise(nameof(Subtitle));

        Kpis.Clear();
        Kpis.Add(new KpiVm("Apprenants actifs", Format.Amount(d.ActiveStudents), $"{d.StudentsUpToDate} à jour · {d.StudentsLate} en retard",
            ThemeBrush.Get("ToneBlue"), MaterialIconKind.AccountGroupOutline));
        Kpis.Add(new KpiVm("Recettes de l'année", Money(d.CollectedTotal), $"Aujourd'hui : {Money(d.CollectedToday)}",
            ThemeBrush.Get("ToneGreen"), MaterialIconKind.CashMultiple));
        Kpis.Add(new KpiVm("Recettes du mois", Money(d.CollectedThisMonth), $"Dépenses du mois : {Money(d.ExpensesThisMonth)}",
            ThemeBrush.Get("ToneTeal"), MaterialIconKind.CalendarMonthOutline));
        Kpis.Add(new KpiVm("Reste exigible", Money(d.Overdue), Format.Count(d.StudentsLate, "apprenant en retard", "apprenants en retard"),
            ThemeBrush.Get("ToneOrange"), MaterialIconKind.AlertCircleOutline));
        Kpis.Add(new KpiVm("Reste à percevoir", Money(d.Outstanding), $"Attendu à ce jour : {Money(d.ExpectedToDate)}",
            ThemeBrush.Get("ToneIndigo"), MaterialIconKind.ProgressClock));
        Kpis.Add(new KpiVm("Taux de recouvrement", Format.Percent(d.RecoveryRate),
            Format.Count(d.NewThisMonth, "nouvelle inscription ce mois", "nouvelles inscriptions ce mois"),
            ThemeBrush.Get("TonePurple"), MaterialIconKind.ChartArc));

        // Chaque mois : l'attendu en piste claire, l'encaissé en couleur par-dessus, à la même échelle.
        Months.Clear();
        var monthMax = d.MonthlyCollections.Select(m => Math.Max(m.Value, m.Secondary ?? 0)).DefaultIfEmpty(1m).Max();
        foreach (var m in d.MonthlyCollections)
            Months.Add(new BarVm(m.Label, m.Value == 0 ? string.Empty : Short(m.Value), Scale(m.Value, monthMax), Scale(m.Secondary ?? 0, monthMax)));

        Aging.Clear();
        var agingMax = d.Aging.Select(a => a.Value).DefaultIfEmpty(1m).Max();
        foreach (var a in d.Aging)
            Aging.Add(new BarVm(a.Label, Money(a.Value), agingMax <= 0 ? 0 : (double)(a.Value / agingMax) * 100));

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

        await LoadTodayAsync();
        await CheckBackupAsync();
    }

    /// <summary>Âge de la dernière sauvegarde : rien à signaler sous 24 heures.</summary>
    private async Task CheckBackupAsync()
    {
        if (!Shell.Can(Perm.BackupManage)) { BackupLine = string.Empty; return; }
        var backups = await Shell.Api.GetAsync<List<BackupDto>>("api/sauvegardes");
        var last = backups.Count == 0 ? (DateTime?)null : backups.Max(b => b.CreatedUtc);
        BackupLine = last is null
            ? "Aucune sauvegarde n'existe encore. Une copie vérifiée protège l'établissement en cas de panne."
            : DateTime.UtcNow - last.Value > TimeSpan.FromHours(24)
                ? $"Dernière sauvegarde le {Format.DateTime(last.Value)}. Pensez à en faire une nouvelle."
                : string.Empty;
    }

    /// <summary>La journée en cours. Un serveur plus ancien sans ce service laisse simplement le panneau masqué.</summary>
    private async Task LoadTodayAsync()
    {
        TodayStats.Clear();
        Appointments.Clear();
        Birthdays.Clear();
        TodayDto t;
        try { t = await Shell.Api.GetAsync<TodayDto>("api/rapports/aujourdhui"); }
        catch (ApiException) { HasToday = false; RaisePeople(); return; }

        TodayTitle = "Aujourd'hui";
        TodayStats.Add(new TodayStatVm("Encaissé", Money(t.CollectedToday), Format.Count(t.ReceiptsToday, "reçu", "reçus"),
            MaterialIconKind.HandCoin, ThemeBrush.Get("ToneGreen")));
        TodayStats.Add(new TodayStatVm("Dépenses", Money(t.ExpensesToday), "Sorties de caisse du jour",
            MaterialIconKind.WalletOutline, ThemeBrush.Get("ToneRed")));
        TodayStats.Add(t.CashClosed
            ? new TodayStatVm("Caisse", "Arrêtée", "L'arrêté du jour est enregistré", MaterialIconKind.LockCheckOutline, ThemeBrush.Get("ToneTeal"))
            : new TodayStatVm("Caisse", t.ReceiptsToday == 0 ? "Ouverte" : "À arrêter", t.ReceiptsToday == 0 ? "Aucune opération pour l'instant" : "Pensez à l'arrêté de fin de journée",
                MaterialIconKind.CashRegister, ThemeBrush.Get("ToneOrange")));
        TodayStats.Add(new TodayStatVm("Appel", t.AttendanceMarked == 0 ? "Non saisi" : Format.Count(t.AttendanceMarked, "apprenant pointé", "apprenants pointés"),
            t.SuspendedToday == 0 ? "Aucun renvoi en cours" : Format.Count(t.SuspendedToday, "apprenant renvoyé ce jour", "apprenants renvoyés ce jour"),
            MaterialIconKind.ClipboardCheckOutline, ThemeBrush.Get("ToneBlue")));

        foreach (var a in t.Appointments) Appointments.Add(Person(a));
        foreach (var b in t.Birthdays) Birthdays.Add(Person(b));
        HasToday = true;
        RaisePeople();
    }

    private TodayPersonVm Person(TodayPersonDto p) =>
        new(p.Name, p.Promotion, p.Detail, Initials(p.Name), new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new StudentDetailPage(Shell, p.StudentId)))));

    private void RaisePeople()
    {
        Raise(nameof(HasAppointments));
        Raise(nameof(HasBirthdays));
        Raise(nameof(HasPeople));
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Fr) + text[1..];

    /// <summary>Montant court pour l'étiquette d'une colonne : 6,9 M ou 850 k.</summary>
    private static string Short(decimal value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000).ToString("0.#", Fr) + " M",
        >= 1_000 => (value / 1_000).ToString("0", Fr) + " k",
        _ => value.ToString("0", Fr),
    };

    private static double Scale(decimal value, decimal max) => max <= 0 ? 0 : (double)(value / max) * ChartHeight;
}
