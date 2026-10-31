using System.Net.Http;
using Avalonia.Threading;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Assistant Tailscale de SCOLIA, sans quitter l'application : vérification, téléchargement du paquet officiel
/// avec sa vraie progression, installation par le système, puis configuration. L'utilisateur ne sort de SCOLIA
/// que pour se connecter à son compte Tailscale ; au retour, SCOLIA détecte le réseau privé et son adresse.
/// Sur iPhone, l'App Store reste le seul chemin : l'assistant le dit.
/// </summary>
public sealed class TailscaleInstaller : Observable, IDisposable
{
    public static TailscaleInstaller Instance { get; } = new();

    /// <summary>Port du serveur SCOLIA, à donner avec l'adresse privée.</summary>
    private const int ServerPort = 5080;
    private const int Steps = 4;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };

    private bool isOpen;
    private bool isInstalled;
    private bool isWaiting;
    private bool busy;
    private bool downloading;
    private double downloadPercent;
    private string downloadText = string.Empty;
    private string step = "Vérification de Tailscale";
    private string status = "Prêt à vérifier l'installation.";
    private int stepProgress;
    private string networkSummary = "Vérification du réseau privé…";
    private string actionText = "Vérifier le réseau privé";
    private string? pendingPackage;
    private bool autoConnectTried;
    private CancellationTokenSource? cancel;
    private TailnetSnapshot? snapshot;

    private TailscaleInstaller()
    {
        Verify = new RelayCommand(() => { CheckInstallation(); });
        Close = new RelayCommand(() => IsOpen = false);
        OpenStore = new RelayCommand(LaunchStore);
        Configure = new RelayCommand(() => Status = TailscaleCompanion.Open());
        DownloadAndInstall = new AsyncCommand(DownloadAndInstallAsync);
        CancelDownload = new RelayCommand(() => cancel?.Cancel());
        CopyAddress = new AsyncCommand(CopyAddressAsync);
        Platform.AppResumed += () => Dispatcher.UIThread.Post(OnResumed);
        Refresh();
    }

    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }

    public bool IsInstalled
    {
        get => isInstalled;
        private set
        {
            if (!Set(ref isInstalled, value)) return;
            Raise(nameof(NeedsInstallation));
            Raise(nameof(DialogTitle));
            Raise(nameof(CanDownload));
            Raise(nameof(ShowStoreFallback));
        }
    }

    // Un VPN privé déjà reconnu par Android n'est pas une raison de forcer une réinstallation.
    public bool NeedsInstallation => !IsInstalled && snapshot?.HasCandidateIp != true;
    public bool NeedsActivation => IsInstalled && !LikelyConnected;
    public bool LikelyConnected => snapshot?.LikelyConnected == true;

    /// <summary>SCOLIA peut télécharger et installer lui-même Tailscale sur cet appareil.</summary>
    public bool CanDownload => NeedsInstallation && Platform.TailscalePackageKind is not null && !Busy;

    /// <summary>Repli : le magasin officiel, quand SCOLIA ne peut pas installer lui-même (iPhone, échec).</summary>
    public bool ShowStoreFallback => NeedsInstallation && !Busy;

    public string NetworkSummary { get => networkSummary; private set => Set(ref networkSummary, value); }
    public string ActionText { get => actionText; private set => Set(ref actionText, value); }
    public string DialogTitle => IsInstalled ? "Réseau privé Tailscale" : "Installation de Tailscale";
    public bool IsWaiting { get => isWaiting; private set => Set(ref isWaiting, value); }
    public string Step { get => step; private set => Set(ref step, value); }
    public string Status { get => status; private set => Set(ref status, value); }

    /// <summary>Étapes franchies sur quatre : vérification, téléchargement, installation, configuration.</summary>
    public int StepProgress { get => stepProgress; private set => Set(ref stepProgress, value); }
    public int StepCount => Steps;

    /// <summary>Un téléchargement ou une installation est en cours : les autres boutons attendent.</summary>
    public bool Busy
    {
        get => busy;
        private set
        {
            if (!Set(ref busy, value)) return;
            Raise(nameof(CanDownload));
            Raise(nameof(ShowStoreFallback));
        }
    }

    public bool IsDownloading { get => downloading; private set => Set(ref downloading, value); }
    /// <summary>Pourcentage réel du téléchargement, mesuré par SCOLIA (0 à 100).</summary>
    public double DownloadPercent { get => downloadPercent; private set => Set(ref downloadPercent, value); }
    /// <summary>« 18,4 Mo sur 61,2 Mo · 30 % ».</summary>
    public string DownloadText { get => downloadText; private set => Set(ref downloadText, value); }

    /// <summary>Adresse privée de cet appareil pour les autres postes : « 100.x.y.z:5080 ».</summary>
    public string TailnetAddress => snapshot is { HasCandidateIp: true } s ? $"{s.CandidateIp}:{ServerPort}" : string.Empty;
    public bool HasTailnetAddress => TailnetAddress.Length > 0;

    public RelayCommand Verify { get; }
    public RelayCommand Close { get; }
    public RelayCommand OpenStore { get; }
    public RelayCommand Configure { get; }
    public AsyncCommand DownloadAndInstall { get; }
    public RelayCommand CancelDownload { get; }
    public AsyncCommand CopyAddress { get; }

    /// <summary>Fourni par l'interface : copie un texte dans le presse-papiers.</summary>
    public Func<string, Task>? CopyHandler { get; set; }

    public void Refresh()
    {
        snapshot = Platform.ReadTailnetState?.Invoke();
        if (snapshot is { } state)
        {
            IsInstalled = state.Installed;
            NetworkSummary = state.Summary;
            ActionText = state.Installed || state.HasCandidateIp ? "Afficher le réseau privé" : "Installer Tailscale";
        }
        else
        {
            NetworkSummary = Platform.IsMobile
                ? "L'état de Tailscale n'est pas consultable automatiquement sur cet appareil."
                : "Vérifiez la connexion de ce poste dans Tailscale.";
            IsInstalled = TailscaleCompanion.IsInstalled();
            ActionText = IsInstalled ? "Afficher le réseau privé" : "Installer Tailscale";
        }
        RaiseState();
        if (IsOpen && !Busy) CheckInstallation();
    }

    private void RaiseState()
    {
        Raise(nameof(NeedsInstallation));
        Raise(nameof(NeedsActivation));
        Raise(nameof(LikelyConnected));
        Raise(nameof(CanDownload));
        Raise(nameof(ShowStoreFallback));
        Raise(nameof(TailnetAddress));
        Raise(nameof(HasTailnetAddress));
    }

    public async Task BeginAsync()
    {
        IsOpen = true;
        Step = $"Étape 1 sur {Steps} · Vérification";
        Status = "SCOLIA recherche Tailscale sur cet appareil…";
        StepProgress = 1;
        IsWaiting = true;
        await Task.Delay(150); // la fenêtre s'affiche avant la vérification
        CheckInstallation();
    }

    /// <summary>Retour dans SCOLIA (après les réglages d'Android, l'installation ou Tailscale) : on reprend seul.</summary>
    private void OnResumed()
    {
        Refresh();
        // L'autorisation « Installer des applications » vient d'être donnée : l'installation reprend d'elle-même.
        if (pendingPackage is { } path && File.Exists(path) && !IsInstalled && !Busy)
        {
            _ = InstallAsync(path);
            return;
        }
        // Tailscale configuré mais pas encore relié : une demande d'activation discrète, une seule fois.
        if (IsInstalled && !LikelyConnected && !autoConnectTried && Platform.ConnectTailscaleInBackground is not null)
        {
            autoConnectTried = true;
            _ = Task.Run(async () =>
            {
                var message = await TailscaleCompanion.ConnectInBackgroundAsync().ConfigureAwait(false);
                await Task.Delay(2500).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => { Refresh(); if (!LikelyConnected && IsOpen) Status = message; });
            });
        }
    }

    /// <summary>Renvoie vrai seulement lorsque Tailscale est réellement détecté.</summary>
    private bool CheckInstallation()
    {
        // iOS n'autorise pas l'énumération des autres applications installées.
        if (Platform.IsMobile && Platform.IsTailscaleInstalled is null)
        {
            IsInstalled = false;
            Step = "Installation sur iPhone";
            StepProgress = 2;
            IsWaiting = false;
            Status = "Sur iPhone, Tailscale s'installe depuis l'App Store. Touchez « Ouvrir l'App Store », installez-le, connectez-vous, puis revenez dans SCOLIA.";
            return false;
        }
        snapshot = Platform.ReadTailnetState?.Invoke();
        var found = snapshot?.Installed ?? TailscaleCompanion.IsInstalled();
        IsInstalled = found;
        if (snapshot is { } state) NetworkSummary = state.Summary;
        RaiseState();
        IsWaiting = false;
        if (found && LikelyConnected)
        {
            Step = "Prêt · réseau privé actif";
            StepProgress = Steps;
            Status = HasTailnetAddress
                ? $"Les autres postes rejoignent cet appareil avec l'adresse {TailnetAddress}."
                : "Tailscale est relié. Vérifiez la liaison depuis un autre poste.";
        }
        else if (found)
        {
            Step = $"Étape 4 sur {Steps} · Configuration";
            StepProgress = 3;
            Status = "Tailscale est installé. Touchez « Configurer Tailscale » : connectez-vous à votre compte et acceptez le VPN, puis revenez ici. SCOLIA détecte la suite seul.";
        }
        else
        {
            Step = snapshot?.HasCandidateIp == true
                ? "Connexion privée repérée · identité à vérifier"
                : $"Étape 2 sur {Steps} · Téléchargement";
            StepProgress = 1;
            Status = Platform.TailscalePackageKind is not null
                ? "Tailscale n'est pas installé. Touchez « Télécharger et installer » : SCOLIA récupère l'application officielle et lance son installation."
                : snapshot?.Summary ?? "Tailscale n'est pas installé.";
        }
        return found;
    }

    private async Task DownloadAndInstallAsync()
    {
        if (Busy || Platform.TailscalePackageKind is not { } kind) return;
        if (CheckInstallation()) return;
        Busy = true;
        cancel = new CancellationTokenSource();
        try
        {
            Step = $"Étape 2 sur {Steps} · Téléchargement";
            StepProgress = 1;
            Status = "Recherche de la dernière version officielle de Tailscale…";
            IsWaiting = true;
            var package = await TailscalePackages.FindAsync(Http, kind == "android", cancel.Token);
            var expected = await TailscalePackages.OfficialSha256Async(Http, package, cancel.Token);
            IsWaiting = false;
            IsDownloading = true;
            DownloadPercent = 0;
            Status = $"Téléchargement de Tailscale {package.Version} depuis le site officiel. Gardez SCOLIA ouvert.";
            var folder = Path.Combine(Platform.DownloadFolder ?? Path.GetTempPath(), "tailscale");
            var progress = new Progress<(long Received, long? Total)>(p =>
            {
                DownloadPercent = p.Total is > 0 ? p.Received * 100d / p.Total.Value : 0;
                DownloadText = p.Total is > 0
                    ? $"{TailscalePackages.Size(p.Received)} sur {TailscalePackages.Size(p.Total.Value)} · {DownloadPercent:0} %"
                    : TailscalePackages.Size(p.Received);
            });
            var path = await TailscalePackages.DownloadAsync(Http, package, folder, progress, cancel.Token);
            IsDownloading = false;

            Status = "Vérification du fichier téléchargé…";
            IsWaiting = true;
            if (expected is not null && !string.Equals(expected, await TailscalePackages.Sha256Async(path, cancel.Token), StringComparison.Ordinal))
            {
                File.Delete(path);
                Step = "Fichier refusé";
                Status = "Le fichier reçu ne correspond pas à l'empreinte officielle de Tailscale : il a été supprimé. Réessayez sur une autre connexion.";
                return;
            }
            IsWaiting = false;
            await InstallAsync(path);
        }
        catch (OperationCanceledException)
        {
            Step = "Téléchargement annulé";
            Status = "Rien n'a été installé. Vous pouvez relancer le téléchargement quand vous voulez.";
        }
        catch (HttpRequestException)
        {
            Step = "Pas de connexion au site de Tailscale";
            Status = "Le téléchargement demande Internet. Vérifiez la connexion, puis réessayez, ou passez par le magasin officiel.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Step = "Téléchargement interrompu";
            Status = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            IsWaiting = false;
            if (pendingPackage is null) Busy = false;
            cancel?.Dispose();
            cancel = null;
        }
    }

    /// <summary>Remet le fichier à l'installateur du système et suit son issue.</summary>
    private async Task InstallAsync(string path)
    {
        var install = Platform.InstallTailscalePackage
            ?? (OperatingSystem.IsWindows() ? TailscaleWindowsSetup.InstallAsync : null);
        if (install is null)
        {
            Status = "L'installation automatique n'est pas disponible sur cet appareil.";
            Busy = false;
            return;
        }
        Busy = true;
        Step = $"Étape 3 sur {Steps} · Installation";
        StepProgress = 2;
        Status = Platform.IsMobile
            ? "Android va vous demander de confirmer l'installation de Tailscale. Touchez « Installer »."
            : "Windows va vous demander l'accord de l'administrateur. Acceptez pour installer Tailscale.";
        IsWaiting = true;
        var result = await install(path);
        IsWaiting = false;
        Status = result.Message;
        switch (result.Outcome)
        {
            case TailscaleInstallOutcome.Installed:
                pendingPackage = null;
                TryDelete(path);
                Busy = false;
                CheckInstallation();
                break;
            case TailscaleInstallOutcome.NeedsPermission:
            case TailscaleInstallOutcome.WaitingForUser:
                // Au retour dans SCOLIA, l'installation reprend ou se vérifie d'elle-même.
                pendingPackage = path;
                Step = result.Outcome == TailscaleInstallOutcome.NeedsPermission ? "Autorisation à donner" : $"Étape 3 sur {Steps} · Installation";
                Busy = false;
                break;
            default:
                pendingPackage = null;
                TryDelete(path);
                Step = result.Outcome == TailscaleInstallOutcome.Refused ? "Installation refusée" : "Installation interrompue";
                Busy = false;
                break;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void LaunchStore()
    {
        if (CheckInstallation()) return;
        Step = "Installation par le magasin officiel";
        StepProgress = 1;
        Status = TailscaleCompanion.OpenInstallPage();
    }

    private async Task CopyAddressAsync()
    {
        if (!HasTailnetAddress || CopyHandler is null) return;
        await CopyHandler(TailscaleAddress());
        Status = $"Adresse copiée : {TailnetAddress}. Envoyez-la aux autres postes pour qu'ils rejoignent l'établissement.";
    }

    private string TailscaleAddress() => TailnetAddress;

    public void Dispose() => cancel?.Dispose();
}
