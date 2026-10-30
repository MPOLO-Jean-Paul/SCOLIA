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

    private TailscaleInstaller()
    {
        Verify = new RelayCommand(() => { CheckInstallation(); });
        Close = new RelayCommand(() => IsOpen = false);
        OpenStore = new RelayCommand(LaunchStore);
        Configure = new RelayCommand(() => Status = TailscaleCompanion.Open());
        Platform.AppResumed += () => Dispatcher.UIThread.Post(() =>
        {
            if (IsOpen) CheckInstallation();
        });
    }

    public bool IsOpen { get => isOpen; private set => Set(ref isOpen, value); }
    public bool IsInstalled
    {
        get => isInstalled;
        private set
        {
            if (Set(ref isInstalled, value)) Raise(nameof(NeedsInstallation));
        }
    }
    public bool NeedsInstallation => !IsInstalled;
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
        if (CheckInstallation()) return;
        LaunchStore();
    }

    /// <summary>Renvoie vrai seulement lorsque le paquet officiel est réellement détecté.</summary>
    private bool CheckInstallation()
    {
        bool found = TailscaleCompanion.IsInstalled();
        IsInstalled = found;
        if (found)
        {
            Step = "Étape 3 sur 3 · Installation terminée";
            StepProgress = 3;
            IsWaiting = false;
            Status = "Tailscale a été détecté sur cet appareil. Fermez cette fenêtre pour revenir au partage, ou configurez le réseau privé si c'est la première utilisation.";
        }
        else
        {
            Step = "Étape 2 sur 3 · Téléchargement et installation";
            StepProgress = 2;
            IsWaiting = true;
            Status = "Tailscale n'est pas encore installé. Téléchargez-le dans le magasin officiel et revenez dans SCOLIA ; la détection reprendra automatiquement.";
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
        IsWaiting = !result.StartsWith("Impossible", StringComparison.OrdinalIgnoreCase)
                    && !result.StartsWith("Android n'est pas", StringComparison.OrdinalIgnoreCase);
    }
}
