using System.Collections.ObjectModel;
using Avalonia.Media;
using Material.Icons;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Tuile d'indicateur : libellé, valeur, précision, icône et couleur d'accent.</summary>
public sealed record KpiVm(string Label, string Value, string Hint, IBrush Accent, MaterialIconKind Icon = MaterialIconKind.ChartLine)
{
    /// <summary>Fond teinté de la tuile d'icône : la couleur d'accent à 14 %.</summary>
    public IBrush Soft => Accent is ISolidColorBrush solid
        ? new SolidColorBrush(Color.FromArgb(0x24, solid.Color.R, solid.Color.G, solid.Color.B))
        : Brushes.Transparent;
}

/// <summary>Ligne libellé et valeur d'une fiche.</summary>
public sealed record InfoLine(string Label, string Value);

/// <summary>Colonne de graphique, hauteur en pixels proportionnelle à la plus grande valeur.</summary>
public sealed record BarVm(string Label, string Value, double Height, double Track = 0, IBrush? Brush = null);

/// <summary>Ligne du tableau des promotions, avec sa barre de recouvrement.</summary>
public sealed record PromotionStatVm(string Code, string Name, string Students, string Expected, string Collected, string Outstanding, string Overdue, string Recovery, double RecoveryValue,
    string Paying = "", string Sponsored = "");

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
    private string selectionTotal = "Chargement de la sélection…";
    private string? loadedQuery, selectionKey;
    public DashboardFiltersVm Filters { get; } = new();
    public string SelectionTotal { get => selectionTotal; private set => Set(ref selectionTotal, value); }

    public DashboardPage(ShellViewModel shell) : base(shell)
    {
        Action("Encaisser", MaterialIconKind.HandCoin, () => Shell.Open(() => new CashierPage(Shell)), primary: true);
        Action("Exporter la sélection", MaterialIconKind.FileExport, () => Filters.ExportAsync(Shell, false, loadedQuery, selectionKey));
        Filters.Changed += () =>
        {
            ClearSelection("Actualisation de la sélection…");
            _ = Shell.Run(() => Filters.RefreshLatestAsync(RefreshAsync, () => loadedQuery == Filters.Query));
        };
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
        loadedQuery = selectionKey = null;
        string query;
        DashboardDto d;
        try
        {
            await Filters.EnsureAsync(Shell.Api, promoter: false);
            query = Filters.Query;
            d = await Shell.Api.GetAsync<DashboardDto>("api/rapports/tableau-de-bord" + query);
        }
        catch
        {
            ClearSelection("Sélection indisponible. Utilisez Actualiser pour réessayer.");
            throw;
        }
        if (query != Filters.Query) return;
        loadedQuery = query;
        selectionKey = d.SelectionKey;
        SelectionTotal = $"Total de la sélection · {Format.Count(d.ActiveStudents, Shell.StudentTerms.Singular, Shell.StudentTerms.Plural)} · {Money(d.CollectedTotal)} encaissés · {Money(d.Outstanding)} à percevoir";
        subtitle = $"Année {d.AcademicYear} · au {DateTime.Today.ToString("d MMMM", Fr)}";
        Raise(nameof(Subtitle));

        Kpis.Clear();
        // Les pris en charge ont leur propre total : « à jour » et « en retard » ne comptent que les payants.
        Kpis.Add(new KpiVm($"{Shell.StudentTerms.PluralTitle} actifs", Format.Amount(d.ActiveStudents),
            $"{Format.Count(d.PayingStudents, "payant", "payants")} : {d.StudentsUpToDate} à jour · {d.StudentsLate} en retard",
            ThemeBrush.Get("ToneBlue"), MaterialIconKind.AccountGroupOutline));
        Kpis.Add(new KpiVm("Pris en charge", Format.Amount(d.SponsoredStudents),
            (d.PartiallySponsoredStudents > 0 ? $"+ {Format.Count(d.PartiallySponsoredStudents, "partielle", "partielles")} · " : string.Empty)
                + $"couvert {Money(d.SponsoredAmount)}",
            ThemeBrush.Get("TonePurple"), MaterialIconKind.HandHeartOutline));
        // Les recettes du jour figurent dans « Aujourd'hui » : la tuile de l'année porte celles du mois et ses dépenses.
        Kpis.Add(new KpiVm("Recettes de l'année", Money(d.CollectedTotal),
            $"Ce mois : {Money(d.CollectedThisMonth)} · dépenses de l'établissement {Money(d.ExpensesThisMonth)}",
            ThemeBrush.Get("ToneGreen"), MaterialIconKind.CashMultiple));
        Kpis.Add(new KpiVm("Reste exigible", Money(d.Overdue), Format.Count(d.StudentsLate, $"{Shell.StudentTerms.Singular} en retard", $"{Shell.StudentTerms.Plural} en retard"),
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
        // Plus le retard est ancien, plus la barre tire vers le rouge.
        string[] agingTones = ["ToneOrange", "ToneOrange", "ToneRed", "ToneRed"];
        var agingIndex = 0;
        foreach (var a in d.Aging)
            Aging.Add(new BarVm(a.Label, Money(a.Value), agingMax <= 0 ? 0 : (double)(a.Value / agingMax) * 100, 0,
                ThemeBrush.Get(agingTones[Math.Min(agingIndex++, agingTones.Length - 1)])));

        Promotions.Clear();
        foreach (var p in d.Promotions)
            Promotions.Add(new PromotionStatVm(p.Code, p.Name, Format.Amount(p.Students), Money(p.Expected), Money(p.Collected),
                Money(p.Outstanding), Money(p.Overdue), Format.Percent(p.RecoveryRate), (double)Math.Clamp(p.RecoveryRate, 0, 100),
                Format.Amount(p.Paying), Format.Amount(p.Sponsored)));

        Latest.Clear();
        foreach (var r in d.LatestReceipts)
            Latest.Add(new ReceiptLineVm(r.Id, r.Number, Format.Date(r.Date), Money(r.Amount), Labels.Method(r.Method)));

        var total = d.GenderMale + d.GenderFemale;
        MaleShare = total == 0 ? 0 : 100.0 * d.GenderMale / total;
        GenderText = $"Garçons {Format.Amount(d.GenderMale)} · Filles {Format.Amount(d.GenderFemale)}";

        if (!Filters.HasScope) await LoadTodayAsync();
        else { HasToday = false; Appointments.Clear(); Birthdays.Clear(); RaisePeople(); }
        await CheckBackupAsync();
    }

    private void ClearSelection(string message)
    {
        loadedQuery = selectionKey = null;
        SelectionTotal = message;
        Kpis.Clear(); Promotions.Clear(); Latest.Clear(); Months.Clear(); Aging.Clear();
        GenderText = string.Empty; MaleShare = 0;
        HasToday = false; TodayStats.Clear(); Appointments.Clear(); Birthdays.Clear(); RaisePeople();
    }

    /// <summary>Âge de la dernière sauvegarde : rien à signaler sous 24 heures.</summary>
    private async Task CheckBackupAsync()
    {
        if (!Shell.Can(Perm.BackupManage)) { BackupLine = string.Empty; return; }
        var backups = await Shell.Api.GetAsync<List<BackupDto>>("api/sauvegardes");
        var schedule = await DataPage.ReadScheduleAsync(Shell);
        var last = backups.Count == 0 ? (DateTime?)null : backups.Max(b => b.CreatedUtc);
        BackupLine = last is null
            ? "Aucune sauvegarde n'existe encore. Une copie vérifiée protège l'établissement en cas de panne."
            : !BackupText.IsFresh(last.Value, DateTime.UtcNow, schedule.LocalIntervalHours)
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
        TodayStats.Add(new TodayStatVm("Appel", t.AttendanceMarked == 0 ? "Non saisi" : Format.Count(t.AttendanceMarked, $"{Shell.StudentTerms.Singular} pointé", $"{Shell.StudentTerms.Plural} pointés"),
            t.SuspendedToday == 0 ? "Aucun renvoi en cours" : Format.Count(t.SuspendedToday, $"{Shell.StudentTerms.Singular} renvoyé ce jour", $"{Shell.StudentTerms.Plural} renvoyés ce jour"),
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
