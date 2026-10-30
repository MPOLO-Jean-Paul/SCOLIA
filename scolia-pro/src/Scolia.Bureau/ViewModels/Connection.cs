using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Serveur proposé dans la liste, avec son bouton « Rejoindre ».</summary>
public sealed class ServerRowVm(DiscoveredServer server, Func<DiscoveredServer, Task> join)
{
    public DiscoveredServer Server { get; } = server;
    public string School => Server.School;
    public string Detail => Server.Detail;
    public Material.Icons.MaterialIconKind Icon => Server.IsPhone ? Material.Icons.MaterialIconKind.Cellphone : Material.Icons.MaterialIconKind.DesktopTowerMonitor;
    public AsyncCommand Join { get; } = new(() => join(server));
}

/// <summary>
/// Choix du serveur de l'établissement : les SCOLIA trouvés sur le réseau, ou cet ordinateur comme serveur.
/// N'apparaît que si le choix n'est pas évident (plusieurs serveurs, ou serveur mémorisé injoignable).
/// </summary>
public sealed class ConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string message = string.Empty;
    private bool searching;

    public ConnectionViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Search = new AsyncCommand(SearchAsync);
        UseThisComputer = new AsyncCommand(() => shell.UseThisComputerAsync());
        ConnectAddress = new AsyncCommand(ConnectAddressAsync);
        ConnectTailnet = new AsyncCommand(ConnectTailnetAsync);
        PrepareTailnet = new AsyncCommand(Installer.BeginAsync);
        JoinInternet = new AsyncCommand(JoinInternetAsync);
    }

    /// <summary>Tout ordinateur peut être le serveur ; un téléphone aussi, quand l'établissement n'a pas d'ordinateur.</summary>
    public bool CanBeServer => Platform.CanHostServer;
    public bool IsPhone => Platform.IsMobile;
    public bool PhoneCanBeServer => Platform.IsMobile && Platform.CanHostServer;
    public bool DesktopCanBeServer => !Platform.IsMobile;

    public string AddressTitle => Platform.IsMobile ? "Ou saisir l'adresse du serveur" : "Ou saisir l'adresse de l'ordinateur principal";
    public string AddressHint => Platform.IsMobile
        ? "Elle figure sur le serveur de l'établissement, ordinateur ou téléphone, dans Poste et réseau."
        : "Elle figure sur l'ordinateur principal, dans Fichier, Poste et réseau.";

    private string address = string.Empty;
    /// <summary>Adresse saisie à la main, quand le Wi-Fi bloque la recherche automatique : 192.168.1.10, ou 192.168.1.10:5080.</summary>
    public string Address { get => address; set => Set(ref address, value); }
    public AsyncCommand ConnectAddress { get; }
    private string tailscaleAddress = string.Empty;
    /// <summary>IPv4 Tailscale ou nom MagicDNS complet de l'appareil qui porte la base.</summary>
    public string TailscaleAddress { get => tailscaleAddress; set => Set(ref tailscaleAddress, value); }
    public AsyncCommand ConnectTailnet { get; }
    /// <summary>Installation ou ouverture du client VPN, uniquement après un clic de l'utilisateur.</summary>
    public AsyncCommand PrepareTailnet { get; }
    public TailscaleInstaller Installer { get; } = TailscaleInstaller.Instance;

    private async Task ConnectTailnetAsync()
    {
        if (string.IsNullOrWhiteSpace(TailscaleAddress))
        {
            Message = "Commencez par installer ou ouvrir Tailscale, puis saisissez l'adresse privée du téléphone ou PC serveur.";
            return;
        }
        if (!TailscaleEndpoint.TryParse(TailscaleAddress, PosteAdmin.DefaultPort, out var target) || target is null)
        {
            Message = "Adresse Tailscale invalide. Utilisez l'adresse 100.64–100.127.x.x ou un nom complet .ts.net, avec le port SCOLIA.";
            return;
        }

        Searching = true;
        Message = "Vérification de la liaison privée Tailscale…";
        try
        {
            // Lors d'un choix volontaire de rejoindre le tailnet, demander sa
            // reconnexion discrète sans présenter l'envoi comme un succès.
            if (Platform.IsMobile && !TailscaleCompanion.IsInstalled())
            {
                Message = "Tailscale est absent. Lancez son assistant d'installation avant de rejoindre le serveur.";
                return;
            }
            if (Platform.ConnectTailscaleInBackground is not null)
                await TailscaleCompanion.ConnectInBackgroundAsync();
            if (!await TailscaleEndpoint.ResolvesPrivatelyAsync(target))
            {
                Message = "L'adresse indiquée ne correspond pas à un appareil privé Tailscale joignable. Vérifiez MagicDNS et l'état du VPN.";
                return;
            }
            using var probe = new Api.ScoliaClient(target);
            if (!await probe.IsRunningAsync())
            {
                Message = "SCOLIA ne répond pas sur le réseau privé. Vérifiez Tailscale sur les deux appareils, leur autorisation et le serveur SCOLIA.";
                return;
            }
            var status = await probe.GetAsync<Api.SetupStatusDto>("api/installation");
            await shell.JoinAsync(new DiscoveredServer(target.Host,
                status.SchoolName ?? "Établissement", target.Host, string.Empty, target));
            Message = "Connecté sur le réseau privé Tailscale. Les autorisations de connexion SCOLIA restent nécessaires.";
        }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException)
        {
            Message = "Connexion Tailscale impossible. Contrôlez que les deux appareils sont approuvés dans le même tailnet et que le port SCOLIA est accessible.";
        }
        finally { Searching = false; }
    }

    /// <summary>Vérifie que SCOLIA répond à l'adresse saisie, puis relie l'appareil à ce serveur.</summary>
    private async Task ConnectAddressAsync()
    {
        var text = Address.Trim();
        if (text.Length == 0) { Message = "Saisissez l'adresse de l'ordinateur principal, par exemple 192.168.1.10."; return; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            Message = "Adresse invalide. Exemple : 192.168.1.10 ou 192.168.1.10:5080.";
            return;
        }
        var server = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort && !text.Contains($":{uri.Port}", StringComparison.Ordinal) ? PosteAdmin.DefaultPort : uri.Port).Uri;
        Searching = true;
        Message = $"Connexion à {server.Host}…";
        try
        {
            using var probe = new Api.ScoliaClient(server);
            if (!await probe.IsRunningAsync())
            {
                Message = $"SCOLIA ne répond pas à {server.Host}:{server.Port}. Vérifiez l'adresse, que l'ordinateur est allumé et que SCOLIA y est partagé.";
                return;
            }
            var status = await probe.GetAsync<Api.SetupStatusDto>("api/installation");
            var school = status.SchoolName ?? "Établissement";
            await shell.JoinAsync(new DiscoveredServer(server.Host, school, server.Host, string.Empty, server));
        }
        catch (Exception ex) when (ex is HttpRequestException or Api.ApiException or TaskCanceledException or IOException)
        {
            Message = $"SCOLIA ne répond pas à {server.Host}. Vérifiez l'adresse et le Wi-Fi.";
        }
        finally { Searching = false; }
    }

    private string invitation = string.Empty;
    public string Invitation { get => invitation; set => Set(ref invitation, value); }
    public AsyncCommand JoinInternet { get; }
    private async Task JoinInternetAsync()
    {
        Searching = true;
        Message = "Vérification de la liaison Internet sécurisée…";
        try
        {
            var link = Scolia.Transport.RemoteLink.Parse(Invitation);
            using var probe = new Api.ScoliaClient(link.VirtualServer);
            probe.UseRemote(link);
            if (!await probe.IsRunningAsync())
            { Message = "L'appareil principal ne répond pas. Vérifiez qu'il a activé la liaison et qu'il est connecté à Internet."; return; }
            await shell.JoinInternetAsync(link);
            Invitation = string.Empty;
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or Api.ApiException or TaskCanceledException or IOException)
        { Message = "Connexion non établie. Vérifiez l'invitation privée, sa date de validité et la connexion de l'appareil principal."; }
        finally { Searching = false; }
    }

    public ObservableCollection<ServerRowVm> Servers { get; } = [];
    public bool HasServers => Servers.Count > 0;
    public string Message { get => message; private set => Set(ref message, value); }
    public bool Searching { get => searching; private set { if (Set(ref searching, value)) Raise(nameof(NotSearching)); } }
    public bool NotSearching => !searching;

    public AsyncCommand Search { get; }
    public AsyncCommand UseThisComputer { get; }

    /// <summary>Affiche une liste déjà obtenue, avec le message qui explique la situation.</summary>
    public void Show(IReadOnlyList<DiscoveredServer> servers, string text)
    {
        Servers.Clear();
        foreach (var server in servers) Servers.Add(new ServerRowVm(server, shell.JoinAsync));
        Raise(nameof(HasServers));
        Message = text;
    }

    public async Task SearchAsync()
    {
        Searching = true;
        Message = "Recherche de SCOLIA sur le réseau de l'établissement…";
        try
        {
            var servers = await LanDiscovery.FindAsync(TimeSpan.FromSeconds(2.5));
            Show(servers, servers.Count switch
            {
                0 => PhoneCanBeServer
                    ? "Aucun SCOLIA ne répond sur ce Wi-Fi. Vérifiez que le serveur de l'établissement est allumé et que SCOLIA y est ouvert, ou faites de ce téléphone le serveur."
                    : "Aucun SCOLIA ne répond sur ce réseau. Vérifiez que l'ordinateur principal est allumé et que SCOLIA y est ouvert.",
                1 => "SCOLIA a été trouvé sur le réseau.",
                _ => "Plusieurs SCOLIA répondent sur ce réseau : choisissez celui de votre établissement.",
            });
        }
        finally { Searching = false; }
    }
}
