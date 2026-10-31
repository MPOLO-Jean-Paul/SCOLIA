using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Recherche Android pilotée par l'écran : Menu = paramètres, Accueil = global,
/// les listes = filtres existants de leur propre métier. Ne mélange jamais les résultats.
/// </summary>
public sealed class PhoneHeaderSearchVm : Observable
{
    private readonly ShellViewModel shell;
    private bool isOpen;

    public PhoneHeaderSearchVm(ShellViewModel shell)
    {
        this.shell = shell;
        Toggle = new RelayCommand(ActivateOrClose);
        Close = new RelayCommand(() => IsOpen = false);
        Clear = new RelayCommand(() => Query = string.Empty);
    }
    public RelayCommand Toggle { get; }
    public RelayCommand Close { get; }
    public RelayCommand Clear { get; }
    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }
    public bool HasQuery => Query.Length > 0;
    public bool IsVisible => shell.PhoneMenuOpen || shell.Current is HomePage or PromoterDashboardPage or SettingsPage ||
                             Scope(shell.Current).Length > 0;
    public static string Scope(PageViewModel? page) => page switch
    {
        StudentsPage => "apprenants",
        ReceiptsPage => "recus",
        MonthlyPage => "mensualites",
        DebtorsPage => "debiteurs",
        CashierPage => "encaissement",
        BatchPaymentPage => "encaissement_lot",
        CardsPage => "cartes",
        AuditPage => "audit",
        ExpensesPage => "depenses",
        _ => string.Empty,
    };
    public string Placeholder => Scope(shell.Current) switch
    {
        "apprenants" => "Nom ou matricule de l'apprenant",
        "recus" => "Reçu, matricule ou nom",
        "mensualites" => "Apprenant ou matricule",
        "debiteurs" => "Apprenant en dette",
        "encaissement" => "Apprenant à encaisser",
        "encaissement_lot" => "Apprenants du lot",
        "cartes" => "Apprenant ou matricule de la carte",
        "audit" => "Événement du journal d'audit",
        "depenses" => "Dépense ou référence",
        _ => shell.Current is SettingsPage ? "Rechercher un réglage ou une option" : "Rechercher dans cette page",
    };
    public string Query
    {
        get => shell.Current switch
        {
            StudentsPage p => p.Search,
            ReceiptsPage p => p.Search,
            MonthlyPage p => p.Search,
            DebtorsPage p => p.Search,
            CashierPage p => p.Search,
            BatchPaymentPage p => p.Search,
            CardsPage p => p.Search,
            AuditPage p => p.Search,
            ExpensesPage p => p.Search,
            _ => string.Empty,
        };
        set
        {
            var query = value ?? string.Empty;
            switch (shell.Current)
            {
                case StudentsPage p: p.Search = query; break;
                case ReceiptsPage p: p.Search = query; break;
                case MonthlyPage p: p.Search = query; break;
                case DebtorsPage p: p.Search = query; break;
                case CashierPage p: p.Search = query; break;
                case BatchPaymentPage p: p.Search = query; break;
                case CardsPage p: p.Search = query; break;
                case AuditPage p: p.Search = query; break;
                case ExpensesPage p: p.Search = query; break;
                default: return;
            }
            Raise(nameof(Query));
            Raise(nameof(HasQuery));
        }
    }
    public void ActivateOrClose()
    {
        if (shell.PhoneMenuOpen)
        {
            shell.SettingsSearch.Activate.Execute(null);
            return;
        }
        if (shell.Current is HomePage or PromoterDashboardPage)
        {
            // L'accueil est le seul écran dont le champ recherche aussi les dossiers
            // et les fonctionnalités de toute l'application (selon les permissions).
            shell.ShowSearch();
            return;
        }
        if (shell.Current is SettingsPage)
        {
            shell.ShowSettingsSearch();
            return;
        }
        // Sur une liste, la saisie est liée uniquement au filtre du métier affiché.
        if (Scope(shell.Current).Length > 0) IsOpen = !IsOpen;
    }
    public void PageChanged()
    {
        IsOpen = false;
        Raise(nameof(Placeholder));
        Raise(nameof(Query));
        Raise(nameof(HasQuery));
        Raise(nameof(IsVisible));
    }
    public void MenuChanged() => Raise(nameof(IsVisible));
}
