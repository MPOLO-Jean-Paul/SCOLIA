using System.Collections.ObjectModel;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.Ribbon;

namespace Scolia.Bureau.ViewModels;

/// <summary>Appareil autorisé à joindre ce serveur : nom, validité et retrait de l'accès.</summary>
public sealed record AuthorizedDeviceRowVm(string Name, string Detail, bool Active, AsyncCommand Revoke);

/// <summary>
/// Poste et réseau : à quel serveur ce poste est relié, et sur l'ordinateur principal, le partage avec les autres
/// ordinateurs et le démarrage avec Windows. Tout s'applique tout de suite, sans adresse à saisir.
/// </summary>
public sealed class PosteViewModel : Observable, ILiveRefresh
{
    private readonly ShellViewModel shell;
    /// <summary>Une seule page à relire au retour des réglages : pas d'abonnement qui retiendrait les anciennes.</summary>
    private static PosteViewModel? shown;
    static PosteViewModel() => Platform.AppResumed += () => shown?.Refresh();
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
            try
            {
                // Le partage direct ouvre HTTP sur le Wi-Fi : informer avant de l'activer.
                // Le refus laisse le réglage existant intact (liaison OneWay dans la vue).
                if (!lanEnabled && !await shell.Confirm(
                    "Ouvrir le partage sur le Wi-Fi de l'établissement ?",
                    "Sur le réseau local, SCOLIA utilise HTTP : les échanges ne sont pas chiffrés par HTTPS. "
                    + "N'ouvrez ce partage que sur le réseau privé de l'établissement, jamais sur un Wi-Fi public. "
                    + "Pour les appareils distants, utilisez l'accès par Internet, chiffré de bout en bout.",
                    "Ouvrir le partage"))
                    return;
                await shell.SetNetworkAsync(!lanEnabled);
            }
            finally { Refresh(); }
        }));
        Search = new AsyncCommand(SearchAsync);
        AllowBackground = new RelayCommand(() => Platform.AllowBackground?.Invoke());
        CheckMobileUpdates = new AsyncCommand(async () =>
        {
            if (Platform.CheckAppUpdatesAsync is { } check) await check();
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
        Remote = new RemoteConnectionViewModel(shell, prepareInvitation: false);
        // Activer ou désactiver Internet change aussi la vue d'ensemble du partage.
        Remote.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RemoteConnectionViewModel.IsActive) or nameof(RemoteConnectionViewModel.StatusBrush))
                foreach (var name in new[] { nameof(AccessSummary), nameof(AccessTitle), nameof(AccessBrush), nameof(InternetState), nameof(InternetBrush) })
                    Raise(name);
        };
        LoadDevices = new AsyncCommand(LoadDevicesAsync);
        CopyAddresses = new AsyncCommand(async () =>
        {
            if (addresses.Count == 0) return;
            if (shell.CopyHandler is { } copy) await copy(string.Join(Environment.NewLine, addresses));
            shell.Notify("Adresse copiée : collez-la dans le navigateur d'un autre poste du même réseau.");
        });
        // Mobile : quitter l'ordinateur principal actuel pour en choisir un autre, ou saisir son adresse.
        ChangeServer = new AsyncCommand(async () =>
        {
            shell.CloseBackstage();
            await shell.LogoutAsync();
            await shell.ChooseServerAsync();
        });
        Refresh();
    }

    /// <summary>Titre « Poste et réseau » : utile dans le menu Fichier, inutile sous l'en-tête de la page Réseau et postes.</summary>
    public bool ShowTitle { get; init; } = true;

    public bool IsWindows => OperatingSystem.IsWindows();
    /// <summary>Note sur le pare-feu : seulement pour l'ordinateur principal sous Windows.</summary>
    public bool IsServerWindows => IsWindows && IsServerComputer;
    /// <summary>Ordinateur serveur hors Windows : le partage Wi-Fi ne s'y règle pas (le téléphone serveur, lui, est toujours ouvert).</summary>
    public bool ShowWifiWindowsNote => !IsWindows && !Platform.IsMobile;
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
        : shell.Api.IsLongDistance
            ? $"Serveur {ConnectionStore.Load()?.Machine ?? "principal"} · par Internet, chiffré. Sur le Wi-Fi de l'établissement, SCOLIA reprend la voie locale seul."
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
            ? "L'API scolaire fonctionne, mais Android peut la suspendre en veille. Autorisez l'activité en arrière-plan de SCOLIA."
        : "Le serveur fonctionne. Pour confirmer l'accès à distance, essayez depuis un autre appareil connecté à Internet.";
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
        ? "Ouvert : les appareils du même réseau local peuvent découvrir SCOLIA."
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
    public AsyncCommand Search { get; }
    public AsyncCommand UseThisComputer { get; }
    public AsyncCommand ChangeServer { get; }
    public RelayCommand ShowCode { get; }
    public AsyncCommand ScanCode { get; }
    public AsyncCommand ImportQrImage { get; }
    public bool CanImportQrImage => Platform.IsMobile && Platform.PickQrImageAsync is not null;
    public string ScanLabel => PairingFlow.HasCamera ? "Scanner le code QR d'un serveur" : "Lire une image du code QR d'un serveur";
    public string ShowCodeLine => Platform.IsMobile
        ? "Un code QR à scanner : par le Wi-Fi ou par Internet, sans installation supplémentaire."
        : "Un code QR à scanner, ou à copier sur un autre ordinateur. Il fonctionne par le Wi-Fi et, s'il est actif, par Internet.";

    // ---------- Accès des autres appareils : vue d'ensemble, Internet et appareils autorisés ----------
    public RemoteConnectionViewModel Remote { get; }

    /// <summary>Les voies ouvertes vers ce serveur, en une phrase : ce qui manque pour qu'un autre appareil le joigne.</summary>
    public string AccessSummary => (lanEnabled, Remote.IsActive) switch
    {
        (true, true) => "Ouvert sur le Wi-Fi et par Internet : les appareils autorisés joignent ce serveur où qu'ils soient.",
        (true, false) => "Ouvert sur le Wi-Fi de l'établissement. Les appareils hors de ce réseau ne peuvent pas le joindre.",
        (false, true) => "Ouvert par Internet uniquement. Sur le Wi-Fi, les autres postes passent aussi par Internet.",
        _ => "Aucun autre appareil ne peut le joindre. Ouvrez le partage sur le Wi-Fi ou l'accès par Internet ci-dessous.",
    };
    public string AccessTitle => lanEnabled || Remote.IsActive ? "Les autres appareils peuvent joindre ce serveur" : "Ce serveur n'est pas partagé";
    public Avalonia.Media.IBrush AccessBrush => ThemeBrush.Get(lanEnabled || Remote.IsActive ? "ToneGreen" : "ToneOrange");
    public string WifiState => lanEnabled ? "Wi-Fi : ouvert" : "Wi-Fi : fermé";
    public string InternetState => Remote.IsActive ? "Internet : actif" : "Internet : désactivé";
    public string DevicesState => Devices.Count(d => d.Active) is var n && n == 0 ? "Aucun appareil autorisé" : Format.Count(n, "appareil autorisé", "appareils autorisés");
    public Avalonia.Media.IBrush WifiBrush => ThemeBrush.Get(lanEnabled ? "ToneGreen" : "ToneNeutral");
    public Avalonia.Media.IBrush InternetBrush => Remote.StatusBrush;

    /// <summary>Le responsable gère les appareils autorisés sur le serveur.</summary>
    public bool CanManageDevices => IsServerComputer && shell.Can("utilisateurs.gerer");
    public ObservableCollection<AuthorizedDeviceRowVm> Devices { get; } = [];
    public bool HasDevices => Devices.Count > 0;
    public bool NoDevices => Devices.Count == 0;
    public string DevicesLine { get; private set; } = string.Empty;
    public AsyncCommand LoadDevices { get; }
    public AsyncCommand CopyAddresses { get; }
    public bool HasAddresses => addresses.Count > 0;
    public string AddressList => string.Join("   ·   ", addresses);

    private async Task LoadDevicesAsync()
    {
        if (!CanManageDevices) return;
        List<AuthorizedDeviceDto> list;
        try { list = await shell.Api.GetAsync<List<AuthorizedDeviceDto>>("api/jumelage/appareils"); }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            DevicesLine = "La liste des appareils autorisés n'a pas pu être lue.";
            Raise(nameof(DevicesLine));
            return;
        }
        Devices.Clear();
        var now = DateTime.UtcNow;
        // Les appareils actifs d'abord ; un accès retiré ou expiré reste visible, sans action.
        foreach (var d in list.OrderBy(d => d.Revoked || d.ExpiresUtc <= now).ThenBy(d => d.Name, StringComparer.CurrentCulture))
        {
            var active = !d.Revoked && d.ExpiresUtc > now;
            var detail = d.Revoked ? "Accès retiré"
                : active ? $"Autorisé jusqu'au {Format.Date(DateOnly.FromDateTime(d.ExpiresUtc.ToLocalTime()))} · prolongé à chaque utilisation"
                : "Autorisation expirée";
            var row = d;
            Devices.Add(new AuthorizedDeviceRowVm(d.Name, detail, active, new AsyncCommand(() => RevokeAsync(row))));
        }
        DevicesLine = Devices.Count == 0
            ? "Aucun appareil n'est encore relié à votre compte. Affichez le code QR et scannez-le depuis l'autre appareil."
            : "Un appareil perdu, vendu ou qui ne doit plus servir : retirez son accès. Il devra être relié de nouveau par un code QR.";
        RaiseDevices();
    }

    private async Task RevokeAsync(AuthorizedDeviceDto device)
    {
        if (!await shell.Confirm("Retirer l'accès de cet appareil ?",
                $"« {device.Name} » ne pourra plus ouvrir SCOLIA avec votre compte, ni par le Wi-Fi ni par Internet. Ses opérations déjà envoyées sont conservées.",
                "Retirer l'accès", danger: true))
            return;
        await shell.Run(async () =>
        {
            await shell.Api.SendAsync(HttpMethod.Post, $"api/jumelage/appareils/{device.Id}/revoquer", new { });
            shell.Notify($"Accès retiré pour « {device.Name} ».");
            await LoadDevicesAsync();
        });
    }

    /// <summary>État du relais rafraîchi en direct pendant l'affichage (connexion établie, perdue, nouvel essai).</summary>
    public void RefreshLive()
    {
        Remote.RefreshStatus();
        foreach (var name in new[] { nameof(AccessSummary), nameof(AccessTitle), nameof(AccessBrush), nameof(InternetState), nameof(InternetBrush) })
            Raise(name);
    }

    private void RaiseDevices()
    {
        foreach (var name in new[] { nameof(HasDevices), nameof(NoDevices), nameof(DevicesLine), nameof(DevicesState) }) Raise(name);
    }

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

    /// <summary>Actualise les indicateurs lors de l'ouverture du centre Réseau.</summary>
    public void Refresh()
    {
        serviceInstalled = OperatingSystem.IsWindows() && PosteAdmin.ServiceInstalled();
        // Le téléphone serveur est toujours ouvert au Wi-Fi : c'est sa raison d'être.
        lanEnabled = Platform.IsMobile ? shell.IsServerComputer : PosteAdmin.LanEnabled();
        addresses = lanEnabled ? PosteAdmin.Addresses() : [];
        Remote.RefreshStatus();
        if (CanManageDevices) _ = LoadDevicesAsync();
        foreach (var name in new[] { nameof(ServiceInstalled), nameof(LanEnabled), nameof(ServiceLine), nameof(LanLine), nameof(AddressLine),
                     nameof(HasAddress), nameof(ConnectionTitle), nameof(ConnectionLine), nameof(IsServerComputer), nameof(IsLinked),
                     nameof(IsPhoneServer), nameof(IsPhoneClient), nameof(CanBecomeServer), nameof(ShowBackground), nameof(BackgroundOk),
                     nameof(BackgroundBlocked), nameof(HostForegroundActive), nameof(HostApiServing),
                     nameof(BackgroundHealthy), nameof(BackgroundNeedsAttention),
                     nameof(BackgroundTitle), nameof(BackgroundLine), nameof(MobileUpdateStatus), nameof(BackgroundDiagnostic), nameof(HasBackgroundHint), nameof(ShowAwake), nameof(AwakeLine),
                     nameof(AccessSummary), nameof(AccessTitle), nameof(AccessBrush), nameof(WifiState), nameof(InternetState), nameof(WifiBrush),
                     nameof(InternetBrush), nameof(CanManageDevices), nameof(HasAddresses), nameof(AddressList), nameof(IsWindows), nameof(IsServerWindows) })
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
