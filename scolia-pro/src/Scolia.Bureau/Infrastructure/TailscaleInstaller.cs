using Avalonia.Threading;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Assistant visible d'installation de Tailscale. L'installation est réalisée par le
/// magasin officiel ; SCOLIA suit les étapes et vérifie le paquet après retour.
/// Ne jamais inventer un pourcentage de téléchargement Google Play.
/// </summary>
public sealed class TailscaleInstaller : Observable
{
    public static TailscaleInstaller Instance { get; } = new();

    private bool isOpen;
    private bool isInstalled;
    private bool isWaiting;
    private string step = "Vérification de Tailscale";
    private string status = "Prêt à vérifier l'installation.";
    private int stepProgress;
    private string networkSummary = "Vérification du réseau privé…";
    private string actionText = "Vérifier le réseau privé";

    private TailscaleInstaller()
    {
        Verify = new RelayCommand(() => { CheckInstallation(); });
        Close = new RelayCommand(() => IsOpen = false);
        OpenStore = new RelayCommand(LaunchStore);
        Configure = new RelayCommand(() => Status = TailscaleCompanion.Open());
        Platform.AppResumed += () => Dispatcher.UIThread.Post(() => Refresh());
        Refresh();
    }

    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }
    public bool IsInstalled
    {
        get => isInstalled;
        private set
        {
            if (Set(ref isInstalled, value))
            {
                Raise(nameof(NeedsInstallation));
                Raise(nameof(DialogTitle));
            }
        }
    }
    // Un VPN privé déjà reconnu par Android n'est pas une raison de forcer
    // une réinstallation, même si la visibilité du paquet est limitée.
    public bool NeedsInstallation => !IsInstalled && snapshot?.HasCandidateIp != true;
    public bool NeedsActivation => IsInstalled && !LikelyConnected;
    public string NetworkSummary { get => networkSummary; private set => Set(ref networkSummary, value); }
    public string ActionText { get => actionText; private set => Set(ref actionText, value); }
    public string DialogTitle => IsInstalled ? "Réseau privé Tailscale" : "Installation de Tailscale";
    public bool LikelyConnected => snapshot?.LikelyConnected == true;
    private TailnetSnapshot? snapshot;
    public void Refresh()
    {
        var state = Platform.ReadTailnetState?.Invoke();
        snapshot = state;
        if (state is not null)
        {
            IsInstalled = state.Installed;
            Raise(nameof(NeedsInstallation));
            Raise(nameof(NeedsActivation));
            NetworkSummary = state.Summary;
            ActionText = state.Installed || state.HasCandidateIp ? "Afficher les réseaux" : "Installer Tailscale";
            Raise(nameof(LikelyConnected));
        }
        else
        {
            NetworkSummary = Platform.IsMobile
                ? "L'état Tailscale n'est pas consultable automatiquement sur cet appareil."
                : "Vérifiez la connexion du poste dans Tailscale.";
            IsInstalled = TailscaleCompanion.IsInstalled();
            Raise(nameof(NeedsInstallation));
            Raise(nameof(NeedsActivation));
            ActionText = IsInstalled ? "Afficher les réseaux" : "Installer Tailscale";
            Raise(nameof(LikelyConnected));
        }
        if (IsOpen) CheckInstallation();
    }
    public bool IsWaiting { get => isWaiting; private set => Set(ref isWaiting, value); }
    public string Step { get => step; private set => Set(ref step, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    /// <summary>Nombre d'étapes terminées/préparées, pas un pourcentage de téléchargement.</summary>
    public int StepProgress { get => stepProgress; private set => Set(ref stepProgress, value); }

    public RelayCommand Verify { get; }
    public RelayCommand Close { get; }
    public RelayCommand OpenStore { get; }
    public RelayCommand Configure { get; }

    public async Task BeginAsync()
    {
        IsOpen = true;
        Step = "Étape 1 sur 3 · Vérification";
        Status = "SCOLIA recherche Tailscale sur cet appareil…";
        StepProgress = 1;
        IsWaiting = true;
        await Task.Delay(150); // Laisse la fenêtre SCOLIA s'afficher avant d'ouvrir le magasin.
        CheckInstallation();
        // Une vérification n'ouvre jamais automatiquement le Play Store.
        // Seul le bouton explicite « Ouvrir le magasin » le fait.
    }

    /// <summary>Renvoie vrai seulement lorsque le paquet officiel est réellement détecté.</summary>
    private bool CheckInstallation()
    {
        // iOS n'autorise pas l'énumération des autres applications installées.
        // Ne jamais afficher « absent » ou une barre d'attente infinie à tort.
        if (Platform.IsMobile && Platform.IsTailscaleInstalled is null)
        {
            IsInstalled = false;
            Step = "Configuration sur iPhone";
            StepProgress = 2;
            IsWaiting = false;
            Status = "iOS ne permet pas à SCOLIA de vérifier automatiquement l'installation de Tailscale. Terminez-la dans l'App Store, activez VPN On Demand, puis revenez à la connexion SCOLIA.";
            return false;
        }
        var state = Platform.ReadTailnetState?.Invoke();
        snapshot = state;
        bool found = state?.Installed ?? TailscaleCompanion.IsInstalled();
        IsInstalled = found;
        Raise(nameof(NeedsInstallation));
        Raise(nameof(NeedsActivation));
        if (state is not null)
        {
            NetworkSummary = state.Summary;
            ActionText = found || state.HasCandidateIp ? "Afficher les réseaux" : "Installer Tailscale";
            Raise(nameof(LikelyConnected));
        }
        if (found)
        {
            Step = "Client Tailscale détecté";
            StepProgress = 3;
            IsWaiting = false;
            Status = state is { LikelyConnected: true }
                ? $"{state.Summary} Votre compte et l'identité du tailnet ne sont pas accessibles à SCOLIA ; testez le partage depuis un autre téléphone."
                : $"{state?.Summary ?? "Tailscale installé."} Ouvrez Tailscale seulement pour autoriser ou rétablir sa connexion.";
        }
        else
        {
            Step = state?.HasCandidateIp == true
                ? "Connexion privée repérée · identité à vérifier"
                : "Étape 2 sur 3 · Téléchargement et installation";
            StepProgress = 2;
            IsWaiting = false;
            Status = state?.Summary ?? "Tailscale non détecté. Choisissez l'installation officielle si nécessaire.";
        }
        return found;
    }

    private void LaunchStore()
    {
        if (CheckInstallation()) return;
        Step = "Étape 2 sur 3 · Installation par le magasin";
        StepProgress = 2;
        string result = TailscaleCompanion.OpenInstallPage();
        Status = result;
        IsWaiting = Platform.IsTailscaleInstalled is not null
                    && !result.StartsWith("Impossible", StringComparison.OrdinalIgnoreCase)
                    && !result.StartsWith("Android n'est pas", StringComparison.OrdinalIgnoreCase);
    }
}
