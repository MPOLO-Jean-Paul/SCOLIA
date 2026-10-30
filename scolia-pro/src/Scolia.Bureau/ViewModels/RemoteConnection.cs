using Scolia.Bureau.Infrastructure;
using Scolia.Transport;

namespace Scolia.Bureau.ViewModels;

/// <summary>Gestion réservée au secrétariat sur l'appareil principal. La comptabilité rejoint par invitation.</summary>
public sealed class RemoteConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string relay = string.Empty;
    private string invitation = string.Empty;
    private string message = string.Empty;
    public RemoteConnectionViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        if (RemoteConnection.Load() is { } current)
        {
            relay = current.Link.Relay;
            if (current.Host && current.Enabled) invitation = current.Link.Invitation();
        }
        Enable = new AsyncCommand(EnableAsync);
        ShareTailnet = new AsyncCommand(ShareTailnetNowAsync);
        Disable = new RelayCommand(() =>
        {
            if (!CanManage) return;
            RemoteConnection.Disable();
            Invitation = string.Empty;
            Message = "Liaison Internet désactivée. Le partage local reste disponible. L'ancienne invitation ne permet plus de joindre cet appareil.";
        });
    }
    public bool CanManage => shell.IsServerComputer && shell.Can("parametres.ecrire");
    public string Relay { get => relay; set => Set(ref relay, value); }
    public string Invitation { get => invitation; private set => Set(ref invitation, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public AsyncCommand Enable { get; }

    /// <summary>Demande Tailscale en arrière-plan ; la connexion doit être testée par un client.</summary>
    public AsyncCommand ShareTailnet { get; }

    private async Task ShareTailnetNowAsync()
    {
        if (!CanManage)
        {
            Message = "Seul un responsable autorisé peut partager cet établissement.";
            return;
        }
        Message = "Demande d'activation du réseau privé en arrière-plan…";
        Message = await TailscaleCompanion.ConnectInBackgroundAsync();
    }

    public RelayCommand Disable { get; }

    private async Task EnableAsync()
    {
        if (!CanManage) { Message = "Le secrétariat configure la liaison sur l'appareil principal."; return; }
        try
        {
            var link = RemoteConnection.Load() is { Host: true } previous
                ? previous.Link.Renew(Relay.Trim()) : RemoteLink.Create(Relay.Trim());
            // Vérifier le relais avant de remplacer une liaison encore utilisable.
            var address = new UriBuilder(link.Relay) { Scheme = new Uri(link.Relay).Scheme == "wss" ? "https" : "http", Path = "/health" }.Uri;
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await probe.GetAsync(address);
            response.EnsureSuccessStatusCode();
            RemoteConnection.Save(new RemoteConfiguration(link, true, shell.Login.SchoolName));
            RemoteConnection.StartHost(shell.Api.Server);
            Invitation = link.Invitation();
            Message = "Liaison créée pour 30 jours. Transmettez cette invitation en privé à l'autre appareil. Il devra aussi se connecter avec son compte Secrétariat ou Comptabilité. Créer une nouvelle liaison révoque la précédente.";
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or TaskCanceledException or IOException)
        { Message = "Liaison non activée : vérifiez l'adresse et la disponibilité du relais sécurisé."; }
    }
}
