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
    private string diagnostic = "Partage distant non encore vérifié.";
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
        ShareTailnet = new AsyncCommand(ShareTailnetNowAsync);
        InstallTailnet = new AsyncCommand(Installer.BeginAsync);
        DiagnoseTailnet = new AsyncCommand(DiagnoseTailnetAsync);
        Disable = new RelayCommand(() =>
        {
            if (!CanManage) return;
            RemoteConnection.Disable();
            Invitation = string.Empty;
            Message = "Liaison Internet désactivée. Le partage local reste disponible. L'ancienne invitation ne permet plus de joindre cet appareil.";
        });
    }
    public bool CanManage => shell.IsServerComputer && shell.Can("parametres.ecrire");

    /// <summary>Explication propre à l'appareil : Android, iPhone ou ordinateur n'activent pas Tailscale de la même façon.</summary>
    public string TailnetHelp =>
        OperatingSystem.IsAndroid()
            ? "Après la première configuration de Tailscale et l'autorisation VPN d'Android, ce bouton demande la connexion en arrière-plan sans ouvrir Tailscale. Les données restent sur cet appareil."
            : OperatingSystem.IsIOS()
                ? "Sur iPhone et iPad, Tailscale s'active dans son application. Activez « VPN à la demande » une fois : l'appareil reste relié au réseau de l'établissement."
                : "Une fois Tailscale installé et connecté au compte de l'établissement, cet ordinateur est joignable par son adresse privée. Les données restent sur cet ordinateur.";

    public string TailnetNote =>
        OperatingSystem.IsAndroid()
            ? "L'envoi de la commande n'est pas une confirmation de connexion. Testez le partage depuis un appareil client autorisé. Android conserve son indicateur VPN."
            : "Testez le partage depuis un appareil client autorisé : c'est la seule preuve que la liaison fonctionne.";
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

    /// <summary>Demande Tailscale en arrière-plan ; la connexion doit être testée par un client.</summary>
    public AsyncCommand ShareTailnet { get; }
    public AsyncCommand InstallTailnet { get; }
    public AsyncCommand DiagnoseTailnet { get; }
    public string Diagnostic { get => diagnostic; private set => Set(ref diagnostic, value); }
    public TailscaleInstaller Installer { get; } = TailscaleInstaller.Instance;

    private async Task ShareTailnetNowAsync()
    {
        if (!CanManage)
        {
            Message = "Seul un responsable autorisé peut partager cet établissement.";
            return;
        }
        Message = "Demande d'activation du réseau privé en arrière-plan…";
        Message = await TailscaleCompanion.ConnectInBackgroundAsync();
        await DiagnoseTailnetAsync();
    }

    private async Task DiagnoseTailnetAsync()
    {
        if (!CanManage) return;
        Diagnostic = "Diagnostic du réseau privé en cours…";
        bool installed = TailscaleCompanion.IsInstalled();
        bool? vpn = Platform.IsVpnActive?.Invoke();
        bool localReady;
        try { localReady = await shell.Api.IsRunningAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        { localReady = false; }
        Diagnostic = TailnetDiagnostics.DescribeServer(installed, vpn, localReady);
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
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var response = await probe.GetAsync(address);
            response.EnsureSuccessStatusCode();
            RemoteConnection.Save(new RemoteConfiguration(link, true, shell.Login.SchoolName));
            RemoteConnection.StartHost(shell.Api.Server);
            if (reuse)
                Message = "Liaison Internet Render déjà active. Les appareils autorisés restent jumelés ; aucun nouveau QR n'est nécessaire.";
            else
                await PrepareInvitationAsync(link);
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or TaskCanceledException or IOException)
        { Message = "Liaison non activée : vérifiez l'adresse et la disponibilité du relais sécurisé."; }
    }
}
