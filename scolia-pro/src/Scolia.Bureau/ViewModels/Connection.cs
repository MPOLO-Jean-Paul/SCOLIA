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
    private bool pairingInProgress;
    /// <summary>Le QR est reconnu : montrer une seule interface de vérification, sans les champs sous-jacents.</summary>
    public bool PairingInProgress
    {
        get => pairingInProgress;
        private set => Set(ref pairingInProgress, value);
    }

    public ConnectionViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Search = new AsyncCommand(SearchAsync);
        UseThisComputer = new AsyncCommand(() => shell.UseThisComputerAsync());
        ConnectAddress = new AsyncCommand(ConnectAddressAsync);
        JoinInternet = new AsyncCommand(JoinInternetAsync);
        ScanCode = new AsyncCommand(ScanCodeAsync);
        ImportQrImage = new AsyncCommand(ImportQrImageAsync);
    }

    // ---------- QR du responsable : Wi-Fi local ou Internet chiffré ----------
    /// <summary>Téléphone : l'appareil photo. Ordinateur : une image du code (photo, capture d'écran).</summary>
    public AsyncCommand ScanCode { get; }
    public AsyncCommand ImportQrImage { get; }
    public bool CanImportQrImage => Platform.IsMobile && Platform.PickQrImageAsync is not null;
    /// <summary>Ordinateur : le code copié sur le serveur (ou une invitation) se colle sous le bouton.</summary>
    public bool CanPasteCode => !PairingFlow.HasCamera;
    public string ScanLabel => PairingFlow.HasCamera ? "Scanner le code QR" : "Lire une image du code QR";
    public string ScanHint => PairingFlow.HasCamera
        ? "Sur le serveur : Relier un appareil, puis scannez son QR. SCOLIA choisit le réseau local ou Internet automatiquement."
        : "Sur le serveur : Poste et réseau, puis Relier un appareil. Ouvrez une photo du code, ou collez le code copié.";

    private Task ScanCodeAsync() => ReadQrAsync(fromPhoto: false);
    private Task ImportQrImageAsync() => ReadQrAsync(fromPhoto: true);

    private async Task ReadQrAsync(bool fromPhoto)
    {
        if (Searching) return;
        Searching = true;
        try
        {
            var (text, problem) = await PairingFlow.CaptureAsync(shell, fromPhoto);
            if (text is null)
            {
                if (problem is not null) Message = problem;
                return;
            }
            await JoinCodeAsync(text);
        }
        finally { Searching = false; }
    }

    private async Task JoinCodeAsync(string text)
    {
        Searching = true;
        PairingInProgress = true;
        Message = "Code reconnu. Vérification de l'appareil et de ses autorisations…";
        try
        {
            Message = await PairingFlow.JoinAsync(shell, text, step => Message = step);
            if (!shell.IsLoggedIn) shell.ReturnToPairingChooser(Message);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException
            or Api.ApiException or InvalidOperationException or UnauthorizedAccessException)
        {
            Message = $"Jumelage impossible : {ex.Message}. Vérifiez le serveur et réessayez.";
            if (!shell.IsLoggedIn) shell.ReturnToPairingChooser(Message);
        }
        finally
        {
            PairingInProgress = false;
            Searching = false;
        }
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
    /// <summary>Vérifie que SCOLIA répond à l'adresse saisie, puis relie l'appareil à ce serveur.</summary>
    private async Task ConnectAddressAsync()
    {
        var text = Address.Trim();
        if (text.Length == 0) { Message = "Saisissez l'adresse de l'ordinateur principal, par exemple 192.168.1.10."; return; }
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0
            || uri.Scheme != Uri.UriSchemeHttp || !PairingCode.IsAllowed(uri.Host)
            || TailscaleEndpoint.IsTailnetAddress(uri) || uri.AbsolutePath != "/"
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            Message = "Saisissez uniquement une adresse IP locale de l'établissement, par exemple 192.168.1.10:5080. Pour Internet, utilisez le QR code.";
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
        // Le code du responsable fonctionne aussi sur le Wi-Fi ou via Render.
        if (Invitation.Trim().StartsWith(PairingCode.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = Invitation;
            await JoinCodeAsync(code);
            if (!shell.ChoosingServer) Invitation = string.Empty;
            return;
        }
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

    // ---------- Retour : on ne reste jamais bloqué sur ce choix ----------
    private Action? back;
    /// <summary>Retour à l'écran d'où l'on vient (connexion ou application), quand un établissement est déjà relié.</summary>
    public Action? BackAction { get => back; set { back = value; Raise(nameof(CanGoBack)); } }
    public bool CanGoBack => back is not null;
    public RelayCommand Back => backCommand ??= new RelayCommand(() => back?.Invoke());
    private RelayCommand? backCommand;

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
