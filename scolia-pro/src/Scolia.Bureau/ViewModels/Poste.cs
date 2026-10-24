using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Poste et réseau : à quel serveur ce poste est relié, et sur l'ordinateur principal, le partage avec les autres
/// ordinateurs et le démarrage avec Windows. Tout s'applique tout de suite, sans adresse à saisir.
/// </summary>
public sealed class PosteViewModel : Observable
{
    private readonly ShellViewModel shell;
    private bool serviceInstalled;
    private bool lanEnabled;
    private bool searching;
    private string searchLine = string.Empty;
    private IReadOnlyList<string> addresses = [];

    public PosteViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        ToggleService = new AsyncCommand(() => RunServiceAsync(serviceInstalled ? "service-desinstaller" : "service-installer"));
        ToggleLan = new AsyncCommand(() => shell.Run(async () =>
        {
            try { await shell.SetNetworkAsync(!lanEnabled); }
            finally { Refresh(); }
        }));
        Search = new AsyncCommand(SearchAsync);
        UseThisComputer = new AsyncCommand(() => shell.UseThisComputerAsync());
        Refresh();
    }

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsServerComputer => shell.IsServerComputer;
    public bool IsLinked => !shell.IsServerComputer;

    public string ConnectionTitle => IsServerComputer
        ? "Cet ordinateur est le serveur de l'établissement"
        : $"Relié à {ConnectionStore.Load()?.School ?? "l'établissement"}";

    public string ConnectionLine => IsServerComputer
        ? (lanEnabled ? "Les autres ordinateurs qui ont SCOLIA le trouvent et s'y connectent automatiquement."
                      : "Le partage n'est pas ouvert : les autres ordinateurs ne le voient pas encore.")
        : $"Ordinateur {ConnectionStore.Load()?.Machine ?? "principal"} · {shell.Api.Server.Host}. Si son adresse change, SCOLIA le retrouve seul.";

    /// <summary>Interrupteur du démarrage automatique : true quand le service est installé.</summary>
    public bool ServiceInstalled => serviceInstalled;

    /// <summary>Interrupteur du partage : true quand les autres ordinateurs peuvent se connecter.</summary>
    public bool LanEnabled => lanEnabled;

    public string ServiceLine => !IsWindows ? "Disponible sous Windows uniquement."
        : serviceInstalled ? "SCOLIA démarre avec Windows, même sans session ouverte."
        : "SCOLIA démarre quand un utilisateur l'ouvre.";

    public string LanLine => lanEnabled
        ? "Ouvert : découverte automatique, pare-feu et réseau privé réglés par SCOLIA."
        : "Fermé : SCOLIA ne répond que sur cet ordinateur.";

    public string AddressLine => !lanEnabled || addresses.Count == 0 ? string.Empty
        : "Depuis un navigateur, les autres postes peuvent aussi ouvrir : " + string.Join(" · ", addresses);

    public bool HasAddress => AddressLine.Length > 0;

    public ObservableCollection<ServerRowVm> Servers { get; } = [];
    public string SearchLine { get => searchLine; private set { if (Set(ref searchLine, value)) Raise(nameof(HasSearchLine)); } }
    public bool HasSearchLine => searchLine.Length > 0;
    public bool Searching { get => searching; private set { if (Set(ref searching, value)) Raise(nameof(NotSearching)); } }
    public bool NotSearching => !searching;

    public AsyncCommand ToggleService { get; }
    public AsyncCommand ToggleLan { get; }
    public AsyncCommand Search { get; }
    public AsyncCommand UseThisComputer { get; }

    private void Refresh()
    {
        serviceInstalled = OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled();
        lanEnabled = PosteAdmin.LanEnabled();
        addresses = lanEnabled ? PosteAdmin.Addresses() : [];
        foreach (var name in new[] { nameof(ServiceInstalled), nameof(LanEnabled), nameof(ServiceLine), nameof(LanLine), nameof(AddressLine),
                     nameof(HasAddress), nameof(ConnectionTitle), nameof(ConnectionLine), nameof(IsServerComputer), nameof(IsLinked) })
            Raise(name);
    }

    private async Task SearchAsync()
    {
        Searching = true;
        SearchLine = "Recherche sur le réseau de l'établissement…";
        try
        {
            var found = await LanDiscovery.FindAsync(TimeSpan.FromSeconds(2.5));
            Servers.Clear();
            foreach (var server in found) Servers.Add(new ServerRowVm(server, shell.JoinAsync));
            SearchLine = found.Count == 0
                ? "Aucun autre SCOLIA ne répond sur ce réseau."
                : $"{found.Count} serveur(s) SCOLIA trouvé(s). Rejoindre ferme la session en cours.";
        }
        finally { Searching = false; }
    }

    private async Task RunServiceAsync(string action)
    {
        await shell.Run(async () =>
        {
            try
            {
                var message = await PosteAdmin.RunAsync(action);
                shell.Notify(message.Replace(Environment.NewLine, " "));
            }
            finally { Refresh(); }
        });
    }
}
