using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Poste et réseau : démarrage automatique de SCOLIA avec Windows, et accès depuis les autres postes.</summary>
public sealed class PosteViewModel : Observable
{
    private readonly ShellViewModel shell;
    private bool serviceInstalled;
    private bool lanEnabled;
    private IReadOnlyList<string> addresses = [];

    public PosteViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        InstallService = new AsyncCommand(() => RunAsync("service-installer"));
        RemoveService = new AsyncCommand(() => RunAsync("service-desinstaller"));
        EnableLan = new AsyncCommand(() => RunAsync("reseau-on"));
        DisableLan = new AsyncCommand(() => RunAsync("reseau-off"));
        Refresh();
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    public string ServiceLine => !IsWindows ? "Disponible sous Windows uniquement."
        : serviceInstalled ? "Installé : SCOLIA démarre avec Windows, même sans session ouverte."
        : "Non installé : SCOLIA démarre quand un utilisateur l'ouvre.";

    public string LanLine => lanEnabled
        ? "Activé : les autres postes du réseau local peuvent ouvrir SCOLIA."
        : "Désactivé : SCOLIA ne répond que sur ce poste.";

    public string AddressLine => !lanEnabled ? "Adresse sur ce poste : http://localhost:" + PosteAdmin.Port()
        : addresses.Count == 0 ? "Aucune adresse réseau détectée. Vérifiez la connexion du poste."
        : "Adresses pour les autres postes : " + string.Join(" · ", addresses);

    public bool CanInstallService => IsWindows && !serviceInstalled;
    public bool CanRemoveService => IsWindows && serviceInstalled;
    public bool CanEnableLan => IsWindows && !lanEnabled;
    public bool CanDisableLan => IsWindows && lanEnabled;

    public AsyncCommand InstallService { get; }
    public AsyncCommand RemoveService { get; }
    public AsyncCommand EnableLan { get; }
    public AsyncCommand DisableLan { get; }

    private void Refresh()
    {
        serviceInstalled = OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled();
        lanEnabled = PosteAdmin.LanEnabled();
        addresses = lanEnabled ? PosteAdmin.Addresses() : [];
        Raise(nameof(ServiceLine));
        Raise(nameof(LanLine));
        Raise(nameof(AddressLine));
        Raise(nameof(CanInstallService));
        Raise(nameof(CanRemoveService));
        Raise(nameof(CanEnableLan));
        Raise(nameof(CanDisableLan));
    }

    private async Task RunAsync(string action)
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
