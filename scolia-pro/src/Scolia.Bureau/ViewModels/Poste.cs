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
        ToggleService = new AsyncCommand(() => RunAsync(serviceInstalled ? "service-desinstaller" : "service-installer"));
        ToggleLan = new AsyncCommand(() => RunAsync(lanEnabled ? "reseau-off" : "reseau-on"));
        Refresh();
    }

    public bool IsWindows => OperatingSystem.IsWindows();

    /// <summary>Interrupteur du démarrage automatique : true quand le service est installé.</summary>
    public bool ServiceInstalled => serviceInstalled;

    /// <summary>Interrupteur de l'accès réseau local : true quand les autres postes peuvent se connecter.</summary>
    public bool LanEnabled => lanEnabled;

    public string ServiceLine => !IsWindows ? "Disponible sous Windows uniquement."
        : serviceInstalled ? "SCOLIA démarre avec Windows, même sans session ouverte."
        : "SCOLIA démarre quand un utilisateur l'ouvre.";

    public string LanLine => lanEnabled
        ? "Les autres postes du réseau local peuvent ouvrir SCOLIA."
        : "SCOLIA ne répond que sur ce poste.";

    public string AddressLine => !lanEnabled ? "Adresse sur ce poste : http://localhost:" + PosteAdmin.Port()
        : addresses.Count == 0 ? "Aucune adresse réseau détectée. Vérifiez la connexion du poste."
        : "Adresses pour les autres postes : " + string.Join(" · ", addresses);

    public AsyncCommand ToggleService { get; }
    public AsyncCommand ToggleLan { get; }

    private void Refresh()
    {
        serviceInstalled = OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled();
        lanEnabled = PosteAdmin.LanEnabled();
        addresses = lanEnabled ? PosteAdmin.Addresses() : [];
        Raise(nameof(ServiceInstalled));
        Raise(nameof(LanEnabled));
        Raise(nameof(ServiceLine));
        Raise(nameof(LanLine));
        Raise(nameof(AddressLine));
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
