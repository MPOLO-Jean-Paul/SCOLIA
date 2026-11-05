using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Poste et réseau : à quel serveur ce poste est relié, et sur l'ordinateur principal, le partage avec les autres
/// ordinateurs et le démarrage avec Windows. Tout s'applique tout de suite, sans adresse à saisir.
/// </summary>
public sealed class PosteViewModel : Observable
{
    private readonly ShellViewModel shell;
    /// <summary>Une seule page à relire au retour des réglages : pas d'abonnement qui retiendrait les anciennes.</summary>
    private static PosteViewModel? shown;
    static PosteViewModel() => Platform.AppResumed += () => shown?.Refresh();
    private bool serviceInstalled;
    private bool lanEnabled;
    private bool searching;
    private string searchLine = string.Empty;
    private string httpsStatus = "HTTPS automatique disponible avec Tailscale sur l'ordinateur principal.";
    private string httpsAddress = string.Empty;
    private bool httpsRefreshing;
    private IReadOnlyList<string> addresses = [];

    public PosteViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        ToggleService = new AsyncCommand(() => RunServiceAsync(serviceInstalled ? "service-desinstaller" : "service-installer"));
        ToggleLan = new AsyncCommand(() => shell.Run(async () =>
        {
            try
            {
                // Le partage direct ouvre HTTP sur le Wi-Fi : informer avant de l'activer.
                // Le refus laisse le réglage existant intact (liaison OneWay dans la vue).
                if (!lanEnabled && !await shell.Confirm(
                    "Activer le partage sur le réseau local ?",
                    "SCOLIA utilise actuellement HTTP sur le réseau local : les échanges ne sont pas chiffrés par HTTPS. "
                    + "N'activez ce partage que sur un réseau privé de confiance, jamais sur un Wi-Fi public. "
                    + "Une connexion passant par un tunnel chiffré doit être configurée séparément.",
                    "Activer sur ce réseau"))
                    return;
                await shell.SetNetworkAsync(!lanEnabled);
            }
            finally { Refresh(); }
        }));
        Search = new AsyncCommand(SearchAsync);
        EnableAutomaticHttps = new AsyncCommand(() => shell.Run(async () =>
        {
            if (!CanEnableAutomaticHttps) return;
            if (!await shell.Confirm("Activer HTTPS automatiquement ?",
                "SCOLIA demandera a Tailscale un certificat HTTPS reconnu et partagera uniquement sur votre reseau prive. "
                + "La premiere autorisation HTTPS/MagicDNS dans Tailscale peut necessiter votre accord. "
                + "Les anciennes adresses HTTP du Wi-Fi ne seront pas modifiees.",
                "Activer HTTPS prive")) return;
            HttpsStatus = "Verification des autorisations et du partage Tailscale…";
            var outcome = await TailscaleHttpsServe.EnableAsync(PosteAdmin.DefaultPort);
            HttpsAddress = outcome.Ready ? outcome.Address?.ToString() ?? string.Empty : string.Empty;
            HttpsStatus = outcome.Message;
            shell.Notify(outcome.Message);
        }));
        AllowBackground = new RelayCommand(() => Platform.AllowBackground?.Invoke());
        CheckMobileUpdates = new AsyncCommand(async () =>
        {
            if (Platform.CheckAppUpdatesAsync is { } check) await check();
            else shell.Notify("Vérification Android indisponible sur cet appareil.");
            Raise(nameof(MobileUpdateStatus));
        });
        VerifyBackgroundServer = new AsyncCommand(async () =>
        {
            if (!IsPhoneServer) return;
            try { Platform.EnsureHostAvailable?.Invoke(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { shell.Notify($"Android n'a pas autorisé la reprise du serveur : {ex.Message}"); }
            // La création initiale de la base peut se poursuivre après le service :
            // laisser le temps à l'API de démarrer, puis relire les deux états.
            for (var i = 0; i < 8 && !(HostForegroundActive && HostApiServing); i++)
                await Task.Delay(400);
            if (Platform.CheckHostNetworkAsync is { } verify)
                await verify();
            Refresh();
        });
        OpenAppSettings = new RelayCommand(() => Platform.OpenAppSettings?.Invoke());
        OpenPowerOptions = new RelayCommand(PosteAdmin.OpenPowerOptions);
        // Code QR : le serveur le montre, l'autre appareil le scanne.
        ShowCode = new RelayCommand(shell.ShowPairing);
        ScanCode = new AsyncCommand(ScanCodeAsync);
        ImportQrImage = new AsyncCommand(() => ReadQrAsync(fromPhoto: true));
        // Retour des réglages d'Android : l'état de l'arrière-plan se relit tout de suite, sur la page affichée.
        shown = this;
        UseThisComputer = new AsyncCommand(() => shell.UseThisComputerAsync());
        // Mobile : quitter l'ordinateur principal actuel pour en choisir un autre, ou saisir son adresse.
        ChangeServer = new AsyncCommand(async () =>
        {
            shell.CloseBackstage();
            await shell.LogoutAsync();
            await shell.ChooseServerAsync();
        });
        Refresh();
    }

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool CanEnableAutomaticHttps => IsWindows && IsServerComputer && shell.Can(Perm.SettingsWrite);
    public string HttpsStatus { get => httpsStatus; private set { Set(ref httpsStatus, value); } }
    public string HttpsAddress { get => httpsAddress; private set { if (Set(ref httpsAddress, value)) Raise(nameof(HasHttpsAddress)); } }
    public bool HasHttpsAddress => httpsAddress.Length > 0;
    public bool IsServerComputer => shell.IsServerComputer;
    public bool IsLinked => !shell.IsServerComputer;
    /// <summary>Un poste relié peut devenir le serveur : tout ordinateur, et le téléphone qui en a les moyens.</summary>
    public bool CanBecomeServer => IsLinked && Platform.CanHostServer;

    /// <summary>Téléphone serveur de l'établissement : il garde les données et les autres postes s'y connectent.</summary>
    public bool CanCheckMobileUpdates => Platform.IsMobile && Platform.CheckAppUpdatesAsync is not null;
    public string MobileUpdateStatus => Platform.AppUpdateStatus?.Invoke() ?? "Contrôle de version indisponible.";
    public bool IsPhoneServer => Platform.IsMobile && IsServerComputer;
    public bool IsPhoneClient => Platform.IsMobile && !IsServerComputer;

    public string ConnectionTitle => IsServerComputer
        ? (Platform.IsMobile ? "Ce téléphone est le serveur de l'établissement" : "Cet ordinateur est le serveur de l'établissement")
        : $"Relié à {ConnectionStore.Load()?.School ?? "l'établissement"}";

    public string ConnectionLine => IsServerComputer
        ? (Platform.IsMobile ? "Les données de l'établissement sont sur ce téléphone. Les autres téléphones et ordinateurs du même Wi-Fi le trouvent et s'y connectent."
            : lanEnabled ? "Les autres ordinateurs qui ont SCOLIA le trouvent et s'y connectent automatiquement."
                         : "Le partage n'est pas ouvert : les autres ordinateurs ne le voient pas encore.")
        : $"Serveur {ConnectionStore.Load()?.Machine ?? "principal"} · {shell.Api.Server.Host}. Si son adresse change, SCOLIA le retrouve seul.";

    /// <summary>Téléphone serveur : Android le laisse-t-il travailler écran éteint, sans économie de batterie ?</summary>
    public bool ShowBackground => IsPhoneServer && Platform.BackgroundAllowed is not null;
    public bool BackgroundOk => Platform.BackgroundAllowed?.Invoke() ?? true;
    public bool BackgroundBlocked => !BackgroundOk;
    public bool HostForegroundActive => Platform.IsHostForegroundActive?.Invoke() == true;
    public bool HostApiServing => Platform.IsHostApiServing?.Invoke() == true;
    public string BackgroundDiagnostic => Platform.HostNetworkDiagnostic?.Invoke() ?? "Diagnostic réseau indisponible.";
    public bool BackgroundHealthy => BackgroundOk && HostForegroundActive && HostApiServing;
    public bool BackgroundNeedsAttention => !BackgroundHealthy;
    public string BackgroundTitle => !HostForegroundActive
        ? "Le service serveur doit être réactivé"
        : !HostApiServing ? "Démarrage du serveur scolaire…"
        : !BackgroundOk ? "Serveur actif · autorisation batterie nécessaire"
        : "API locale active · liaison aux ordinateurs à confirmer";
    public string BackgroundLine => !HostForegroundActive
        ? "Le service Android n'est pas au premier plan. Touchez « Vérifier le serveur » pendant que SCOLIA est ouvert pour demander sa réactivation."
        : !HostApiServing
            ? "La notification serveur est présente, mais l'API scolaire n'écoute pas encore. Vérifiez le serveur ; ses données seront conservées."
        : !BackgroundOk
            ? "L'API scolaire fonctionne, mais Android peut la suspendre en veille. Autorisez l'arrière-plan pour SCOLIA et, à distance, pour Tailscale."
        : "Le service du téléphone fonctionne, mais seul un essai depuis un ordinateur confirme l'accès distant. Si la connexion PC échoue après réduction de SCOLIA, vérifiez l'adresse IP, le Wi-Fi et Tailscale.";
    public string BackgroundHint => Platform.BackgroundHint;
    public bool HasBackgroundHint => ShowBackground && BackgroundHint.Length > 0;

    /// <summary>Ordinateur principal sous Windows : il ne se met pas en veille tant que le partage est ouvert.</summary>
    public bool ShowAwake => IsWindows && IsServerComputer;
    public string AwakeLine => lanEnabled
        ? "Tant que le partage est ouvert, Windows ne met pas cet ordinateur en veille. L'écran peut s'éteindre, la fenêtre être réduite."
        : "Ouvrez le partage : l'ordinateur restera alors éveillé pour les autres postes.";

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
        : Platform.IsMobile ? "Adresse à saisir sur un autre poste, si la recherche ne trouve pas ce téléphone : " + string.Join(" · ", addresses.Select(a => a.Replace("http://", string.Empty, StringComparison.Ordinal)))
        : "Depuis un navigateur, les autres postes peuvent aussi ouvrir : " + string.Join(" · ", addresses);

    public bool HasAddress => AddressLine.Length > 0;

    public ObservableCollection<ServerRowVm> Servers { get; } = [];
    public string SearchLine { get => searchLine; private set { if (Set(ref searchLine, value)) Raise(nameof(HasSearchLine)); } }
    public bool HasSearchLine => searchLine.Length > 0;
    public bool Searching { get => searching; private set { if (Set(ref searching, value)) Raise(nameof(NotSearching)); } }
    public bool NotSearching => !searching;

    public AsyncCommand ToggleService { get; }
    public AsyncCommand ToggleLan { get; }
    public AsyncCommand EnableAutomaticHttps { get; }
    public AsyncCommand Search { get; }
    public AsyncCommand UseThisComputer { get; }
    public AsyncCommand ChangeServer { get; }
    public RelayCommand ShowCode { get; }
    public AsyncCommand ScanCode { get; }
    public AsyncCommand ImportQrImage { get; }
    public bool CanImportQrImage => Platform.IsMobile && Platform.PickQrImageAsync is not null;
    public string ScanLabel => PairingFlow.HasCamera ? "Scanner le code QR d'un serveur" : "Lire une image du code QR d'un serveur";
    public string ShowCodeLine => Platform.IsMobile
        ? "Un code QR à scanner avec l'autre appareil : il contient les adresses du Wi-Fi, de Tailscale et d'Internet."
        : "Un code QR à scanner avec un téléphone, ou à copier pour un autre ordinateur. Wi-Fi, Tailscale et Internet sont inclus.";

    private Task ScanCodeAsync() => ReadQrAsync(fromPhoto: false);

    private async Task ReadQrAsync(bool fromPhoto)
    {
        var (text, problem) = await PairingFlow.CaptureAsync(shell, fromPhoto);
        if (text is null)
        {
            if (problem is not null) shell.Notify(problem);
            return;
        }
        // Le serveur actuel n'est quitté que si le nouveau répond : la liaison ferme alors la session elle-même.
        await shell.Run(async () =>
        {
            shell.CloseBackstage();
            shell.Notify(await PairingFlow.JoinAsync(shell, text));
        });
    }
    public RelayCommand AllowBackground { get; }
    public AsyncCommand CheckMobileUpdates { get; }
    public AsyncCommand VerifyBackgroundServer { get; }
    public RelayCommand OpenAppSettings { get; }
    public RelayCommand OpenPowerOptions { get; }

    private void Refresh()
    {
        serviceInstalled = OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled();
        // Le téléphone serveur est toujours ouvert au Wi-Fi : c'est sa raison d'être.
        lanEnabled = Platform.IsMobile ? shell.IsServerComputer : PosteAdmin.LanEnabled();
        addresses = lanEnabled ? PosteAdmin.Addresses() : [];
        if (CanEnableAutomaticHttps && !httpsRefreshing) _ = RefreshHttpsStateAsync();
        foreach (var name in new[] { nameof(ServiceInstalled), nameof(LanEnabled), nameof(ServiceLine), nameof(LanLine), nameof(AddressLine),
                     nameof(HasAddress), nameof(CanEnableAutomaticHttps), nameof(ConnectionTitle), nameof(ConnectionLine), nameof(IsServerComputer), nameof(IsLinked),
                     nameof(IsPhoneServer), nameof(IsPhoneClient), nameof(CanBecomeServer), nameof(ShowBackground), nameof(BackgroundOk),
                     nameof(BackgroundBlocked), nameof(HostForegroundActive), nameof(HostApiServing),
                     nameof(BackgroundHealthy), nameof(BackgroundNeedsAttention),
                     nameof(BackgroundTitle), nameof(BackgroundLine), nameof(MobileUpdateStatus), nameof(BackgroundDiagnostic), nameof(HasBackgroundHint), nameof(ShowAwake), nameof(AwakeLine) })
            Raise(name);
    }

    private async Task RefreshHttpsStateAsync()
    {
        if (httpsRefreshing || !CanEnableAutomaticHttps) return;
        httpsRefreshing = true;
        try
        {
            var address = await TailscaleHttpsServe.CurrentAddressAsync(PosteAdmin.DefaultPort);
            if (address is not null)
            {
                HttpsAddress = address.ToString();
                HttpsStatus = "HTTPS prive actif. Certificat gere et renouvele par Tailscale.";
            }
        }
        finally { httpsRefreshing = false; }
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
