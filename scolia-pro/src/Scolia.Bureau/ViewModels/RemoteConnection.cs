using Scolia.Bureau.Infrastructure;
using Scolia.Transport;

namespace Scolia.Bureau.ViewModels;

/// <summary>Gestion réservée au secrétariat sur l'appareil principal. La comptabilité rejoint par invitation.</summary>
public sealed class RemoteConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string relay = RemoteLink.DefaultRelay;
    private string invitation = string.Empty;
    private string message = string.Empty;
    public RemoteConnectionViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        if (RemoteConnection.Load() is { } current)
        {
            relay = current.Link.Relay;
            if (current.Host && current.Enabled) _ = PrepareInvitationAsync(current.Link);
        }
        Enable = new AsyncCommand(EnableAsync);
        RenewAccess = new AsyncCommand(async () =>
        {
            if (CanManage && RemoteConnection.Load() is { Host: true, Enabled: true } current)
                await PrepareInvitationAsync(current.Link);
        });
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
    public AsyncCommand RenewAccess { get; }
    private sealed record InvitationReply(string Token, DateTime ExpiresUtc);

    private async Task PrepareInvitationAsync(RemoteLink link)
    {
        if (!CanManage) return;
        try
        {
            var access = await shell.Api.SendAsync<InvitationReply>(HttpMethod.Post, "api/jumelage/inviter", new { });
            var addresses = Platform.IsMobile || PosteAdmin.LanEnabled() ? PosteAdmin.Addresses() : [];
            var qr = PairingCode.ForThisServer(shell.Login.SchoolName, DeviceIdentity.Name, addresses, link.Invitation())
                with { AccessTicket = access.Token };
            Invitation = qr.Text();
            Message = "Invitation de connexion automatique prête. Valable 5 minutes pour un seul appareil ; renouvelez-la pour le prochain.";
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            Invitation = string.Empty;
            Message = "Liaison Internet active, mais l'invitation de compte n'a pas été créée. Ouvrez « Relier un appareil » pour générer un nouveau QR.";
        }
    }

    public RelayCommand Disable { get; }

    private async Task EnableAsync()
    {
        if (!CanManage) { Message = "Le secrétariat configure la liaison sur l'appareil principal."; return; }
        try
        {
            var previous = RemoteConnection.Load();
            // Un clic répété sur « Activer » ne doit jamais invalider les appareils
            // déjà autorisés. Rotation seulement après expiration, changement de
            // relais ou réactivation d'une connexion volontairement désactivée.
            var addressText = Relay.Trim();
            var reuse = previous is { Host: true, Enabled: true }
                && previous.Link.ExpiresUtc > DateTime.UtcNow
                && string.Equals(previous.Link.Relay, addressText, StringComparison.OrdinalIgnoreCase);
            var link = reuse ? previous!.Link
                : previous is { Host: true } ? previous.Link.Renew(addressText)
                : RemoteLink.Create(addressText);
            // Vérifier le relais avant de remplacer une liaison encore utilisable.
            var address = new UriBuilder(link.Relay) { Scheme = new Uri(link.Relay).Scheme == "wss" ? "https" : "http", Path = "/health" }.Uri;
            Message = "Vérification de la connexion Internet sécurisée et du certificat HTTPS…";
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
            using var response = await probe.GetAsync(address);
            response.EnsureSuccessStatusCode();
            RemoteConnection.Save(new RemoteConfiguration(link, true, shell.Login.SchoolName));
            if (reuse) RemoteConnection.StartHostIfMissing(shell.Api.Server);
            else RemoteConnection.StartHost(shell.Api.Server);
            if (reuse)
                Message = "Liaison Internet déjà active. Les appareils autorisés restent jumelés ; aucun nouveau QR n'est nécessaire.";
            else
                await PrepareInvitationAsync(link);
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or TaskCanceledException or IOException)
        { Message = "Liaison non activée : vérifiez Internet, le certificat HTTPS et la disponibilité du relais. La configuration précédente est conservée."; }
    }
}
