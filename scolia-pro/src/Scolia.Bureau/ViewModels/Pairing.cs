using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Une voie par laquelle l'autre appareil peut joindre ce serveur, montrée sous le code.</summary>
public sealed record PairingRouteVm(Material.Icons.MaterialIconKind Icon, string Title, string Detail);

/// <summary>
/// « Relier un appareil » sur le serveur : un grand code QR que l'autre appareil scanne. Il contient les adresses
/// du Wi-Fi local et l'invitation Internet si elle est active ; aucun mot de passe.
/// </summary>
public sealed class PairingViewModel : Observable
{
    private readonly ShellViewModel shell;

    public PairingViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        var school = shell.SchoolLine.Split(" · ")[0] is { Length: > 0 } name ? name : ConnectionStore.Load()?.School ?? "Établissement";
        var machine = Platform.DeviceName ?? Environment.MachineName;
        var remote = RemoteConnection.Load();
        var invitation = remote is { Host: true, Enabled: true } && remote.Link.ExpiresUtc > DateTime.UtcNow ? remote.Link.Invitation() : null;
        var localAvailable = Platform.IsMobile || PosteAdmin.LanEnabled();
        Shared = localAvailable || invitation is not null;
        InternetOnly = !localAvailable && invitation is not null;
        Code = PairingCode.ForThisServer(school, machine, localAvailable ? PosteAdmin.Addresses() : [], invitation);
        School = school;
        CodeText = string.Empty;
        Image = null;
        Renew = new AsyncCommand(RenewInvitationAsync);
        _ = RenewInvitationAsync();

        var routes = new List<PairingRouteVm>();
        foreach (var lan in Code.LanAddresses)
            routes.Add(new(Material.Icons.MaterialIconKind.Wifi, "Wi-Fi de l'établissement", $"{lan.Host}:{lan.Port}"));
        if (Code.HasInvitation)
            routes.Add(new(Material.Icons.MaterialIconKind.Web, "Internet", "Postes reconnus jusqu’au retrait ; nouveau QR valable 5 minutes"));
        Routes = routes;

        Copy = new AsyncCommand(async () =>
        {
            if (!Ready) return;
            if (shell.CopyHandler is { } copy) await copy(CodeText);
            shell.Notify("Invitation copiée : utilisez-la sur l'autre appareil avant son expiration.");
        });
        Close = new RelayCommand(() => { countdown?.Stop(); shell.CloseDialog(); });
        OpenSharing = new RelayCommand(() =>
        {
            countdown?.Stop();
            shell.CloseDialog();
            _ = shell.Run(() => shell.Open(() => new NetworkHubPage(shell)));
        });
    }

    // ---------- Validité du code : 5 minutes, un seul appareil ----------
    private DispatcherTimer? countdown;
    private DateTime expiresUtc;
    public string RemainingText { get; private set; } = string.Empty;
    public bool HasRemaining => RemainingText.Length > 0;
    public bool Expired { get; private set; }

    private void StartCountdown(DateTime expires)
    {
        expiresUtc = expires;
        Expired = false;
        countdown?.Stop();
        countdown = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        countdown.Start();
        Tick();
    }

    private void Tick()
    {
        // La fenêtre a été fermée autrement (Échap, clic à côté) : ne plus rien mettre à jour.
        if (!ReferenceEquals(shell.Dialog, this)) { countdown?.Stop(); return; }
        var left = expiresUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            countdown?.Stop();
            Expired = true;
            Image = null;
            CodeText = string.Empty;
            RemainingText = string.Empty;
            ProgressText = "Ce code a expiré sans être utilisé. Générez-en un nouveau pour relier l'appareil.";
            foreach (var name in new[] { nameof(Expired), nameof(Image), nameof(CodeText), nameof(Ready), nameof(RemainingText), nameof(HasRemaining), nameof(ProgressText) })
                Raise(name);
            return;
        }
        RemainingText = $"Valable encore {(int)left.TotalMinutes}:{left.Seconds:00}";
        Raise(nameof(RemainingText));
        Raise(nameof(HasRemaining));
    }

    /// <summary>Wi-Fi fermé mais Internet actif : le code fonctionne, par Internet uniquement.</summary>
    public bool InternetOnly { get; }
    public RelayCommand OpenSharing { get; }

    private sealed record InvitationReply(string Token, DateTime ExpiresUtc);
    public PairingCode Code { get; private set; }
    public string School { get; }
    public string CodeText { get; private set; }
    public Bitmap? Image { get; private set; }
    public bool Ready => Image is not null && Code.AccessTicket is not null;
    public string ProgressText { get; private set; } = "Préparation d'une invitation sécurisée…";
    public bool CanInvite => shell.IsServerComputer && shell.Can("utilisateurs.gerer");
    public AsyncCommand Renew { get; }

    private async Task RenewInvitationAsync()
    {
        if (!CanInvite)
        {
            ProgressText = "Seul un responsable connecté sur le serveur peut inviter un appareil.";
            Raise(nameof(ProgressText));
            return;
        }
        ProgressText = "Génération d'un accès à usage unique…";
        Expired = false;
        Raise(nameof(Expired));
        Raise(nameof(ProgressText));
        CodeText = string.Empty;
        Image = null;
        Raise(nameof(Image));
        Raise(nameof(Ready));
        try
        {
            var invite = await shell.Api.SendAsync<InvitationReply>(HttpMethod.Post, "api/jumelage/inviter", new { });
            Code = Code with { AccessTicket = invite.Token };
            if (Code.Addresses.Count == 0 && !Code.HasInvitation)
            {
                // Un QR composé uniquement d'un ticket, sans adresse de serveur,
                // paraît valable visuellement mais ne permet aucun jumelage.
                ProgressText = "Ce serveur n'est pas partagé : ouvrez le Wi-Fi de l'établissement ou l'accès par Internet, puis générez un nouveau code.";
                Raise(nameof(ProgressText));
                Raise(nameof(Ready));
                return;
            }
            CodeText = Code.Text();
            Image = QrCodes.Render(CodeText);
            ProgressText = InternetOnly
                ? "Utilisable une seule fois. Le Wi-Fi de ce serveur est fermé : l'appareil se connectera par Internet."
                : "Utilisable une seule fois. L'appareil choisit seul le Wi-Fi ou Internet.";
            StartCountdown(invite.ExpiresUtc);
            Raise(nameof(Code));
            Raise(nameof(CodeText));
            Raise(nameof(Image));
            Raise(nameof(Ready));
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            ProgressText = "Impossible de générer l'invitation. Vérifiez l'accès au serveur et vos autorisations.";
        }
        Raise(nameof(ProgressText));
    }
    public IReadOnlyList<PairingRouteVm> Routes { get; private set; }
    public bool HasRoutes => Routes.Count > 0;

    /// <summary>Ordinateur dont le partage est fermé : aucun autre appareil ne pourrait le joindre.</summary>
    public bool Shared { get; private set; }
    public bool NotShared => !Shared;

    public string Title => "Relier un autre appareil";
    public string Steps => Platform.IsMobile
        ? "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR."
        : "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR. Sur un ordinateur, collez le code copié.";
    public string Footer => "Ce QR autorise uniquement un appareil à retrouver le serveur. Chaque personne doit ensuite ouvrir sa propre session. L'appareil reste autorisé après une coupure, jusqu'à son retrait dans Réseau et activité.";

    public AsyncCommand Copy { get; }
    public RelayCommand Close { get; }
}
