using System.Collections.ObjectModel;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Écran de connexion.</summary>
public sealed class LoginViewModel : Observable
{
    private readonly ScoliaClient api;
    private string userName = string.Empty;
    private string password = string.Empty;
    private string? error;
    private bool busy;

    public LoginViewModel(ScoliaClient api)
    {
        this.api = api;
        Submit = new AsyncCommand(SubmitAsync);
    }

    public event Action<SessionDto>? LoggedIn;

    public string UserName { get => userName; set { if (Set(ref userName, value)) Submit.Refresh(); } }
    public string Password { get => password; set { if (Set(ref password, value)) Submit.Refresh(); } }
    public string? Error { get => error; set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(error);
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public AsyncCommand Submit { get; }

    private async Task SubmitAsync()
    {
        Error = null;
        Busy = true;
        try
        {
            var session = await api.LoginAsync(UserName.Trim(), Password);
            Password = string.Empty;
            LoggedIn?.Invoke(session);
        }
        catch (ApiException ex) { Error = ex.Message; }
        catch (HttpRequestException) { Error = "Le serveur SCOLIA ne répond pas. Vérifiez qu'il est lancé sur ce poste."; }
        finally { Busy = false; }
    }
}

/// <summary>Indicateur affiché en tuile.</summary>
public sealed record Kpi(string Label, string Value, string Hint, string Tone);

/// <summary>Barre du graphique mensuel, hauteur proportionnelle.</summary>
public sealed record MonthBar(string Label, string Value, double Height);

/// <summary>Tableau de bord : indicateurs et encaissements par mois.</summary>
public sealed class DashboardViewModel(ScoliaClient api, string currency) : Observable
{
    private string? title;
    public string? Title { get => title; private set => Set(ref title, value); }
    public ObservableCollection<Kpi> Kpis { get; } = [];
    public ObservableCollection<MonthBar> Months { get; } = [];
    private bool loading;
    public bool Loading { get => loading; private set => Set(ref loading, value); }

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var d = await api.DashboardAsync();
            Title = $"Année scolaire {d.AcademicYear}";
            Kpis.Clear();
            Kpis.Add(new Kpi("Apprenants actifs", d.ActiveStudents.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), $"{d.StudentsUpToDate} à jour · {d.StudentsLate} en retard", "blue"));
            Kpis.Add(new Kpi("Recettes de l'année", Infrastructure.Format.Money(d.CollectedTotal, currency), $"Ce mois : {Infrastructure.Format.Money(d.CollectedThisMonth, currency)}", "green"));
            Kpis.Add(new Kpi("Reste exigible", Infrastructure.Format.Money(d.Overdue, currency), $"{d.StudentsLate} apprenant(s) en retard", "orange"));
            Kpis.Add(new Kpi("Reste à percevoir", Infrastructure.Format.Money(d.Outstanding, currency), $"Dépenses du mois : {Infrastructure.Format.Money(d.ExpensesThisMonth, currency)}", "neutral"));
            Kpis.Add(new Kpi("Taux de recouvrement", Infrastructure.Format.Percent(d.RecoveryRate), $"Attendu à ce jour : {Infrastructure.Format.Money(d.ExpectedToDate, currency)}", "neutral"));
            Months.Clear();
            var max = d.MonthlyCollections.Select(m => Math.Max(m.Value, m.Secondary)).DefaultIfEmpty(1m).Max();
            if (max <= 0) max = 1m;
            foreach (var m in d.MonthlyCollections)
                Months.Add(new MonthBar(m.Label, Infrastructure.Format.Money(m.Value, currency), (double)(m.Value / max) * 160));
        }
        finally { Loading = false; }
    }
}

/// <summary>Ligne d'apprenant prête à afficher.</summary>
public sealed record StudentRow(string Matricule, string Name, string Promotion, string Phone, string Status, string Balance, string Overdue, bool IsLate);

/// <summary>Liste des apprenants, avec recherche.</summary>
public sealed class StudentsViewModel(ScoliaClient api, string currency) : Observable
{
    private AsyncCommand? reload;
    public AsyncCommand Reload => reload ??= new AsyncCommand(LoadAsync);
    private string search = string.Empty;
    private string summary = string.Empty;
    private bool loading;

    public string Search { get => search; set => Set(ref search, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public bool Loading { get => loading; private set => Set(ref loading, value); }
    public ObservableCollection<StudentRow> Items { get; } = [];

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var page = await api.StudentsAsync(Search, 200);
            Items.Clear();
            foreach (var s in page.Items)
                Items.Add(new StudentRow(
                    s.Matricule, Infrastructure.Format.FullName(s.LastName, s.MiddleName, s.FirstName), s.PromotionCode,
                    s.GuardianPhone ?? string.Empty, s.Status, Infrastructure.Format.Money(s.Balance, currency),
                    s.Overdue > 0 ? Infrastructure.Format.Money(s.Overdue, currency) : "À jour", s.Overdue > 0));
            Summary = $"{page.Total} apprenant(s)";
        }
        finally { Loading = false; }
    }
}

/// <summary>Ligne du journal des reçus prête à afficher.</summary>
public sealed record ReceiptLine(string Number, string Date, string Matricule, string Student, string Promotion, string Method, string Amount, string Status, bool Cancelled);

/// <summary>Journal des reçus, du plus récent au plus ancien.</summary>
public sealed class ReceiptsViewModel(ScoliaClient api, string currency) : Observable
{
    private AsyncCommand? reload;
    public AsyncCommand Reload => reload ??= new AsyncCommand(LoadAsync);
    private string summary = string.Empty;
    private bool loading;

    public string Summary { get => summary; private set => Set(ref summary, value); }
    public bool Loading { get => loading; private set => Set(ref loading, value); }
    public ObservableCollection<ReceiptLine> Items { get; } = [];

    public async Task LoadAsync()
    {
        Loading = true;
        try
        {
            var page = await api.ReceiptsAsync(200);
            Items.Clear();
            foreach (var r in page.Items)
                Items.Add(new ReceiptLine(r.Number, Infrastructure.Format.Date(r.Date), r.Matricule, r.StudentName, r.PromotionCode,
                    r.Method, Infrastructure.Format.Money(r.Amount, currency), r.Status, r.Status != "Valide"));
            Summary = $"{page.Total} reçu(s)";
        }
        finally { Loading = false; }
    }
}
