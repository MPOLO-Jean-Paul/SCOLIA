using Material.Icons;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;
namespace Scolia.Bureau.ViewModels;
/// <summary>Point d'entrée léger, sans requête financière avant le choix d'Accueil.</summary>
public sealed class StartPage : PageViewModel
{
    public StartPage(ShellViewModel shell) : base(shell)
    {
        var actions = new List<ToolCardVm>();
        void Add(string title, string description, MaterialIconKind icon, string tone, Func<Task> run)
            => actions.Add(new ToolCardVm(title, description, icon, ThemeBrush.Get(tone), new AsyncCommand(() => Shell.Run(run))));
        if (Shell.Can(Perm.StudentsWrite))
            Add("Inscrire", "Créer un nouveau dossier.", MaterialIconKind.AccountPlus, "ToneBlue", () => StudentForms.CreateAsync(Shell));
        if (Shell.Can(Perm.PaymentsCreate))
            Add("Encaisser", "Enregistrer un versement et son reçu.", MaterialIconKind.HandCoin, "ToneGreen", () => Shell.Open(() => new CashierPage(Shell)));
        NewActions = actions;
        OpenHome = new AsyncCommand(() => Shell.Run(() => Shell.Open(() => new HomePage(Shell))));
    }
    public override string Title => "Démarrer";
    public override MaterialIconKind Glyph => MaterialIconKind.ViewDashboard;
    public string School => Shell.SchoolHeaderTitle;
    public string Greeting => $"Bienvenue, {Shell.AccountName}";
    public IReadOnlyList<ToolCardVm> NewActions { get; }
    public bool HasNewActions => NewActions.Count > 0;
    public AsyncCommand OpenHome { get; }
}
