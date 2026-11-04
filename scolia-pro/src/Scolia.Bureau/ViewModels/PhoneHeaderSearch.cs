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

    /// <summary>Micro du filtre, comme la recherche de l'accueil.</summary>
    public bool VoiceAvailable => VoiceInput.Available;
    private readonly VoiceCaptureSession voiceSession = new();
    private bool listening;
    public bool Listening { get => listening; private set { if (Set(ref listening, value)) { Raise(nameof(Placeholder)); Raise(nameof(VoiceTip)); } } }
    public string VoiceTip => Listening ? "Arrêter l'écoute vocale" : "Filtrer à la voix";
    public RelayCommand Listen => listen ??= new RelayCommand(() =>
    {
        var page = shell.Current;
        _ = voiceSession.ToggleAsync(text =>
        {
            // Une dictée démarrée sur une liste ne peut filtrer une autre page.
            if (ReferenceEquals(page, shell.Current)) Query = text;
        }, active => Listening = active);
    });
    private RelayCommand? listen;
    // À chaque ouverture, le micro suit le service vocal du moment (installé, coupé ou refusé entre-temps).
    public bool IsOpen { get => isOpen; private set { if (Set(ref isOpen, value) && value) Raise(nameof(VoiceAvailable)); } }
    public bool HasQuery => Query.Length > 0;
    public bool IsVisible => shell.PhoneMenuOpen || shell.Current is HomePage or PromoterDashboardPage or SettingsPage ||
                             Scope(shell.Current).Length > 0;
    public bool IsContextual => Scope(shell.Current).Length > 0 && !shell.PhoneMenuOpen;
    public string ShortcutText => IsContextual ? "Filtre" : "Ctrl+K";
    public string SearchHint => shell.PhoneMenuOpen ? "Rechercher une fonctionnalité ou un paramètre du menu"
        : IsContextual ? "Filtrer uniquement les données de cet écran"
        : "Recherche globale : dossiers, reçus et fonctionnalités autorisées";
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
    public string Placeholder => listening ? "Parlez maintenant…" : Scope(shell.Current) switch
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
        _ => shell.Current is SettingsPage ? "Rechercher un réglage ou une option" : "Rechercher dans SCOLIA",
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
        // Téléphone : la loupe ouvre et ferme le filtre. Ordinateur : il reste ouvert, le raccourci y place le curseur.
        if (Scope(shell.Current).Length > 0) IsOpen = !shell.Phone || !IsOpen;
    }
    /// <summary>Ouvre le filtre de l'écran, sans le refermer s'il est déjà ouvert.</summary>
    public void Open()
    {
        if (Scope(shell.Current).Length > 0) IsOpen = true;
    }

    public void PageChanged()
    {
        // Ordinateur : sur une liste, le champ de la barre du haut est ouvert d'emblée, prêt à la saisie.
        // Téléphone : seul Encaisser l'ouvre, pour ne pas sortir le clavier à chaque page.
        IsOpen = shell.Current is CashierPage { OpensHeaderSearch: true }
            || (!shell.Phone && Scope(shell.Current).Length > 0 && shell.Current is not CashierPage);
        Raise(nameof(Placeholder));
        Raise(nameof(Query));
        Raise(nameof(HasQuery));
        Raise(nameof(IsVisible));
        Raise(nameof(IsContextual));
        Raise(nameof(ShortcutText));
        Raise(nameof(SearchHint));
    }
    public void MenuChanged()
    {
        Raise(nameof(IsVisible));
        Raise(nameof(IsContextual));
        Raise(nameof(ShortcutText));
        Raise(nameof(SearchHint));
    }
}
